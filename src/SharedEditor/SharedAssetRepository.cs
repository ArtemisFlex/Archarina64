using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpOcarina.Shared
{
    /// <summary>
    /// Shared, editor-neutral access to authored geometry. Editors own their
    /// import/render code; this class only owns the library location and
    /// non-destructive legacy migration.
    /// </summary>
    public sealed class SharedAssetRepository
    {
        public SharedAssetRepository(string rootDirectory, string legacyDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentNullException("rootDirectory");
            RootDirectory = Path.GetFullPath(rootDirectory);
            LegacyDirectory = string.IsNullOrWhiteSpace(legacyDirectory) ? null : Path.GetFullPath(legacyDirectory);
        }

        public string RootDirectory { get; private set; }
        public string LegacyDirectory { get; private set; }

        public event EventHandler AssetsChanged;

        public void NotifyAssetChanged(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            EventHandler handler = AssetsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Ensure()
        {
            Directory.CreateDirectory(RootDirectory);
            CopyMissingFiles(LegacyDirectory, RootDirectory);
        }

        public void ImportLegacyDirectory(string legacyDirectory)
        {
            CopyMissingFiles(legacyDirectory, RootDirectory);
        }

        public List<string> EnumerateAuthoredMeshes()
        {
            Ensure();
            return Directory.GetFiles(RootDirectory, "*.sog.xml", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void CopyMissingFiles(string source, string destination)
        {
            if (string.IsNullOrEmpty(source) || !Directory.Exists(source) ||
                string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return;
            string sourceRoot = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(sourceRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                string parent = Path.GetDirectoryName(target);
                if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                if (!File.Exists(target)) File.Copy(file, target);
            }
        }
    }
}
