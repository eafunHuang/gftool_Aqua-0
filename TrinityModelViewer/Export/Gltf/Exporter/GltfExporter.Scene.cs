using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrinityModelViewer.Export
{
    internal static partial class GltfExporter
    {
        internal class SceneModelInstance
        {
            public required string Name { get; init; }
            public required string SourcePath { get; init; }
            public required Matrix4 Transform { get; init; }
            public required Model Model { get; init; }
        }

        private sealed class SceneMeshTemplate
        {
            public required string Name { get; init; }
            public required int MeshIndex { get; init; }
            public required bool HasSkinning { get; init; }
        }

        private sealed class SceneModelTemplate
        {
            public required string Name { get; init; }
            public required Armature? Armature { get; init; }
            public required List<SceneMeshTemplate> Meshes { get; init; }
        }

        public static void ExportScene(
            IReadOnlyList<SceneModelInstance> instances,
            string gltfPath,
            string? sceneName = null)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (string.IsNullOrWhiteSpace(gltfPath)) throw new ArgumentException("Missing output path.", nameof(gltfPath));

            int validCount = 0;
            for (int i = 0; i < instances.Count; i++)
            {
                var inst = instances[i];
                if (inst == null || inst.Model == null || string.IsNullOrWhiteSpace(inst.SourcePath))
                {
                    continue;
                }

                validCount++;
            }

            if (validCount == 0)
            {
                throw new InvalidOperationException("Scene has no loaded model instances to export.");
            }

            var outDir = Path.GetDirectoryName(gltfPath) ?? Environment.CurrentDirectory;
            Directory.CreateDirectory(outDir);

            var baseName = Path.GetFileNameWithoutExtension(gltfPath);
            var binName = $"{baseName}.bin";
            var binPath = Path.Combine(outDir, binName);
            var texDir = Path.Combine(outDir, $"{baseName}_textures");
            Directory.CreateDirectory(texDir);

            var buffer = new BinaryBufferBuilder();
            var gltf = new GltfRoot
            {
                Asset = new GltfAsset { Version = "2.0", Generator = "TrinityModelViewer" }
            };

            gltf.Scene = 0;
            var scene = new GltfScene();
            gltf.Scenes.Add(scene);

            int rootNodeIndex = gltf.Nodes.Count;
            gltf.Nodes.Add(new GltfNode
            {
                Name = string.IsNullOrWhiteSpace(sceneName) ? "Scene" : sceneName,
                Children = new List<int>()
            });
            scene.Nodes.Add(rootNodeIndex);

            var gltfMaterialIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var textureCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var usedTextureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var templateCache = new Dictionary<string, SceneModelTemplate>(StringComparer.OrdinalIgnoreCase);

            int templateOrdinal = 0;
            int instanceOrdinal = 0;
            for (int i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                if (instance == null || instance.Model == null || string.IsNullOrWhiteSpace(instance.SourcePath))
                {
                    continue;
                }

                string templateKey = Path.GetFullPath(instance.SourcePath);
                if (!templateCache.TryGetValue(templateKey, out var template))
                {
                    template = BuildSceneModelTemplate(
                        gltf,
                        buffer,
                        textureCache,
                        usedTextureNames,
                        gltfMaterialIndex,
                        texDir,
                        instance.Model,
                        templateKey,
                        templateOrdinal++);

                    templateCache[templateKey] = template;
                }

                if (template.Meshes.Count == 0)
                {
                    continue;
                }

                AddSceneInstanceNodes(gltf, buffer, rootNodeIndex, instance, template, instanceOrdinal++);
            }

            var binBytes = buffer.ToArray();
            File.WriteAllBytes(binPath, binBytes);
            gltf.Buffers.Add(new GltfBuffer { Uri = binName, ByteLength = binBytes.Length });

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(gltf, jsonOptions);
            File.WriteAllText(gltfPath, json);
        }

        private static SceneModelTemplate BuildSceneModelTemplate(
            GltfRoot gltf,
            BinaryBufferBuilder buffer,
            Dictionary<string, int> textureCache,
            HashSet<string> usedTextureNames,
            Dictionary<string, int> gltfMaterialIndex,
            string texDir,
            Model model,
            string templateKey,
            int templateOrdinal)
        {
            var data = model.CreateExportData();

            // Export textures referenced by materials into the scene's texture folder.
            ExportAllTexturesInto(gltf, texDir, data.Materials, textureCache, usedTextureNames);

            string materialPrefix = $"scene_{templateOrdinal:D4}";

            var materialByName = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            var mats = data.Materials;
            for (int i = 0; i < mats.Count; i++)
            {
                var mat = mats[i];
                if (mat == null)
                {
                    continue;
                }

                string originalName = mat.Name ?? string.Empty;
                string prefixedName = PrefixMaterialName(materialPrefix, originalName);
                if (!materialByName.ContainsKey(prefixedName))
                {
                    materialByName[prefixedName] = mat;
                }
            }

            int submeshCount = data.Submeshes.Count;
            var renamedSubmeshes = new List<Model.ExportSubmesh>(submeshCount);
            for (int i = 0; i < submeshCount; i++)
            {
                var sub = data.Submeshes[i];
                string prefixedMaterialName = PrefixMaterialName(materialPrefix, sub.MaterialName);
                renamedSubmeshes.Add(CloneSubmeshWithMaterialName(sub, prefixedMaterialName));
            }

            var groups = new Dictionary<string, List<Model.ExportSubmesh>>(StringComparer.OrdinalIgnoreCase);
            var groupKeys = new List<string>();

            for (int i = 0; i < renamedSubmeshes.Count; i++)
            {
                var s = renamedSubmeshes[i];
                if (s.Positions.Length == 0 || s.Indices.Length == 0)
                {
                    continue;
                }

                string shapeKey = GetShapeName(s.Name);
                if (!groups.TryGetValue(shapeKey, out var list))
                {
                    list = new List<Model.ExportSubmesh>();
                    groups[shapeKey] = list;
                    groupKeys.Add(shapeKey);
                }

                list.Add(s);
            }

            var meshes = new List<SceneMeshTemplate>(groupKeys.Count);
            int shapeIndex = 0;

            for (int g = 0; g < groupKeys.Count; g++)
            {
                var key = groupKeys[g];
                var parts = groups[key];
                if (parts.Count == 0)
                {
                    continue;
                }

                bool anySkinning = false;
                bool anyTangents = false;
                for (int p = 0; p < parts.Count; p++)
                {
                    if (parts[p].HasSkinning) anySkinning = true;
                    if (parts[p].HasTangents) anyTangents = true;
                }

                int canonicalIndex = -1;
                for (int p = 0; p < parts.Count; p++)
                {
                    var s = parts[p];
                    if ((!anySkinning || s.HasSkinning) && (!anyTangents || s.HasTangents))
                    {
                        canonicalIndex = p;
                        break;
                    }
                }

                if (canonicalIndex < 0)
                {
                    for (int p = 0; p < parts.Count; p++)
                    {
                        var s = parts[p];
                        if (!anySkinning || s.HasSkinning)
                        {
                            canonicalIndex = p;
                            break;
                        }
                    }
                }

                if (canonicalIndex < 0)
                {
                    for (int p = 0; p < parts.Count; p++)
                    {
                        var s = parts[p];
                        if (!anyTangents || s.HasTangents)
                        {
                            canonicalIndex = p;
                            break;
                        }
                    }
                }

                if (canonicalIndex < 0)
                {
                    canonicalIndex = 0;
                }

                var canonical = parts[canonicalIndex];
                int meshIndex = AddMeshShape(
                    gltf,
                    buffer,
                    model,
                    canonical,
                    parts,
                    shapeIndex,
                    gltfMaterialIndex,
                    materialByName,
                    textureCache,
                    texDir);

                meshes.Add(new SceneMeshTemplate
                {
                    Name = key,
                    MeshIndex = meshIndex,
                    HasSkinning = anySkinning
                });

                shapeIndex++;
            }

            return new SceneModelTemplate
            {
                Name = Path.GetFileNameWithoutExtension(templateKey),
                Armature = data.Armature,
                Meshes = meshes
            };
        }

        private static void AddSceneInstanceNodes(
            GltfRoot gltf,
            BinaryBufferBuilder buffer,
            int rootNodeIndex,
            SceneModelInstance instance,
            SceneModelTemplate template,
            int instanceOrdinal)
        {
            int instanceRootNodeIndex = gltf.Nodes.Count;
            gltf.Nodes.Add(new GltfNode
            {
                Name = BuildInstanceNodeName(instance.Name, instanceOrdinal),
                Matrix = ToGltfMatrix(instance.Transform),
                Children = new List<int>()
            });
            gltf.Nodes[rootNodeIndex].Children!.Add(instanceRootNodeIndex);

            int? skinIndex = null;
            if (template.Armature != null && template.Armature.Bones.Count > 0)
            {
                (skinIndex, _) = AddSkin(gltf, buffer, template.Armature, instanceRootNodeIndex);
            }

            for (int i = 0; i < template.Meshes.Count; i++)
            {
                var mesh = template.Meshes[i];
                int nodeIndex = gltf.Nodes.Count;
                gltf.Nodes.Add(new GltfNode
                {
                    Name = mesh.Name,
                    Mesh = mesh.MeshIndex,
                    Skin = mesh.HasSkinning ? skinIndex : null
                });
                gltf.Nodes[instanceRootNodeIndex].Children!.Add(nodeIndex);
            }
        }

        private static string BuildInstanceNodeName(string? name, int index)
        {
            string baseName = string.IsNullOrWhiteSpace(name) ? "SceneModel" : name;
            return baseName + "_" + index.ToString("D4");
        }

        private static string PrefixMaterialName(string prefix, string? materialName)
        {
            string m = materialName ?? string.Empty;
            return prefix + ":" + m;
        }

        private static Model.ExportSubmesh CloneSubmeshWithMaterialName(Model.ExportSubmesh source, string materialName)
        {
            return new Model.ExportSubmesh
            {
                Name = source.Name,
                MaterialName = materialName,
                Positions = source.Positions,
                Normals = source.Normals,
                UVs = source.UVs,
                Colors = source.Colors,
                Tangents = source.Tangents,
                Binormals = source.Binormals,
                BlendIndices = source.BlendIndices,
                BlendIndicesRaw = source.BlendIndicesRaw,
                BlendWeights = source.BlendWeights,
                Indices = source.Indices,
                HasVertexColors = source.HasVertexColors,
                HasTangents = source.HasTangents,
                HasBinormals = source.HasBinormals,
                HasSkinning = source.HasSkinning
            };
        }

        private static float[] ToGltfMatrix(Matrix4 transform)
        {
            var m = Matrix4.Transpose(transform);

            // glTF uses column-major matrices; OpenTK uses row-major fields.
            return new[]
            {
                m.M11, m.M21, m.M31, m.M41,
                m.M12, m.M22, m.M32, m.M42,
                m.M13, m.M23, m.M33, m.M43,
                m.M14, m.M24, m.M34, m.M44
            };
        }
    }
}

