using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Utils;

namespace TrinityModelViewer.Export.ZaUnity
{
    internal static class ZaUnityColorVariationBuilder
    {
        public static List<ZaUnityAlternateMaterialSet> BuildAlternateSets(
            Model model,
            string texDir,
            HashSet<string> exportedTextureNames,
            IReadOnlyList<ZaUnityMaterialEntry> baselineEntries)
        {
            var sets = new List<ZaUnityAlternateMaterialSet>();

            var rareSet = TryBuildRareSet(model, texDir, exportedTextureNames);
            if (rareSet != null)
            {
                sets.Add(rareSet);
            }

            sets.AddRange(BuildVariantSets(model, texDir, exportedTextureNames, baselineEntries));
            return sets;
        }

        private static ZaUnityAlternateMaterialSet? TryBuildRareSet(
            Model model,
            string texDir,
            HashSet<string> exportedTextureNames)
        {
            string? defaultPath = model.DefaultMaterialFilePath ?? model.CurrentMaterialFilePath;
            string? rarePath = TryGetRareTrmtrPath(defaultPath);
            if (string.IsNullOrWhiteSpace(rarePath) || string.IsNullOrWhiteSpace(defaultPath))
            {
                return null;
            }

            TrmtrFile? defaultTrmtr = TryLoadTrmtr(defaultPath);
            TrmtrFile? rareTrmtr = TryLoadTrmtr(rarePath);
            if (defaultTrmtr == null || rareTrmtr == null)
            {
                return null;
            }

            var variantSnapshot = model.GetMaterialVariantParams().ToArray();
            try
            {
                model.ApplyTrmtrFile(rarePath, rareTrmtr, preserveMaterialUniformOverrides: true);
                var entries = ZaUnityMaterialEntryBuilder.BuildAllEntries(model, texDir, exportedTextureNames);
                if (entries.Count == 0)
                {
                    return null;
                }

                return new ZaUnityAlternateMaterialSet
                {
                    Kind = "rare",
                    Suffix = "_rare",
                    Materials = entries
                };
            }
            finally
            {
                model.ApplyTrmtrFile(defaultPath, defaultTrmtr, preserveMaterialUniformOverrides: true);
                RestoreVariantSelections(model, variantSnapshot);
            }
        }

        private static IEnumerable<ZaUnityAlternateMaterialSet> BuildVariantSets(
            Model model,
            string texDir,
            HashSet<string> exportedTextureNames,
            IReadOnlyList<ZaUnityMaterialEntry> baselineEntries)
        {
            var variantSnapshot = model.GetMaterialVariantParams().ToArray();
            foreach (var vp in variantSnapshot)
            {
                if (vp.VariationCount <= 1)
                {
                    continue;
                }

                for (int index = 1; index < vp.VariationCount; index++)
                {
                    RestoreVariantSelections(model, variantSnapshot);
                    if (!model.TrySetMaterialVariantParam(vp.Name, index))
                    {
                        continue;
                    }

                    var current = ZaUnityMaterialEntryBuilder.BuildAllEntries(model, texDir, exportedTextureNames);
                    var changed = ZaUnityMaterialEntryBuilder.BuildColorChangedEntries(baselineEntries, current);
                    if (changed.Count == 0)
                    {
                        continue;
                    }

                    yield return new ZaUnityAlternateMaterialSet
                    {
                        Kind = "variant",
                        Suffix = $"_v{index}",
                        VariantParam = vp.Name,
                        VariantIndex = index,
                        Materials = changed
                    };
                }
            }

            RestoreVariantSelections(model, variantSnapshot);
        }

        private static void RestoreVariantSelections(Model model, IReadOnlyList<Model.MaterialVariantParam> snapshot)
        {
            foreach (var vp in snapshot)
            {
                model.TrySetMaterialVariantParam(vp.Name, vp.SelectedIndex);
            }
        }

        private static string? TryGetRareTrmtrPath(string? trmtrPath)
        {
            if (string.IsNullOrWhiteSpace(trmtrPath) ||
                !trmtrPath.EndsWith(".trmtr", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string? dir = Path.GetDirectoryName(trmtrPath);
            string stem = Path.GetFileNameWithoutExtension(trmtrPath);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(stem))
            {
                return null;
            }

            string rarePath = Path.Combine(dir, $"{stem}_rare.trmtr");
            return File.Exists(rarePath) ? rarePath : null;
        }

        private static TrmtrFile? TryLoadTrmtr(string path)
        {
            try
            {
                return FlatBufferConverter.DeserializeFrom<TrmtrFile>(path);
            }
            catch
            {
                return null;
            }
        }
    }
}
