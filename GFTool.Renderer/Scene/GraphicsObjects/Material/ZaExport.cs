using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Trinity.Core.Flatbuffers.TR.Model;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Material
    {
        public bool TryGetEffectiveVector4(string name, out Vector4 value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (TryGetUniformOverride(name, out var overrideValue) && TryConvertToVector4(overrideValue, out value))
            {
                return true;
            }

            lock (colorTableUniformOverrideLock)
            {
                if (colorTableUniformOverrides != null &&
                    colorTableUniformOverrides.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            for (int i = 0; i < vec4Params.Length; i++)
            {
                var p = vec4Params[i];
                if (p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    // FlatBuffer vec4 is stored W,X,Y,Z; shaders consume x,y,z,w.
                    value = new Vector4(p.Value.W, p.Value.X, p.Value.Y, p.Value.Z);
                    return true;
                }
            }

            return false;
        }

        public bool IsLayerMaskPlaceholderTexture()
        {
            return textures.Any(t =>
                string.Equals(t.Name, "LayerMaskMap", StringComparison.OrdinalIgnoreCase) &&
                IsPlaceholderMaskTexturePath(t.SourceFile));
        }

        public bool TryGetTextureWrap(string slot, out string wrapU, out string wrapV)
        {
            wrapU = "Clamp";
            wrapV = "Clamp";
            if (string.IsNullOrWhiteSpace(slot))
            {
                return false;
            }

            var tex = textures.FirstOrDefault(t => string.Equals(t.Name, slot, StringComparison.OrdinalIgnoreCase));
            if (tex == null)
            {
                return false;
            }

            wrapU = WrapModeToExportName(tex.WrapS);
            wrapV = WrapModeToExportName(tex.WrapT);
            return true;
        }

        public string GetUvScaleOffsetUniformForSlot(string slot)
        {
            if (string.Equals(slot, "NormalMap", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(slot, "NormalMap1", StringComparison.OrdinalIgnoreCase))
            {
                return "UVScaleOffsetNormal";
            }

            if (string.Equals(slot, "BaseColorMap1", StringComparison.OrdinalIgnoreCase))
            {
                return vec4Params.Any(p => string.Equals(p?.Name, "UVScaleOffset1", StringComparison.OrdinalIgnoreCase))
                    ? "UVScaleOffset1"
                    : "UVScaleOffset";
            }

            if (string.Equals(slot, "LayerMaskMap", StringComparison.OrdinalIgnoreCase) &&
                vec4Params.Any(p => string.Equals(p?.Name, "UVScaleOffsetLayerMask", StringComparison.OrdinalIgnoreCase)))
            {
                return "UVScaleOffsetLayerMask";
            }

            return "UVScaleOffset";
        }

        public bool TryGetIkCharacterTextureEnable(string optionName, IReadOnlyCollection<string> textureSlotNames, bool defaultWhenPresent = true)
        {
            bool OptionEnabled(string name, bool defaultValue)
            {
                foreach (var (paramName, paramValue) in ShaderParams)
                {
                    if (!string.Equals(paramName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = paramValue?.Trim();
                    if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return defaultValue;
                }

                return defaultValue;
            }

            bool HasTexture(string slotName) =>
                textureSlotNames.Contains(slotName) ||
                textures.Any(t => string.Equals(t.Name, slotName, StringComparison.OrdinalIgnoreCase));

            return optionName switch
            {
                "EnableBaseColorMap" => HasTexture("BaseColorMap") && OptionEnabled("EnableBaseColorMap", defaultWhenPresent),
                "EnableLayerMaskMap" => !IsLayerMaskPlaceholderTexture() &&
                                        HasTexture("LayerMaskMap") &&
                                        OptionEnabled("EnableLayerMaskMap", defaultWhenPresent),
                "EnableNormalMap" => HasTexture("NormalMap") && OptionEnabled("EnableNormalMap", defaultWhenPresent),
                "EnableNormalMap1" => HasTexture("NormalMap1") && OptionEnabled("EnableNormalMap1", defaultWhenPresent),
                "EnableBaseColorMap1" => HasTexture("BaseColorMap1") && OptionEnabled("EnableBaseColorMap1", defaultWhenPresent),
                "EnableAOMap" => HasTexture("AOMap") && OptionEnabled("EnableAOMap", defaultWhenPresent),
                "EnableEmissionColorMap" => HasTexture("EmissionColorMap") && OptionEnabled("EnableEmissionColorMap", defaultWhenPresent),
                "EnableFresnelTexture" => HasTexture("BaseColorMap1") && OptionEnabled("EnableFresnelTexture", defaultWhenPresent),
                "EnableLocalIBL" => HasTexture("LocalSpecularProbe") && OptionEnabled("EnableLocalIBL", defaultWhenPresent),
                "EnableRoughnessMap" => HasTexture("RoughnessMap") && OptionEnabled("EnableRoughnessMap", defaultWhenPresent),
                "EnableMetallicMap" => HasTexture("MetallicMap") && OptionEnabled("EnableMetallicMap", defaultWhenPresent),
                "EnableAlphaTest" => OptionEnabled("EnableAlphaTest", defaultValue: false),
                "BaseColorMultiply" => OptionEnabled("BaseColorMultiply", defaultWhenPresent),
                _ => TryGetShaderOptionBool(optionName, defaultWhenPresent)
            };
        }

        private static string WrapModeToExportName(TextureWrapMode mode)
        {
            const TextureWrapMode mirrorClampToEdge = (TextureWrapMode)0x8743;
            return mode switch
            {
                TextureWrapMode.Repeat => "Repeat",
                TextureWrapMode.MirroredRepeat => "Mirror",
                mirrorClampToEdge => "MirrorOnce",
                _ => "Clamp"
            };
        }

        public bool TryGetEffectiveFloat(string name, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (TryGetUniformOverride(name, out var overrideValue) && TryConvertToFloat(overrideValue, out value))
            {
                return true;
            }

            for (int i = 0; i < floatParams.Length; i++)
            {
                var p = floatParams[i];
                if (p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetShaderOptionBool(string name, bool defaultValue = false)
        {
            if (TryGetUniformOverride(name, out var overrideValue))
            {
                if (TryConvertToBool(overrideValue, out var b))
                {
                    return b;
                }
            }

            foreach (var (paramName, paramValue) in ShaderParams)
            {
                if (string.Equals(paramName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return IsTruthy(paramValue);
                }
            }

            if (TryGetEffectiveFloat(name, out var f))
            {
                return MathF.Abs(f) > 0.0001f;
            }

            return defaultValue;
        }

        private static bool IsTruthy(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            return string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryConvertToBool(object? value, out bool result)
        {
            switch (value)
            {
                case bool b:
                    result = b;
                    return true;
                case int n:
                    result = n != 0;
                    return true;
                case float f:
                    result = MathF.Abs(f) > 0.0001f;
                    return true;
                case string s:
                    if (string.Equals(s.Trim(), "false", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.Trim(), "0", StringComparison.OrdinalIgnoreCase))
                    {
                        result = false;
                        return true;
                    }
                    result = IsTruthy(s);
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        private static bool TryConvertToFloat(object? value, out float result)
        {
            switch (value)
            {
                case float f:
                    result = f;
                    return true;
                case int n:
                    result = n;
                    return true;
                case double d:
                    result = (float)d;
                    return true;
                case string s:
                    return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
                default:
                    result = 0f;
                    return false;
            }
        }

        private static bool TryConvertToVector4(object? value, out Vector4 result)
        {
            switch (value)
            {
                case Vector4 v:
                    result = v;
                    return true;
                case float[] arr when arr.Length >= 4:
                    result = new Vector4(arr[0], arr[1], arr[2], arr[3]);
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
    }
}
