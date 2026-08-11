using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Diagnostics;
using System.IO;

namespace TrinityModelViewer.Export.BuildingUnity
{
    public static class BuildingUnityExporter
    {
        public static void ExportModelToUnityPipeline(Model model, string? exportRootOverride = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            string modelName = SanitizeDirName(string.IsNullOrWhiteSpace(model.Name) ? "model" : model.Name);
            string exportRoot = string.IsNullOrWhiteSpace(exportRootOverride) ? BuildingUnityExportPaths.ExportRoot : exportRootOverride;
            string exportDir = Path.Combine(exportRoot, modelName);
            Directory.CreateDirectory(exportDir);

            string gltfPath = Path.Combine(exportDir, modelName + ".gltf");
            GltfExporter.ExportModel(model, gltfPath);

            RunGltfToFbx(exportDir);
            CopyToUnityAssets(exportDir, modelName);
        }

        private static void RunGltfToFbx(string exportDir)
        {
            if (!File.Exists(BuildingUnityExportPaths.GltfToFbxScript))
            {
                throw new FileNotFoundException("gltf2Fbx.py not found.", BuildingUnityExportPaths.GltfToFbxScript);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{BuildingUnityExportPaths.GltfToFbxScript}\" \"{exportDir}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start python for gltf2Fbx conversion.");
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"gltf2Fbx failed (exit={process.ExitCode}).\n{stderr}\n{stdout}");
            }
        }

        private static void CopyToUnityAssets(string exportDir, string modelName)
        {
            string destRoot = Path.Combine(BuildingUnityExportPaths.UnityImportRoot, modelName);
            Directory.CreateDirectory(BuildingUnityExportPaths.UnityImportRoot);
            CopyDirectory(exportDir, destRoot);
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(destDir, name), overwrite: true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }

        private static string SanitizeDirName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
