using System.Collections.Generic;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SharpOcarina.Shared
{
    /// <summary>Loads the shared Archarina64 modeling-tool icon family.</summary>
    public static class ArcharinaModelerIcons
    {
        private static readonly Dictionary<string, Image> Cache = new Dictionary<string, Image>();
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "bevel", "edge" },
            { "bevel-edge", "edge" },
            { "bevel-vertex", "vertex" },
            { "edge-loop", "edge" },
            { "face-loop", "face" },
            { "close-loop", "edge" },
            { "subdivide", "face" },
            { "subdivide-edges", "edge" },
            { "knife-cut", "edge" },
            { "connect-vertices", "align" },
            { "make-face", "face" },
            { "make-quad", "face" },
            { "triangulate", "face" },
            { "merge", "align" },
            { "merge-faces", "face" },
            { "merge-edges", "edge" },
            { "merge-vertices", "vertex" },
            { "merge-islands", "align" },
            { "show-hide", "object-mode" },
            { "import", "uv-editing" },
            { "export", "save-asset" }
        };

        public static Image Load(string key)
        {
            Image image;
            if (Cache.TryGetValue(key, out image)) return image;
            string path = Path.Combine(Application.StartupPath, "Resources", "ModelerIcons", key + ".png");
            string alias;
            if (!File.Exists(path) && Aliases.TryGetValue(key, out alias))
                path = Path.Combine(Application.StartupPath, "Resources", "ModelerIcons", alias + ".png");
            if (!File.Exists(path)) return null;
            using (Image source = Image.FromFile(path)) image = new Bitmap(source);
            Cache[key] = image;
            return image;
        }
    }
}
