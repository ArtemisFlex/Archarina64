using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace SharpOcarina.Shared
{
    /// <summary>
    /// Editor-neutral summary of a shared authored mesh. The two editors keep
    /// their own render/edit models; this contract only describes the XML
    /// asset well enough for a common library and diagnostics.
    /// </summary>
    public sealed class AuthoredMeshAssetManifest
    {
        public string Path { get; internal set; }
        public string Name { get; internal set; }
        public int VertexCount { get; internal set; }
        public int FaceCount { get; internal set; }
        public int MaterialCount { get; internal set; }
        public int PolygonCount { get; internal set; }
        public int CollectionCount { get; internal set; }
        public bool HasSourceSceneScale { get; internal set; }
        public double SourceSceneScale { get; internal set; }
        public bool HasMirrorModifier { get; internal set; }
        public bool HasUvData { get; internal set; }
        public bool HasVertexColors { get; internal set; }

        public string DisplayName
        {
            get { return string.IsNullOrWhiteSpace(Name) ? System.IO.Path.GetFileName(Path) : Name; }
        }
    }

    public static class AuthoredMeshAssetManifestReader
    {
        public static bool TryRead(string path, out AuthoredMeshAssetManifest manifest)
        {
            try
            {
                manifest = Read(path);
                return true;
            }
            catch
            {
                manifest = null;
                return false;
            }
        }

        public static AuthoredMeshAssetManifest Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException("path");
            string fullPath = System.IO.Path.GetFullPath(path);
            XDocument document = XDocument.Load(fullPath, LoadOptions.None);
            XElement root = document.Root;
            if (root == null) throw new InvalidDataException("Authored mesh asset has no XML root.");

            AuthoredMeshAssetManifest manifest = new AuthoredMeshAssetManifest
            {
                Path = fullPath,
                Name = Value(root, "Name"),
                VertexCount = Count(root, "AuthoredMeshAssetVertex"),
                FaceCount = Count(root, "AuthoredMeshAssetFace"),
                MaterialCount = Count(root, "AuthoredMeshAssetMaterial"),
                PolygonCount = Count(root, "AuthoredMeshAssetPolygon"),
                CollectionCount = Count(root, "AuthoredMeshAssetCollection"),
                HasSourceSceneScale = BooleanValue(root, "HasSourceSceneScale"),
                SourceSceneScale = DoubleValue(root, "SourceSceneScale", 1.0),
                HasMirrorModifier = BooleanValue(root, "HasMirrorModifier"),
                HasUvData = BooleanValueFromChildren(root, "AuthoredMeshAssetFace", "HasUv"),
                HasVertexColors = BooleanValueFromChildren(root, "AuthoredMeshAssetVertex", "HasColor")
            };
            return manifest;
        }

        private static string Value(XElement root, string name)
        {
            XElement element = Child(root, name);
            return element == null ? string.Empty : element.Value;
        }

        private static int Count(XElement root, string name)
        {
            int count = 0;
            foreach (XElement element in root.Descendants())
                if (element.Name.LocalName == name) count++;
            return count;
        }

        private static bool BooleanValue(XElement root, string name)
        {
            bool value;
            return Boolean.TryParse(Value(root, name), out value) && value;
        }

        private static double DoubleValue(XElement root, string name, double fallback)
        {
            double value;
            return Double.TryParse(Value(root, name), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static bool BooleanValueFromChildren(XElement root, string parentName, string childName)
        {
            foreach (XElement parent in root.Descendants())
            {
                if (parent.Name.LocalName != parentName) continue;
                XElement child = Child(parent, childName);
                bool value;
                if (child != null && Boolean.TryParse(child.Value, out value) && value) return true;
            }
            return false;
        }

        private static XElement Child(XElement parent, string name)
        {
            foreach (XElement child in parent.Elements())
                if (child.Name.LocalName == name) return child;
            return null;
        }
    }
}
