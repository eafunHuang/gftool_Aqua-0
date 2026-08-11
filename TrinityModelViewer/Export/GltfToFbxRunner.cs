using System;
using System.Diagnostics;
using System.IO;

namespace TrinityModelViewer.Export
{
    internal static class GltfToFbxRunner
    {
        public static void ConvertDirectory(string exportDir, string scriptPath)
        {
            if (string.IsNullOrWhiteSpace(exportDir))
            {
                throw new ArgumentException("Export directory is required.", nameof(exportDir));
            }

            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("gltf2Fbx.py not found.", scriptPath);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" \"{exportDir}\"",
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
    }
}
