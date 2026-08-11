using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrinityModelViewer.Scene;

namespace TrinityModelViewer.Export.ZaUnity
{
    public static class ZaUnityAnimationLoader
    {
        public const int DefaultMaxAnimationsPerModel = 500;

        public static string? ResolveAnimationDirectory(string trmdlPath, string modelsRoot, string animRoot)
        {
            if (string.IsNullOrWhiteSpace(trmdlPath) ||
                string.IsNullOrWhiteSpace(modelsRoot) ||
                string.IsNullOrWhiteSpace(animRoot))
            {
                return null;
            }

            string fullTrmdl;
            string fullModels;
            string fullAnim;
            try
            {
                fullTrmdl = Path.GetFullPath(trmdlPath);
                fullModels = Path.GetFullPath(modelsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                fullAnim = Path.GetFullPath(animRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return null;
            }

            string? modelDir = Path.GetDirectoryName(fullTrmdl);
            if (string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(fullAnim))
            {
                return null;
            }

            if (modelDir.StartsWith(fullModels, StringComparison.OrdinalIgnoreCase))
            {
                string relative = Path.GetRelativePath(fullModels, modelDir);
                if (!string.IsNullOrWhiteSpace(relative) && relative != ".")
                {
                    string mapped = Path.Combine(fullAnim, relative);
                    if (Directory.Exists(mapped))
                    {
                        return mapped;
                    }
                }
            }

            string leafFolder = Path.GetFileName(modelDir);
            if (string.IsNullOrWhiteSpace(leafFolder))
            {
                return null;
            }

            try
            {
                return Directory.EnumerateDirectories(fullAnim, leafFolder, SearchOption.AllDirectories)
                    .FirstOrDefault(Directory.Exists);
            }
            catch
            {
                return null;
            }
        }

        public static async Task<IReadOnlyList<Animation>> LoadForTrmdlAsync(
            string trmdlPath,
            string modelsRoot,
            string animRoot,
            int maxAnimations = DefaultMaxAnimationsPerModel,
            CancellationToken token = default)
        {
            string? animDir = ResolveAnimationDirectory(trmdlPath, modelsRoot, animRoot);
            if (string.IsNullOrWhiteSpace(animDir))
            {
                return Array.Empty<Animation>();
            }

            var loader = new AnimationAutoLoader();
            var loaded = await loader.LoadAnimationsFromDirectoriesAsync(
                new[] { animDir },
                Math.Clamp(maxAnimations, 1, 50000),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                token);

            return loaded.Select(item => item.Animation).ToList();
        }
    }
}
