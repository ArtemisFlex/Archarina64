using System;
using System.Collections.Generic;
using OpenTK;

namespace SharpOcarina
{
    /// <summary>
    /// Authoring primitives shared by the visual room model and the collision model.
    /// Coordinates use SharpOcarina's existing room coordinate system.
    /// </summary>
    public enum RoomPrimitiveType
    {
        Plane,
        Triangle,
        Cube,
        Ramp
    }

    public sealed class RoomPrimitiveSpec
    {
        public RoomPrimitiveType Type = RoomPrimitiveType.Cube;
        public string Name = "Primitive";
        public string MaterialName = "Default";
        public List<string> FaceMaterials = new List<string>();
        public string TexturePath = null;
        public Vector3d Min = new Vector3d(-50, 0, -50);
        public Vector3d Max = new Vector3d(50, 100, 50);
        public Vector3d A = new Vector3d(-50, 0, -50);
        public Vector3d B = new Vector3d(50, 0, -50);
        public Vector3d C = new Vector3d(0, 0, 50);
        public Vector3d Scale = Vector3d.One;
        public int CollisionPolyType = 0;
        public bool AddToCollision = true;
    }

    public static class RoomGeometryBuilder
    {
        private sealed class Face
        {
            public Vector3d[] Points;
            public Vector3d Normal;
            public string MaterialName;
            public Face(Vector3d[] points, Vector3d normal, string materialName)
            {
                Points = points;
                Normal = normal;
                MaterialName = materialName;
            }
        }

        public static void Append(ObjFile visual, ObjFile collision, RoomPrimitiveSpec spec)
        {
            if (visual == null) throw new ArgumentNullException("visual");
            if (spec == null) throw new ArgumentNullException("spec");

            string groupName = string.IsNullOrEmpty(spec.Name) ? "Primitive" : spec.Name;
            string materialName = string.IsNullOrEmpty(spec.MaterialName) ? "Default" : spec.MaterialName;
            EnsureMaterial(visual, materialName, spec.TexturePath);

            List<Face> faces = BuildFaces(spec, materialName);
            foreach (Face face in faces)
                EnsureMaterial(visual, face.MaterialName, spec.TexturePath);
            AppendFaces(visual, groupName, faces, false, spec.CollisionPolyType);

            if (collision != null && spec.AddToCollision)
                AppendFaces(collision, groupName + "_Collision", faces, true, spec.CollisionPolyType);
        }

        public static void AppendAll(ObjFile visual, ObjFile collision, IEnumerable<RoomPrimitiveSpec> primitives)
        {
            if (primitives == null) return;
            foreach (RoomPrimitiveSpec primitive in primitives)
                Append(visual, collision, primitive);
        }

        public static void AppendSquareTube(ObjFile visual, ObjFile collision, SquareTubeCorridor corridor, string materialName, int collisionPolyType)
        {
            AppendSquareTube(visual, collision, corridor, materialName, materialName, materialName, collisionPolyType);
        }

        public static void AppendSquareTube(ObjFile visual, ObjFile collision, SquareTubeCorridor corridor, string floorMaterial, string ceilingMaterial, string wallMaterial, int collisionPolyType)
        {
            if (corridor == null) throw new ArgumentNullException("corridor");
            corridor.Validate();
            Vector3d direction = corridor.End - corridor.Start;
            direction.Y = 0;
            if (direction.LengthSquared < 0.000001) direction = Vector3d.UnitZ;
            else direction.Normalize();
            Vector3d right = new Vector3d(direction.Z, 0, -direction.X);
            double half = corridor.Width * 0.5;
            double floor = Math.Min(corridor.Start.Y, corridor.End.Y);
            double ceiling = floor + corridor.Height;
            Vector3d leftStart = corridor.Start - right * half;
            Vector3d rightStart = corridor.Start + right * half;
            Vector3d leftEnd = corridor.End - right * half;
            Vector3d rightEnd = corridor.End + right * half;
            string floorTexture = string.IsNullOrWhiteSpace(floorMaterial) ? "Archarina64_Corridor" : floorMaterial;
            string ceilingTexture = string.IsNullOrWhiteSpace(ceilingMaterial) ? floorTexture : ceilingMaterial;
            string wallTexture = string.IsNullOrWhiteSpace(wallMaterial) ? floorTexture : wallMaterial;
            EnsureMaterial(visual, floorTexture, null);
            EnsureMaterial(visual, ceilingTexture, null);
            EnsureMaterial(visual, wallTexture, null);
            List<Vector3d[]> faces = new List<Vector3d[]>
            {
                new[] { new Vector3d(leftStart.X, floor, leftStart.Z), new Vector3d(rightStart.X, floor, rightStart.Z), new Vector3d(rightEnd.X, floor, rightEnd.Z), new Vector3d(leftEnd.X, floor, leftEnd.Z) },
                new[] { new Vector3d(leftStart.X, ceiling, leftStart.Z), new Vector3d(leftEnd.X, ceiling, leftEnd.Z), new Vector3d(rightEnd.X, ceiling, rightEnd.Z), new Vector3d(rightStart.X, ceiling, rightStart.Z) },
                new[] { new Vector3d(leftStart.X, floor, leftStart.Z), new Vector3d(leftEnd.X, floor, leftEnd.Z), new Vector3d(leftEnd.X, ceiling, leftEnd.Z), new Vector3d(leftStart.X, ceiling, leftStart.Z) },
                new[] { new Vector3d(rightStart.X, floor, rightStart.Z), new Vector3d(rightStart.X, ceiling, rightStart.Z), new Vector3d(rightEnd.X, ceiling, rightEnd.Z), new Vector3d(rightEnd.X, floor, rightEnd.Z) }
            };
            string[] materials = { floorTexture, ceilingTexture, wallTexture, wallTexture };
            for (int i = 0; i < faces.Count; i++)
            {
                AppendRawFaces(visual, "Archarina64_Corridor_" + i, new[] { faces[i] }, materials[i], 0);
                if (collision != null) AppendRawFaces(collision, "Archarina64_Corridor_Collision_" + i, new[] { faces[i] }, null, collisionPolyType);
            }
        }

        public static void AppendSquareTubePath(ObjFile visual, ObjFile collision, IList<Vector3d> points, double width, double height, double grid,
            string floorMaterial, string ceilingMaterial, string wallMaterial, int collisionPolyType)
        {
            if (points == null || points.Count < 2) throw new InvalidOperationException("A corridor path needs at least two points.");
            if (width <= 0 || height <= 0 || grid < 1) throw new InvalidOperationException("Corridor dimensions must be positive.");
            List<Vector3d> path = SimplifyTubePath(points);
            if (path.Count < 2) throw new InvalidOperationException("A corridor path needs two distinct points.");
            List<TubeRing> rings = new List<TubeRing>();
            double half = width * 0.5;
            double floor = path[0].Y;
            for (int i = 1; i < path.Count; i++) floor = Math.Min(floor, path[i].Y);
            double ceiling = floor + height;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3d incoming = i == 0 ? HorizontalDirection(path[0], path[1]) : HorizontalDirection(path[i - 1], path[i]);
                Vector3d outgoing = i == path.Count - 1 ? incoming : HorizontalDirection(path[i], path[i + 1]);
                Vector3d left = TubeJoin(path[i], incoming, outgoing, -1, half);
                Vector3d right = TubeJoin(path[i], incoming, outgoing, 1, half);
                rings.Add(new TubeRing { LeftBottom = new Vector3d(left.X, floor, left.Z), RightBottom = new Vector3d(right.X, floor, right.Z),
                    LeftTop = new Vector3d(left.X, ceiling, left.Z), RightTop = new Vector3d(right.X, ceiling, right.Z) });
            }
            string floorTexture = string.IsNullOrWhiteSpace(floorMaterial) ? "Archarina64_Corridor" : floorMaterial;
            string ceilingTexture = string.IsNullOrWhiteSpace(ceilingMaterial) ? floorTexture : ceilingMaterial;
            string wallTexture = string.IsNullOrWhiteSpace(wallMaterial) ? floorTexture : wallMaterial;
            EnsureMaterial(visual, floorTexture, null); EnsureMaterial(visual, ceilingTexture, null); EnsureMaterial(visual, wallTexture, null);
            List<Vector3d[]> floors = new List<Vector3d[]>(), ceilings = new List<Vector3d[]>(), leftWalls = new List<Vector3d[]>(), rightWalls = new List<Vector3d[]>();
            for (int i = 0; i + 1 < rings.Count; i++)
            {
                TubeRing a = rings[i], b = rings[i + 1];
                floors.Add(new[] { a.LeftBottom, a.RightBottom, b.RightBottom, b.LeftBottom });
                ceilings.Add(new[] { a.LeftTop, b.LeftTop, b.RightTop, a.RightTop });
                leftWalls.Add(new[] { a.LeftBottom, b.LeftBottom, b.LeftTop, a.LeftTop });
                rightWalls.Add(new[] { a.RightBottom, a.RightTop, b.RightTop, b.RightBottom });
            }
            AppendTubeStrip(visual, collision, "Archarina64_Corridor_Sweep_Floor", floors, floorTexture, collisionPolyType);
            AppendTubeStrip(visual, collision, "Archarina64_Corridor_Sweep_Ceiling", ceilings, ceilingTexture, collisionPolyType);
            AppendTubeStrip(visual, collision, "Archarina64_Corridor_Sweep_LeftWall", leftWalls, wallTexture, collisionPolyType);
            AppendTubeStrip(visual, collision, "Archarina64_Corridor_Sweep_RightWall", rightWalls, wallTexture, collisionPolyType);
            for (int i = 1; i + 1 < path.Count; i++)
            {
                Vector3d incoming = HorizontalDirection(path[i - 1], path[i]);
                Vector3d outgoing = HorizontalDirection(path[i], path[i + 1]);
                if (Math.Abs(Vector3d.Dot(incoming, outgoing)) < 0.999)
                    AppendSquareTubeCorner(visual, collision, path[i], width, height, floorTexture, ceilingTexture, wallTexture, collisionPolyType);
            }
        }

        // A height change is a single low-poly ramp prism, not a staircase
        // made from many boxes. Five quad faces become exactly ten triangles.
        public static void AppendLowPolyRamp(ObjFile visual, ObjFile collision, Vector3d start, Vector3d end,
            double width, double height, string floorMaterial, string ceilingMaterial, string wallMaterial, int collisionPolyType)
        {
            if (visual == null) throw new ArgumentNullException("visual");
            if (width <= 0 || height <= 0) throw new InvalidOperationException("Ramp dimensions must be positive.");
            Vector3d horizontal = new Vector3d(end.X - start.X, 0, end.Z - start.Z);
            if (horizontal.LengthSquared < 0.000001)
                throw new InvalidOperationException("A ramp needs horizontal run; use a native room transition for a pure vertical link.");
            horizontal.Normalize();
            Vector3d right = new Vector3d(horizontal.Z, 0, -horizontal.X);
            double half = width * 0.5;
            Vector3d startLeft = start - right * half, startRight = start + right * half;
            Vector3d endLeft = end - right * half, endRight = end + right * half;
            Vector3d startLeftTop = startLeft + Vector3d.UnitY * height, startRightTop = startRight + Vector3d.UnitY * height;
            Vector3d endLeftTop = endLeft + Vector3d.UnitY * height, endRightTop = endRight + Vector3d.UnitY * height;
            string floor = string.IsNullOrWhiteSpace(floorMaterial) ? "Archarina64_Corridor" : floorMaterial;
            string ceiling = string.IsNullOrWhiteSpace(ceilingMaterial) ? floor : ceilingMaterial;
            string wall = string.IsNullOrWhiteSpace(wallMaterial) ? floor : wallMaterial;
            EnsureMaterial(visual, floor, null); EnsureMaterial(visual, ceiling, null); EnsureMaterial(visual, wall, null);
            Vector3d[][] faces =
            {
                new[] { startLeft, startRight, endRight, endLeft },
                new[] { startLeftTop, endLeftTop, endRightTop, startRightTop },
                new[] { startLeft, endLeft, endLeftTop, startLeftTop },
                new[] { startRight, startRightTop, endRightTop, endRight },
                new[] { endLeft, endRight, endRightTop, endLeftTop }
            };
            string[] materials = { floor, ceiling, wall, wall, wall };
            for (int i = 0; i < faces.Length; i++)
            {
                AppendRawFaces(visual, "Archarina64_Corridor_Ramp_" + i, new[] { faces[i] }, materials[i], 0);
                if (collision != null)
                    AppendRawFaces(collision, "Archarina64_Corridor_Ramp_Collision_" + i, new[] { faces[i] }, null, collisionPolyType);
            }
        }

        private sealed class TubeRing
        {
            public Vector3d LeftBottom, RightBottom, LeftTop, RightTop;
        }

        private static List<Vector3d> SimplifyTubePath(IList<Vector3d> points)
        {
            List<Vector3d> result = new List<Vector3d>();
            foreach (Vector3d point in points)
            {
                if (result.Count == 0 || (point - result[result.Count - 1]).Length >= 1) result.Add(point);
            }
            for (int i = result.Count - 2; i > 0; i--)
            {
                Vector3d before = HorizontalDirection(result[i - 1], result[i]);
                Vector3d after = HorizontalDirection(result[i], result[i + 1]);
                if (Math.Abs(Vector3d.Dot(before, after) - 1) < 0.0001) result.RemoveAt(i);
            }
            return result;
        }

        private static Vector3d HorizontalDirection(Vector3d start, Vector3d end)
        {
            Vector3d direction = end - start; direction.Y = 0;
            if (direction.LengthSquared < 0.000001) return Vector3d.UnitX;
            direction.Normalize(); return direction;
        }

        private static Vector3d TubeJoin(Vector3d center, Vector3d incoming, Vector3d outgoing, int side, double half)
        {
            Vector3d incomingSide = new Vector3d(incoming.Z, 0, -incoming.X) * side;
            Vector3d outgoingSide = new Vector3d(outgoing.Z, 0, -outgoing.X) * side;
            Vector3d first = center + incomingSide * half, second = center + outgoingSide * half;
            double determinant = incoming.X * outgoing.Z - incoming.Z * outgoing.X;
            if (Math.Abs(determinant) < 0.000001) return second;
            Vector3d delta = second - first;
            double distance = (delta.X * outgoing.Z - delta.Z * outgoing.X) / determinant;
            Vector3d result = first + incoming * distance;
            double limit = half * 2.5;
            if ((result - center).Length > limit) return center + outgoingSide * half;
            return result;
        }

        private static void AppendTubeStrip(ObjFile visual, ObjFile collision, string name, IEnumerable<Vector3d[]> faces, string material, int collisionPolyType)
        {
            AppendRawFaces(visual, name, faces, material, 0);
            if (collision != null) AppendRawFaces(collision, name + "_Collision", faces, null, collisionPolyType);
        }

        // A corner module closes the four-way seam left when two axis-aligned tube pieces meet.
        // It is deliberately generated from the same materials and collision type as its neighbors.
        private static void AppendSquareTubeCorner(ObjFile visual, ObjFile collision, Vector3d center, double width, double height,
            string floorMaterial, string ceilingMaterial, string wallMaterial, int collisionPolyType)
        {
            double half = width * 0.5;
            Vector3d min = new Vector3d(center.X - half, center.Y, center.Z - half);
            Vector3d max = new Vector3d(center.X + half, center.Y + height, center.Z + half);
            string floor = string.IsNullOrWhiteSpace(floorMaterial) ? "Archarina64_Corridor" : floorMaterial;
            string ceiling = string.IsNullOrWhiteSpace(ceilingMaterial) ? floor : ceilingMaterial;
            string wall = string.IsNullOrWhiteSpace(wallMaterial) ? floor : wallMaterial;
            EnsureMaterial(visual, floor, null); EnsureMaterial(visual, ceiling, null); EnsureMaterial(visual, wall, null);
            List<Vector3d[]> faces = new List<Vector3d[]>
            {
                new[] { new Vector3d(min.X, min.Y, min.Z), new Vector3d(max.X, min.Y, min.Z), new Vector3d(max.X, min.Y, max.Z), new Vector3d(min.X, min.Y, max.Z) },
                new[] { new Vector3d(min.X, max.Y, min.Z), new Vector3d(min.X, max.Y, max.Z), new Vector3d(max.X, max.Y, max.Z), new Vector3d(max.X, max.Y, min.Z) },
                new[] { new Vector3d(min.X, min.Y, min.Z), new Vector3d(min.X, min.Y, max.Z), new Vector3d(min.X, max.Y, max.Z), new Vector3d(min.X, max.Y, min.Z) },
                new[] { new Vector3d(max.X, min.Y, max.Z), new Vector3d(max.X, min.Y, min.Z), new Vector3d(max.X, max.Y, min.Z), new Vector3d(max.X, max.Y, max.Z) }
            };
            string[] materials = { floor, ceiling, wall, wall };
            for (int i = 0; i < faces.Count; i++)
            {
                AppendRawFaces(visual, "Archarina64_Corridor_Corner_" + i, new[] { faces[i] }, materials[i], collisionPolyType);
                if (collision != null) AppendRawFaces(collision, "Archarina64_Corridor_Corner_Collision_" + i, new[] { faces[i] }, null, collisionPolyType);
            }
        }

        private static void AppendSquareTubeSegment(ObjFile visual, ObjFile collision, Vector3d start, Vector3d end, double width, double height,
            string floorMaterial, string ceilingMaterial, string wallMaterial, int collisionPolyType)
        {
            Vector3d axis = end - start;
            if (axis.LengthSquared < 0.000001) return;
            axis.Normalize();
            Vector3d reference = Math.Abs(Vector3d.Dot(axis, Vector3d.UnitY)) > 0.95 ? Vector3d.UnitZ : Vector3d.UnitY;
            Vector3d right = Vector3d.Cross(axis, reference);
            if (right.LengthSquared < 0.000001) right = Vector3d.UnitX;
            else right.Normalize();
            Vector3d up = Vector3d.Cross(right, axis);
            if (up.LengthSquared < 0.000001) up = Vector3d.UnitY;
            else up.Normalize();
            double halfWidth = width * 0.5, halfHeight = height * 0.5;
            Vector3d startCenter = start + up * halfHeight, endCenter = end + up * halfHeight;
            Vector3d a = startCenter - right * halfWidth - up * halfHeight;
            Vector3d b = startCenter + right * halfWidth - up * halfHeight;
            Vector3d c = startCenter + right * halfWidth + up * halfHeight;
            Vector3d d = startCenter - right * halfWidth + up * halfHeight;
            Vector3d e = endCenter - right * halfWidth - up * halfHeight;
            Vector3d f = endCenter + right * halfWidth - up * halfHeight;
            Vector3d g = endCenter + right * halfWidth + up * halfHeight;
            Vector3d h = endCenter - right * halfWidth + up * halfHeight;
            List<Vector3d[]> faces = new List<Vector3d[]>
            {
                new[] { a, e, f, b }, new[] { d, c, g, h },
                new[] { a, d, h, e }, new[] { b, f, g, c }
            };
            string floor = string.IsNullOrWhiteSpace(floorMaterial) ? "Archarina64_Corridor" : floorMaterial;
            string ceiling = string.IsNullOrWhiteSpace(ceilingMaterial) ? floor : ceilingMaterial;
            string wall = string.IsNullOrWhiteSpace(wallMaterial) ? floor : wallMaterial;
            EnsureMaterial(visual, floor, null); EnsureMaterial(visual, ceiling, null); EnsureMaterial(visual, wall, null);
            string[] materials = { floor, ceiling, wall, wall };
            for (int i = 0; i < faces.Count; i++)
            {
                AppendRawFaces(visual, "Archarina64_Corridor_Path_" + i, new[] { faces[i] }, materials[i], 0);
                if (collision != null) AppendRawFaces(collision, "Archarina64_Corridor_Path_Collision_" + i, new[] { faces[i] }, null, collisionPolyType);
            }
        }

        private static void AppendRawFaces(ObjFile model, string groupName, IEnumerable<Vector3d[]> faces, string materialName, int polyType)
        {
            ObjFile.Group group = new ObjFile.Group { Name = groupName, PolyType = polyType, BackfaceCulling = false };
            foreach (Vector3d[] points in faces)
            {
                if (points == null || points.Length < 3) continue;
                int[] vertices = new int[points.Length], uvs = new int[points.Length], normals = new int[points.Length];
                Vector3d normal = Vector3d.Cross(points[1] - points[0], points[2] - points[0]);
                if (normal.LengthSquared > 0.000001) normal.Normalize();
                for (int i = 0; i < points.Length; i++)
                {
                    vertices[i] = model.Vertices.Count; model.Vertices.Add(new ObjFile.Vertex(points[i].X, points[i].Y, points[i].Z));
                    uvs[i] = model.TextureCoordinates.Count; model.TextureCoordinates.Add(new ObjFile.TextureCoord(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 1 : 0));
                    normals[i] = model.Normals.Count; model.Normals.Add(new ObjFile.Normal(normal.X, normal.Y, normal.Z));
                }
                for (int i = 1; i < points.Length - 1; i++)
                    group.Triangles.Add(new ObjFile.Triangle(materialName, new[] { vertices[0], vertices[i], vertices[i + 1] }, new[] { uvs[0], uvs[i], uvs[i + 1] }, new[] { normals[0], normals[i], normals[i + 1] }));
            }
            if (group.Triangles.Count > 0) model.Groups.Add(group);
        }

        private static List<Face> BuildFaces(RoomPrimitiveSpec spec, string materialName)
        {
            spec = ApplyScale(spec);
            switch (spec.Type)
            {
                case RoomPrimitiveType.Plane:
                    return new List<Face>
                    {
                        MakeFace(new Vector3d[]
                        {
                            new Vector3d(spec.Min.X, spec.Min.Y, spec.Min.Z),
                            new Vector3d(spec.Max.X, spec.Min.Y, spec.Min.Z),
                            new Vector3d(spec.Max.X, spec.Min.Y, spec.Max.Z),
                            new Vector3d(spec.Min.X, spec.Min.Y, spec.Max.Z)
                        }, FaceMaterial(spec, 0, materialName))
                    };
                case RoomPrimitiveType.Triangle:
                    return new List<Face> { MakeFace(new Vector3d[] { spec.A, spec.B, spec.C }, FaceMaterial(spec, 0, materialName)) };
                case RoomPrimitiveType.Ramp:
                    return BuildRamp(spec, materialName);
                default:
                    return BuildCube(spec, materialName);
            }
        }

        private static RoomPrimitiveSpec ApplyScale(RoomPrimitiveSpec source)
        {
            Vector3d center = (source.Min + source.Max) * 0.5;
            Vector3d scale = source.Scale;
            RoomPrimitiveSpec scaled = new RoomPrimitiveSpec
            {
                Type = source.Type,
                Name = source.Name,
                MaterialName = source.MaterialName,
                FaceMaterials = source.FaceMaterials,
                TexturePath = source.TexturePath,
                Min = center + new Vector3d((source.Min.X - center.X) * scale.X, (source.Min.Y - center.Y) * scale.Y, (source.Min.Z - center.Z) * scale.Z),
                Max = center + new Vector3d((source.Max.X - center.X) * scale.X, (source.Max.Y - center.Y) * scale.Y, (source.Max.Z - center.Z) * scale.Z),
                A = center + new Vector3d((source.A.X - center.X) * scale.X, (source.A.Y - center.Y) * scale.Y, (source.A.Z - center.Z) * scale.Z),
                B = center + new Vector3d((source.B.X - center.X) * scale.X, (source.B.Y - center.Y) * scale.Y, (source.B.Z - center.Z) * scale.Z),
                C = center + new Vector3d((source.C.X - center.X) * scale.X, (source.C.Y - center.Y) * scale.Y, (source.C.Z - center.Z) * scale.Z),
                Scale = Vector3d.One,
                CollisionPolyType = source.CollisionPolyType,
                AddToCollision = source.AddToCollision
            };
            return scaled;
        }

        private static List<Face> BuildCube(RoomPrimitiveSpec spec, string materialName)
        {
            Vector3d a = new Vector3d(spec.Min.X, spec.Min.Y, spec.Min.Z);
            Vector3d b = new Vector3d(spec.Max.X, spec.Min.Y, spec.Min.Z);
            Vector3d c = new Vector3d(spec.Max.X, spec.Min.Y, spec.Max.Z);
            Vector3d d = new Vector3d(spec.Min.X, spec.Min.Y, spec.Max.Z);
            Vector3d e = new Vector3d(spec.Min.X, spec.Max.Y, spec.Min.Z);
            Vector3d f = new Vector3d(spec.Max.X, spec.Max.Y, spec.Min.Z);
            Vector3d g = new Vector3d(spec.Max.X, spec.Max.Y, spec.Max.Z);
            Vector3d h = new Vector3d(spec.Min.X, spec.Max.Y, spec.Max.Z);

            return new List<Face>
            {
                MakeFace(new Vector3d[] { a, d, c, b }, FaceMaterial(spec, 0, materialName)),
                MakeFace(new Vector3d[] { e, f, g, h }, FaceMaterial(spec, 1, materialName)),
                MakeFace(new Vector3d[] { a, b, f, e }, FaceMaterial(spec, 2, materialName)),
                MakeFace(new Vector3d[] { b, c, g, f }, FaceMaterial(spec, 3, materialName)),
                MakeFace(new Vector3d[] { c, d, h, g }, FaceMaterial(spec, 4, materialName)),
                MakeFace(new Vector3d[] { d, a, e, h }, FaceMaterial(spec, 5, materialName))
            };
        }

        // The ramp rises from Min.Z to Max.Z. Min/Max still define its footprint.
        private static List<Face> BuildRamp(RoomPrimitiveSpec spec, string materialName)
        {
            Vector3d a = new Vector3d(spec.Min.X, spec.Min.Y, spec.Min.Z);
            Vector3d b = new Vector3d(spec.Max.X, spec.Min.Y, spec.Min.Z);
            Vector3d c = new Vector3d(spec.Max.X, spec.Min.Y, spec.Max.Z);
            Vector3d d = new Vector3d(spec.Min.X, spec.Min.Y, spec.Max.Z);
            Vector3d e = new Vector3d(spec.Min.X, spec.Max.Y, spec.Max.Z);
            Vector3d f = new Vector3d(spec.Max.X, spec.Max.Y, spec.Max.Z);

            return new List<Face>
            {
                MakeFace(new Vector3d[] { a, d, c, b }, FaceMaterial(spec, 0, materialName)),
                MakeFace(new Vector3d[] { a, b, f, e }, FaceMaterial(spec, 1, materialName)),
                MakeFace(new Vector3d[] { b, c, f }, FaceMaterial(spec, 2, materialName)),
                MakeFace(new Vector3d[] { c, d, e, f }, FaceMaterial(spec, 3, materialName)),
                MakeFace(new Vector3d[] { d, a, e }, FaceMaterial(spec, 4, materialName))
            };
        }

        private static string FaceMaterial(RoomPrimitiveSpec spec, int index, string fallback)
        {
            if (spec.FaceMaterials != null && index < spec.FaceMaterials.Count && !string.IsNullOrWhiteSpace(spec.FaceMaterials[index]))
                return spec.FaceMaterials[index].Trim();
            return fallback;
        }

        private static Face MakeFace(Vector3d[] points, string materialName)
        {
            Vector3d normal = Vector3d.Cross(points[1] - points[0], points[2] - points[0]);
            if (normal.LengthSquared > 0.000001)
                normal.Normalize();
            return new Face(points, normal, materialName);
        }

        private static void AppendFaces(ObjFile model, string groupName, List<Face> faces, bool collision, int polyType)
        {
            ObjFile.Group group = new ObjFile.Group { Name = groupName, PolyType = polyType };
            // Authored geometry should remain visible while it is being
            // positioned. Primitive face winding can vary by shape, and
            // culling would otherwise make a newly inserted object appear to
            // be missing from the viewport.
            group.BackfaceCulling = false;

            foreach (Face face in faces)
            {
                int[] vertexIndices = new int[face.Points.Length];
                int[] texCoordIndices = new int[face.Points.Length];
                int[] normalIndices = new int[face.Points.Length];

                for (int i = 0; i < face.Points.Length; i++)
                {
                    Vector3d point = face.Points[i];
                    vertexIndices[i] = model.Vertices.Count;
                    model.Vertices.Add(new ObjFile.Vertex(point.X, point.Y, point.Z));

                    texCoordIndices[i] = model.TextureCoordinates.Count;
                    model.TextureCoordinates.Add(new ObjFile.TextureCoord(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 1 : 0));

                    normalIndices[i] = model.Normals.Count;
                    model.Normals.Add(new ObjFile.Normal(face.Normal.X, face.Normal.Y, face.Normal.Z));
                }

                for (int i = 1; i < face.Points.Length - 1; i++)
                {
                    int[] triangleVertices = { vertexIndices[0], vertexIndices[i], vertexIndices[i + 1] };
                    int[] triangleUvs = { texCoordIndices[0], texCoordIndices[i], texCoordIndices[i + 1] };
                    int[] triangleNormals = { normalIndices[0], normalIndices[i], normalIndices[i + 1] };
                    ObjFile.Triangle triangle = new ObjFile.Triangle(face.MaterialName, triangleVertices, triangleUvs, triangleNormals);
                    group.Triangles.Add(triangle);
                }
            }

            model.Groups.Add(group);
        }

        private static void EnsureMaterial(ObjFile model, string name, string texturePath)
        {
            foreach (ObjFile.Material material in model.Materials)
            {
                if (string.Equals(material.Name, name, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(texturePath)) material.map_Kd = texturePath;
                    return;
                }
            }

            ObjFile.Material created = new ObjFile.Material { Name = name, map_Kd = texturePath };
            model.Materials.Add(created);
        }
    }
}
