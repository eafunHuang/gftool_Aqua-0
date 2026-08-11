using System;
using System.Collections.Generic;

namespace TrinityModelViewer.Export.ZaUnity
{
    public sealed class ZaUnityExportManifest
    {
        public string Scheme { get; set; } = "ZaShaderUnity/v2";
        public string ModelName { get; set; } = string.Empty;
        public string GltfFile { get; set; } = string.Empty;
        public string FbxFile { get; set; } = string.Empty;
        public string TexturesFolder { get; set; } = string.Empty;
        public List<ZaUnityMaterialEntry> Materials { get; set; } = new();
        public List<ZaUnityAlternateMaterialSet> AlternateMaterialSets { get; set; } = new();
    }

    public sealed class ZaUnityAlternateMaterialSet
    {
        public string Kind { get; set; } = string.Empty;
        public string Suffix { get; set; } = string.Empty;
        public string? VariantParam { get; set; }
        public int VariantIndex { get; set; }
        public List<ZaUnityMaterialEntry> Materials { get; set; } = new();
    }

    public sealed class ZaUnityMaterialEntry
    {
        public string Name { get; set; } = string.Empty;
        public string ShaderKey { get; set; } = string.Empty;
        public List<ZaUnityNamedTexture> Textures { get; set; } = new();
        public List<ZaUnityNamedFloat> Floats { get; set; } = new();
        public List<ZaUnityNamedVector4> Vector4s { get; set; } = new();
        public List<ZaUnityNamedBool> Bools { get; set; } = new();
    }

    public sealed class ZaUnityNamedTexture
    {
        public string Slot { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string WrapU { get; set; } = "Clamp";
        public string WrapV { get; set; } = "Clamp";
        public float TilingX { get; set; } = 1f;
        public float TilingY { get; set; } = 1f;
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public bool HalfTextureMirrorU { get; set; }
    }

    public sealed class ZaUnityNamedFloat
    {
        public string Name { get; set; } = string.Empty;
        public float Value { get; set; }
    }

    public sealed class ZaUnityNamedVector4
    {
        public string Name { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; } = 1f;
    }

    public sealed class ZaUnityNamedBool
    {
        public string Name { get; set; } = string.Empty;
        public bool Value { get; set; }
    }

    public static class ZaUnityExportPaths
    {
        public const string ExportRoot = @"e:\temp_Za_export";
        public const string UnityImportRoot = @"d:\Program Files\unityEditor\ZaView\Assets\Imported";
        public const string GltfToFbxScript = @"e:\gltf2Fbx.py";
        public const string ManifestFileName = "za_materials.json";
        public const string UnityShaderName = "Za/IkCharacter";
    }
}
