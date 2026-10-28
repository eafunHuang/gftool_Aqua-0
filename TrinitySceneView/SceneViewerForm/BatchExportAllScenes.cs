using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrinityModelViewer.Export;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private const string BatchSceneExportRoot = @"e:\temp_SV_scene_export";
        private const string BatchSceneUnityRoot = @"d:\Program Files\unityEditor\FbxSpilt\Assets\fbx\scenes";
        private const string GltfToFbxScript = @"e:\gltf2Fbx.py";

        // Soft caps to avoid OOM on huge field scenes.
        private const int MaxUniqueModelsPerScene = 600;
        private const int MaxInstancesPerScene = 4000;

        private async void batchExportAllSvScenes_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            {
                MessageBox.Show(this,
                    "Asset Root not set.\nUse Assets -> Set Asset Root... first.",
                    "Batch Export All SV Scenes",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using var fbd = new FolderBrowserDialog();
            fbd.Description = "Select folder containing .trscn scene files (recursive)";
            fbd.UseDescriptionForTitle = true;
            if (!string.IsNullOrWhiteSpace(config.LastSceneDirectory) && Directory.Exists(config.LastSceneDirectory))
            {
                fbd.SelectedPath = config.LastSceneDirectory;
            }
            else if (Directory.Exists(assetRoot))
            {
                fbd.SelectedPath = assetRoot;
            }

            if (fbd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            string scenesRoot = fbd.SelectedPath;
            var sceneFiles = new List<string>();
            try
            {
                foreach (string f in Directory.EnumerateFiles(scenesRoot, "*.trscn", SearchOption.AllDirectories))
                {
                    sceneFiles.Add(f);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to enumerate scene files:\n{ex.Message}",
                    "Batch Export All SV Scenes", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (sceneFiles.Count == 0)
            {
                MessageBox.Show(this, $"No .trscn files found under:\n{scenesRoot}",
                    "Batch Export All SV Scenes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            sceneFiles.Sort(StringComparer.OrdinalIgnoreCase);

            var confirm = MessageBox.Show(this,
                $"Found {sceneFiles.Count} scene file(s) under:\n{scenesRoot}\n\n" +
                $"Export root: {BatchSceneExportRoot}\n" +
                $"Unity root: {BatchSceneUnityRoot}\n\n" +
                $"Caps: unique models ≤ {MaxUniqueModelsPerScene}, instances ≤ {MaxInstancesPerScene}\n" +
                "Each scene is exported separately; memory is released between scenes.\n\n" +
                "Start batch export?",
                "Batch Export All SV Scenes",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.OK)
            {
                return;
            }

            await BatchExportAllScenesAsync(scenesRoot, sceneFiles);
        }

        private async Task BatchExportAllScenesAsync(string scenesRoot, List<string> sceneFiles)
        {
            CancelSceneLoad();
            int loadVersion = Interlocked.Increment(ref sceneLoadVersion);
            var myCts = new CancellationTokenSource();
            sceneLoadCts = myCts;
            var token = myCts.Token;
            isSceneLoading = true;

            int succeeded = 0;
            int failed = 0;
            int skipped = 0;
            var errors = new List<string>();

            BeginSceneLoadUi(loadVersion, $"Batch exporting 0/{sceneFiles.Count}...");

            try
            {
                for (int si = 0; si < sceneFiles.Count; si++)
                {
                    token.ThrowIfCancellationRequested();

                    string sceneFile = sceneFiles[si];
                    string sceneKey = BuildSceneExportKey(scenesRoot, sceneFile);
                    string label = $"Scene ({si + 1}/{sceneFiles.Count}): {sceneKey}";
                    int overallPercent = (int)Math.Round((si / (double)sceneFiles.Count) * 100.0);
                    ReportSceneLoadUi(loadVersion, overallPercent, label);

                    ReleaseBatchSceneMemory();

                    try
                    {
                        // 0=ok, 1=skip, 2=fail
                        int outcome = await ExportOneSceneAsync(sceneFile, sceneKey, loadVersion, label, token, errors);
                        if (outcome == 0) succeeded++;
                        else if (outcome == 1) skipped++;
                        else failed++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (OutOfMemoryException)
                    {
                        failed++;
                        errors.Add($"{sceneKey}: OutOfMemory — skipped remaining work for this scene");
                        ReleaseBatchSceneMemory();
                        try { GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); } catch { }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        errors.Add($"{sceneKey}: {ex.GetType().Name}: {ex.Message}");
                        ReleaseBatchSceneMemory();
                    }

                    await Task.Yield();
                    ReleaseBatchSceneMemory();
                }

                ReportSceneLoadUi(loadVersion, 100, "Batch export complete.");
            }
            catch (OperationCanceledException)
            {
                EndSceneLoadUi(loadVersion, "Batch export canceled.");
                return;
            }
            catch (Exception ex)
            {
                errors.Add($"Unexpected: {ex.Message}");
            }
            finally
            {
                ReleaseBatchSceneMemory();
                isSceneLoading = false;
                renderCtrl.Invalidate();
                try { myCts.Dispose(); } catch { }
                if (ReferenceEquals(sceneLoadCts, myCts))
                {
                    sceneLoadCts = null;
                }
            }

            EndSceneLoadUi(loadVersion, $"Exported {succeeded}/{sceneFiles.Count} scene(s).");

            var sb = new StringBuilder();
            sb.AppendLine($"Scenes root: {scenesRoot}");
            sb.AppendLine($"Total: {sceneFiles.Count}, OK: {succeeded}, Failed: {failed}, Skipped: {skipped}");
            sb.AppendLine($"Export: {BatchSceneExportRoot}");
            sb.AppendLine($"Unity: {BatchSceneUnityRoot}");

            if (errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Errors:");
                int show = errors.Count < 20 ? errors.Count : 20;
                for (int i = 0; i < show; i++)
                {
                    sb.AppendLine($"  - {errors[i]}");
                }
                if (errors.Count > 20)
                {
                    sb.AppendLine($"  ... and {errors.Count - 20} more");
                }
            }

            MessageBox.Show(this, sb.ToString(),
                "Batch Export All SV Scenes",
                MessageBoxButtons.OK,
                failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        /// <returns>0=ok, 1=skip, 2=fail</returns>
        private async Task<int> ExportOneSceneAsync(
            string sceneFile,
            string sceneKey,
            int loadVersion,
            string label,
            CancellationToken token,
            List<string> errors)
        {
            preferredSceneVariant = TryDetectVariantFromPath(sceneFile);

            SceneModelSpawnCollectionResult result;
            try
            {
                result = await Task.Run(() => CollectSceneModelSpawns(sceneFile, token), token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add($"{sceneKey}: collect spawns failed: {ex.Message}");
                return 2;
            }

            if (result.Spawns.Count == 0)
            {
                return 0;
            }

            if (result.Spawns.Count > MaxInstancesPerScene)
            {
                errors.Add($"SKIP: {sceneKey}: {result.Spawns.Count} instances > cap {MaxInstancesPerScene}");
                return 1;
            }

            var uniquePaths = new List<string>();
            var pathToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var spawnResolved = new List<(string? Name, string Resolved, Matrix4 Mat)>(result.Spawns.Count);

            for (int i = 0; i < result.Spawns.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var spawn = result.Spawns[i];
                var resolved = ResolveModelPath(spawn.ModelPath);
                if (resolved == null)
                {
                    continue;
                }

                string full;
                try { full = Path.GetFullPath(resolved); }
                catch { full = resolved; }

                if (!pathToIndex.ContainsKey(full))
                {
                    if (uniquePaths.Count >= MaxUniqueModelsPerScene)
                    {
                        errors.Add($"SKIP: {sceneKey}: unique models > cap {MaxUniqueModelsPerScene}");
                        return 1;
                    }
                    pathToIndex[full] = uniquePaths.Count;
                    uniquePaths.Add(full);
                }

                var mat = ApplyViewerMatrixOptions(
                    spawn.ModelMatrix,
                    config.SpawnModelsAtOrigin,
                    config.RotateModels180X,
                    config.RotateModels180Y,
                    out _,
                    out _);

                spawnResolved.Add((
                    string.IsNullOrWhiteSpace(spawn.SceneObjectName)
                        ? Path.GetFileNameWithoutExtension(spawn.ModelPath)
                        : spawn.SceneObjectName,
                    full,
                    mat));
            }

            if (spawnResolved.Count == 0 || uniquePaths.Count == 0)
            {
                return 0;
            }

            ReportSceneLoadUi(loadVersion, 0,
                $"{label} — loading {uniquePaths.Count} unique / {spawnResolved.Count} instances");

            var modelsByPath = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int i = 0; i < uniquePaths.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    string path = uniquePaths[i];
                    ReportSceneLoadUi(loadVersion,
                        (int)Math.Round(((i + 1) / (double)uniquePaths.Count) * 100.0),
                        $"{label} — model {i + 1}/{uniquePaths.Count}");

                    Model model;
                    try
                    {
                        model = await renderCtrl.renderer.AddSceneModelAsync(path, token: token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (OutOfMemoryException)
                    {
                        throw;
                    }
                    catch
                    {
                        continue;
                    }

                    modelsByPath[path] = model;
                }

                if (modelsByPath.Count == 0)
                {
                    errors.Add($"{sceneKey}: no models loaded");
                    return 2;
                }

                var instances = new List<LoadedSceneModelInstance>(spawnResolved.Count);
                for (int i = 0; i < spawnResolved.Count; i++)
                {
                    var (name, path, mat) = spawnResolved[i];
                    if (!modelsByPath.TryGetValue(path, out var model) || model == null)
                    {
                        continue;
                    }

                    instances.Add(new LoadedSceneModelInstance
                    {
                        Name = name ?? Path.GetFileNameWithoutExtension(path),
                        SourcePath = path,
                        Transform = mat,
                        Model = model
                    });
                }

                if (instances.Count == 0)
                {
                    errors.Add($"{sceneKey}: no exportable instances");
                    return 2;
                }

                string sceneExportDir = Path.Combine(BatchSceneExportRoot, sceneKey);
                try
                {
                    if (Directory.Exists(sceneExportDir))
                    {
                        Directory.Delete(sceneExportDir, true);
                    }
                    Directory.CreateDirectory(sceneExportDir);
                }
                catch (Exception ex)
                {
                    errors.Add($"{sceneKey}: create dir failed: {ex.Message}");
                    return 2;
                }

                string gltfPath = Path.Combine(sceneExportDir, sceneKey + ".gltf");
                ReportSceneLoadUi(loadVersion, 90, $"{label} — writing glTF ({instances.Count} instances)");
                try
                {
                    GltfExporter.ExportScene(instances, gltfPath, sceneKey);
                }
                catch (OutOfMemoryException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"{sceneKey}: glTF export failed: {ex.Message}");
                    return 2;
                }

                // Drop CPU/GPU before heavy Python convert.
                ReleaseBatchSceneMemory();
                try { GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); } catch { }

                ReportSceneLoadUi(loadVersion, 95, $"{label} — glTF→FBX");
                try
                {
                    GltfToFbxRunner.ConvertDirectory(sceneExportDir, GltfToFbxScript);
                }
                catch (Exception ex)
                {
                    errors.Add($"{sceneKey}: FBX convert failed: {ex.Message}");
                    return 2;
                }

                try
                {
                    string unityDest = Path.Combine(BatchSceneUnityRoot, sceneKey);
                    if (Directory.Exists(unityDest))
                    {
                        Directory.Delete(unityDest, true);
                    }
                    CopyDirectoryRecursive(sceneExportDir, unityDest);
                }
                catch (Exception ex)
                {
                    errors.Add($"{sceneKey}: Unity copy failed: {ex.Message}");
                }

                return 0;
            }
            finally
            {
                modelsByPath.Clear();
                ReleaseBatchSceneMemory();
            }
        }

        private void ReleaseBatchSceneMemory()
        {
            try
            {
                if (renderCtrl?.renderer != null)
                {
                    renderCtrl.renderer.ClearScene();
                }
            }
            catch { }

            try { ClearModelsList(); } catch { }
            try { loadedSceneModelInstances.Clear(); } catch { }
            try { renderCtrl?.Invalidate(); } catch { }

            try
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            catch { }
        }

        private static string BuildSceneExportKey(string scenesRoot, string sceneFile)
        {
            string name = Path.GetFileNameWithoutExtension(sceneFile);
            try
            {
                string rel = Path.GetRelativePath(scenesRoot, sceneFile);
                string noExt = Path.ChangeExtension(rel, null) ?? name;
                char[] chars = noExt.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                {
                    char c = chars[i];
                    if (c == '/' || c == '\\')
                    {
                        chars[i] = '_';
                    }
                    else if (c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                    {
                        chars[i] = '_';
                    }
                }
                string key = new string(chars).Trim('_');
                if (!string.IsNullOrWhiteSpace(key))
                {
                    return key;
                }
            }
            catch { }

            return name;
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            string[] files = Directory.GetFiles(sourceDir);
            for (int i = 0; i < files.Length; i++)
            {
                File.Copy(files[i], Path.Combine(destDir, Path.GetFileName(files[i])), overwrite: true);
            }
            string[] dirs = Directory.GetDirectories(sourceDir);
            for (int i = 0; i < dirs.Length; i++)
            {
                CopyDirectoryRecursive(dirs[i], Path.Combine(destDir, Path.GetFileName(dirs[i])));
            }
        }
    }
}
