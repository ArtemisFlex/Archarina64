using System;
using System.Collections.Generic;
using OpenTK;

namespace SharpOcarina
{
    [Serializable]
    public sealed class DungeonTraversalObstacle
    {
        public string Name;
        public Vector3d Min, Max;
    }

    public static class DungeonTraversalGeometry
    {
        public static bool Validate(DungeonTraversalConstraint constraint, Vector3d roomMin, Vector3d roomMax, out string error)
        {
            return Validate(constraint, roomMin, roomMax, null, out error);
        }

        public static bool Validate(DungeonTraversalConstraint constraint, Vector3d roomMin, Vector3d roomMax, IEnumerable<DungeonCollisionVolume> collisionVolumes, out string error)
        {
            error = null;
            if (constraint == null) { error = "Traversal contract is missing."; return false; }
            if (!Finite(constraint.Start) || !Finite(constraint.End)) { error = "Traversal endpoints are not finite."; return false; }
            bool hasBounds = roomMax.X > roomMin.X || roomMax.Y > roomMin.Y || roomMax.Z > roomMin.Z;
            if (hasBounds && (!Inside(constraint.Start, roomMin, roomMax, 50) || !Inside(constraint.End, roomMin, roomMax, 50)))
            {
                error = "Traversal endpoint lies outside the room collision bounds.";
                return false;
            }
            double clearance = constraint.ClearanceRadius > 0 ? constraint.ClearanceRadius : 0;
            List<DungeonTraversalObstacle> obstacles = new List<DungeonTraversalObstacle>(constraint.Obstacles ?? new List<DungeonTraversalObstacle>());
            foreach (DungeonTraversalObstacle obstacle in obstacles)
            {
                if (obstacle == null) continue;
                if (SegmentIntersectsBox(constraint.Start, constraint.End, obstacle.Min, obstacle.Max, clearance))
                {
                    error = "Traversal path intersects obstacle " + (string.IsNullOrWhiteSpace(obstacle.Name) ? "<unnamed>" : obstacle.Name) + ".";
                    return false;
                }
            }
            foreach (DungeonCollisionVolume volume in collisionVolumes ?? new List<DungeonCollisionVolume>())
            {
                if (volume == null || Contains(constraint.Start, volume.Min, volume.Max) || Contains(constraint.End, volume.Min, volume.Max)) continue;
                if (!SegmentIntersectsBox(constraint.Start, constraint.End, volume.Min, volume.Max, clearance)) continue;
                foreach (DungeonCollisionTriangle triangle in volume.Triangles ?? new List<DungeonCollisionTriangle>())
                    if (SegmentIntersectsTriangle(constraint.Start, constraint.End, triangle, clearance))
                    {
                        error = "Traversal path intersects collision triangle " + (string.IsNullOrWhiteSpace(volume.Name) ? "<unnamed>" : volume.Name) + ".";
                        return false;
                    }
            }
            return true;
        }

        private static bool Finite(Vector3d value)
        {
            return !double.IsNaN(value.X) && !double.IsInfinity(value.X) && !double.IsNaN(value.Y) && !double.IsInfinity(value.Y) && !double.IsNaN(value.Z) && !double.IsInfinity(value.Z);
        }

        private static bool Inside(Vector3d value, Vector3d min, Vector3d max, double tolerance)
        {
            return value.X >= min.X - tolerance && value.X <= max.X + tolerance && value.Y >= min.Y - tolerance && value.Y <= max.Y + tolerance && value.Z >= min.Z - tolerance && value.Z <= max.Z + tolerance;
        }

        private static bool Contains(Vector3d value, Vector3d min, Vector3d max)
        {
            return value.X >= Math.Min(min.X, max.X) && value.X <= Math.Max(min.X, max.X) &&
                value.Y >= Math.Min(min.Y, max.Y) && value.Y <= Math.Max(min.Y, max.Y) &&
                value.Z >= Math.Min(min.Z, max.Z) && value.Z <= Math.Max(min.Z, max.Z);
        }

        private static bool SegmentIntersectsBox(Vector3d start, Vector3d end, Vector3d min, Vector3d max, double padding)
        {
            double tMin = 0, tMax = 1;
            double[] a = { start.X, start.Y, start.Z }, b = { end.X, end.Y, end.Z };
            double[] lo = { Math.Min(min.X, max.X) - padding, Math.Min(min.Y, max.Y) - padding, Math.Min(min.Z, max.Z) - padding };
            double[] hi = { Math.Max(min.X, max.X) + padding, Math.Max(min.Y, max.Y) + padding, Math.Max(min.Z, max.Z) + padding };
            for (int axis = 0; axis < 3; axis++)
            {
                double delta = b[axis] - a[axis];
                if (Math.Abs(delta) < 0.000001)
                {
                    if (a[axis] < lo[axis] || a[axis] > hi[axis]) return false;
                    continue;
                }
                double t1 = (lo[axis] - a[axis]) / delta, t2 = (hi[axis] - a[axis]) / delta;
                if (t1 > t2) { double swap = t1; t1 = t2; t2 = swap; }
                tMin = Math.Max(tMin, t1); tMax = Math.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
            return true;
        }

        private static bool SegmentIntersectsTriangle(Vector3d start, Vector3d end, DungeonCollisionTriangle triangle, double padding)
        {
            if (triangle == null) return false;
            Vector3d direction = end - start;
            Vector3d edge1 = triangle.B - triangle.A, edge2 = triangle.C - triangle.A;
            Vector3d p = Vector3d.Cross(direction, edge2);
            double determinant = Vector3d.Dot(edge1, p);
            if (Math.Abs(determinant) < 0.000001) return false;
            double inverse = 1.0 / determinant;
            Vector3d toStart = start - triangle.A;
            double u = Vector3d.Dot(toStart, p) * inverse;
            if (u < -0.001 || u > 1.001) return false;
            Vector3d q = Vector3d.Cross(toStart, edge1);
            double v = Vector3d.Dot(direction, q) * inverse;
            if (v < -0.001 || u + v > 1.001) return false;
            double t = Vector3d.Dot(edge2, q) * inverse;
            if (t >= -0.001 && t <= 1.001) return true;
            if (padding <= 0) return false;
            for (int sample = 0; sample <= 24; sample++)
            {
                double amount = sample / 24.0;
                Vector3d point = start + direction * amount;
                if (DistanceToTriangle(point, triangle) <= padding) return true;
            }
            return false;
        }

        private static double DistanceToTriangle(Vector3d point, DungeonCollisionTriangle triangle)
        {
            Vector3d normal = Vector3d.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            double normalLength = normal.Length;
            if (normalLength < 0.000001) return Math.Min(DistanceToSegment(point, triangle.A, triangle.B), Math.Min(DistanceToSegment(point, triangle.B, triangle.C), DistanceToSegment(point, triangle.C, triangle.A)));
            double signedDistance = Vector3d.Dot(point - triangle.A, normal) / normalLength;
            Vector3d projected = point - normal * (signedDistance / normalLength);
            if (PointInTriangle(projected, triangle, normal)) return Math.Abs(signedDistance);
            return Math.Min(DistanceToSegment(point, triangle.A, triangle.B), Math.Min(DistanceToSegment(point, triangle.B, triangle.C), DistanceToSegment(point, triangle.C, triangle.A)));
        }

        private static bool PointInTriangle(Vector3d point, DungeonCollisionTriangle triangle, Vector3d normal)
        {
            Vector3d ab = Vector3d.Cross(triangle.B - triangle.A, point - triangle.A);
            Vector3d bc = Vector3d.Cross(triangle.C - triangle.B, point - triangle.B);
            Vector3d ca = Vector3d.Cross(triangle.A - triangle.C, point - triangle.C);
            return Vector3d.Dot(ab, normal) >= -0.0001 && Vector3d.Dot(bc, normal) >= -0.0001 && Vector3d.Dot(ca, normal) >= -0.0001;
        }

        private static double DistanceToSegment(Vector3d point, Vector3d start, Vector3d end)
        {
            Vector3d direction = end - start;
            double lengthSquared = Vector3d.Dot(direction, direction);
            if (lengthSquared < 0.000001) return (point - start).Length;
            double amount = Math.Max(0, Math.Min(1, Vector3d.Dot(point - start, direction) / lengthSquared));
            return (point - (start + direction * amount)).Length;
        }
    }
}
