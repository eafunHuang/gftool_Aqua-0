using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrinityModelViewer.Export.SvFbx;
using TrinityModelViewer.Export.ZaUnity;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
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
