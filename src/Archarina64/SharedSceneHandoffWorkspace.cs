using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenTK;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    public partial class MainForm
    {
        /// <summary>
        /// Applies the latest Scene/Modeling snapshot to the editable Dungeon
        /// workspace. Native room meshes remain local; transition actors,
        /// gameplay packages, names, and authored tile references cross the
        /// boundary through the neutral handoff contract.
        /// </summary>
        private void ApplyLatestSharedSceneHandoff()
        {
            if (CurrentScene == null) throw new InvalidOperationException("Open or create a Dungeon workspace first.");
            SharedSceneHandoff handoff;
            if (!ArcharinaWorkspace.SharedWorkspace.TryGetSceneHandoff("SharpOcarina", out handoff))
                throw new InvalidOperationException("SharpOcarina has not published a scene handoff in this editor session.");

            SandboxRecord();
            List<string> diagnostics = new List<string>();
            int addedRooms = EnsureHandoffRooms(handoff, diagnostics);
            int addedConnections = ApplyHandoffConnections(handoff, diagnostics);
            int placedTiles = ApplyHandoffTiles(handoff, diagnostics);
            int addedGameplay = ApplyHandoffGameplay(handoff, diagnostics);

            if (!string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile) || CurrentScene.ColModel != null)
            {
                try { PersistSandboxGeometry(); }
                catch (Exception error) { diagnostics.Add("Sidecar persistence skipped: " + error.Message); }
            }
            NativeGameplayMaterializer.Apply(CurrentScene);
            if (CurrentDocument != null) CurrentDocument.MarkChanged("Applied shared Scene/Modeling handoff");
            savechanges = true;
            manualRoomListItems = true;
            RefreshRoomListItems();
            if (RoomList.Items.Count > 0 && RoomList.SelectedIndex < 0) SelectRoom(0);
            RefreshSandboxRooms();
            RefreshSandboxContents();
            RefreshSandboxLayers();
            ApplySandboxPreview();
            FocusSandboxRoom();

            string summary = string.Format("Applied {0}: {1} room(s), {2} connection(s), {3} gameplay package(s), {4} tile placement(s).",
                handoff.Publisher, addedRooms, addedConnections, addedGameplay, placedTiles);
            sandboxStatus.Text = diagnostics.Count == 0 ? summary : summary + " " + diagnostics.Count + " diagnostic(s).";
            if (diagnostics.Count > 0)
                MessageBox.Show(this, summary + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, diagnostics.ToArray()),
                    "Shared scene handoff", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private int EnsureHandoffRooms(SharedSceneHandoff handoff, IList<string> diagnostics)
        {
            int highest = (handoff.Rooms ?? new List<SharedRoomHandoff>()).Where(r => r != null)
                .Select(r => r.RoomIndex).Where(index => index >= 0).DefaultIfEmpty(-1).Max();
            int added = 0;
            while (CurrentScene.Rooms.Count <= highest)
            {
                ZScene.ZRoom room = CreateEmptyDungeonRoom();
                room.ModelShortFilename = "Dungeon Room " + CurrentScene.Rooms.Count.ToString("D2") + " (handoff)";
                CurrentScene.Rooms.Add(room); added++;
            }
            foreach (SharedRoomHandoff source in handoff.Rooms ?? new List<SharedRoomHandoff>())
            {
                if (source == null || source.RoomIndex < 0 || source.RoomIndex >= CurrentScene.Rooms.Count) continue;
                ZScene.ZRoom target = CurrentScene.Rooms[source.RoomIndex];
                if (string.IsNullOrWhiteSpace(target.ModelShortFilename) || target.ModelShortFilename.IndexOf("(empty)", StringComparison.OrdinalIgnoreCase) >= 0)
                    target.ModelShortFilename = string.IsNullOrWhiteSpace(source.Name) ? "Dungeon Room " + source.RoomIndex.ToString("D2") : source.Name;
                if (!string.IsNullOrWhiteSpace(source.GeometryAssetPath) && !IsTileAssetPath(source.GeometryAssetPath))
                    diagnostics.Add("Room " + source.RoomIndex + " references native geometry " + Path.GetFileName(source.GeometryAssetPath) + "; native mesh ownership remains in SharpOcarina.");
            }
            return added;
        }

        private int ApplyHandoffConnections(SharedSceneHandoff handoff, IList<string> diagnostics)
        {
            int added = 0;
            foreach (SharedConnectionHandoff source in handoff.Connections ?? new List<SharedConnectionHandoff>())
            {
                if (source == null || source.FromRoom < 0 || source.ToRoom < 0 || source.FromRoom >= CurrentScene.Rooms.Count || source.ToRoom >= CurrentScene.Rooms.Count || source.FromRoom == source.ToRoom)
                {
                    diagnostics.Add("Skipped an invalid handoff connection.");
                    continue;
                }
                if (source.ActorNumber == 0)
                {
                    diagnostics.Add("Skipped a connection without a native transition actor id.");
                    continue;
                }
                if (CurrentScene.Transitions.Any(existing => existing != null && existing.FrontSwitchTo == source.FromRoom && existing.BackSwitchTo == source.ToRoom &&
                    Math.Abs(existing.XPos - source.X) < 0.01 && Math.Abs(existing.YPos - source.Y) < 0.01 && Math.Abs(existing.ZPos - source.Z) < 0.01)) continue;
                if (CurrentScene.Transitions.Count >= RoomConnections.MaximumConnections)
                {
                    diagnostics.Add("Skipped a connection because the OoT transition limit is 64.");
                    continue;
                }
                short rotation = unchecked((short)(int)Math.Round((source.Yaw % 360.0) * 65536.0 / 360.0));
                CurrentScene.Transitions.Add(new ZActor((byte)source.FromRoom, source.FrontCamera, (byte)source.ToRoom, source.BackCamera,
                    source.ActorNumber, (float)source.X, (float)source.Y, (float)source.Z, rotation, source.Variable));
                added++;
            }
            return added;
        }

        private int ApplyHandoffGameplay(SharedSceneHandoff handoff, IList<string> diagnostics)
        {
            if (handoff.Gameplay == null) return 0;
            if (CurrentScene.SandboxGameplayPackages == null) CurrentScene.SandboxGameplayPackages = new List<RoomPackage>();
            int added = 0;
            foreach (SharedGameplayPackage source in handoff.Gameplay)
            {
                if (source == null) continue;
                if (CurrentScene.SandboxGameplayPackages.Any(existing => existing != null && existing.PlacementId == source.PlacementId &&
                    !string.IsNullOrEmpty(source.PlacementId))) continue;
                try { CurrentScene.SandboxGameplayPackages.Add(RoomPackageSharedAdapter.Import(source)); added++; }
                catch (Exception error) { diagnostics.Add("Skipped gameplay package " + (source.Name ?? "unnamed") + ": " + error.Message); }
            }
            return added;
        }

        private int ApplyHandoffTiles(SharedSceneHandoff handoff, IList<string> diagnostics)
        {
            List<Tuple<SharedRoomHandoff, string>> tileSources = new List<Tuple<SharedRoomHandoff, string>>();
            foreach (SharedRoomHandoff room in handoff.Rooms ?? new List<SharedRoomHandoff>())
            {
                if (room == null || !IsTileAssetPath(room.GeometryAssetPath)) continue;
                string path = ResolveHandoffAssetPath(handoff, room.GeometryAssetPath);
                if (!File.Exists(path))
                {
                    diagnostics.Add("Skipped tile " + Path.GetFileName(room.GeometryAssetPath) + "; the referenced asset was not found.");
                    continue;
                }
                tileSources.Add(Tuple.Create(room, path));
            }
            if (tileSources.Count == 0) return 0;
            try
            {
                PersistSandboxGeometry();
                TileSceneGeometry geometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
                int placed = 0;
                foreach (Tuple<SharedRoomHandoff, string> tileSource in tileSources)
                {
                    SharedRoomHandoff source = tileSource.Item1;
                    if (source.RoomIndex < 0 || source.RoomIndex >= geometry.Rooms.Count) { diagnostics.Add("Skipped tile outside the destination room range."); continue; }
                    SectionTile tile;
                    try { tile = TileStorage.Load<SectionTile>(tileSource.Item2); }
                    catch (Exception error) { diagnostics.Add("Skipped tile " + Path.GetFileName(tileSource.Item2) + ": " + error.Message); continue; }
                    try
                    {
                        int surface = source.SurfaceOverride;
                        if (tile.SourceScene == geometry.SceneId && surface < 0) surface = -1;
                        if (tile.SourceScene != geometry.SceneId && surface < 0)
                        {
                            diagnostics.Add("Skipped cross-scene tile " + Path.GetFileName(source.GeometryAssetPath) + "; no destination collision surface mapping was supplied.");
                            continue;
                        }
                        geometry.Place(tile, source.RoomIndex, new Vector3d(source.X, source.Y, source.Z), source.Turns, surface, null, tileSource.Item2, source.Scale <= 0 ? 1.0 : source.Scale);
                        placed++;
                    }
                    catch (Exception error) { diagnostics.Add("Skipped tile " + Path.GetFileName(source.GeometryAssetPath) + ": " + error.Message); }
                }
                if (placed > 0)
                {
                    string path = TileStorage.Save(geometry, false, geometry.Rooms.Concat(new[] { geometry.Collision }));
                    RestoreSandboxGeometry(path);
                }
                return placed;
            }
            catch (Exception error)
            {
                diagnostics.Add("Tile handoff could not be applied: " + error.Message);
                return 0;
            }
        }

        private static bool IsTileAssetPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.EndsWith(".ztile.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveHandoffAssetPath(SharedSceneHandoff handoff, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
            if (!string.IsNullOrWhiteSpace(handoff.SourcePath))
            {
                string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(handoff.SourcePath));
                if (!string.IsNullOrWhiteSpace(sourceDirectory))
                {
                    string sourceRelative = Path.Combine(sourceDirectory, path);
                    if (File.Exists(sourceRelative)) return sourceRelative;
                }
            }
            string sharedRelative = Path.Combine(ArcharinaWorkspace.SharedAuthoredGeometryPath, path);
            return File.Exists(sharedRelative) ? sharedRelative : path;
        }
    }
}
