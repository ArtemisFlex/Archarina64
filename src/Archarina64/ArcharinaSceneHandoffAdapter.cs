using System;
using System.Collections.Generic;
using System.Linq;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    /// <summary>
    /// Exports the dungeon editor's current native scene and tile sidecar into
    /// the renderer-neutral handoff used by the shared application session.
    /// </summary>
    public static class ArcharinaSceneHandoffAdapter
    {
        public static SharedSceneHandoff Export(ArcharinaSceneDocument document, SharedEditorWorkspace workspace)
        {
            if (document == null) throw new ArgumentNullException("document");
            if (workspace == null) throw new ArgumentNullException("workspace");
            ZScene scene = document.NativeScene;
            SharedSceneHandoff result = new SharedSceneHandoff
            {
                WorkspaceId = workspace.WorkspaceId,
                DocumentId = document.DocumentId,
                Publisher = "Archarina64",
                SceneName = scene == null ? string.Empty : scene.Name,
                SourcePath = document.SourcePath,
                Revision = document.Revision
            };
            if (scene != null)
            {
                for (int i = 0; i < (scene.Rooms == null ? 0 : scene.Rooms.Count); i++)
                {
                    ZScene.ZRoom room = scene.Rooms[i];
                    result.Rooms.Add(new SharedRoomHandoff
                    {
                        Id = "native-room-" + i,
                        RoomIndex = i,
                        Name = room == null ? string.Empty : room.ModelShortFilename
                    });
                }
                ExportTransitions(scene.Transitions, scene.Rooms == null ? 0 : scene.Rooms.Count, result.Connections);
            }

            TileSceneGeometry sidecar = document.SidecarState;
            if (sidecar != null)
            {
                foreach (PlacedTile tile in sidecar.PlacedTiles ?? new List<PlacedTile>())
                    result.Rooms.Add(new SharedRoomHandoff
                    {
                        Id = tile.Id,
                        RoomIndex = tile.Room,
                        Name = tile.Name,
                        GeometryAssetPath = tile.TilePath,
                        X = tile.X, Y = tile.Y, Z = tile.Z,
                        Turns = tile.Turns, Scale = tile.Scale, SurfaceOverride = tile.SurfaceOverride
                    });
                foreach (TileSocket socket in sidecar.Sockets ?? new List<TileSocket>())
                    result.Rooms.AddOrGet(socket.Room).Doorways.Add(new SharedDoorwayHandoff
                    {
                        Id = socket.Id, Name = socket.Name, ConnectedTo = socket.ConnectedTo,
                        RoomIndex = socket.Room, X = socket.X, Y = socket.Y, Z = socket.Z,
                        Facing = socket.Facing, Width = socket.Width, Height = socket.Height
                    });
                foreach (RoomPackage package in sidecar.GameplayPackages ?? new List<RoomPackage>())
                    if (package != null) result.Gameplay.Add(RoomPackageSharedAdapter.Export(package));
                if (!string.IsNullOrWhiteSpace(sidecar.TextureThemeTilePath))
                    result.Assets.Add(new SharedAssetReference { Path = sidecar.TextureThemeTilePath, Kind = "texture-theme", Name = "Dungeon texture theme" });
            }

            return result;
        }

        private static void ExportTransitions(IList<ZActor> transitions, int roomCount, IList<SharedConnectionHandoff> target)
        {
            for (int i = 0; i < (transitions == null ? 0 : transitions.Count); i++)
            {
                ZActor transition = transitions[i];
                if (transition == null) continue;
                if (transition.FrontSwitchTo < 0 || transition.BackSwitchTo < 0 ||
                    transition.FrontSwitchTo >= roomCount || transition.BackSwitchTo >= roomCount ||
                    transition.FrontSwitchTo == transition.BackSwitchTo || transition.Number == 0 ||
                    new[] { transition.XPos, transition.YPos, transition.ZPos }.Any(value => float.IsNaN(value) || float.IsInfinity(value)))
                    continue;
                target.Add(new SharedConnectionHandoff
                {
                    Id = "transition-" + i,
                    FromRoom = transition.FrontSwitchTo,
                    ToRoom = transition.BackSwitchTo,
                    ActorNumber = transition.Number,
                    Variable = transition.Variable,
                    FrontCamera = transition.FrontCamera,
                    BackCamera = transition.BackCamera,
                    X = transition.XPos, Y = transition.YPos, Z = transition.ZPos,
                    Yaw = transition.YRot * 360.0 / 65536.0,
                    OneWay = false
                });
            }
        }
    }

    internal static class SharedRoomHandoffListExtensions
    {
        internal static SharedRoomHandoff AddOrGet(this IList<SharedRoomHandoff> rooms, int roomIndex)
        {
            SharedRoomHandoff room = rooms.FirstOrDefault(x => x != null && x.Id == "native-room-" + roomIndex);
            if (room != null) return room;
            room = new SharedRoomHandoff { Id = "sidecar-room-" + roomIndex, RoomIndex = roomIndex, Name = "Room " + roomIndex };
            rooms.Add(room);
            return room;
        }
    }
}
