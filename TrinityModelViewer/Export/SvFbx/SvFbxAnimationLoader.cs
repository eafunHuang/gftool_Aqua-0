using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Trinity.Core.Utils;
using TrinityModelViewer.Scene;
using GfAnim = Trinity.Core.Flatbuffers.GF.Animation;
using TrAnim = Trinity.Core.Flatbuffers.TR.Animation;

namespace TrinityModelViewer.Export.SvFbx
{
    /// <summary>
    /// Resolves and loads SV Pokemon animation clips for batch FBX export.
    /// More aggressive than ZaUnity matching: relative path, model_→motion_,
    /// leaf folder, pokemon id/stem, and recursive .tranm/.gfbanm under matches.
    /// </summary>
    public static class SvFbxAnimationLoader
    {
        public const int DefaultMaxAnimationsPerModel = 2000;

        public static async Task<IReadOnlyList<Animation>> LoadForTrmdlAsync(
            string trmdlPath,
            string modelsRoot,
            string animRoot,
            int maxAnimations = DefaultMaxAnimationsPerModel,
            CancellationToken token = default)
        {
            var dirs = ResolveAnimationDirectories(trmdlPath, modelsRoot, animRoot);
            if (dirs.Count == 0)
            {
                return Array.Empty<Animation>();
            }

            var loaded = await Task.Run(() => LoadFromDirectories(dirs, maxAnimations, token), token);

            // When falling back to a shared/flat anim root, keep only clips for this model stem.
            string fullAnim;
            try
            {
                fullAnim = Path.GetFullPath(animRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return loaded;
            }

            bool onlyAnimRoot = dirs.Count == 1 &&
                                string.Equals(dirs[0], fullAnim, StringComparison.OrdinalIgnoreCase);
            if (onlyAnimRoot)
            {
                return FilterByModelStem(loaded, trmdlPath).ToList();
            }

            return loaded;
        }

        public static IReadOnlyList<string> ResolveAnimationDirectories(string trmdlPath, string modelsRoot, string animRoot)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(trmdlPath) ||
                string.IsNullOrWhiteSpace(modelsRoot) ||
                string.IsNullOrWhiteSpace(animRoot))
            {
                return results;
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
                return results;
            }

            if (!Directory.Exists(fullAnim))
            {
                return results;
            }

            string? modelDir = Path.GetDirectoryName(fullTrmdl);
            if (string.IsNullOrWhiteSpace(modelDir))
            {
                return results;
            }

            void AddDir(string? dir)
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    return;
                }

                string full = Path.GetFullPath(dir);
                if (!results.Any(d => string.Equals(d, full, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(full);
                }
            }

            // 1) Parallel relative path under anim root
            if (modelDir.StartsWith(fullModels, StringComparison.OrdinalIgnoreCase))
            {
                string relative = Path.GetRelativePath(fullModels, modelDir);
                if (!string.IsNullOrWhiteSpace(relative) && relative != ".")
                {
                    AddDir(Path.Combine(fullAnim, relative));

                    // model_xxx → motion_xxx (SV chara / pokemon layout)
                    string motionRelative = RewriteModelFolderToMotion(relative);
                    if (!string.Equals(motionRelative, relative, StringComparison.OrdinalIgnoreCase))
                    {
                        AddDir(Path.Combine(fullAnim, motionRelative));
                    }
                }
                else if (relative == ".")
                {
                    // Model sits directly in models root — try anim root itself
                    AddDir(fullAnim);
                }
            }

            // 2) Leaf folder name (e.g. pm0001_00)
            string leafFolder = Path.GetFileName(modelDir);
            if (!string.IsNullOrWhiteSpace(leafFolder))
            {
                try
                {
                    foreach (var hit in Directory.EnumerateDirectories(fullAnim, leafFolder, SearchOption.AllDirectories))
                    {
                        AddDir(hit);
                    }
                }
                catch
                {
                    // ignore enumeration errors
                }

                string motionLeaf = RewriteModelFolderToMotion(leafFolder);
                if (!string.Equals(motionLeaf, leafFolder, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        foreach (var hit in Directory.EnumerateDirectories(fullAnim, motionLeaf, SearchOption.AllDirectories))
                        {
                            AddDir(hit);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            // 3) Pokemon / file stem (pm0001_00.trmdl → folders / files named pm0001_00*)
            string stem = Path.GetFileNameWithoutExtension(fullTrmdl);
            if (!string.IsNullOrWhiteSpace(stem) &&
                !string.Equals(stem, leafFolder, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (var hit in Directory.EnumerateDirectories(fullAnim, stem, SearchOption.AllDirectories))
                    {
                        AddDir(hit);
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // 4) If still nothing matched, use anim root when it (recursively) has clips
            if (results.Count == 0 && DirectoryHasAnimationFiles(fullAnim, recursive: true))
            {
                // Prefer a subdirectory whose name appears in the model path / stem
                string? best = FindBestAnimSubdirByNameHints(fullAnim, leafFolder, stem);
                if (!string.IsNullOrWhiteSpace(best))
                {
                    AddDir(best);
                }
                else if (!string.IsNullOrWhiteSpace(stem) && stem.StartsWith("pm", StringComparison.OrdinalIgnoreCase))
                {
                    // Flat anim root: load only files whose names start with this pokemon stem
                    AddDir(fullAnim);
                }
            }

            return results;
        }

        private static List<Animation> LoadFromDirectories(
            IReadOnlyList<string> dirs,
            int maxAnimations,
            CancellationToken token)
        {
            var loaded = new List<Animation>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int limit = Math.Clamp(maxAnimations, 1, 50000);

            // If only the anim root itself was selected and we are filtering by stem later,
            // collect files recursively from each dir.
            foreach (var dir in dirs)
            {
                token.ThrowIfCancellationRequested();
                if (loaded.Count >= limit)
                {
                    break;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                        .Where(f =>
                        {
                            string ext = Path.GetExtension(f);
                            return string.Equals(ext, ".tranm", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(ext, ".gfbanm", StringComparison.OrdinalIgnoreCase);
                        })
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    if (loaded.Count >= limit)
                    {
                        break;
                    }

                    if (!seenPaths.Add(file))
                    {
                        continue;
                    }

                    try
                    {
                        var ext = Path.GetExtension(file);
                        GfAnim.Animation animFile;
                        if (string.Equals(ext, ".tranm", StringComparison.OrdinalIgnoreCase))
                        {
                            var tranmData = FlatBufferConverter.DeserializeFrom<TrAnim.TRANM>(file);
                            animFile = TranmToGfAnimation.Convert(tranmData);
                        }
                        else
                        {
                            animFile = FlatBufferConverter.DeserializeFrom<GfAnim.Animation>(file);
                        }

                        string name = Path.GetFileNameWithoutExtension(file) ?? "anim";
                        loaded.Add(new Animation(animFile, name, file));
                    }
                    catch
                    {
                        // Skip unreadable clips
                    }
                }
            }

            return loaded;
        }

        /// <summary>
        /// When loading from a shared flat anim root, keep only clips whose filename
        /// starts with the model stem (e.g. pm0001_00_wait → keep for pm0001_00).
        /// </summary>
        public static IReadOnlyList<Animation> FilterByModelStem(IReadOnlyList<Animation> animations, string trmdlPath)
        {
            if (animations == null || animations.Count == 0 || string.IsNullOrWhiteSpace(trmdlPath))
            {
                return animations ?? Array.Empty<Animation>();
            }

            string stem = Path.GetFileNameWithoutExtension(trmdlPath);
            if (string.IsNullOrWhiteSpace(stem))
            {
                return animations;
            }

            // Only filter when looking at pm* pokemon ids to avoid over-filtering generic names
            if (!stem.StartsWith("pm", StringComparison.OrdinalIgnoreCase) &&
                !stem.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
            {
                return animations;
            }

            var filtered = animations
                .Where(a =>
                {
                    string name = a.Name ?? string.Empty;
                    return name.StartsWith(stem, StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            // If filter wiped everything, keep original (folder was already model-specific)
            return filtered.Count > 0 ? filtered : animations;
        }

        private static string RewriteModelFolderToMotion(string relativeOrLeaf)
        {
            if (string.IsNullOrWhiteSpace(relativeOrLeaf))
            {
                return relativeOrLeaf;
            }

            char[] seps = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            var parts = relativeOrLeaf.Split(seps, StringSplitOptions.RemoveEmptyEntries).ToList();
            bool changed = false;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].StartsWith("model_", StringComparison.OrdinalIgnoreCase))
                {
                    string suffix = parts[i].Length > "model_".Length ? parts[i].Substring("model_".Length) : string.Empty;
                    parts[i] = string.IsNullOrEmpty(suffix) ? "motion" : $"motion_{suffix}";
                    changed = true;
                }
                else if (string.Equals(parts[i], "model", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = "motion";
                    changed = true;
                }
            }

            return changed
                ? string.Join(Path.DirectorySeparatorChar, parts)
                : relativeOrLeaf;
        }

        private static bool DirectoryHasAnimationFiles(string dir, bool recursive)
        {
            try
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                return Directory.EnumerateFiles(dir, "*.tranm", option).Any() ||
                       Directory.EnumerateFiles(dir, "*.gfbanm", option).Any();
            }
            catch
            {
                return false;
            }
        }

        private static string? FindBestAnimSubdirByNameHints(string animRoot, string? leafFolder, string? stem)
        {
            var hints = new List<string>();
            if (!string.IsNullOrWhiteSpace(leafFolder)) hints.Add(leafFolder);
            if (!string.IsNullOrWhiteSpace(stem) &&
                !hints.Any(h => string.Equals(h, stem, StringComparison.OrdinalIgnoreCase)))
            {
                hints.Add(stem!);
            }

            foreach (var hint in hints)
            {
                try
                {
                    string? hit = Directory.EnumerateDirectories(animRoot, hint, SearchOption.AllDirectories)
                        .FirstOrDefault(Directory.Exists);
                    if (!string.IsNullOrWhiteSpace(hit))
                    {
                        return hit;
                    }
                }
                catch
                {
                    // continue
                }
            }

            return null;
        }
    }
}
