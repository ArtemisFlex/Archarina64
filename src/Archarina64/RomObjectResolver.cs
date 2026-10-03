using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpOcarina
{
    public static class RomObjectResolver
    {
        public static string Resolve(string directory, string requestedName)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(requestedName) || !Directory.Exists(directory))
                return null;
            string name = Path.GetFileNameWithoutExtension(requestedName.Trim());
            string exact = Path.Combine(directory, name + ".zobj");
            if (File.Exists(exact)) return exact;
            string normalized = Normalize(name);
            foreach (string path in Directory.EnumerateFiles(directory, "*.zobj", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)) return path;
                if (Normalize(Path.GetFileNameWithoutExtension(path)) == normalized) return path;
            }
            return null;
        }

        public static IEnumerable<string> SafeAliases(string definitionText)
        {
            if (string.IsNullOrWhiteSpace(definitionText)) yield break;
            foreach (string token in definitionText.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                string alias = Path.GetFileNameWithoutExtension(token.Trim());
                if (alias.Length == 0 || alias.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                if (alias.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '-'))) continue;
                yield return alias;
            }
        }

        private static string Normalize(string value)
        {
            StringBuilder result = new StringBuilder();
            foreach (char c in value.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) result.Append(c);
            return result.ToString();
        }
    }
}
