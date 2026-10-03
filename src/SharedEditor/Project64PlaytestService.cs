using System;
using System.Diagnostics;
using System.IO;

namespace SharpOcarina.Shared
{
    /// <summary>
    /// Editor-neutral Project64 configuration and launch boundary. ROM
    /// materialization and validation remain owned by the dungeon editor.
    /// </summary>
    public sealed class Project64PlaytestService
    {
        private readonly string configurationPath;
        private string executablePath;

        public Project64PlaytestService(string settingsDirectory)
        {
            if (string.IsNullOrWhiteSpace(settingsDirectory)) throw new ArgumentNullException("settingsDirectory");
            Directory.CreateDirectory(settingsDirectory);
            configurationPath = Path.Combine(Path.GetFullPath(settingsDirectory), "project64.path");
        }

        public string ConfigurationPath { get { return configurationPath; } }

        public string GetConfiguredExecutable()
        {
            if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath)) return executablePath;
            if (!File.Exists(configurationPath)) return null;
            string configured = File.ReadAllText(configurationPath).Trim();
            if (!File.Exists(configured)) return null;
            executablePath = Path.GetFullPath(configured);
            return executablePath;
        }

        public void SetExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Project64.exe was not found.", path);
            executablePath = Path.GetFullPath(path);
            File.WriteAllText(configurationPath, executablePath);
        }

        public ProcessStartInfo CreateStartInfo(string romPath)
        {
            if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
                throw new FileNotFoundException("The playtest ROM was not found.", romPath);
            string emulator = GetConfiguredExecutable();
            if (string.IsNullOrEmpty(emulator))
                throw new InvalidOperationException("Configure Project64 before launching a playtest.");
            return new ProcessStartInfo
            {
                FileName = emulator,
                Arguments = "\"" + romPath.Replace("\"", "\\\"") + "\"",
                WorkingDirectory = Path.GetDirectoryName(emulator),
                UseShellExecute = false
            };
        }

        public Process Launch(string romPath)
        {
            return Process.Start(CreateStartInfo(romPath));
        }
    }
}
