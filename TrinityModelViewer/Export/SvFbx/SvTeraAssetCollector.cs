using System;
using System.Collections.Generic;
using System.IO;

namespace TrinityModelViewer.Export.SvFbx
{
    public sealed class SvTeraAssetSet
    {
        public List<string> Files { get; } = new List<string>();
        public List<string> TrmdlFiles { get; } = new List<string>();
    }

    public static class SvTeraAssetCollector
    {
        private static readonly string[] RelativeRoots =
        {
            @"field_graphic\terrain_model\objects_un_plan_raid_jewelry01",
            @"world\obj_template\parts\raid",
            @"world\obj_template\effect\field_raid_jewel_distant_view00_objtemp_",
            @"world\obj_template\effect\field_raid_jewel_distant_view01_objtemp_",
            @"effect\field\jewel",
            @"effect\battle_ej\ej_raid_bg00",
            @"field_graphic\terrain_model\objects_un_a_w23crystal01",
            @"field_graphic\terrain_model\objects_un_a_w23crystal02",
            @"world\data\gem\gem"
        };

        public static SvTeraAssetSet Collect(string svRoot)
        {
            var set = new SvTeraAssetSet();
            if (string.IsNullOrWhiteSpace(svRoot) || !Directory.Exists(svRoot))
            {
                return set;
            }

            string rootFull = Path.GetFullPath(svRoot);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < RelativeRoots.Length; i++)
            {
                string dir = Path.Combine(rootFull, RelativeRoots[i]);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int f = 0; f < files.Length; f++)
                {
                    string path = files[f];
                    if (!seen.Add(path))
                    {
                        continue;
                    }

                    set.Files.Add(path);
                    if (path.EndsWith(".trmdl", StringComparison.OrdinalIgnoreCase))
                    {
                        set.TrmdlFiles.Add(path);
                    }
                }
            }

            return set;
        }

        public static int CopyPreservingRelative(string svRoot, List<string> files, string destRoot)
        {
            if (string.IsNullOrWhiteSpace(svRoot) || files == null || files.Count == 0)
            {
                return 0;
            }

            string rootFull = Path.GetFullPath(svRoot);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar) && !rootFull.EndsWith(Path.AltDirectorySeparatorChar))
            {
                rootFull += Path.DirectorySeparatorChar;
            }

            Directory.CreateDirectory(destRoot);
            int copied = 0;
            for (int i = 0; i < files.Count; i++)
            {
                string src = files[i];
                if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
                {
                    continue;
                }

                string full = Path.GetFullPath(src);
                string rel = full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)
                    ? full.Substring(rootFull.Length)
                    : Path.GetFileName(full);
                string dest = Path.Combine(destRoot, rel);
                string? destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrWhiteSpace(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(src, dest, overwrite: true);
                copied++;
            }

            return copied;
        }

        public static string UniqueModelName(string trmdlPath, HashSet<string> used)
        {
            string name = Sanitize(Path.GetFileNameWithoutExtension(trmdlPath));
            if (used.Add(name))
            {
                return name;
            }

            string parent = Sanitize(Path.GetFileName(Path.GetDirectoryName(trmdlPath) ?? string.Empty));
            string combined = string.IsNullOrWhiteSpace(parent) ? name + "_2" : parent + "_" + name;
            if (used.Add(combined))
            {
                return combined;
            }

            int n = 3;
            while (true)
            {
                string numbered = combined + "_" + n.ToString();
                if (used.Add(numbered))
                {
                    return numbered;
                }
                n++;
            }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "model";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }
    }
}
