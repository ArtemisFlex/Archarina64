using System;
using System.IO;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    /// <summary>
    /// Archarina's document adapter keeps native scene data and the optional
    /// tile/dungeon sidecar on one ownership boundary. The sidecar is still
    /// loaded from its existing XML path for now; this adapter makes that
    /// transition explicit without changing the file format yet.
    /// </summary>
    public sealed class ArcharinaSceneDocument : ProjectDocument<ZScene, TileSceneGeometry>
    {
        public string SidecarLoadError { get; private set; }

        public ArcharinaSceneDocument(ZScene scene, TileSceneGeometry sidecar = null, string sourcePath = null)
            : base(scene, sidecar, sourcePath)
        {
            string sidecarPath = scene == null ? null : scene.SandboxGeometryFile;
            if (!string.IsNullOrEmpty(sidecarPath) && !Path.IsPathRooted(sidecarPath) && !string.IsNullOrEmpty(sourcePath))
            {
                sidecarPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourcePath)), sidecarPath);
                if (scene != null) scene.SandboxGeometryFile = sidecarPath;
            }
            if (sidecar == null && !string.IsNullOrEmpty(sidecarPath) && File.Exists(sidecarPath))
            {
                try
                {
                    SetSidecarState(TileStorage.Load<TileSceneGeometry>(sidecarPath));
                }
                catch (Exception error)
                {
                    SidecarLoadError = error.Message;
                    ArcharinaWorkspace.LogError(error);
                }
            }
        }

        public void SynchronizeSidecar(TileSceneGeometry geometry)
        {
            SetSidecarState(geometry);
        }
    }
}
