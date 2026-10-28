using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.IO;

namespace TrinityModelViewer.Export.SvFbx
{
    /// <summary>
    /// SV Pokemon FBX export without Za Shader materials — GLTF (+ animations) → FBX → Unity Assets.
    /// </summary>
    public static class SvFbxExporter
    {
        public static void ExportModelToUnityPipeline(
            Model model,
            string? exportRootOverride = null,
            string? unityImportRootOverride = null,
            IReadOnlyList<Animation>? animations = null,
            string? modelNameOverride = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            string rawName = !string.IsNullOrWhiteSpace(modelNameOverride)
                ? modelNameOverride
                : (string.IsNullOrWhiteSpace(model.Name) ? "model" : model.Name);
            string modelName = SanitizeDirName(rawName);
            string exportRoot = string.IsNullOrWhiteSpace(exportRootOverride) ? SvFbxExportPaths.ExportRoot : exportRootOverride;
            string unityRoot = string.IsNullOrWhiteSpace(unityImportRootOverride)
                ? SvFbxExportPaths.UnityImportRoot
                : unityImportRootOverride;

            string exportDir = Path.Combine(exportRoot, modelName);
            RecreateDirectory(exportDir);

            string gltfPath = Path.Combine(exportDir, modelName + ".gltf");
            GltfExporter.ExportModel(model, gltfPath, animations);

            GltfToFbxRunner.ConvertDirectory(exportDir, SvFbxExportPaths.GltfToFbxScript);
            CopyToUnityAssets(exportDir, modelName, unityRoot);
        }

        private static void CopyToUnityAssets(string exportDir, string modelName, string unityImportRoot)
        {
            string destRoot = Path.Combine(unityImportRoot, modelName);
            Directory.CreateDirectory(unityImportRoot);
            // Wipe previous Unity folder so leftover textures / meta / old clips cannot linger.
            RecreateDirectory(destRoot);
            CopyDirectory(exportDir, destRoot);
        }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path);
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
