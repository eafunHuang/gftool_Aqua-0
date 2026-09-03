using GFTool.Renderer.Scene.GraphicsObjects;

using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using System.Text;

using System.Threading;

using System.Threading.Tasks;

using System.Windows.Forms;

using TrinityModelViewer.Export.ZaUnity;



namespace TrinityModelViewer

{

    public partial class ModelViewerForm

    {

        private void ExportSelectedModelToUnityZaShader()

        {

            var selected = sceneTree.SelectedNode;

            if (selected?.Tag is not NodeTag tag || tag.Type != NodeType.ModelRoot)

            {

                MessageBox.Show(

                    this,

                    "Select a model root node first (the top-level model entry in the scene tree).",

                    "Export to Unity (Za Shader)",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Information);

                return;

            }



            if (!modelMap.TryGetValue(selected, out var mdl) || mdl == null)

            {

                MessageBox.Show(this, "Model not found.", "Export to Unity (Za Shader)", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return;

            }



            string? trmdlPath = mdl.TrmdlSourcePath;

            string? animRoot = PromptOptionalAnimationFolder();

            if (animRoot == string.Empty)

            {

                return;

            }



            try

            {

                IReadOnlyList<Animation>? animations = null;

                if (!string.IsNullOrWhiteSpace(animRoot) && !string.IsNullOrWhiteSpace(trmdlPath))

                {

                    string? modelsRoot = Path.GetDirectoryName(trmdlPath);

                    if (!string.IsNullOrWhiteSpace(modelsRoot))

                    {

                        animations = ZaUnityAnimationLoader.LoadForTrmdlAsync(

                                trmdlPath,

                                modelsRoot,

                                animRoot,

                                token: CancellationToken.None)

                            .GetAwaiter()

                            .GetResult();

                    }

                }



                string exportRoot = NullIfEmpty(settings.LastZaUnityExportRoot) ?? ZaUnityExportPaths.ExportRoot;

                string unityImportRoot = NullIfEmpty(settings.LastZaUnityImportRoot) ?? ZaUnityExportPaths.UnityImportRoot;

                ZaUnityExporter.ExportModelToUnityPipeline(

                    mdl,

                    exportRootOverride: exportRoot,

                    unityImportRootOverride: unityImportRoot,

                    animations: animations);

                var animInfo = animations is { Count: > 0 }

                    ? $"\nAnimations: {animations.Count}"

                    : string.Empty;

                MessageBox.Show(

                    this,

                    "Za Shader Unity export completed.\n\n" +

                    $"Staging: {exportRoot}\n" +

                    $"Unity Assets: {unityImportRoot}{animInfo}\n\n" +

                    "Open Unity project ZaView. If materials were not auto-applied, use menu:\n" +

                    "Za → Apply IkCharacter Materials (All Imported)",

                    "Export to Unity (Za Shader)",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Information);

            }

            catch (Exception ex)

            {

                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export to Unity (Za Shader)", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }

        }



        private void ExportFolderToUnityZaShaderFromDialog()

        {

            string? modelsFolder = PromptFolder(

                "Select the models folder (e.g. ZA-Poke) containing .trmdl files to export");

            if (string.IsNullOrWhiteSpace(modelsFolder))

            {

                return;

            }



            string? animFolder = PromptOptionalAnimationFolder();

            if (animFolder == string.Empty)

            {

                return;

            }



            if (!TryPromptZaUnityExportPaths(out string exportRoot, out string unityImportRoot))

            {

                return;

            }



            _ = BatchExportFolderToUnityAsync(

                modelsFolder,

                recursive: true,

                exitAfterComplete: false,

                animRoot: animFolder,

                exportRoot: exportRoot,

                unityImportRoot: unityImportRoot);

        }



        private async Task RunStartupUnityBatchExportIfRequestedAsync()

        {

            if (string.IsNullOrWhiteSpace(startupExportUnityFolder))

            {

                return;

            }



            await BatchExportFolderToUnityAsync(

                startupExportUnityFolder,

                startupExportUnityRecursive,

                exitAfterComplete: true,

                animRoot: startupExportUnityAnimFolder,

                exportRoot: NullIfEmpty(settings.LastZaUnityExportRoot),

                unityImportRoot: NullIfEmpty(settings.LastZaUnityImportRoot));

        }



        private async Task BatchExportFolderToUnityAsync(

            string folderPath,

            bool recursive,

            bool exitAfterComplete,

            string? animRoot = null,

            string? exportRoot = null,

            string? unityImportRoot = null)

        {

            if (string.IsNullOrWhiteSpace(folderPath))

            {

                return;

            }



            var files = ZaUnityBatchExporter.EnumerateTrmdlFiles(folderPath, recursive);

            if (files.Count == 0)

            {

                var msg = $"No .trmdl files found under:\n{folderPath}";

                if (exitAfterComplete)

                {

                    Console.Error.WriteLine(msg);

                    Close();

                }

                else

                {

                    MessageBox.Show(this, msg, "Batch Export to Unity (Za Shader)", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                }



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

                    string label = $"Exporting ({i + 1}/{files.Count}): {Path.GetFileName(trmdlPath)}";

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

                                animations = await ZaUnityAnimationLoader.LoadForTrmdlAsync(

                                    trmdlPath,

                                    folderPath,

                                    animRoot,

                                    token: token);

                                if (animations.Count > 0)

                                {

                                    result.ModelsWithAnimations++;

                                    result.TotalAnimationsLoaded += animations.Count;

                                }

                            }



                            ZaUnityExporter.ExportModelToUnityPipeline(

                                mdl,

                                exportRootOverride: exportRoot,

                                unityImportRootOverride: unityImportRoot,

                                animations: animations);

                            result.Succeeded++;

                            string exportLabel = animations is { Count: > 0 }

                                ? $"{mdl.Name} ({animations.Count} anims)"

                                : mdl.Name;

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

                ShowBatchExportSummary(

                    folderPath,

                    recursive,

                    animRoot,

                    result,

                    exitAfterComplete,

                    exportRoot,

                    unityImportRoot);

            }

            finally

            {

                EndModelLoad();

            }

        }



        private static string? NullIfEmpty(string? value)

        {

            return string.IsNullOrWhiteSpace(value) ? null : value;

        }



        private bool TryPromptZaUnityExportPaths(out string exportRoot, out string unityImportRoot)

        {

            string lastUnity = NullIfEmpty(settings.LastZaUnityImportRoot) ?? ZaUnityExportPaths.UnityImportRoot;

            string? unity = PromptFolder(

                "Select Unity Assets destination (e.g. ...\\Assets\\Imported)",

                lastUnity,

                allowNewFolder: true);

            if (string.IsNullOrWhiteSpace(unity))

            {

                exportRoot = string.Empty;

                unityImportRoot = string.Empty;

                return false;

            }



            string lastStaging = NullIfEmpty(settings.LastZaUnityExportRoot) ?? ZaUnityExportPaths.ExportRoot;

            string? staging = PromptFolder(

                "Select staging/temp export folder (GLTF/FBX workspace)",

                lastStaging,

                allowNewFolder: true);

            if (string.IsNullOrWhiteSpace(staging))

            {

                exportRoot = string.Empty;

                unityImportRoot = string.Empty;

                return false;

            }



            settings.LastZaUnityImportRoot = unity;

            settings.LastZaUnityExportRoot = staging;

            settings.Save();



            exportRoot = staging;

            unityImportRoot = unity;

            return true;

        }



        private string? PromptFolder(string description, string? initialPath = null, bool allowNewFolder = false)

        {

            using var dialog = new FolderBrowserDialog

            {

                Description = description,

                UseDescriptionForTitle = true,

                ShowNewFolderButton = allowNewFolder

            };



            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))

            {

                dialog.SelectedPath = initialPath;

            }



            return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)

                ? dialog.SelectedPath

                : null;

        }



        /// <summary>

        /// Returns null when skipped, empty string when cancelled, otherwise the selected path.

        /// </summary>

        private string? PromptOptionalAnimationFolder()

        {

            var choice = MessageBox.Show(

                this,

                "Do you want to include animations from a parallel folder?\n\n" +

                "Example:\n" +

                "  Models: e:\\za10\\ZA-Poke\n" +

                "  Animations: e:\\za10\\ZA-PokeAnim\n\n" +

                "Subfolder structure under the animation root will be matched to each model.",

                "Export to Unity (Za Shader)",

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

                "Select the animations folder (e.g. ZA-PokeAnim) containing .tranm / .gfbanm files");

            if (string.IsNullOrWhiteSpace(animFolder))

            {

                return string.Empty;

            }



            if (!Directory.Exists(animFolder))

            {

                MessageBox.Show(

                    this,

                    $"Animation folder does not exist:\n{animFolder}",

                    "Export to Unity (Za Shader)",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Warning);

                return string.Empty;

            }



            return animFolder;

        }



        private void ShowBatchExportSummary(

            string folderPath,

            bool recursive,

            string? animRoot,

            ZaUnityBatchExportResult result,

            bool exitAfterComplete,

            string? exportRoot,

            string? unityImportRoot)

        {

            string staging = NullIfEmpty(exportRoot) ?? ZaUnityExportPaths.ExportRoot;

            string unity = NullIfEmpty(unityImportRoot) ?? ZaUnityExportPaths.UnityImportRoot;



            var sb = new StringBuilder();

            sb.AppendLine($"Folder: {folderPath}");

            sb.AppendLine($"Recursive: {recursive}");

            if (!string.IsNullOrWhiteSpace(animRoot))

            {

                sb.AppendLine($"Animations: {animRoot}");

                sb.AppendLine($"Models with animations: {result.ModelsWithAnimations}, total anims: {result.TotalAnimationsLoaded}");

            }

            sb.AppendLine($"Total: {result.Total}, OK: {result.Succeeded}, Failed: {result.Failed}");

            sb.AppendLine($"Staging: {staging}");

            sb.AppendLine($"Unity Assets: {unity}");



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



            if (exitAfterComplete)

            {

                Console.WriteLine(sb.ToString());

                if (result.Failed > 0)

                {

                    Environment.ExitCode = 1;

                }



                Close();

                return;

            }



            MessageBox.Show(

                this,

                sb.ToString(),

                "Batch Export to Unity (Za Shader)",

                MessageBoxButtons.OK,

                result.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

        }



        private void SupplementSelectedModelUnityIshokuMaterials()

        {

            var selected = sceneTree.SelectedNode;

            if (selected?.Tag is not NodeTag tag || tag.Type != NodeType.ModelRoot)

            {

                MessageBox.Show(

                    this,

                    "Select a model root node first (the top-level model entry in the scene tree).",

                    "Supplement Unity Ishoku Materials",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Information);

                return;

            }



            if (!modelMap.TryGetValue(selected, out var mdl) || mdl == null)

            {

                MessageBox.Show(this, "Model not found.", "Supplement Unity Ishoku Materials", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return;

            }



            try

            {

                string exportRoot = NullIfEmpty(settings.LastZaUnityExportRoot) ?? ZaUnityExportPaths.ExportRoot;

                string unityImportRoot = NullIfEmpty(settings.LastZaUnityImportRoot) ?? ZaUnityExportPaths.UnityImportRoot;

                ZaUnityExporter.SupplementAlternateMaterials(mdl, exportRoot, unityImportRoot);

                MessageBox.Show(

                    this,

                    "Alternate color material data written to za_materials.json.\n\n" +

                    $"Staging: {exportRoot}\n" +

                    $"Unity Assets: {unityImportRoot}\n\n" +

                    "In Unity, run Za → Apply IkCharacter Materials to create *_rare / *_vN .mat files.",

                    "Supplement Unity Ishoku Materials",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Information);

            }

            catch (Exception ex)

            {

                MessageBox.Show(this, $"Supplement failed:\n{ex.Message}", "Supplement Unity Ishoku Materials", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }

        }



        private void SupplementFolderUnityIshokuMaterialsFromDialog()

        {

            string? modelsFolder = PromptFolder(

                "Select the models folder containing .trmdl files to supplement ishoku materials");

            if (string.IsNullOrWhiteSpace(modelsFolder))

            {

                return;

            }



            _ = BatchSupplementFolderUnityIshokuAsync(modelsFolder, recursive: true);

        }



        private async Task BatchSupplementFolderUnityIshokuAsync(string folderPath, bool recursive)

        {

            if (string.IsNullOrWhiteSpace(folderPath))

            {

                return;

            }



            var files = ZaUnityBatchExporter.EnumerateTrmdlFiles(folderPath, recursive);

            if (files.Count == 0)

            {

                MessageBox.Show(this, $"No .trmdl files found under:\n{folderPath}", "Batch Supplement Unity Ishoku", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return;

            }



            int succeeded = 0;

            int failed = 0;

            var errors = new List<string>();

            string exportRoot = NullIfEmpty(settings.LastZaUnityExportRoot) ?? ZaUnityExportPaths.ExportRoot;

            string unityImportRoot = NullIfEmpty(settings.LastZaUnityImportRoot) ?? ZaUnityExportPaths.UnityImportRoot;

            BeginModelLoad();

            ClearAll();



            try

            {

                for (int i = 0; i < files.Count; i++)

                {

                    string trmdlPath = files[i];

                    loadingForm?.SetMessage($"Supplementing ishoku ({i + 1}/{files.Count}): {Path.GetFileName(trmdlPath)}");

                    ReportModelLoadProgress((int)Math.Round((i / (double)files.Count) * 100.0));



                    Model? mdl = null;

                    try

                    {

                        mdl = await AddModelToSceneAsync(trmdlPath, transient: true);

                        if (mdl == null)

                        {

                            failed++;

                            errors.Add($"{trmdlPath}: load returned null");

                            continue;

                        }



                        ZaUnityExporter.SupplementAlternateMaterials(mdl, exportRoot, unityImportRoot);

                        succeeded++;

                    }

                    catch (Exception ex)

                    {

                        failed++;

                        errors.Add($"{trmdlPath}: {ex.Message}");

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



                ReportModelLoadProgress(100);



                var sb = new StringBuilder();

                sb.AppendLine($"Folder: {folderPath}");

                sb.AppendLine($"Recursive: {recursive}");

                sb.AppendLine($"Total: {files.Count}, OK: {succeeded}, Failed: {failed}");

                sb.AppendLine($"Unity Assets: {unityImportRoot}");



                if (errors.Count > 0)

                {

                    sb.AppendLine();

                    sb.AppendLine("Errors:");

                    foreach (var err in errors.Take(10))

                    {

                        sb.AppendLine($"  - {err}");

                    }



                    if (errors.Count > 10)

                    {

                        sb.AppendLine($"  ... and {errors.Count - 10} more");

                    }

                }



                MessageBox.Show(

                    this,

                    sb.ToString(),

                    "Batch Supplement Unity Ishoku",

                    MessageBoxButtons.OK,

                    failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            }

            finally

            {

                EndModelLoad();

            }

        }

    }

}


