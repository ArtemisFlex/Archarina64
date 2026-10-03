using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK;

namespace SharpOcarina
{
    /// <summary>
    /// Produces reusable, grid-aligned geometry tiles. Gameplay is intentionally
    /// left empty so the author can populate each tile with real ROM actors.
    /// </summary>
    public static class ProceduralTileKitGenerator
    {
        public const double Grid = 100;
        public const double DoorWidth = 100;
        public const double DoorHeight = 180;
        public const double CorridorWidth = 140;
        public const double CorridorHeight = 220;
        private const double RoomHeight = 300;
        private const string FloorMaterial = "GeneratedDungeon_Floor";
        private const string CeilingMaterial = "GeneratedDungeon_Ceiling";
        private const string WallMaterial = "GeneratedDungeon_Wall";
        private const string RampMaterial = "GeneratedDungeon_Stairs";

        public static List<SectionTile> Generate(int count, int seed, int sourceScene, string romFingerprint)
        {
            if (count < 1 || count > 128) throw new InvalidOperationException("Custom tile count must be between 1 and 128.");
            Random random = new Random(seed);
            List<SectionTile> result = new List<SectionTile>();
            for (int index = 0; index < count; index++)
            {
                int family = index % 14;
                int variant = index / 12;
                SectionTile tile;
                if (family == 0) tile = CreateRectangularRoom("Square Room 1x1 - four way", 1, 1, 4, variant, "standard");
                else if (family == 1) tile = CreateRectangularRoom("Square Room 2x2 - four way", 2, 2, 4, variant, "standard");
                else if (family == 2) tile = CreateRectangularRoom("Room 1x2 - two way", 1, 2, 2, variant, "standard");
                else if (family == 3) tile = CreateRectangularRoom("Room 1x3 - straight", 1, 3, 2, variant, "standard");
                else if (family == 4) tile = CreateRectangularRoom("Room 1x4 - dead end", 1, 4, 1, variant, "standard");
                else if (family == 5) tile = CreateRectangularRoom("Room 2x4 - platform ramp", 2, 4, 2, variant, "platform");
                else if (family == 6) tile = CreateRoundRoom("Circular Room 2x2 - two way", variant, random);
                else if (family == 7) tile = CreateRoundRoom("Circular Room 4x4 - four way", variant + 2, random);
                else if (family == 8) tile = CreateCorridor("Tube Corridor 1x2 - elbow", variant, random);
                else if (family == 9) tile = CreateRectangularRoom("Room 2x2 - spiral stairs", 2, 2, 2, variant, "spiral stairs");
                else if (family == 10) tile = CreateRectangularRoom("Room 2x2 - fall shaft and vine climb", 2, 2, 2, variant, "fall shaft");
                else if (family == 11) tile = CreateRectangularRoom("Hub Arena 4x4 - four way", 4, 4, 4, variant, "platform");
                else if (family == 12) tile = CreateRectangularRoom("Room 2x2 - push block maze", 2, 2, 2, variant, "push block");
                else tile = CreateRectangularRoom("Room 2x2 - Hookshot gap", 2, 2, 2, variant, "hookshot gap");
                tile.Name = string.Format("Generated Kit {0:D2} - {1} {2:D2}", index + 1, tile.Name, variant + 1);
                tile.SourceScene = sourceScene;
                tile.RomFingerprint = romFingerprint;
                tile.Gameplay = new RoomPackage { Scene = sourceScene, Room = 0, RomFingerprint = romFingerprint, Name = tile.Name + " gameplay" };
                result.Add(tile);
            }
            return result;
        }

        private static SectionTile CreateRoom(string family, int variant, int doorCount, Random random, bool arena = false)
        {
            double radius = (arena ? 700 : 500) + (variant % 3) * 100;
            int[] doors = DoorSides(doorCount, variant);
            return CreatePolygonTile(family, variant, 4, radius, doors);
        }

        private static SectionTile CreateRectangularRoom(string family, int widthCells, int depthCells, int doorCount, int variant, string feature)
        {
            ObjFile visual = new ObjFile();
            ObjFile collision = new ObjFile();
            double width = Math.Max(1, widthCells) * Grid;
            double depth = Math.Max(1, depthCells) * Grid;
            double floorY = feature == "fall shaft" ? -300 : 0;
            List<Vector3d> perimeter = new List<Vector3d>
            {
                new Vector3d(-width * 0.5, floorY, -depth * 0.5),
                new Vector3d(width * 0.5, floorY, -depth * 0.5),
                new Vector3d(width * 0.5, floorY, depth * 0.5),
                new Vector3d(-width * 0.5, floorY, depth * 0.5)
            };
            AppendFan(visual, collision, perimeter, floorY, FloorMaterial, 0, false);
            AppendFan(visual, collision, perimeter, RoomHeight, CeilingMaterial, 0, true);
            int[] doors = DoorSides(doorCount, variant);
            List<TileSocket> sockets = new List<TileSocket>();
            for (int i = 0; i < perimeter.Count; i++)
            {
                Vector3d start = perimeter[i], end = perimeter[(i + 1) % perimeter.Count];
                Vector3d midpoint = (start + end) * 0.5;
                double facing = Math.Atan2(midpoint.Z, midpoint.X) * 180.0 / Math.PI;
                if (doors.Contains(i))
                {
                    Vector3d tangent = end - start;
                    double length = tangent.Length;
                    tangent.Normalize();
                    double gap = Math.Min(DoorWidth, length);
                    Vector3d a = midpoint - tangent * (gap * 0.5), b = midpoint + tangent * (gap * 0.5);
                    double wallBase = feature == "fall shaft" ? 0 : floorY;
                    AppendWall(visual, collision, start, a, "WallDoorLeft_" + i, wallBase);
                    AppendWall(visual, collision, b, end, "WallDoorRight_" + i, wallBase);
                    sockets.Add(new TileSocket { Id = "door_" + i, Name = family + " doorway " + i, Position = new Vector3d(midpoint.X, 0, midpoint.Z), Facing = facing, Width = DoorWidth, Height = DoorHeight, Room = -1 });
                }
                else AppendWall(visual, collision, start, end, "Wall_" + i, feature == "fall shaft" ? 0 : floorY);
            }
            if (feature == "platform")
            {
                double platformWidth = Math.Max(Grid, width - 2 * Grid);
                double platformDepth = Math.Max(Grid, depth * 0.5);
                AppendBox(visual, collision, -platformWidth * 0.5, platformWidth * 0.5, -platformDepth * 0.5, platformDepth * 0.5, 100, 200, "Platform");
                AppendRamp(visual, collision, -width * 0.5 + 20, -platformWidth * 0.5, -platformDepth * 0.5, platformDepth * 0.5, 0, 100, "PlatformRamp");
            }
            else if (feature == "spiral stairs") AppendSpiralRamp(visual, collision, 80, 0, "SpiralRamp");
            else if (feature == "fall shaft")
            {
                double shaft = Math.Min(width, depth) * 0.45;
                double half = shaft * 0.5;
                AppendWall(visual, collision, new Vector3d(-half, -300, -half), new Vector3d(half, -300, -half), "VineClimbWallFront", -300, 300);
                AppendWall(visual, collision, new Vector3d(half, -300, half), new Vector3d(-half, -300, half), "VineClimbWallBack", -300, 300);
                AppendWall(visual, collision, new Vector3d(-half, -300, half), new Vector3d(-half, -300, -half), "VineClimbWallLeft", -300, 300);
                AppendWall(visual, collision, new Vector3d(half, -300, -half), new Vector3d(half, -300, half), "VineClimbWallRight", -300, 300);
            }
            else if (feature == "push block")
            {
                AppendBox(visual, collision, -100, 0, -100, 0, 0, 100, "MovablePushBlock");
                AppendBox(visual, collision, 100, 200, 100, 200, 0, 100, "PushBlockGoal");
            }
            else if (feature == "hookshot gap")
            {
                AppendBox(visual, collision, -350, -50, -100, 100, 0, 40, "HookshotStartPlatform");
                AppendBox(visual, collision, 50, 350, -100, 100, 100, 140, "HookshotLandingPlatform");
                AppendBox(visual, collision, -20, 20, 0, 40, 160, 260, "HookshotAnchor");
            }
            SectionTile tile = new SectionTile { Version = 1, Name = family, Grid = Grid, Visual = TileMesh.Copy(visual, visual.Groups), Collision = TileMesh.Copy(collision, collision.Groups), Surfaces = new List<ZColPolyType> { new ZColPolyType(0) }, Sockets = sockets };
            AddTraversalContracts(tile, feature, width, depth, floorY);
            return tile;
        }

        private static SectionTile CreateRoundRoom(string family, int variant, Random random)
        {
            double radius = 450 + (variant % 4) * 100;
            int doorCount = 1 + (variant % 4);
            return CreatePolygonTile(family, variant, 12, radius, DoorSides(doorCount, variant));
        }

        private static SectionTile CreatePolygonTile(string family, int variant, int sides, double radius, int[] doorSides)
        {
            ObjFile visual = new ObjFile();
            ObjFile collision = new ObjFile();
            List<Vector3d> perimeter = new List<Vector3d>();
            for (int i = 0; i < sides; i++)
            {
                double angle = (45.0 + i * 360.0 / sides) * Math.PI / 180.0;
                perimeter.Add(new Vector3d(Math.Cos(angle) * radius, 0, Math.Sin(angle) * radius));
            }
            AppendFan(visual, collision, perimeter, 0, FloorMaterial, 0, false);
            AppendFan(visual, collision, perimeter, RoomHeight, CeilingMaterial, 0, true);
            List<TileSocket> sockets = new List<TileSocket>();
            for (int i = 0; i < sides; i++)
            {
                Vector3d start = perimeter[i], end = perimeter[(i + 1) % sides];
                Vector3d midpoint = (start + end) * 0.5;
                double facing = Math.Atan2(midpoint.Z, midpoint.X) * 180.0 / Math.PI;
                if (doorSides.Contains(i))
                {
                    Vector3d tangent = end - start;
                    double length = tangent.Length;
                    tangent.Normalize();
                    double gap = Math.Min(DoorWidth, length * 0.55);
                    Vector3d a = midpoint - tangent * (gap * 0.5), b = midpoint + tangent * (gap * 0.5);
                    AppendWall(visual, collision, start, a, "WallDoorLeft_" + i);
                    AppendWall(visual, collision, b, end, "WallDoorRight_" + i);
                    sockets.Add(new TileSocket { Id = "door_" + i, Name = family + " doorway " + i, Position = new Vector3d(midpoint.X, 0, midpoint.Z), Facing = facing, Width = DoorWidth, Height = DoorHeight, Room = -1 });
                }
                else AppendWall(visual, collision, start, end, "Wall_" + i);
            }
            SectionTile tile = new SectionTile { Version = 1, Name = family, Grid = Grid, Visual = TileMesh.Copy(visual, visual.Groups), Collision = TileMesh.Copy(collision, collision.Groups), Surfaces = new List<ZColPolyType> { new ZColPolyType(0) }, Sockets = sockets };
            AddTraversalContracts(tile, "round", radius * 2, radius * 2, 0);
            return tile;
        }

        private static SectionTile CreateCorridor(string family, int variant, Random random)
        {
            ObjFile visual = new ObjFile();
            ObjFile collision = new ObjFile();
            List<Vector3d> points;
            int shape = variant % 5;
            if (shape == 0) points = new List<Vector3d> { new Vector3d(-400, 0, 0), new Vector3d(400, 0, 0) };
            else if (shape == 1) points = new List<Vector3d> { new Vector3d(-400, 0, 0), new Vector3d(0, 0, 0), new Vector3d(0, 0, 400) };
            else if (shape == 2) points = new List<Vector3d> { new Vector3d(-400, 0, 0), new Vector3d(0, 0, 0), new Vector3d(400, 0, 0) };
            else if (shape == 3) points = new List<Vector3d> { new Vector3d(-400, 0, 0), new Vector3d(-100, 0, 0), new Vector3d(-100, 0, 300), new Vector3d(400, 0, 300) };
            else points = new List<Vector3d> { new Vector3d(-400, 0, 0), new Vector3d(0, 0, 0), new Vector3d(0, 0, 300), new Vector3d(400, 0, 300) };
            RoomGeometryBuilder.AppendSquareTubePath(visual, collision, points, CorridorWidth, CorridorHeight, Grid, FloorMaterial, CeilingMaterial, WallMaterial, 0);
            Vector3d firstDirection = points[1] - points[0]; firstDirection.Y = 0; firstDirection.Normalize();
            Vector3d lastDirection = points[points.Count - 1] - points[points.Count - 2]; lastDirection.Y = 0; lastDirection.Normalize();
            List<TileSocket> sockets = new List<TileSocket>
            {
                new TileSocket { Id = "door_start", Name = "Corridor entrance", Position = points[0], Facing = Math.Atan2(-firstDirection.Z, -firstDirection.X) * 180.0 / Math.PI, Width = DoorWidth, Height = DoorHeight, Room = -1 },
                new TileSocket { Id = "door_end", Name = "Corridor exit", Position = points[points.Count - 1], Facing = Math.Atan2(lastDirection.Z, lastDirection.X) * 180.0 / Math.PI, Width = DoorWidth, Height = DoorHeight, Room = -1 }
            };
            SectionTile tile = new SectionTile { Version = 1, Name = family, Grid = Grid, Visual = TileMesh.Copy(visual, visual.Groups), Collision = TileMesh.Copy(collision, collision.Groups), Surfaces = new List<ZColPolyType> { new ZColPolyType(0) }, Sockets = sockets };
            AddTraversalContracts(tile, "corridor", 800, 800, 0);
            return tile;
        }

        private static void AddTraversalContracts(SectionTile tile, string feature, double width, double depth, double floorY)
        {
            string value = (feature ?? "").ToLowerInvariant();
            if (value.Contains("platform"))
            {
                tile.TraversalCapabilities |= DungeonTraversalCapabilities.JumpGap | DungeonTraversalCapabilities.Stairs;
                tile.Traversal.Add(new TileTraversalContract { Id = "platform-ramp", Kind = "Stairs", Evidence = "Generated platform ramp", Start = new Vector3d(-width * 0.4, floorY, 0), End = new Vector3d(width * 0.2, floorY + 100, 0), MaxDistance = 250 });
                tile.Traversal.Add(new TileTraversalContract { Id = "platform-gap", Kind = "JumpGap", Evidence = "Generated platform separation", Start = new Vector3d(width * 0.2, floorY + 100, 0), End = new Vector3d(width * 0.4, floorY + 100, 0), MaxDistance = 180, Optional = true });
            }
            if (value.Contains("spiral"))
            {
                tile.TraversalCapabilities |= DungeonTraversalCapabilities.Stairs;
                tile.Traversal.Add(new TileTraversalContract { Id = "spiral-stairs", Kind = "Stairs", Evidence = "Generated spiral stair steps", Start = new Vector3d(0, 0, 0), End = new Vector3d(0, 175, 0), MaxDistance = 300 });
            }
            if (value.Contains("fall"))
            {
                tile.TraversalCapabilities |= DungeonTraversalCapabilities.FallDrop | DungeonTraversalCapabilities.ClimbVines;
                tile.Traversal.Add(new TileTraversalContract { Id = "fall-drop", Kind = "FallDrop", Evidence = "Generated fall shaft", Start = new Vector3d(0, 0, 0), End = new Vector3d(0, -300, 0), MaxDistance = 300 });
                tile.Traversal.Add(new TileTraversalContract { Id = "vine-return", Kind = "VineClimb", Evidence = "Generated climbable shaft wall", Start = new Vector3d(0, -300, 0), End = new Vector3d(0, 0, 0), MaxDistance = 300 });
            }
            if (value.Contains("push"))
            {
                tile.TraversalCapabilities |= DungeonTraversalCapabilities.PushBlock;
                tile.Traversal.Add(new TileTraversalContract
                {
                    Id = "push-block-maze", Kind = "PushBlock", Evidence = "Generated movable block and goal",
                    Start = new Vector3d(-100, 0, -100), End = new Vector3d(100, 0, 100), MaxDistance = 400,
                    PushBlockPuzzle = new DungeonPushBlockPuzzle
                    {
                        Width = 6, Height = 5, PlayerStart = new DungeonGridPoint(0, 2),
                        Blocks = new List<DungeonGridPoint> { new DungeonGridPoint(2, 2) },
                        Goals = new List<DungeonGridPoint> { new DungeonGridPoint(5, 2) }
                    }
                });
            }
            if (value.Contains("hookshot"))
            {
                tile.TraversalCapabilities |= DungeonTraversalCapabilities.Hookshot | DungeonTraversalCapabilities.JumpGap;
                tile.Traversal.Add(new TileTraversalContract { Id = "hookshot-gap", Kind = "Hookshot", RequiredItem = "Hookshot", Evidence = "Generated Hookshot anchor and elevated landing", Start = new Vector3d(-200, 0, 0), End = new Vector3d(200, 120, 0), MaxDistance = 500 });
            }
            if (value.Contains("corridor"))
            {
                tile.TraversalCapabilities |= DungeonTraversalCapabilities.Walk;
                tile.Traversal.Add(new TileTraversalContract { Id = "corridor-walk", Kind = "Walk", Evidence = "Generated square tube corridor", Start = new Vector3d(-400, 0, 0), End = new Vector3d(400, 0, 0), MaxDistance = 800 });
            }
        }

        private static int[] DoorSides(int count, int variant)
        {
            int[] preferred = { 0, 2, 1, 3 };
            return preferred.Take(Math.Max(1, Math.Min(4, count))).Select(i => (i + variant) % 4).Distinct().ToArray();
        }

        private static void AppendWall(ObjFile visual, ObjFile collision, Vector3d start, Vector3d end, string name, double baseY = 0, double height = RoomHeight)
        {
            if ((end - start).Length < 1) return;
            start.Y = baseY; end.Y = baseY;
            Vector3d topStart = start + Vector3d.UnitY * height;
            Vector3d topEnd = end + Vector3d.UnitY * height;
            AppendQuad(visual, name, new[] { start, end, topEnd, topStart }, WallMaterial, 0);
            AppendQuad(collision, name + "_Collision", new[] { start, end, topEnd, topStart }, null, 0);
        }

        private static void AppendBox(ObjFile visual, ObjFile collision, double minX, double maxX, double minZ, double maxZ, double minY, double maxY, string name)
        {
            Vector3d a = new Vector3d(minX, minY, minZ), b = new Vector3d(maxX, minY, minZ), c = new Vector3d(maxX, minY, maxZ), d = new Vector3d(minX, minY, maxZ);
            Vector3d e = new Vector3d(minX, maxY, minZ), f = new Vector3d(maxX, maxY, minZ), g = new Vector3d(maxX, maxY, maxZ), h = new Vector3d(minX, maxY, maxZ);
            AppendQuad(visual, name + "Top", new[] { e, f, g, h }, FloorMaterial, 0); AppendQuad(collision, name + "TopCollision", new[] { e, f, g, h }, null, 0);
            AppendQuad(visual, name + "Front", new[] { a, b, f, e }, WallMaterial, 0); AppendQuad(collision, name + "FrontCollision", new[] { a, b, f, e }, null, 0);
            AppendQuad(visual, name + "Right", new[] { b, c, g, f }, WallMaterial, 0); AppendQuad(collision, name + "RightCollision", new[] { b, c, g, f }, null, 0);
            AppendQuad(visual, name + "Back", new[] { c, d, h, g }, WallMaterial, 0); AppendQuad(collision, name + "BackCollision", new[] { c, d, h, g }, null, 0);
            AppendQuad(visual, name + "Left", new[] { d, a, e, h }, WallMaterial, 0); AppendQuad(collision, name + "LeftCollision", new[] { d, a, e, h }, null, 0);
        }

        private static void AppendRamp(ObjFile visual, ObjFile collision, double xStart, double xEnd, double minZ, double maxZ, double lowY, double highY, string name)
        {
            RoomGeometryBuilder.AppendLowPolyRamp(visual, collision,
                new Vector3d(xStart, lowY, (minZ + maxZ) * 0.5),
                new Vector3d(xEnd, highY, (minZ + maxZ) * 0.5),
                Math.Max(1, maxZ - minZ), RoomHeight, RampMaterial, CeilingMaterial, WallMaterial, 0);
        }

        private static void AppendSpiralRamp(ObjFile visual, ObjFile collision, double radius, double baseY, string name)
        {
            RoomGeometryBuilder.AppendLowPolyRamp(visual, collision,
                new Vector3d(-radius, baseY, 0), new Vector3d(radius, baseY + 175, 0),
                radius * 0.65, RoomHeight, RampMaterial, CeilingMaterial, WallMaterial, 0);
        }

        private static void AppendFan(ObjFile visual, ObjFile collision, IList<Vector3d> points, double y, string material, int polyType, bool reverse)
        {
            Vector3d center = new Vector3d(0, y, 0);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3d a = new Vector3d(points[i].X, y, points[i].Z), b = new Vector3d(points[(i + 1) % points.Count].X, y, points[(i + 1) % points.Count].Z);
                Vector3d[] face = reverse ? new[] { center, b, a } : new[] { center, a, b };
                AppendTriangle(visual, "FloorOrCeiling", face, material, polyType);
                AppendTriangle(collision, "FloorOrCeiling_Collision", face, null, polyType);
            }
        }

        private static void AppendTriangle(ObjFile model, string name, Vector3d[] points, string material, int polyType)
        {
            AppendQuad(model, name, points, material, polyType);
        }

        private static void AppendQuad(ObjFile model, string name, Vector3d[] points, string material, int polyType)
        {
            if (model == null || points == null || points.Length < 3) return;
            if (material != null && model.Materials.All(m => m.Name != material)) model.Materials.Add(new ObjFile.Material { Name = material });
            ObjFile.Group group = new ObjFile.Group { Name = name, PolyType = polyType, BackfaceCulling = false };
            int[] vertices = new int[points.Length], uvs = new int[points.Length], normals = new int[points.Length];
            Vector3d normal = Vector3d.Cross(points[1] - points[0], points[2] - points[0]);
            if (normal.LengthSquared > 0.000001) normal.Normalize();
            for (int i = 0; i < points.Length; i++)
            {
                vertices[i] = model.Vertices.Count; model.Vertices.Add(new ObjFile.Vertex(points[i].X, points[i].Y, points[i].Z));
                uvs[i] = model.TextureCoordinates.Count; model.TextureCoordinates.Add(new ObjFile.TextureCoord(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 1 : 0));
                normals[i] = model.Normals.Count; model.Normals.Add(new ObjFile.Normal(normal.X, normal.Y, normal.Z));
            }
            for (int i = 1; i + 1 < points.Length; i++)
                group.Triangles.Add(new ObjFile.Triangle(material, new[] { vertices[0], vertices[i], vertices[i + 1] }, new[] { uvs[0], uvs[i], uvs[i + 1] }, new[] { normals[0], normals[i], normals[i + 1] }));
            model.Groups.Add(group);
        }
    }
}
