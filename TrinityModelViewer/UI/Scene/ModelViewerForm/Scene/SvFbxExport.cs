using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrinityModelViewer.Export;
using TrinityModelViewer.Export.SvFbx;
using TrinityModelViewer.Export.ZaUnity;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private void ExportSvTeraBuildingsFromDialog()
        {
            string initial = settings.SvExtractedOutRoot?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(initial) || !Directory.Exists(initial))
            {
                initial = @"e:\sv_unpack";
            }

            string? svRoot = PromptFolder(
                "Select SV unpack root (contains field_graphic / effect / world)",
                initial);
            if (string.IsNullOrWhiteSpace(svRoot))
            {
                return;
            }

            _ = BatchExportSvTeraBuildingsAsync(svRoot);
        }

        private async Task BatchExportSvTeraBuildingsAsync(string svRoot)
        {
            if (string.IsNullOrWhiteSpace(svRoot) || !Directory.Exists(svRoot))
            {
                MessageBox.Show(this, $"SV unpack root not found:\n{svRoot}", "Export SV Tera Buildings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var assets = SvTeraAssetCollector.Collect(svRoot);
            if (assets.Files.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No Tera building files found.\nExpected raid jewelry / raid dome / Area Zero crystal under:\n" + svRoot,
                    "Export SV Tera Buildings",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            int copied = 0;
            try
            {
                copied = SvTeraAssetCollector.CopyPreservingRelative(svRoot, assets.Files, SvTeraExportPaths.SourceCopyRoot);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to copy source files:\n{ex.Message}", "Export SV Tera Buildings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var result = new ZaUnityBatchExportResult { Total = assets.TrmdlFiles.Count };
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sceneTrmdlFiles = new List<string>();
            var sceneNodeNames = new List<string>();
            string? sceneGltfPath = null;
            BeginModelLoad();
            ClearAll();

            try
            {
                for (int i = 0; i < assets.TrmdlFiles.Count; i++)
                {
                    string trmdlPath = assets.TrmdlFiles[i];
                    string modelName = SvTeraAssetCollector.UniqueModelName(trmdlPath, usedNames);
                    string label = $"Exporting SV Tera ({i + 1}/{assets.TrmdlFiles.Count}): {modelName}";
                    loadingForm?.SetMessage(label);
                    ReportModelLoadProgress((int)Math.Round((i / (double)Math.Max(assets.TrmdlFiles.Count, 1)) * 100.0));

                    Model? mdl = null;
                    try
                    {
                        mdl = await AddModelToSceneAsync(trmdlPath, transient: true);
                        if (mdl == null)
                        {
                            result.Failed++;
                            result.Errors.Add($"{trmdlPath}: load returned null");
                            continue;
                        }

                        SvFbxExporter.ExportModelToUnityPipeline(
                            mdl,
                            exportRootOverride: SvTeraExportPaths.FbxStagingRoot,
                            unityImportRootOverride: SvTeraExportPaths.UnityImportRoot,
                            modelNameOverride: modelName);
                        result.Succeeded++;
                        result.Exported.Add(modelName);
                        sceneTrmdlFiles.Add(trmdlPath);
                        sceneNodeNames.Add(modelName);
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        result.Errors.Add($"{trmdlPath}: {ex.Message}");
                    }
                    finally
                    {
                        if (mdl != null)
                        {
                            var node = FindModelRootNode(mdl);
                            if (node != null)
                            {
                                RemoveModelNode(node);
                            }
                        }
                    }
                }

                if (sceneTrmdlFiles.Count > 0)
                {
                    string sceneStagingDir = Path.Combine(SvTeraExportPaths.FbxStagingRoot, "_scene");
                    Directory.CreateDirectory(sceneStagingDir);
                    sceneGltfPath = Path.Combine(sceneStagingDir, SvTeraExportPaths.SceneGltfFileName);
                    loadingForm?.SetMessage($"Exporting SV Tera scene glTF ({sceneTrmdlFiles.Count} models)...");
                    ReportModelLoadProgress(0);
                    try
                    {
                        await ExportSvTeraSceneGltfAsync(sceneTrmdlFiles, sceneNodeNames, sceneGltfPath);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Scene glTF export failed: {ex.Message}");
                    }
                }

                ReportModelLoadProgress(100);
                ShowSvTeraBatchExportSummary(svRoot, copied, assets.Files.Count, result, sceneGltfPath);
            }
            finally
            {
                EndModelLoad();
            }
        }

        private async Task ExportSvTeraSceneGltfAsync(
            IReadOnlyList<string> trmdlFiles,
            IReadOnlyList<string> nodeNames,
            string sceneGltfPath)
        {
            int count = trmdlFiles.Count;
            if (nodeNames.Count < count)
            {
                count = nodeNames.Count;
            }

            if (count <= 0)
            {
                throw new InvalidOperationException("No scene models to export.");
            }

            var instances = new List<GltfExporter.SceneModelInstance>(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    string trmdlPath = trmdlFiles[i];
                    if (string.IsNullOrWhiteSpace(trmdlPath))
                    {
                        continue;
                    }

                    string nodeName = nodeNames[i];
                    if (string.IsNullOrWhiteSpace(nodeName))
                    {
                        nodeName = Path.GetFileNameWithoutExtension(trmdlPath);
                    }

                    loadingForm?.SetMessage($"Scene glTF loading ({i + 1}/{count}): {nodeName}");
                    ReportModelLoadProgress((int)Math.Round((i / (double)Math.Max(count, 1)) * 100.0));

                    Model? mdl = null;
                    try
                    {
                        mdl = await AddModelToSceneAsync(trmdlPath, transient: true);
                    }
                    catch
                    {
                        // ignore per-model failure for scene export
                        mdl = null;
                    }

                    if (mdl == null)
                    {
                        continue;
                    }

                    instances.Add(new GltfExporter.SceneModelInstance
                    {
                        Name = nodeName,
                        SourcePath = trmdlPath,
                        Transform = mdl.GetModelMatrix(),
                        Model = mdl
                    });
                }

                if (instances.Count == 0)
                {
                    throw new InvalidOperationException("Scene glTF export has no valid loaded models.");
                }

                loadingForm?.SetMessage("Writing scene glTF...");
                GltfExporter.ExportScene(instances, sceneGltfPath, sceneName: "SV Tera Buildings");

                // Convert scene glTF → FBX
                string? sceneDir = Path.GetDirectoryName(sceneGltfPath);
                if (!string.IsNullOrWhiteSpace(sceneDir))
                {
                    loadingForm?.SetMessage("Converting scene glTF to FBX...");
                    GltfToFbxRunner.ConvertDirectory(sceneDir, SvFbxExportPaths.GltfToFbxScript);

                    // Copy scene FBX + textures to Unity Assets
                    string sceneFbxName = Path.GetFileNameWithoutExtension(SvTeraExportPaths.SceneGltfFileName);
                    string unitySceneDir = Path.Combine(SvTeraExportPaths.UnityImportRoot, sceneFbxName);
                    Directory.CreateDirectory(unitySceneDir);
                    CopySceneOutputToUnity(sceneDir, sceneFbxName, unitySceneDir);
                }
            }
            finally
            {
                // Release models loaded just for scene glTF generation.
                for (int i = 0; i < instances.Count; i++)
                {
                    var mdl = instances[i].Model;
                    var node = FindModelRootNode(mdl);
                    if (node != null)
                    {
                        RemoveModelNode(node);
                    }
                }
            }
        }

        private static void CopySceneOutputToUnity(string sourceDir, string sceneFbxName, string destDir)
        {
            // Copy .fbx files and texture folder produced by gltf2Fbx conversion.
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string ext = Path.GetExtension(file);
                if (string.Equals(ext, ".fbx", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
                }
            }

            // Copy textures subfolder
            string texSrcDir = Path.Combine(sourceDir, sceneFbxName + "_textures");
            if (Directory.Exists(texSrcDir))
            {
                string texDestDir = Path.Combine(destDir, sceneFbxName + "_textures");
                Directory.CreateDirectory(texDestDir);
                foreach (string file in Directory.GetFiles(texSrcDir))
                {
                    File.Copy(file, Path.Combine(texDestDir, Path.GetFileName(file)), overwrite: true);
                }
            }
        }

        private void ShowSvTeraBatchExportSummary(
            string svRoot,
            int copied,
            int totalFiles,
            ZaUnityBatchExportResult result,
            string? sceneGltfPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"SV root: {svRoot}");
            sb.AppendLine($"Source files: {copied}/{totalFiles}");
            sb.AppendLine($"Source copy: {SvTeraExportPaths.SourceCopyRoot}");
            sb.AppendLine($"Models: Total={result.Total}, OK={result.Succeeded}, Failed={result.Failed}");
            sb.AppendLine($"FBX staging: {SvTeraExportPaths.FbxStagingRoot}");
            sb.AppendLine($"Unity Assets: {SvTeraExportPaths.UnityImportRoot}");
            sb.AppendLine("Mode: FBX only (no Za Shader) + scene glTF (models) + original scenes/textures/particles");

            if (!string.IsNullOrWhiteSpace(sceneGltfPath))
            {
                sb.AppendLine($"Scene glTF: {sceneGltfPath}");
                string sceneFbx = Path.ChangeExtension(sceneGltfPath, ".fbx");
                if (File.Exists(sceneFbx))
                {
                    sb.AppendLine($"Scene FBX: {sceneFbx}");
                    string sceneFbxName = Path.GetFileNameWithoutExtension(SvTeraExportPaths.SceneGltfFileName);
                    sb.AppendLine($"Scene FBX (Unity): {Path.Combine(SvTeraExportPaths.UnityImportRoot, sceneFbxName)}");
                }
            }

            if (result.Exported.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Exported models:");
                int show = result.Exported.Count < 20 ? result.Exported.Count : 20;
                for (int i = 0; i < show; i++)
                {
                    sb.AppendLine($"  - {result.Exported[i]}");
                }

                if (result.Exported.Count > 20)
                {
                    sb.AppendLine($"  ... and {result.Exported.Count - 20} more");
                }
            }

            if (result.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Errors:");
                int showErr = result.Errors.Count < 15 ? result.Errors.Count : 15;
                for (int i = 0; i < showErr; i++)
                {
                    sb.AppendLine($"  - {result.Errors[i]}");
                }

                if (result.Errors.Count > 15)
                {
                    sb.AppendLine($"  ... and {result.Errors.Count - 15} more");
                }
            }

            MessageBox.Show(
                this,
                sb.ToString(),
                "Export SV Tera Buildings",
                MessageBoxButtons.OK,
                result.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void ExportFolderToUnitySvFbxFromDialog()
        {
            string? modelsFolder = PromptFolder(
                "Select the SV Pokemon models folder containing .trmdl files");
            if (string.IsNullOrWhiteSpace(modelsFolder))
            {
                return;
            }

            string? animFolder = PromptFolder(
                "Select the SV Pokemon animations folder containing .tranm / .gfbanm clips");
            if (string.IsNullOrWhiteSpace(animFolder))
            {
                return;
            }

            if (!Directory.Exists(animFolder))
            {
                MessageBox.Show(
                    this,
                    $"Animation folder does not exist:\n{animFolder}",
                    "Batch Export SV Pokemon to FBX",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _ = BatchExportFolderToSvFbxAsync(modelsFolder, recursive: true, animRoot: animFolder);
        }

        private void ExportSelectedModelToUnitySvFbx()
        {
            var selected = sceneTree.SelectedNode;
            if (selected?.Tag is not NodeTag tag || tag.Type != NodeType.ModelRoot)
            {
                MessageBox.Show(
                    this,
                    "Select a model root node first (the top-level model entry in the scene tree).",
                    "Export SV Pokemon to FBX",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!modelMap.TryGetValue(selected, out var mdl) || mdl == null)
            {
                MessageBox.Show(this, "Model not found.", "Export SV Pokemon to FBX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string? trmdlPath = mdl.TrmdlSourcePath;
            string? animRoot = PromptOptionalAnimationFolderForSvFbx();
            if (animRoot == string.Empty)
            {
                return;
            }

            try
            {
                IReadOnlyList<Animation>? animations = null;
                if (!string.IsNullOrWhiteSpace(animRoot) && !string.IsNullOrWhiteSpace(trmdlPath))
                {
                    // Prefer models folder root if user previously picked one; else walk up from trmdl.
                    string modelsRoot = Path.GetDirectoryName(trmdlPath) ?? string.Empty;
                    animations = SvFbxAnimationLoader.LoadForTrmdlAsync(
                            trmdlPath,
                            modelsRoot,
                            animRoot,
                            token: CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                SvFbxExporter.ExportModelToUnityPipeline(mdl, animations: animations);
                var animInfo = animations is { Count: > 0 }
                    ? $"\nAnimations: {animations.Count}"
                    : "\nAnimations: 0 (check anim folder matching)";

                MessageBox.Show(
                    this,
                    "SV Pokemon FBX export completed (no Za Shader).\n\n" +
                    $"Staging: {SvFbxExportPaths.ExportRoot}\n" +
                    $"Unity Assets: {SvFbxExportPaths.UnityImportRoot}{animInfo}",
                    "Export SV Pokemon to FBX",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export SV Pokemon to FBX", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Returns null when skipped, empty string when cancelled, otherwise the selected path.
        /// </summary>
        private string? PromptOptionalAnimationFolderForSvFbx()
        {
            var choice = MessageBox.Show(
                this,
                "Do you want to include animations from a parallel folder?\n\n" +
                "Example:\n" +
                "  Models: ...\\pokemon\\pm0001_00\n" +
                "  Animations: matching pm0001_00 / motion_* under the anim root\n\n" +
                "Clips (.tranm / .gfbanm) under matched folders are loaded recursively.",
                "Export SV Pokemon to FBX",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
            {
                return string.Empty;
            }

            if (choice == DialogResult.No)
            {
                return null;
            }

            string? animFolder = PromptFolder(
                "Select the animations folder containing .tranm / .gfbanm clips");
            if (string.IsNullOrWhiteSpace(animFolder))
            {
                return string.Empty;
            }

            if (!Directory.Exists(animFolder))
            {
                MessageBox.Show(
                    this,
                    $"Animation folder does not exist:\n{animFolder}",
                    "Export SV Pokemon to FBX",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return string.Empty;
            }

            return animFolder;
        }

        private async Task BatchExportFolderToSvFbxAsync(string folderPath, bool recursive, string? animRoot)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            var files = ZaUnityBatchExporter.EnumerateTrmdlFiles(folderPath, recursive);
            if (files.Count == 0)
            {
                MessageBox.Show(
                    this,
                    $"No .trmdl files found under:\n{folderPath}",
                    "Batch Export SV Pokemon to FBX",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var result = new ZaUnityBatchExportResult { Total = files.Count };
            BeginModelLoad();
            ClearAll();

            var token = modelLoadCts?.Token ?? CancellationToken.None;

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string trmdlPath = files[i];
                    string label = $"Exporting SV ({i + 1}/{files.Count}): {Path.GetFileName(trmdlPath)}";
                    loadingForm?.SetMessage(label);
                    ReportModelLoadProgress((int)Math.Round((i / (double)files.Count) * 100.0));

                    try
                    {
                        Model? mdl = null;
                        try
                        {
                            mdl = await AddModelToSceneAsync(trmdlPath, transient: true);
                            if (mdl == null)
                            {
                                result.Failed++;
                                result.Errors.Add($"{trmdlPath}: load returned null");
                                continue;
                            }

                            IReadOnlyList<Animation>? animations = null;
                            if (!string.IsNullOrWhiteSpace(animRoot))
                            {
                                loadingForm?.SetMessage($"{label} — loading animations...");
                                animations = await SvFbxAnimationLoader.LoadForTrmdlAsync(
                                    trmdlPath,
                                    folderPath,
                                    animRoot,
                                    token: token);

                                if (animations.Count > 0)
                                {
                                    result.ModelsWithAnimations++;
                                    result.TotalAnimationsLoaded += animations.Count;
                                }
                                else
                                {
                                    var resolved = SvFbxAnimationLoader.ResolveAnimationDirectories(
                                        trmdlPath, folderPath, animRoot);
                                    string hint = resolved.Count == 0
                                        ? "no matching anim folder"
                                        : $"matched [{string.Join("; ", resolved.Select(Path.GetFileName))}] but 0 clips";
                                    result.Errors.Add($"{Path.GetFileName(trmdlPath)}: {hint}");
                                }
                            }

                            SvFbxExporter.ExportModelToUnityPipeline(mdl, animations: animations);
                            result.Succeeded++;
                            string exportLabel = animations is { Count: > 0 }
                                ? $"{mdl.Name} ({animations.Count} anims)"
                                : $"{mdl.Name} (0 anims)";
                            result.Exported.Add(exportLabel);
                        }
                        finally
                        {
                            if (mdl != null)
                            {
                                var node = FindModelRootNode(mdl);
                                if (node != null)
                                {
                                    RemoveModelNode(node);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        result.Errors.Add($"{trmdlPath}: {ex.Message}");
                    }
                }

                ReportModelLoadProgress(100);
                ShowSvFbxBatchExportSummary(folderPath, recursive, animRoot, result);
            }
            finally
            {
                EndModelLoad();
            }
        }

        private void ShowSvFbxBatchExportSummary(
            string folderPath,
            bool recursive,
            string? animRoot,
            ZaUnityBatchExportResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Folder: {folderPath}");
            sb.AppendLine($"Recursive: {recursive}");
            if (!string.IsNullOrWhiteSpace(animRoot))
            {
                sb.AppendLine($"Animations: {animRoot}");
                sb.AppendLine($"Models with animations: {result.ModelsWithAnimations}, total anims: {result.TotalAnimationsLoaded}");
            }

            sb.AppendLine($"Total: {result.Total}, OK: {result.Succeeded}, Failed: {result.Failed}");
            sb.AppendLine($"Staging: {SvFbxExportPaths.ExportRoot}");
            sb.AppendLine($"Unity Assets: {SvFbxExportPaths.UnityImportRoot}");
            sb.AppendLine("Mode: FBX only (no Za Shader) + animation clips");

            if (result.Exported.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Exported models:");
                foreach (var name in result.Exported.Take(20))
                {
                    sb.AppendLine($"  - {name}");
                }

                if (result.Exported.Count > 20)
                {
                    sb.AppendLine($"  ... and {result.Exported.Count - 20} more");
                }
            }

            if (result.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Notes / Errors:");
                foreach (var err in result.Errors.Take(15))
                {
                    sb.AppendLine($"  - {err}");
                }

                if (result.Errors.Count > 15)
                {
                    sb.AppendLine($"  ... and {result.Errors.Count - 15} more");
                }
            }

            MessageBox.Show(
                this,
                sb.ToString(),
                "Batch Export SV Pokemon to FBX",
                MessageBoxButtons.OK,
                result.Failed > 0 || (result.ModelsWithAnimations == 0 && !string.IsNullOrWhiteSpace(animRoot))
                    ? MessageBoxIcon.Warning
                    : MessageBoxIcon.Information);
        }
    }
}
