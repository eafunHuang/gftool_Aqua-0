using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrinityModelViewer.Export.ZaUnity
{
    internal static class ZaUnityMaterialEntryBuilder
    {
        internal static readonly string[] FireTextureSlots =
        {
            "BaseColorMap",
            "LayerMaskMap",
            "DisplacementMap"
        };

        internal static readonly string[] FireFloatUniforms =
        {
            "LayerMaskScale1", "LayerMaskScale2", "LayerMaskScale3", "LayerMaskScale4",
            "NumMaterialLayer",
            "DisplacementHeight",
            "DiscardValue",
            "AlphaTestThreshold"
        };

        internal static readonly string[] FireVec4Uniforms =
        {
            "BaseColor",
            "BaseColorLayer1", "BaseColorLayer2", "BaseColorLayer3", "BaseColorLayer4",
            "UVScaleOffset",
            "UVScaleOffset3",
            "ScrollUVSpeed"
        };

        internal static readonly string[] FireBoolUniforms =
        {
            "EnableBaseColorMap",
            "EnableLayerMaskMap",
            "EnableDisplacementMap",
            "EnableAlphaTest",
            "BaseColorMultiply"
        };

        internal static readonly string[] FresnelEffectTextureSlots =
        {
            "BaseColorMap",
            "BaseColorMap1",
            "NormalMap",
            "NormalMap1",
            "AOMap",
            "EmissionColorMap",
            "LocalSpecularProbe"
        };

        internal static readonly string[] FresnelEffectFloatUniforms =
        {
            "LayerMaskScale1",
            "FresnelAlphaMin", "FresnelAlphaMax", "FresnelAngleBias",
            "EmissionIntensity", "EmissionIntensityLayer1",
            "BaseColorMapSaturation",
            "NormalHeight", "NormalHeight1",
            "Metallic", "Roughness",
            "LocalSpecularProbeIntensity",
            "LightWrap", "OcclusionStrength"
        };

        internal static readonly string[] FresnelEffectVec4Uniforms =
        {
            "BaseColor",
            "BaseColorLayer1",
            "EmissionColor",
            "UVScaleOffset",
            "UVScaleOffset1",
            "UVScaleOffsetNormal",
            "ParallaxUVIntensity"
        };

        internal static readonly string[] FresnelEffectBoolUniforms =
        {
            "EnableBaseColorMap",
            "EnableBaseColorMap1",
            "EnableNormalMap",
            "EnableNormalMap1",
            "EnableAOMap",
            "EnableEmissionColorMap",
            "EnableFresnelTexture",
            "EnableLocalIBL"
        };

        internal static readonly string[] IkCharacterTextureSlots =
        {
            "BaseColorMap",
            "LayerMaskMap",
            "NormalMap",
            "AOMap",
            "RoughnessMap",
            "MetallicMap",
            "ColorTableMap",
            "ShadowingColorMap",
            "SpecularMaskMap",
            "HighlightMaskMap",
            "DiscardMaskMap",
            "DisplacementMap"
        };

        internal static readonly string[] IkCharacterFloatUniforms =
        {
            "LayerMaskScale1", "LayerMaskScale2", "LayerMaskScale3", "LayerMaskScale4",
            "NumMaterialLayer",
            "HalfLambertBias", "LightWrap", "ShadowingShift", "ShadowingContrast",
            "OcclusionStrength", "SpecularOffset", "SpecularContrast", "SpecularIntensity",
            "SpecularScale", "ShadowStrength", "AlphaTestThreshold"
        };

        internal static readonly string[] IkCharacterVec4Uniforms =
        {
            "BaseColor",
            "BaseColorLayer1", "BaseColorLayer2", "BaseColorLayer3", "BaseColorLayer4",
            "ShadowingColor",
            "ShadowingColorLayer1", "ShadowingColorLayer2", "ShadowingColorLayer3", "ShadowingColorLayer4",
            "UVScaleOffset", "UVScaleOffsetNormal", "UVScaleOffsetLayerMask"
        };

        internal static readonly string[] IkCharacterBoolUniforms =
        {
            "EnableBaseColorMap", "EnableLayerMaskMap", "EnableNormalMap", "EnableAOMap",
            "EnableRoughnessMap", "EnableMetallicMap", "EnableAlphaTest", "BaseColorMultiply"
        };

        private static readonly HashSet<string> ColorRelatedVector4Names = new(StringComparer.OrdinalIgnoreCase)
        {
            "BaseColor",
            "BaseColorLayer1", "BaseColorLayer2", "BaseColorLayer3", "BaseColorLayer4",
            "ShadowingColor",
            "ShadowingColorLayer1", "ShadowingColorLayer2", "ShadowingColorLayer3", "ShadowingColorLayer4"
        };

        private static readonly HashSet<string> ColorRelatedFloatNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "HalfLambertBias", "ShadowingShift", "ShadowingContrast", "ShadowStrength",
            "SpecularOffset", "SpecularContrast", "SpecularIntensity", "SpecularScale"
        };

        internal static HashSet<string> CollectReferencedMaterialNames(Model model)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in model.GetSubmeshMaterials())
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        internal static IEnumerable<Material> EnumerateExportMaterials(Model model)
        {
            var referenced = CollectReferencedMaterialNames(model);
            var byName = model.GetMaterials()
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name))
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var ordered = new List<Material>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mat in model.GetMaterials())
            {
                if (mat == null || string.IsNullOrWhiteSpace(mat.Name) || !seen.Add(mat.Name))
                {
                    continue;
                }

                if (ShouldExportForUnity(mat, referenced))
                {
                    ordered.Add(mat);
                }
            }

            foreach (var name in referenced.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (byName.TryGetValue(name, out var mat) && seen.Add(name) && ShouldExportForUnity(mat, referenced))
                {
                    ordered.Add(mat);
                }
            }

            return ordered;
        }

        internal static bool ShouldExportForUnity(Material mat, IReadOnlySet<string> referencedMaterialNames)
        {
            if (mat == null || string.IsNullOrWhiteSpace(mat.Name))
            {
                return false;
            }

            if (string.Equals(mat.ShaderName, "IkCharacter", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(mat.ShaderName, "FresnelEffect", StringComparison.OrdinalIgnoreCase))
            {
                return referencedMaterialNames.Contains(mat.Name);
            }

            if (string.Equals(mat.ShaderName, "Fire", StringComparison.OrdinalIgnoreCase))
            {
                return referencedMaterialNames.Contains(mat.Name);
            }

            bool referenced = referencedMaterialNames.Contains(mat.Name);
            if (!referenced)
            {
                return false;
            }

            bool hasBaseColor = mat.Textures.Any(t =>
                string.Equals(t.Name, "BaseColorMap", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(t.SourceFile));

            if (mat.Name.Contains("body_b", StringComparison.OrdinalIgnoreCase))
            {
                return hasBaseColor;
            }

            if (IsFireLikeMaterial(mat))
            {
                return hasBaseColor;
            }

            return hasBaseColor;
        }

        private static bool IsFireLikeMaterial(Material mat)
        {
            if (mat.Name.Contains("fire", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(mat.ShaderName, "Fire", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mat.ShaderName, "Unlit", StringComparison.OrdinalIgnoreCase))
            {
                return mat.Textures.Any(t =>
                    string.Equals(t.Name, "DisplacementMap", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(t.SourceFile));
            }

            return false;
        }

        internal static string ResolveExportShaderKey(Material mat)
        {
            if (string.Equals(mat.ShaderName, "Fire", StringComparison.OrdinalIgnoreCase))
            {
                return "Fire";
            }

            if (string.Equals(mat.ShaderName, "FresnelEffect", StringComparison.OrdinalIgnoreCase))
            {
                return "FresnelEffect";
            }

            if (string.Equals(mat.ShaderName, "IkCharacter", StringComparison.OrdinalIgnoreCase))
            {
                return "IkCharacter";
            }

            if (IsFireLikeMaterial(mat))
            {
                return "Fire";
            }

            return "IkCharacter";
        }

        internal static List<ZaUnityMaterialEntry> BuildAllEntries(
            Model model,
            string texDir,
            HashSet<string> exportedTextureNames)
        {
            var entries = new List<ZaUnityMaterialEntry>();
            foreach (var mat in EnumerateExportMaterials(model))
            {
                mat.RefreshColorTableOverridesFromUniformOverrides();
                var entry = BuildEntry(mat, texDir, exportedTextureNames);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        internal static ZaUnityMaterialEntry? BuildEntry(
            Material mat,
            string texDir,
            HashSet<string> exportedTextureNames)
        {
            if (mat == null || string.IsNullOrWhiteSpace(mat.Name))
            {
                return null;
            }

            string shaderKey = ResolveExportShaderKey(mat);
            if (string.Equals(shaderKey, "Fire", StringComparison.OrdinalIgnoreCase))
            {
                return BuildFireEntry(mat, texDir, exportedTextureNames);
            }

            if (string.Equals(shaderKey, "FresnelEffect", StringComparison.OrdinalIgnoreCase))
            {
                return BuildFresnelEffectEntry(mat, texDir, exportedTextureNames);
            }

            return BuildIkCharacterEntry(mat, texDir, exportedTextureNames);
        }

        private static ZaUnityMaterialEntry? BuildIkCharacterEntry(
            Material mat,
            string texDir,
            HashSet<string> exportedTextureNames)
        {
            if (mat == null || string.IsNullOrWhiteSpace(mat.Name))
            {
                return null;
            }

            var entry = new ZaUnityMaterialEntry
            {
                Name = mat.Name,
                ShaderKey = "IkCharacter"
            };

            foreach (var slot in IkCharacterTextureSlots)
            {
                var tex = mat.Textures.FirstOrDefault(t => string.Equals(t.Name, slot, StringComparison.OrdinalIgnoreCase));
                if (tex == null || string.IsNullOrWhiteSpace(tex.SourceFile))
                {
                    continue;
                }

                string fileName = Path.GetFileName(tex.SourceFile);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                string resolved = ResolveExportedTextureFileName(texDir, fileName, exportedTextureNames);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    continue;
                }

                mat.TryGetTextureWrap(slot, out var wrapU, out var wrapV);
                bool isNormal = string.Equals(slot, "NormalMap", StringComparison.OrdinalIgnoreCase);
                if (!isNormal && string.Equals(wrapU, "Clamp", StringComparison.OrdinalIgnoreCase))
                {
                    wrapU = "Mirror";
                }

                string uvUniform = mat.GetUvScaleOffsetUniformForSlot(slot);
                if (!mat.TryGetEffectiveVector4(uvUniform, out var uvScaleOffset))
                {
                    uvScaleOffset = new Vector4(1, 1, 0, 0);
                }

                bool useShaderMirror = !isNormal &&
                                       !string.Equals(wrapU, "Mirror", StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(wrapU, "MirrorOnce", StringComparison.OrdinalIgnoreCase);

                entry.Textures.Add(new ZaUnityNamedTexture
                {
                    Slot = slot,
                    File = resolved,
                    WrapU = wrapU,
                    WrapV = wrapV,
                    TilingX = uvScaleOffset.X,
                    TilingY = uvScaleOffset.Y,
                    OffsetX = uvScaleOffset.Z,
                    OffsetY = uvScaleOffset.W,
                    HalfTextureMirrorU = useShaderMirror
                });
            }

            var exportedSlots = new HashSet<string>(entry.Textures.Select(t => t.Slot), StringComparer.OrdinalIgnoreCase);

            foreach (var uniform in IkCharacterFloatUniforms)
            {
                if (mat.TryGetEffectiveFloat(uniform, out var value))
                {
                    entry.Floats.Add(new ZaUnityNamedFloat { Name = uniform, Value = value });
                }
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "NumMaterialLayer", StringComparison.OrdinalIgnoreCase)) &&
                mat.TryGetIkCharacterTextureEnable("EnableLayerMaskMap", exportedSlots))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "NumMaterialLayer", Value = 4f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "LightWrap", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "LightWrap", Value = 0.5f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "SpecularScale", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "SpecularScale", Value = 0.45f });
            }

            foreach (var uniform in IkCharacterVec4Uniforms)
            {
                if (mat.TryGetEffectiveVector4(uniform, out var value))
                {
                    entry.Vector4s.Add(new ZaUnityNamedVector4
                    {
                        Name = uniform,
                        X = value.X,
                        Y = value.Y,
                        Z = value.Z,
                        W = value.W
                    });
                }
            }

            foreach (var uniform in IkCharacterBoolUniforms)
            {
                entry.Bools.Add(new ZaUnityNamedBool
                {
                    Name = uniform,
                    Value = mat.TryGetIkCharacterTextureEnable(uniform, exportedSlots)
                });
            }

            bool materialShaderMirror = entry.Textures.Any(t =>
                t != null &&
                !string.Equals(t.Slot, "NormalMap", StringComparison.OrdinalIgnoreCase) &&
                t.HalfTextureMirrorU);
            entry.Bools.Add(new ZaUnityNamedBool { Name = "HalfTextureMirrorU", Value = materialShaderMirror });

            return entry;
        }

        private static ZaUnityMaterialEntry? BuildFresnelEffectEntry(
            Material mat,
            string texDir,
            HashSet<string> exportedTextureNames)
        {
            if (mat == null || string.IsNullOrWhiteSpace(mat.Name))
            {
                return null;
            }

            var entry = new ZaUnityMaterialEntry
            {
                Name = mat.Name,
                ShaderKey = "FresnelEffect"
            };

            foreach (var slot in FresnelEffectTextureSlots)
            {
                var tex = mat.Textures.FirstOrDefault(t => string.Equals(t.Name, slot, StringComparison.OrdinalIgnoreCase));
                if (tex == null || string.IsNullOrWhiteSpace(tex.SourceFile))
                {
                    continue;
                }

                string fileName = Path.GetFileName(tex.SourceFile);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                string resolved = ResolveExportedTextureFileName(texDir, fileName, exportedTextureNames);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    continue;
                }

                mat.TryGetTextureWrap(slot, out var wrapU, out var wrapV);
                bool isNormal = string.Equals(slot, "NormalMap", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(slot, "NormalMap1", StringComparison.OrdinalIgnoreCase);
                if (!isNormal && string.Equals(wrapU, "Clamp", StringComparison.OrdinalIgnoreCase))
                {
                    wrapU = "Mirror";
                }

                string uvUniform = mat.GetUvScaleOffsetUniformForSlot(slot);
                if (!mat.TryGetEffectiveVector4(uvUniform, out var uvScaleOffset))
                {
                    uvScaleOffset = new Vector4(1, 1, 0, 0);
                }

                bool useShaderMirror = !isNormal &&
                                       !string.Equals(wrapU, "Mirror", StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(wrapU, "MirrorOnce", StringComparison.OrdinalIgnoreCase);

                entry.Textures.Add(new ZaUnityNamedTexture
                {
                    Slot = slot,
                    File = resolved,
                    WrapU = wrapU,
                    WrapV = wrapV,
                    TilingX = uvScaleOffset.X,
                    TilingY = uvScaleOffset.Y,
                    OffsetX = uvScaleOffset.Z,
                    OffsetY = uvScaleOffset.W,
                    HalfTextureMirrorU = useShaderMirror
                });
            }

            var exportedSlots = new HashSet<string>(entry.Textures.Select(t => t.Slot), StringComparer.OrdinalIgnoreCase);

            foreach (var uniform in FresnelEffectFloatUniforms)
            {
                if (mat.TryGetEffectiveFloat(uniform, out var value))
                {
                    entry.Floats.Add(new ZaUnityNamedFloat { Name = uniform, Value = value });
                }
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "LightWrap", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "LightWrap", Value = 0.5f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "EmissionIntensityLayer1", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "EmissionIntensityLayer1", Value = 2f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "EmissionIntensity", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "EmissionIntensity", Value = 0.65f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "FresnelAlphaMin", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "FresnelAlphaMin", Value = 0.8f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "FresnelAlphaMax", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "FresnelAlphaMax", Value = 0f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "FresnelAngleBias", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "FresnelAngleBias", Value = 0.5f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "Metallic", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "Metallic", Value = 0.3f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "Roughness", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "Roughness", Value = 0.35f });
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "LocalSpecularProbeIntensity", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "LocalSpecularProbeIntensity", Value = 0.8f });
            }

            foreach (var uniform in FresnelEffectVec4Uniforms)
            {
                if (mat.TryGetEffectiveVector4(uniform, out var value))
                {
                    entry.Vector4s.Add(new ZaUnityNamedVector4
                    {
                        Name = uniform,
                        X = value.X,
                        Y = value.Y,
                        Z = value.Z,
                        W = value.W
                    });
                }
            }

            foreach (var uniform in FresnelEffectBoolUniforms)
            {
                entry.Bools.Add(new ZaUnityNamedBool
                {
                    Name = uniform,
                    Value = mat.TryGetIkCharacterTextureEnable(uniform, exportedSlots)
                });
            }

            if (!entry.Bools.Any(b => string.Equals(b.Name, "EnableFresnelTexture", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Bools.Add(new ZaUnityNamedBool
                {
                    Name = "EnableFresnelTexture",
                    Value = exportedSlots.Contains("BaseColorMap1")
                });
            }

            if (!entry.Bools.Any(b => string.Equals(b.Name, "EnableLocalIBL", StringComparison.OrdinalIgnoreCase)))
            {
                entry.Bools.Add(new ZaUnityNamedBool
                {
                    Name = "EnableLocalIBL",
                    Value = exportedSlots.Contains("LocalSpecularProbe")
                });
            }

            bool materialShaderMirror = entry.Textures.Any(t =>
                t != null &&
                !string.Equals(t.Slot, "NormalMap", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(t.Slot, "NormalMap1", StringComparison.OrdinalIgnoreCase) &&
                t.HalfTextureMirrorU);
            entry.Bools.Add(new ZaUnityNamedBool { Name = "HalfTextureMirrorU", Value = materialShaderMirror });

            return entry;
        }

        private static ZaUnityMaterialEntry? BuildFireEntry(
            Material mat,
            string texDir,
            HashSet<string> exportedTextureNames)
        {
            if (mat == null || string.IsNullOrWhiteSpace(mat.Name))
            {
                return null;
            }

            var entry = new ZaUnityMaterialEntry
            {
                Name = mat.Name,
                ShaderKey = "Fire"
            };

            foreach (var slot in FireTextureSlots)
            {
                var tex = mat.Textures.FirstOrDefault(t => string.Equals(t.Name, slot, StringComparison.OrdinalIgnoreCase));
                if (tex == null || string.IsNullOrWhiteSpace(tex.SourceFile))
                {
                    continue;
                }

                string fileName = Path.GetFileName(tex.SourceFile);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                string resolved = ResolveExportedTextureFileName(texDir, fileName, exportedTextureNames);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    continue;
                }

                mat.TryGetTextureWrap(slot, out var wrapU, out var wrapV);
                if (string.Equals(wrapU, "Clamp", StringComparison.OrdinalIgnoreCase))
                {
                    wrapU = "Mirror";
                }

                string uvUniform = string.Equals(slot, "DisplacementMap", StringComparison.OrdinalIgnoreCase)
                    ? "UVScaleOffset3"
                    : mat.GetUvScaleOffsetUniformForSlot(slot);
                if (!mat.TryGetEffectiveVector4(uvUniform, out var uvScaleOffset))
                {
                    uvScaleOffset = new Vector4(1, 1, 0, 0);
                }

                bool useShaderMirror = !string.Equals(wrapU, "Mirror", StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(wrapU, "MirrorOnce", StringComparison.OrdinalIgnoreCase);

                entry.Textures.Add(new ZaUnityNamedTexture
                {
                    Slot = slot,
                    File = resolved,
                    WrapU = wrapU,
                    WrapV = wrapV,
                    TilingX = uvScaleOffset.X,
                    TilingY = uvScaleOffset.Y,
                    OffsetX = uvScaleOffset.Z,
                    OffsetY = uvScaleOffset.W,
                    HalfTextureMirrorU = useShaderMirror
                });
            }

            var exportedSlots = new HashSet<string>(entry.Textures.Select(t => t.Slot), StringComparer.OrdinalIgnoreCase);

            foreach (var uniform in FireFloatUniforms)
            {
                if (mat.TryGetEffectiveFloat(uniform, out var value))
                {
                    entry.Floats.Add(new ZaUnityNamedFloat { Name = uniform, Value = value });
                }
            }

            if (!entry.Floats.Any(f => string.Equals(f.Name, "NumMaterialLayer", StringComparison.OrdinalIgnoreCase)) &&
                exportedSlots.Contains("LayerMaskMap"))
            {
                entry.Floats.Add(new ZaUnityNamedFloat { Name = "NumMaterialLayer", Value = 4f });
            }

            foreach (var uniform in FireVec4Uniforms)
            {
                if (mat.TryGetEffectiveVector4(uniform, out var value))
                {
                    entry.Vector4s.Add(new ZaUnityNamedVector4
                    {
                        Name = uniform,
                        X = value.X,
                        Y = value.Y,
                        Z = value.Z,
                        W = value.W
                    });
                }
            }

            if (!entry.Vector4s.Any(v => string.Equals(v.Name, "ScrollUVSpeed", StringComparison.OrdinalIgnoreCase)) &&
                exportedSlots.Contains("DisplacementMap"))
            {
                entry.Vector4s.Add(new ZaUnityNamedVector4
                {
                    Name = "ScrollUVSpeed",
                    X = 0f,
                    Y = 0.15f,
                    Z = 0f,
                    W = 0f
                });
            }

            foreach (var uniform in FireBoolUniforms)
            {
                bool defaultWhenPresent = !string.Equals(uniform, "EnableAlphaTest", StringComparison.OrdinalIgnoreCase);
                entry.Bools.Add(new ZaUnityNamedBool
                {
                    Name = uniform,
                    Value = mat.TryGetIkCharacterTextureEnable(uniform, exportedSlots, defaultWhenPresent)
                });
            }

            bool materialShaderMirror = entry.Textures.Any(t => t != null && t.HalfTextureMirrorU);
            entry.Bools.Add(new ZaUnityNamedBool { Name = "HalfTextureMirrorU", Value = materialShaderMirror });

            return entry;
        }

        internal static List<ZaUnityMaterialEntry> BuildColorChangedEntries(
            IReadOnlyList<ZaUnityMaterialEntry> baseline,
            IReadOnlyList<ZaUnityMaterialEntry> current)
        {
            var baselineByName = baseline
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name))
                .ToDictionary(e => e.Name, e => e, StringComparer.OrdinalIgnoreCase);

            var changed = new List<ZaUnityMaterialEntry>();
            foreach (var entry in current)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                if (!baselineByName.TryGetValue(entry.Name, out var baseEntry))
                {
                    changed.Add(CloneEntry(entry));
                    continue;
                }

                if (HasColorDifference(baseEntry, entry))
                {
                    changed.Add(CloneEntry(entry));
                }
            }

            return changed;
        }

        private static bool HasColorDifference(ZaUnityMaterialEntry baseline, ZaUnityMaterialEntry current)
        {
            foreach (var v in current.Vector4s)
            {
                if (v == null || !ColorRelatedVector4Names.Contains(v.Name))
                {
                    continue;
                }

                var baseV = baseline.Vector4s.FirstOrDefault(b =>
                    string.Equals(b?.Name, v.Name, StringComparison.OrdinalIgnoreCase));
                if (baseV == null ||
                    MathF.Abs(baseV.X - v.X) > 0.0001f ||
                    MathF.Abs(baseV.Y - v.Y) > 0.0001f ||
                    MathF.Abs(baseV.Z - v.Z) > 0.0001f ||
                    MathF.Abs(baseV.W - v.W) > 0.0001f)
                {
                    return true;
                }
            }

            foreach (var f in current.Floats)
            {
                if (f == null || !ColorRelatedFloatNames.Contains(f.Name))
                {
                    continue;
                }

                var baseF = baseline.Floats.FirstOrDefault(b =>
                    string.Equals(b?.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                if (baseF == null || MathF.Abs(baseF.Value - f.Value) > 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private static ZaUnityMaterialEntry CloneEntry(ZaUnityMaterialEntry source)
        {
            return new ZaUnityMaterialEntry
            {
                Name = source.Name,
                ShaderKey = source.ShaderKey,
                Textures = source.Textures.Select(t => new ZaUnityNamedTexture
                {
                    Slot = t.Slot,
                    File = t.File,
                    WrapU = t.WrapU,
                    WrapV = t.WrapV,
                    TilingX = t.TilingX,
                    TilingY = t.TilingY,
                    OffsetX = t.OffsetX,
                    OffsetY = t.OffsetY,
                    HalfTextureMirrorU = t.HalfTextureMirrorU
                }).ToList(),
                Floats = source.Floats.Select(f => new ZaUnityNamedFloat { Name = f.Name, Value = f.Value }).ToList(),
                Vector4s = source.Vector4s.Select(v => new ZaUnityNamedVector4
                {
                    Name = v.Name,
                    X = v.X,
                    Y = v.Y,
                    Z = v.Z,
                    W = v.W
                }).ToList(),
                Bools = source.Bools.Select(b => new ZaUnityNamedBool { Name = b.Name, Value = b.Value }).ToList()
            };
        }

        private static string ResolveExportedTextureFileName(string texDir, string preferredFileName, HashSet<string> exportedTextureNames)
        {
            if (exportedTextureNames.Contains(preferredFileName))
            {
                return preferredFileName;
            }

            string stem = Path.GetFileNameWithoutExtension(preferredFileName);
            var match = exportedTextureNames.FirstOrDefault(n =>
                n.StartsWith(stem, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(n).StartsWith(stem, StringComparison.OrdinalIgnoreCase));
            return match ?? preferredFileName;
        }
    }
}
