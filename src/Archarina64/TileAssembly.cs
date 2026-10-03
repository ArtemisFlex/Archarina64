using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Serialization;
using OpenTK;

namespace SharpOcarina
{
    public sealed class TileSocket
    {
        public string Id, Name = "Door", ConnectedTo;
        public double X, Y, Z, Facing, Width = 100, Height = 180;
        public int Room = -1;
        [XmlIgnore] public Vector3d Position { get { return new Vector3d(X, Y, Z); } set { X = value.X; Y = value.Y; Z = value.Z; } }
        public TileSocket Clone() { return (TileSocket)MemberwiseClone(); }
        public void Validate()
        {
            if (new[] { X, Y, Z, Facing, Width, Height }.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || Width <= 0 || Height <= 0 || Width > 4096 || Height > 4096)
                throw new InvalidOperationException("Socket coordinates must be finite; width/height must be between 0 and 4096.");
        }
        public TileSocket Placed(Vector3d position, int turns, int room, string id, double scale = 1.0)
        {
            Validate();
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) throw new ArgumentOutOfRangeException("scale");
            TileSocket result = Clone(); result.Position = TileAssembly.Rotate(Position * scale, turns) + position;
            // Rotate() turns +X toward -Z, so doorway yaw follows the same
            // clockwise convention as the geometry.
            result.Facing = TileAssembly.Angle(Facing - turns * 90); result.Room = room; result.Id = id; result.ConnectedTo = null;
            result.Width *= scale; result.Height *= scale;
            if (new[] { result.X, result.Y, result.Z }.Any(v => v < short.MinValue || v > short.MaxValue)) throw new InvalidOperationException("Socket is outside the N64 coordinate range.");
            return result;
        }
    }

    public sealed class TileSnap
    {
        public int SourceSocket, Turns;
        public string TargetId;
        public Vector3d Position;
        public double Distance;
    }

    public static class TileAssembly
    {
        public static Vector3d Rotate(Vector3d p, int turns)
        {
            for (int i = 0; i < ((turns % 4) + 4) % 4; i++) p = new Vector3d(p.Z, p.Y, -p.X);
            return p;
        }
        public static double Angle(double value) { return ((value + 180) % 360 + 360) % 360 - 180; }

        public static Vector3d FacingVector(double facing)
        {
            double radians = facing * Math.PI / 180.0;
            return new Vector3d(Math.Cos(radians), 0, Math.Sin(radians));
        }

        // OoT actor yaw uses 0 degrees as +Z. Tile sockets use 0 degrees as
        // +X, so convert the native transition yaw into socket space first.
        public static double RoomDoorFacing(double transitionYawDegrees, bool isFront)
        {
            return Angle(90.0 - transitionYawDegrees + (isFront ? 0 : 180));
        }

        public static double NativeDoorYaw(double socketFacingDegrees)
        {
            return Angle(90.0 - socketFacingDegrees);
        }

        public static TileSnap FindSnap(IList<TileSocket> sockets, IEnumerable<TileSocket> targets, Vector3d requested, int turns, double radius)
        {
            if (double.IsNaN(radius) || double.IsInfinity(radius) || radius <= 0) throw new ArgumentOutOfRangeException("radius");
            TileSnap best = null;
            var orderedTargets = targets.Where(t => string.IsNullOrEmpty(t.ConnectedTo)).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
            foreach (TileSocket target in orderedTargets)
            {
                target.Validate();
                for (int offset = 0; offset < 4; offset++)
                {
                    int rotation = ((turns + offset) % 4 + 4) % 4;
                    for (int i = 0; i < sockets.Count; i++)
                    {
                        TileSocket source = sockets[i]; source.Validate();
                        if (Math.Abs(source.Width - target.Width) > 0.01 || Math.Abs(source.Height - target.Height) > 0.01 ||
                            Math.Abs(Angle(source.Facing - rotation * 90 - target.Facing - 180)) > 0.01) continue;
                        Vector3d rotated = Rotate(source.Position, rotation);
                        double distance = (requested + rotated - target.Position).Length;
                        if (distance > radius || best != null && distance >= best.Distance - 0.000001) continue;
                        best = new TileSnap { SourceSocket = i, TargetId = target.Id, Turns = rotation, Position = target.Position - rotated, Distance = distance };
                    }
                }
            }
            return best;
        }

        public static HashSet<int> FacesInBox(ObjFile model, IList<ObjFile.Group> groups, Vector3d min, Vector3d max, out int crossing)
        {
            if (new[] { min.X, min.Y, min.Z, max.X, max.Y, max.Z }.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
                throw new InvalidOperationException("Each box minimum must be smaller than its maximum.");
            crossing = 0; int index = 0; HashSet<int> selected = new HashSet<int>();
            foreach (ObjFile.Triangle triangle in groups.SelectMany(g => g.Triangles))
            {
                var p = triangle.VertIndex.Select(i => model.Vertices[i]).ToArray();
                bool inside = p.All(v => v.X >= min.X && v.X <= max.X && v.Y >= min.Y && v.Y <= max.Y && v.Z >= min.Z && v.Z <= max.Z);
                if (inside) selected.Add(index);
                else if (p.Max(v => v.X) >= min.X && p.Min(v => v.X) <= max.X && p.Max(v => v.Y) >= min.Y && p.Min(v => v.Y) <= max.Y && p.Max(v => v.Z) >= min.Z && p.Min(v => v.Z) <= max.Z) crossing++;
                index++;
            }
            return selected;
        }

        public static HashSet<int> ConnectedFaces(ObjFile model, IList<ObjFile.Group> groups, ISet<int> seeds)
        {
            var faces = groups.SelectMany(g => g.Triangles).ToList();
            var edges = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var keys = new List<string[]>();
            // Captured OBJ meshes can duplicate vertices at material/UV seams; compare positions.
            Func<int, string> vertex = i => model.Vertices[i].X.ToString("R", CultureInfo.InvariantCulture) + "," + model.Vertices[i].Y.ToString("R", CultureInfo.InvariantCulture) + "," + model.Vertices[i].Z.ToString("R", CultureInfo.InvariantCulture);
            for (int i = 0; i < faces.Count; i++)
            {
                string[] points = faces[i].VertIndex.Select(vertex).ToArray(); string[] triangleEdges = new string[3];
                for (int e = 0; e < 3; e++)
                {
                    string a = points[e], b = points[(e + 1) % 3]; string key = string.CompareOrdinal(a, b) < 0 ? a + ";" + b : b + ";" + a;
                    triangleEdges[e] = key; List<int> incident;
                    if (!edges.TryGetValue(key, out incident)) { incident = new List<int>(); edges.Add(key, incident); }
                    incident.Add(i);
                }
                keys.Add(triangleEdges);
            }
            HashSet<int> result = new HashSet<int>(seeds.Where(i => i >= 0 && i < faces.Count)); Queue<int> pending = new Queue<int>(result);
            while (pending.Count > 0)
                foreach (string edge in keys[pending.Dequeue()])
                    foreach (int face in edges[edge]) if (result.Add(face)) pending.Enqueue(face);
            return result;
        }
    }
}
