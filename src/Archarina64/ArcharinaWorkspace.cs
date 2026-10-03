using System;
using System.IO;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    internal static class ArcharinaWorkspace
    {
        private static readonly string SharedAuthoredGeometryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SharpOcarina", "authored_geometry");
        private static readonly string LegacySharpAuthoredGeometryPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "authored_geometry");
        private static readonly string LegacyArcharinaAuthoredGeometryPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "arch_authored_geometry");
        private static readonly SharedEditorWorkspace DefaultSharedWorkspace =
            new SharedEditorWorkspace(
                SharedAuthoredGeometryRoot,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Archarina64", "library"),
                LegacySharpAuthoredGeometryPath);
        private static SharedEditorWorkspace activeSharedWorkspace = DefaultSharedWorkspace;

        internal static string PrivatePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Archarina64"); } }
        internal static string ImportPath { get { return Path.Combine(PrivatePath, "import", GuidForSession); } }
        private static readonly string GuidForSession = Guid.NewGuid().ToString("N");
        internal static string ActiveObjectDirectory = Path.Combine(PrivatePath, "no-rom-loaded");
        internal static string LibraryPath
        {
            get { return Path.Combine(PrivatePath, "library"); }
        }

        internal static string SharedAuthoredGeometryPath
        {
            get { return activeSharedWorkspace.AuthoredGeometry.RootDirectory; }
        }

        internal static string LegacyAuthoredGeometryPath
        {
            get { return LegacyArcharinaAuthoredGeometryPath; }
        }

        internal static SharedEditorWorkspace SharedWorkspace
        {
            get { return activeSharedWorkspace; }
        }

        internal static Project64PlaytestService Project64Playtest
        {
            get { return activeSharedWorkspace.Project64; }
        }

        internal static void Initialize()
        {
            Initialize(null);
        }

        internal static void Initialize(SharedEditorWorkspace sharedWorkspace)
        {
            activeSharedWorkspace = sharedWorkspace ?? DefaultSharedWorkspace;
            Directory.CreateDirectory(LibraryPath);
            Directory.CreateDirectory(ImportPath);
            activeSharedWorkspace.AuthoredGeometry.Ensure();
            activeSharedWorkspace.AuthoredGeometry.ImportLegacyDirectory(LegacyArcharinaAuthoredGeometryPath);
            string xml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML");
            Directory.CreateDirectory(xml);
            foreach (string name in new[] { "Archarina64RecentFiles.xml" })
            {
                string path = Path.Combine(xml, name);
                if (!File.Exists(path))
                    File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><Table KeyType=\"System.UInt16\" ValueType=\"System.String\"></Table>");
            }
        }

        internal static string GetSharedAuthoredGeometryPath()
        {
            activeSharedWorkspace.AuthoredGeometry.Ensure();
            return SharedAuthoredGeometryPath;
        }

        internal static void LogError(Exception error)
        {
            try
            {
                Directory.CreateDirectory(PrivatePath);
                File.AppendAllText(Path.Combine(PrivatePath, "errors.log"), DateTime.UtcNow.ToString("O") + Environment.NewLine + error + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
