using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrinityModelViewer.Export.ZaUnity
{
    public static class ZaUnityExporter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static void ExportModelToUnityPipeline(
            Model model,
            string? exportRootOverride = null,
            IReadOnlyList<Animation>? animations = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            string modelName = SanitizeDirName(string.IsNullOrWhiteSpace(model.Name) ? "model" : model.Name);
            string exportRoot = string.IsNullOrWhiteSpace(exportRootOverride) ? ZaUnityExportPaths.ExportRoot : exportRootOverride;
            string exportDir = Path.Combine(exportRoot, modelName);
            Directory.CreateDirectory(exportDir);

            string gltfPath = Path.Combine(exportDir, modelName + ".gltf");
            GltfExporter.ExportModel(model, gltfPath, animations);

            string texDirName = modelName + "_textures";
            string texDir = Path.Combine(exportDir, texDirName);

            var manifest = BuildManifest(model, modelName, gltfPath, texDirName, texDir);
            string manifestPath = Path.Combine(exportDir, ZaUnityExportPaths.ManifestFileName);
            WriteManifest(manifestPath, manifest);

            GltfToFbxRunner.ConvertDirectory(exportDir, ZaUnityExportPaths.GltfToFbxScript);

            string fbxFileName = modelName + ".fbx";
            manifest.FbxFile = fbxFileName;
            WriteManifest(manifestPath, manifest);

            CopyToUnityAssets(exportDir, modelName);
        }

        public static void SupplementAlternateMaterials(Model model, string? exportRootOverride = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            string modelName = SanitizeDirName(string.IsNullOrWhiteSpace(model.Name) ? "model" : model.Name);
            string exportRoot = string.IsNullOrWhiteSpace(exportRootOverride) ? ZaUnityExportPaths.ExportRoot : exportRootOverride;
            string exportDir = Path.Combine(exportRoot, modelName);
            Directory.CreateDirectory(exportDir);

            string texDirName = modelName + "_textures";
            string texDir = Path.Combine(exportDir, texDirName);
            string manifestPath = Path.Combine(exportDir, ZaUnityExportPaths.ManifestFileName);

            var manifest = TryReadManifest(manifestPath) ?? new ZaUnityExportManifest
            {
                ModelName = modelName,
                TexturesFolder = texDirName
            };

            if (string.IsNullOrWhiteSpace(manifest.TexturesFolder))
            {
                manifest.TexturesFolder = texDirName;
            }

            if (string.IsNullOrWhiteSpace(manifest.FbxFile))
            {
                manifest.FbxFile = modelName + ".fbx";
            }

            var exportedTextureNames = Directory.Exists(texDir)
                ? new HashSet<string>(Directory.GetFiles(texDir, "*.png").Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (manifest.Materials.Count == 0)
            {
                manifest.Materials = ZaUnityMaterialEntryBuilder.BuildAllEntries(model, texDir, exportedTextureNames);
            }

            manifest.Scheme = "ZaShaderUnity/v2";
            manifest.AlternateMaterialSets = ZaUnityColorVariationBuilder.BuildAlternateSets(
                model,
                texDir,
                exportedTextureNames,
                manifest.Materials);

            WriteManifest(manifestPath, manifest);

            string unityDir = Path.Combine(ZaUnityExportPaths.UnityImportRoot, modelName);
            Directory.CreateDirectory(unityDir);
            File.Copy(manifestPath, Path.Combine(unityDir, ZaUnityExportPaths.ManifestFileName), overwrite: true);
        }

        private static ZaUnityExportManifest BuildManifest(Model model, string modelName, string gltfPath, string texDirName, string texDir)
        {
            var exportedTextureNames = Directory.Exists(texDir)
                ? new HashSet<string>(Directory.GetFiles(texDir, "*.png").Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var materials = ZaUnityMaterialEntryBuilder.BuildAllEntries(model, texDir, exportedTextureNames);

            return new ZaUnityExportManifest
            {
                Scheme = "ZaShaderUnity/v2",
                ModelName = modelName,
                GltfFile = Path.GetFileName(gltfPath),
                TexturesFolder = texDirName,
                Materials = materials,
                AlternateMaterialSets = ZaUnityColorVariationBuilder.BuildAlternateSets(
                    model,
                    texDir,
                    exportedTextureNames,
                    materials)
            };
        }

        private static ZaUnityExportManifest? TryReadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ZaUnityExportManifest>(File.ReadAllText(manifestPath), JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteManifest(string manifestPath, ZaUnityExportManifest manifest)
        {
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        }

        private static void RunGltfToFbx(string exportDir)
        {
            if (!File.Exists(ZaUnityExportPaths.GltfToFbxScript))
            {
                throw new FileNotFoundException("gltf2Fbx.py not found.", ZaUnityExportPaths.GltfToFbxScript);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{ZaUnityExportPaths.GltfToFbxScript}\" \"{exportDir}\"",
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
            string destRoot = Path.Combine(ZaUnityExportPaths.UnityImportRoot, modelName);
            Directory.CreateDirectory(ZaUnityExportPaths.UnityImportRoot);
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
