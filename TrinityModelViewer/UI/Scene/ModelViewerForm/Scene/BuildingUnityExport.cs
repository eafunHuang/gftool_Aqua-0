using GFTool.Renderer.Scene.GraphicsObjects;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using TrinityModelViewer.Export.BuildingUnity;
using TrinityModelViewer.Export.ZaUnity;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private void ExportSelectedModelToUnityBuilding()
        {
            var selected = sceneTree.SelectedNode;
            if (selected?.Tag is not NodeTag tag || tag.Type != NodeType.ModelRoot)
            {
                MessageBox.Show(
                    this,
                    "Select a model root node first (the top-level model entry in the scene tree).",
                    "Export to Unity (Building)",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!modelMap.TryGetValue(selected, out var mdl) || mdl == null)
            {
                MessageBox.Show(this, "Model not found.", "Export to Unity (Building)", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                BuildingUnityExporter.ExportModelToUnityPipeline(mdl);
                MessageBox.Show(
                    this,
                    "Building Unity export completed.\n\n" +
                    $"Staging: {BuildingUnityExportPaths.ExportRoot}\n" +
                    $"Unity Assets: {BuildingUnityExportPaths.UnityImportRoot}\n\n" +
                    "GLTF was converted to FBX via gltf2Fbx.py.",
                    "Export to Unity (Building)",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export to Unity (Building)", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportFolderToUnityBuildingFromDialog()
        {
            string? modelsFolder = PromptFolder(
                "Select the models folder containing .trmdl building models to export");
            if (string.IsNullOrWhiteSpace(modelsFolder))
            {
                return;
            }

            _ = BatchExportFolderToUnityBuildingAsync(modelsFolder, recursive: true);
        }

        private async Task BatchExportFolderToUnityBuildingAsync(string folderPath, bool recursive)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            var files = ZaUnityBatchExporter.EnumerateTrmdlFiles(folderPath, recursive);
            if (files.Count == 0)
            {
                MessageBox.Show(this, $"No .trmdl files found under:\n{folderPath}", "Batch Export to Unity (Building)", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = new ZaUnityBatchExportResult { Total = files.Count };
            BeginModelLoad();
            ClearAll();

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string trmdlPath = files[i];
                    string label = $"Exporting building ({i + 1}/{files.Count}): {Path.GetFileName(trmdlPath)}";
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

                            BuildingUnityExporter.ExportModelToUnityPipeline(mdl);
                            result.Succeeded++;
                            result.Exported.Add(mdl.Name);
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
                ShowBuildingBatchExportSummary(folderPath, recursive, result);
            }
            finally
            {
                EndModelLoad();
            }
        }

        private void ShowBuildingBatchExportSummary(string folderPath, bool recursive, ZaUnityBatchExportResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Folder: {folderPath}");
            sb.AppendLine($"Recursive: {recursive}");
            sb.AppendLine($"Total: {result.Total}, OK: {result.Succeeded}, Failed: {result.Failed}");
            sb.AppendLine($"Staging: {BuildingUnityExportPaths.ExportRoot}");
            sb.AppendLine($"Unity Assets: {BuildingUnityExportPaths.UnityImportRoot}");

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
                sb.AppendLine("Errors:");
                foreach (var err in result.Errors.Take(10))
                {
                    sb.AppendLine($"  - {err}");
                }

                if (result.Errors.Count > 10)
                {
                    sb.AppendLine($"  ... and {result.Errors.Count - 10} more");
                }
            }

            MessageBox.Show(
                this,
                sb.ToString(),
                "Batch Export to Unity (Building)",
                MessageBoxButtons.OK,
                result.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
    }
}
