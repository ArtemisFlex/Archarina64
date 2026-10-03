using System;
using System.Collections.Generic;
using OpenTK;

namespace SharpOcarina
{
    [Serializable]
    public sealed class DungeonCollisionVolume
    {
        public string Name;
        public Vector3d Min, Max;
        public int PolygonType = -1;
        public bool IsWallLike;
        public List<DungeonCollisionTriangle> Triangles = new List<DungeonCollisionTriangle>();
    }

    [Serializable]
    public sealed class DungeonCollisionTriangle
    {
        public Vector3d A, B, C;
    }

    public static class DungeonCollisionVolumeExtractor
    {
        public static List<DungeonCollisionVolume> Extract(ObjFile model, IList<ObjFile.Group> groups, Vector3d roomMin, Vector3d roomMax)
        {
            List<DungeonCollisionVolume> result = new List<DungeonCollisionVolume>();
            if (model == null || groups == null) return result;
            double roomWidth = Math.Max(1, roomMax.X - roomMin.X), roomDepth = Math.Max(1, roomMax.Z - roomMin.Z);
            foreach (ObjFile.Group group in groups)
            {
                Vector3d min, max;
                GetBounds(model, group, out min, out max);
                if (max.X <= min.X || max.Y <= min.Y || max.Z <= min.Z) continue;
                double height = max.Y - min.Y, width = max.X - min.X, depth = max.Z - min.Z;
                bool floorLike = height <= 24 && (width >= roomWidth * 0.35 || depth >= roomDepth * 0.35);
                bool roomShell = width >= roomWidth * 0.9 && depth >= roomDepth * 0.9;
                if (floorLike || roomShell) continue;
                if (height < 48) continue;
                DungeonCollisionVolume volume = new DungeonCollisionVolume
                {
                    Name = group.Name,
                    Min = min,
                    Max = max,
                    PolygonType = group.PolyType,
                    IsWallLike = height >= Math.Max(width, depth) * 0.5
                };
                foreach (ObjFile.Triangle triangle in group.Triangles ?? new List<ObjFile.Triangle>())
                {
                    if (triangle.VertIndex == null || triangle.VertIndex.Length < 3) continue;
                    int ia = triangle.VertIndex[0], ib = triangle.VertIndex[1], ic = triangle.VertIndex[2];
                    if (ia < 0 || ib < 0 || ic < 0 || ia >= model.Vertices.Count || ib >= model.Vertices.Count || ic >= model.Vertices.Count) continue;
                    volume.Triangles.Add(new DungeonCollisionTriangle
                    {
                        A = model.Vertices[ia].ToVector3d(), B = model.Vertices[ib].ToVector3d(), C = model.Vertices[ic].ToVector3d()
                    });
                }
                result.Add(volume);
            }
            return result;
        }

        private static void GetBounds(ObjFile model, ObjFile.Group group, out Vector3d min, out Vector3d max)
        {
            min = Vector3d.Zero; max = Vector3d.Zero; bool found = false;
            if (group == null || group.Triangles == null) return;
            foreach (ObjFile.Triangle triangle in group.Triangles ?? new List<ObjFile.Triangle>())
                foreach (int index in triangle.VertIndex ?? new int[0])
                {
                    if (index < 0 || index >= model.Vertices.Count) continue;
                    Vector3d value = model.Vertices[index].ToVector3d();
                    if (!found) { min = value; max = value; found = true; }
                    else
                    {
                        min = new Vector3d(Math.Min(min.X, value.X), Math.Min(min.Y, value.Y), Math.Min(min.Z, value.Z));
                        max = new Vector3d(Math.Max(max.X, value.X), Math.Max(max.Y, value.Y), Math.Max(max.Z, value.Z));
                    }
                }
        }
    }
}
