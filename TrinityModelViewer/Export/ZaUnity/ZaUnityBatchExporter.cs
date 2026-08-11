using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrinityModelViewer.Export.ZaUnity
{
    public sealed class ZaUnityBatchExportResult
    {
        public int Total { get; init; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int ModelsWithAnimations { get; set; }
        public int TotalAnimationsLoaded { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> Exported { get; } = new();
    }

    public static class ZaUnityBatchExporter
    {
        public static IReadOnlyList<string> EnumerateTrmdlFiles(string rootFolder, bool recursive = true)
        {
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                return Array.Empty<string>();
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(rootFolder);
            }
            catch
            {
                fullRoot = rootFolder;
            }

            if (!Directory.Exists(fullRoot))
            {
                return Array.Empty<string>();
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(fullRoot, "*.trmdl", searchOption)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static (string[] RemainingPaths, string? ExportUnityFolder, string? ExportUnityAnimFolder, bool ExportUnityRecursive) ParseStartupArgs(string[]? args)
        {
            var remaining = new List<string>();
            string? exportFolder = null;
            string? exportAnimFolder = null;
            bool recursive = true;

            if (args == null || args.Length == 0)
            {
                return (Array.Empty<string>(), null, null, true);
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--export-unity", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        exportFolder = args[++i];
                    }

                    continue;
                }

                if (string.Equals(arg, "--export-unity-anim", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        exportAnimFolder = args[++i];
                    }

                    continue;
                }

                if (string.Equals(arg, "--export-unity-top-only", StringComparison.OrdinalIgnoreCase))
                {
                    recursive = false;
                    continue;
                }

                if (arg.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                remaining.Add(arg);
            }

            return (remaining.ToArray(), exportFolder, exportAnimFolder, recursive);
        }
    }
}
