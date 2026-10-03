using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using OpenTK;

namespace SharpOcarina
{
    [Serializable]
    public sealed class DungeonTilingRule
    {
        public int Scene = -1, Room = -1, Transition = -1;
        public double Width = 1200, Depth = 1600, Height = 1600, ForwardOffset;
        public bool IncludeWholeRoom;
        public string Name;
    }

    public sealed class DungeonRoomTilePlan
    {
        public int Scene, Room, Transition;
        public string Name, Warning;
        public Vector3d Min, Max;
        public TileSocket Socket;
        public double SocketWidth = 100, SocketHeight = 180;
        public HashSet<int> VisualFaces = new HashSet<int>();
        public HashSet<int> CollisionFaces = new HashSet<int>();
    }

    public sealed class SquareTubeCorridor
    {
        public Vector3d Start, End;
        public double Width = 200, Height = 240, Grid = 100;
        public int Turns;
        public void Validate()
        {
            if (new[] { Start.X, Start.Y, Start.Z, End.X, End.Y, End.Z, Width, Height, Grid }.Any(double.IsNaN) ||
                new[] { Start.X, Start.Y, Start.Z, End.X, End.Y, End.Z, Width, Height, Grid }.Any(double.IsInfinity))
                throw new InvalidOperationException("Corridor values must be finite.");
            if (Width <= 0 || Height <= 0 || Grid < 1 || Width > 4096 || Height > 4096)
                throw new InvalidOperationException("Corridor width and height must be between 1 and 4096.");
            if ((End - Start).Length < Grid) throw new InvalidOperationException("Corridor endpoints are too close together.");
            if (Turns < 0 || Turns > 3) throw new InvalidOperationException("Corridor turns must be 0 through 3.");
            if (new[] { Start.X, Start.Y, Start.Z, End.X, End.Y, End.Z }.Any(v => v < short.MinValue || v > short.MaxValue))
                throw new InvalidOperationException("Corridor is outside the N64 coordinate range.");
        }
        public Vector3d Direction { get { Vector3d value = End - Start; return value.LengthSquared == 0 ? Vector3d.UnitZ : value.Normalized(); } }
    }

    public static class DungeonRoomTiling
    {
        public static List<DungeonRoomTilePlan> Analyze(ZScene scene, int sceneId, int roomId, IEnumerable<DungeonTilingRule> rules)
        {
            if (scene == null || roomId < 0 || roomId >= scene.Rooms.Count) throw new InvalidOperationException("Choose an existing room.");
            ZScene.ZRoom room = scene.Rooms[roomId];
            if (room.ObjModel == null || scene.ColModel == null) throw new InvalidOperationException("The room needs visual and collision geometry.");
            var overrides = (rules ?? Enumerable.Empty<DungeonTilingRule>()).Where(r => r.Scene < 0 || r.Scene == sceneId).Where(r => r.Room < 0 || r.Room == roomId).ToList();
            var doors = scene.Transitions.Select((door, index) => new { door, index }).Where(x => x.door.FrontSwitchTo == roomId || x.door.BackSwitchTo == roomId).ToList();
            var plans = new List<DungeonRoomTilePlan>();
            foreach (var entry in doors)
            {
                bool front = entry.door.FrontSwitchTo == roomId;
                DungeonTilingRule rule = overrides.FirstOrDefault(r => r.Transition == entry.index) ?? overrides.FirstOrDefault(r => r.Transition < 0);
                Vector3d center = new Vector3d(entry.door.XPos, entry.door.YPos, entry.door.ZPos);
                double facing = TileAssembly.RoomDoorFacing(entry.door.YRot * 360.0 / 65536.0, front);
                Vector3d forward = TileAssembly.FacingVector(facing);
                double width = rule == null ? 1200 : rule.Width, depth = rule == null ? 1600 : rule.Depth, height = rule == null ? 1600 : rule.Height;
                if (width <= 0 || depth <= 0 || height <= 0) throw new InvalidOperationException("Entrance tiling rule dimensions must be positive.");
                Vector3d shifted = center + forward * (rule == null ? 0 : rule.ForwardOffset);
                Vector3d min = new Vector3d(shifted.X - width / 2, shifted.Y - height / 2, shifted.Z - depth / 2);
                Vector3d max = new Vector3d(shifted.X + width / 2, shifted.Y + height / 2, shifted.Z + depth / 2);
                DungeonRoomTilePlan plan = new DungeonRoomTilePlan { Scene = sceneId, Room = roomId, Transition = entry.index,
                    Name = rule != null && !string.IsNullOrWhiteSpace(rule.Name) ? rule.Name : string.Format("Scene {0:X2} Room {1:D2} Entrance {2:D2}", sceneId, roomId, entry.index), Min = min, Max = max,
                    Socket = new TileSocket { Id = "entrance_" + entry.index, Name = "Entrance " + entry.index, Position = center, Facing = facing, Room = roomId },
                    SocketWidth = width, SocketHeight = height };
                plan.Socket.Width = width;
                plan.Socket.Height = height;
                if (rule != null && rule.IncludeWholeRoom)
                {
                    plan.VisualFaces.UnionWith(Enumerable.Range(0, room.TrueGroups.SelectMany(g => g.Triangles).Count()));
                    plan.CollisionFaces.UnionWith(Enumerable.Range(0, scene.ColModel.Groups.SelectMany(g => g.Triangles).Count()));
                }
                else
                {
                    plan.VisualFaces = FacesByCentroid(room.ObjModel, room.TrueGroups, min, max);
                    plan.CollisionFaces = FacesByCentroid(scene.ColModel, scene.ColModel.Groups, min, max);
                    int visualCrossing, collisionCrossing;
                    TileAssembly.FacesInBox(room.ObjModel, room.TrueGroups, min, max, out visualCrossing);
                    TileAssembly.FacesInBox(scene.ColModel, scene.ColModel.Groups, min, max, out collisionCrossing);
                    if (visualCrossing > 0 || collisionCrossing > 0) plan.Warning = string.Format("{0} visual and {1} collision faces cross the entrance bounds.", visualCrossing, collisionCrossing);
                }
                if (plan.VisualFaces.Count == 0 || plan.CollisionFaces.Count == 0) plan.Warning = (plan.Warning == null ? "" : plan.Warning + " ") + "The entrance region has no complete visual or collision faces.";
                plans.Add(plan);
            }
            if (doors.Count == 0) plans.Add(new DungeonRoomTilePlan { Scene = sceneId, Room = roomId, Name = string.Format("Scene {0:X2} Room {1:D2} has no entrances", sceneId, roomId), Warning = "No native transition references this room." });
            return plans;
        }

        private static HashSet<int> FacesByCentroid(ObjFile model, IList<ObjFile.Group> groups, Vector3d min, Vector3d max)
        {
            int index = 0; var result = new HashSet<int>();
            foreach (ObjFile.Triangle triangle in groups.SelectMany(g => g.Triangles))
            {
                Vector3d center = triangle.VertIndex.Select(i => model.Vertices[i].ToVector3d()).Aggregate(Vector3d.Zero, (a, b) => a + b) / 3.0;
                if (center.X >= min.X && center.X <= max.X && center.Y >= min.Y && center.Y <= max.Y && center.Z >= min.Z && center.Z <= max.Z) result.Add(index);
                index++;
            }
            return result;
        }
    }
}
