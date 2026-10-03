using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Serialization;
using SharpOcarina;
using SharpOcarina.Shared;
using OpenTK;

internal static class SceneBuilderTests
{
    private static void Check(bool result, string message)
    {
        if (!result) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Put(byte[] rom, int at, uint value)
    {
        rom[at] = (byte)(value >> 24); rom[at + 1] = (byte)(value >> 16);
        rom[at + 2] = (byte)(value >> 8); rom[at + 3] = (byte)value;
    }

    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, message);
    }

    private static void TestConnections()
    {
        byte[] data = new byte[256];
        data[32] = 0x0E; data[33] = 1; Put(data, 36, 0x02000020); data[40] = 0x14;
        data[64] = 1; data[66] = 2; data[69] = 9; data[76] = 0x40; data[79] = 0x42;
        var diagnostics = new List<string>();
        var definitions = new Dictionary<ushort, System.Xml.Linq.XElement>();
        var presets = RomAssetCatalog.ReadTransitions(data, 32, 128, definitions, diagnostics);
        Check(presets.Count == 1 && presets[0].Transition && presets[0].Category == "Connections" && presets[0].Id == 9
            && presets[0].Variable == 0x42 && presets[0].YRotation == 0x4000, "Parse scene transition record offsets");
        Put(data, 36, 0x03000020);
        Check(RomAssetCatalog.ReadTransitions(data, 32, 128, definitions, diagnostics).Count == 0, "Reject transition pointer with room segment");
        Put(data, 36, 0x02000058);
        Check(RomAssetCatalog.ReadTransitions(data, 32, 128, definitions, diagnostics).Count == 0, "Reject truncated transition record");
        ZScene scene = new ZScene();
        scene.Rooms.Add(new ZScene.ZRoom()); scene.Rooms.Add(new ZScene.ZRoom());
        RomPaletteAsset preset = presets[0]; preset.Objects = new ushort[] { 7, 7 };
        var banks = new HashSet<ushort> { 7 };
        var origin = new OpenTK.Vector3d(100, 200, -300);
        ZActor connection = RoomConnections.Prepare(scene, preset, 0, 1, origin, 90, banks);
        Check(connection.IsTransition && connection.FrontSwitchTo == 0 && connection.BackSwitchTo == 1
            && connection.FrontCamera == 255 && connection.BackCamera == 255 && connection.YRot == 16384
            && connection.Variable == 0x42, "Explicit connection mapping and binary-angle conversion");
        Check(RoomConnections.Prepare(scene, preset, 0, 1, origin, 180, banks).YRot == -32768, "Half-turn wraps to signed binary angle");
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 0, origin, 0, banks), "Reject same-room connection");
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 2, origin, 0, banks), "Reject absent destination room");
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 255, origin, 0, banks), "Reject exterior sentinel for two-room placement");
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 1, origin, 0, new HashSet<ushort>()), "Reject missing connection bank");
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 1, new OpenTK.Vector3d(double.NaN, 0, 0), 0, banks), "Reject nonfinite connection position");
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 1, origin, double.PositiveInfinity, banks), "Reject nonfinite facing");
        preset.Variable = 0x400;
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 1, origin, 0, banks), "Reject reserved transition parameter bits");
        preset.Variable = 0x42;
        Check(scene.Transitions.Count == 0 && scene.Rooms.All(r => r.ZObjects.Count == 0), "Preparation and rejection do not mutate the scene");
        for (int i = 0; i < 255; i++) scene.Rooms[1].ZObjects.Add(new ZScene.ZUShort(8));
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 1, origin, 0, banks), "Reject object-list overflow before mutation");
        scene.Rooms[1].ZObjects.Clear();
        while (scene.Rooms.Count < 129) scene.Rooms.Add(new ZScene.ZRoom());
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 128, origin, 0, banks), "Reject existing room outside signed transition range");
        for (int i = 0; i < 64; i++) scene.Transitions.Add(connection.Clone());
        Reject(() => RoomConnections.Prepare(scene, preset, 0, 1, origin, 0, banks), "Reject transition index overflow");
    }

    private static void TestMeshEditorMaterialStateSnapshot()
    {
        ObjFile model = new ObjFile();
        model.Vertices.AddRange(new[]
        {
            new ObjFile.Vertex(0, 0, 0),
            new ObjFile.Vertex(1, 0, 0),
            new ObjFile.Vertex(0, 1, 0)
        });
        ObjFile.Group group = new ObjFile.Group { Name = "native-group", Metallic = true, Animated = true, TintAlpha = 0x7F204060 };
        model.TextureCoordinates.Add(new ObjFile.TextureCoord(0.25, 0.75));
        model.Normals.Add(new ObjFile.Normal(0, 1, 0));
        model.VertexColors.Add(new ObjFile.VertexColor(0.2, 0.4, 0.6, 0.8));
        model.Materials.Add(new ObjFile.Material { Name = "roof", Kd = new[] { 0.1f, 0.3f, 0.5f }, ForceRGBA = true, ForcedFormat = "RGBA16" });
        model.AdditionalTextures.Add(new ObjFile.Material { Name = "overlay", ForceRGBA = true, ForcedFormat = "CI8" });
        group.Custom = true;
        group.CustomDL[0] = 0xDEADBEEFUL;
        group.MultiTexMaterialName = "overlay";
        group.Triangles.Add(new ObjFile.Triangle
        {
            MaterialName = "roof",
            VertIndex = new[] { 0, 1, 2 },
            TexCoordIndex = new[] { 4, 5, 6 },
            NormalIndex = new[] { 7, 8, 9 },
            VertColor = new[] { 10, 11, 12 }
        });

        Type meshDataType = typeof(MainForm).Assembly.GetType("SharpOcarina.MeshEditorData");
        object data = meshDataType.GetMethod("FromGroup", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { model, group });
        FieldInfo sourceTrianglesField = meshDataType.GetField("SourceTriangles", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo groupStateField = meshDataType.GetField("SourceGroupState", BindingFlags.Public | BindingFlags.Instance);
        var sourceTriangles = (System.Collections.IEnumerable)sourceTrianglesField.GetValue(data);
        Check(sourceTriangles.Cast<object>().Count() == 1, "Mesh editor marks imported faces as source faces");
        FieldInfo trianglesField = meshDataType.GetField("Triangles", BindingFlags.Public | BindingFlags.Instance);
        ObjFile.Triangle imported = ((System.Collections.IEnumerable)trianglesField.GetValue(data)).Cast<ObjFile.Triangle>().Single();
        Check(imported.MaterialName == "roof" && imported.TexCoordIndex.SequenceEqual(new[] { 4, 5, 6 }) &&
            imported.NormalIndex.SequenceEqual(new[] { 7, 8, 9 }) && imported.VertColor.SequenceEqual(new[] { 10, 11, 12 }),
            "Mesh editor snapshot retains source UV, normal, color, and material indices");
        model.TextureCoordinates[0].U = 9.0;
        model.Normals[0].Y = 0.0;
        model.VertexColors[0].A = 0.0;
        model.Materials[0].Kd[0] = 1.0f;
        model.Materials[0].ForceRGBA = false;
        model.AdditionalTextures[0].ForceRGBA = false;
        data.GetType().GetMethod("RestoreSourceSurfaceState").Invoke(data, new object[] { model });
        Check(Math.Abs(model.TextureCoordinates[0].U - 0.25) < 0.000001 &&
            Math.Abs(model.Normals[0].Y - 1.0) < 0.000001 &&
            Math.Abs(model.VertexColors[0].A - 0.8) < 0.000001 &&
            Math.Abs(model.Materials[0].Kd[0] - 0.1f) < 0.000001 && model.Materials[0].ForceRGBA &&
            model.Materials[0].ForcedFormat == "RGBA16" && model.AdditionalTextures.Count == 1 &&
            model.AdditionalTextures[0].ForceRGBA && model.AdditionalTextures[0].ForcedFormat == "CI8",
            "Mesh editor restores complete source surface and additional texture tables");

        group.Metallic = false;
        group.Animated = false;
        group.TintAlpha = 0xFFFFFFFF;
        group.Custom = false;
        group.CustomDL[0] = 0;
        group.MultiTexMaterialName = "";
        object state = groupStateField.GetValue(data);
        state.GetType().GetMethod("Apply").Invoke(state, new object[] { group });
        Check(group.Metallic && group.Animated && group.TintAlpha == 0x7F204060 && group.Custom && group.CustomDL[0] == 0xDEADBEEFUL && group.MultiTexMaterialName == "overlay",
            "Mesh editor restores native material and display-list group state");
        object clone = meshDataType.GetMethod("Clone").Invoke(data, null);
        Check(((System.Collections.IEnumerable)sourceTrianglesField.GetValue(clone)).Cast<object>().Count() == 1,
            "Mesh editor undo clone retains source-face attribute provenance");
    }

    private static void TestCapturedRoomVertexColors()
    {
        ZScene scene = new ZScene();
        List<SharpOcarina.SayakaGL.UcodeSimulator.CapturedTriangle> triangles = new List<SharpOcarina.SayakaGL.UcodeSimulator.CapturedTriangle>
        {
            new SharpOcarina.SayakaGL.UcodeSimulator.CapturedTriangle
            {
                Positions = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) },
                TexCoords = new[] { new Vector2d(0, 0), new Vector2d(1, 0), new Vector2d(0, 1) },
                Normals = new[] { new Vector3d(0, 0, 1), new Vector3d(0, 0, 1), new Vector3d(0, 0, 1) },
                Colors = new[]
                {
                    new OpenTK.Graphics.Color4(0.25f, 0.5f, 0.75f, 0.5f),
                    new OpenTK.Graphics.Color4(1.0f, 0.0f, 0.0f, 1.0f),
                    new OpenTK.Graphics.Color4(0.0f, 1.0f, 0.0f, 1.0f)
                }
            }
        };
        string path = null;
        try
        {
            MethodInfo exporter = typeof(ZScene).GetMethod("WriteCapturedRoomObj", BindingFlags.Instance | BindingFlags.NonPublic);
            path = (string)exporter.Invoke(scene, new object[] { 0, triangles });
            string obj = File.ReadAllText(path);
            Check(obj.Contains("vc 0.25 0.5 0.75 0.5") && obj.Contains("fc 0 1 2"),
                "Captured room OBJ exports per-corner RGBA shade data");
        }
        finally
        {
            string directory = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void TestProceduralTileKit()
    {
        List<SectionTile> tiles = ProceduralTileKitGenerator.Generate(50, 38117, 3, "test-rom");
        Check(tiles.Count == 50 && tiles.Select(t => t.Name).Distinct().Count() == 50, "Generate 50 permanent custom dungeon tile definitions");
        Check(tiles.All(t => t.Grid == ProceduralTileKitGenerator.Grid && t.Visual.Vertices.Count > 0 && t.Collision.Vertices.Count > 0), "Custom kit geometry is grid-aligned and collision-backed");
        Check(tiles.SelectMany(t => t.Sockets).All(s => s.Width == ProceduralTileKitGenerator.DoorWidth && s.Height == ProceduralTileKitGenerator.DoorHeight), "Custom kit sockets use the OoT doorway contract");
        Check(tiles.Any(t => t.Name.Contains("Square Room")) && tiles.Any(t => t.Name.Contains("Circular Room")) &&
            tiles.Any(t => t.Name.Contains("Tube Corridor")) && tiles.Any(t => t.Name.Contains("Hub Arena")), "Custom kit includes square, circular, corridor, and hub families");
        Check(tiles.Any(t => t.Name.Contains("1x1")) && tiles.Any(t => t.Name.Contains("2x2")) && tiles.Any(t => t.Name.Contains("1x2")) &&
            tiles.Any(t => t.Name.Contains("1x3")) && tiles.Any(t => t.Name.Contains("1x4")) && tiles.Any(t => t.Name.Contains("2x4")) && tiles.Any(t => t.Name.Contains("4x4")),
            "Custom kit includes snap-sized footprints and endpoint room roles");
        Check(tiles.Any(t => t.Name.Contains("platform ramp")) && tiles.Any(t => t.Name.Contains("spiral stairs")) && tiles.Any(t => t.Name.Contains("fall shaft")),
            "Custom kit includes platforms, ramps, stairs, and fall-shaft feature rooms");
        Check(tiles.Any(t => t.Name.Contains("push block maze") && t.Traversal.Any(c => c.PushBlockPuzzle != null && DungeonPushBlockSolver.Solve(c.PushBlockPuzzle).Solvable)) &&
            tiles.Any(t => t.Name.Contains("Hookshot gap") && t.Traversal.Any(c => c.Kind == "Hookshot" && c.RequiredItem == "Hookshot")),
            "Custom kit includes solvable push-block and Hookshot challenge rooms");
        Check(tiles.Any(t => (t.TraversalCapabilities & DungeonTraversalCapabilities.JumpGap) != 0 && t.Traversal.Any(c => c.Kind == "JumpGap")) &&
            tiles.Any(t => (t.TraversalCapabilities & DungeonTraversalCapabilities.ClimbVines) != 0 && t.Traversal.Any(c => c.Kind == "VineClimb")),
            "Custom kit exposes authored jump-gap and vine-climb traversal contracts");
        Check(tiles.All(t => t.Gameplay != null && t.Gameplay.Actors.Count == 0 && t.Gameplay.Links.Count == 0), "Custom kit starts gameplay-empty for manual actor authoring");
    }

    private static void TestRomMechanicsScanner()
    {
        byte[] rom = new byte[1024];
        Put(rom, 480, 0x41A00000);
        rom[470] = 13; rom[490] = 26;
        Put(rom, 600, 0xBF99999A); Put(rom, 620, 0x41000000);
        DungeonRomMechanicsProfile profile = DungeonRomMechanicsScanner.Scan(rom, "test-rom", "OOT");
        Check(profile.IsEngineDerived && profile.HookshotRange == 260 && profile.LongshotRange == 520 && profile.JumpPhysicsDerived && profile.JumpEnvelope == 181 &&
            profile.Detection.IndexOf("matched", StringComparison.OrdinalIgnoreCase) >= 0,
            "Scan ROM Hookshot and Longshot movement signature");
        Check(DungeonTraversalCalibration.FromRomMechanics(profile).Hookshot == 260,
            "Use ROM-derived Hookshot range in traversal calibration");
        Check(DungeonTraversalCalibration.FromRomMechanics(profile).JumpGap == 181,
            "Use ROM-derived jump envelope in traversal calibration");
    }

    private static void TestCollisionVolumeExtraction()
    {
        ObjFile model = new ObjFile();
        model.Vertices.AddRange(new[] { new ObjFile.Vertex(0, 0, 0), new ObjFile.Vertex(100, 0, 0), new ObjFile.Vertex(100, 10, 100), new ObjFile.Vertex(0, 10, 100), new ObjFile.Vertex(40, 0, 40), new ObjFile.Vertex(60, 0, 40), new ObjFile.Vertex(50, 150, 60), new ObjFile.Vertex(40, 150, 40) });
        ObjFile.Group floor = new ObjFile.Group { Name = "floor" }; floor.Triangles.Add(new ObjFile.Triangle { VertIndex = new[] { 0, 1, 2 } });
        ObjFile.Group wall = new ObjFile.Group { Name = "wall" }; wall.Triangles.Add(new ObjFile.Triangle { VertIndex = new[] { 4, 5, 6 } });
        List<DungeonCollisionVolume> volumes = DungeonCollisionVolumeExtractor.Extract(model, new[] { floor, wall }, new Vector3d(0, 0, 0), new Vector3d(100, 150, 100));
        Check(volumes.Count == 1 && volumes[0].Name == "wall", "Extract wall-like collision volumes while ignoring floors");
        volumes[0].Min = new Vector3d(0, 0, -1); volumes[0].Max = new Vector3d(100, 100, 1);
        volumes[0].Triangles.Clear(); volumes[0].Triangles.Add(new DungeonCollisionTriangle { A = new Vector3d(0, 0, 0), B = new Vector3d(100, 0, 0), C = new Vector3d(0, 100, 0) });
        DungeonTraversalConstraint route = new DungeonTraversalConstraint { Kind = "Hookshot", Start = new Vector3d(25, 25, -100), End = new Vector3d(25, 25, 100), ClearanceRadius = 0 };
        DungeonTraversalPlanCheck(volumes, route);
    }

    private static void TestWaterMechanics()
    {
        ZScene scene = new ZScene();
        scene.Waterboxes.Add(new ZWaterbox(0, 200, 0, 400, 400, 1, 0, 0));
        scene.Waterboxes.Add(new ZWaterbox(500, 400, 0, 400, 400, 1, 0, 0));
        DungeonWaterProfile profile = DungeonWaterMechanics.Analyze(scene, 0, 0);
        Check(profile.VolumeCount == 2 && profile.SurfaceLevels.Count == 2 && profile.HasDeepWater && profile.RequiresZoraTunic && profile.RequiresIronBoots,
            "Analyze water volumes, surface levels, and deep-water equipment requirements");
        DungeonRoomAnalysis room = new DungeonRoomAnalysis { RoomId = 0 };
        DungeonWaterMechanics.AddTraversalContracts(room, profile);
        Check(room.TraversalCapabilities.HasFlag(DungeonTraversalCapabilities.Swim) && room.TraversalConstraints.Any(c => c.Kind == "Underwater" && c.RequiredItem == "Zora Tunic") && room.TraversalConstraints.Any(c => c.Kind == "Sink" && c.RequiredItem == "Iron Boots"),
            "Create swim, underwater-breathing, and Iron Boots traversal contracts");
    }

    private static void DungeonTraversalPlanCheck(List<DungeonCollisionVolume> volumes, DungeonTraversalConstraint route)
    {
        string error;
        Check(!DungeonTraversalGeometry.Validate(route, new Vector3d(0, 0, -100), new Vector3d(100, 150, 100), volumes, out error) && error.Contains("collision triangle"),
            "Reject traversal segment that intersects an extracted collision triangle");
        route.Start = new Vector3d(25, 25, -100); route.End = new Vector3d(25, 25, -10); route.ClearanceRadius = 20;
        Check(!DungeonTraversalGeometry.Validate(route, new Vector3d(0, 0, -100), new Vector3d(100, 150, 100), volumes, out error) && error.Contains("collision triangle"),
            "Reject near-miss traversal that violates player clearance");
    }

    private static void TestGeneratedDungeonPlayability()
    {
        ZScene scene = new ZScene { SceneNumber = 99, Scale = 1 };
        scene.Rooms.Add(new ZScene.ZRoom()); scene.Rooms.Add(new ZScene.ZRoom()); scene.Rooms.Add(new ZScene.ZRoom());
        scene.SpawnPoints.Add(new ZActor { SpawnRoom = 0 });
        scene.Transitions.Add(new ZActor { FrontSwitchTo = 0, BackSwitchTo = 1 });
        scene.Transitions.Add(new ZActor { FrontSwitchTo = 1, BackSwitchTo = 2 });

        RoomPackage start = new RoomPackage { Scene = 99, Room = 0, DestinationRoom = 0, Name = "Start" };
        start.Spawns.Add(new RoomPackageActor { Id = "player-start", Role = "Player Spawn", Name = "Player Spawn" });
        start.Actors.Add(new RoomPackageActor { Id = "starter-item", Role = "Room Reward", Name = "Hookshot Chest", GrantedItems = new List<string> { "Hookshot" } });
        RoomPackage middle = new RoomPackage { Scene = 99, Room = 1, DestinationRoom = 1, Name = "Puzzle" };
        middle.Actors.Add(new RoomPackageActor { Id = "pressure-plate", Role = "Puzzle Switch", Name = "Pressure Plate" });
        middle.Actors.Add(new RoomPackageActor { Id = "stalfos", Role = "Enemy", Name = "Stalfos" });
        middle.Actors.Add(new RoomPackageActor { Id = "item-door", Role = "Door", Name = "Hookshot Door", RequiredItems = new List<string> { "Hookshot" } });
        middle.Links.Add(new LogicLink { SourceId = "pressure-plate", TargetId = "stalfos", Kind = LogicLinkKind.CompletesPuzzle });
        RoomPackage finale = new RoomPackage { Scene = 99, Room = 2, DestinationRoom = 2, Name = "Finale" };
        finale.Actors.Add(new RoomPackageActor { Id = "boss", Role = "Boss / Finale", Name = "Dungeon Boss" });
        finale.Actors.Add(new RoomPackageActor { Id = "reward", Role = "Boss Reward", Name = "Boss Chest", GrantedItems = new List<string> { "Boss Key" } });
        scene.SandboxGameplayPackages.Add(start); scene.SandboxGameplayPackages.Add(middle); scene.SandboxGameplayPackages.Add(finale);

        GeneratedDungeonPlayabilityReport report = GeneratedDungeonPlayability.Check(scene);
        Check(report.Solvable && report.Progression != null && report.Progression.Solvable && report.Progression.ItemArc != null && report.Progression.ItemArc.Steps.Any(step => step.Item == "Hookshot") && report.Progression.SolverReport.CollectedItems.Contains("Hookshot") && report.ReachableCount == 3 && report.FinaleRoom == 2 && report.LogicLinkCount == 1,
            "Native generated dungeon certificate accepts a connected start, puzzle, finale, reward, and logic link");
        RoomPackage duplicate = new RoomPackage { Scene = 99, Room = 1, DestinationRoom = 1, Name = "Duplicate" };
        duplicate.Actors.Add(new RoomPackageActor { Id = "duplicate-actor", Role = "Enemy", Name = "Deku Scrub" });
        scene.SandboxGameplayPackages.Add(duplicate);
        report = GeneratedDungeonPlayability.Check(scene);
        Check(!report.Solvable && report.Errors.Any(error => error.Contains("Multiple gameplay packages target room 1")), "Reject duplicate gameplay packages instead of silently dropping room content");
        scene.SandboxGameplayPackages.Remove(duplicate);
        scene.Transitions.RemoveAt(1);
        report = GeneratedDungeonPlayability.Check(scene);
        Check(!report.Solvable && report.UnreachableRooms.Contains(2),
            "Native generated dungeon certificate rejects an unreachable finale room");
    }

    private static void TestPushBlockSolver()
    {
        DungeonPushBlockPuzzle puzzle = new DungeonPushBlockPuzzle { Width = 6, Height = 5, PlayerStart = new DungeonGridPoint(0, 2) };
        puzzle.Blocks.Add(new DungeonGridPoint(2, 2)); puzzle.Goals.Add(new DungeonGridPoint(5, 2));
        DungeonPushBlockReport solved = DungeonPushBlockSolver.Solve(puzzle);
        Check(solved.Solvable && solved.Pushes == 3 && solved.PushRoute.Count == 3, "Solve a reachable push-block route");
        puzzle.Walls.Add(new DungeonGridPoint(3, 2)); puzzle.Walls.Add(new DungeonGridPoint(2, 1)); puzzle.Walls.Add(new DungeonGridPoint(2, 3));
        DungeonPushBlockReport blocked = DungeonPushBlockSolver.Solve(puzzle);
        Check(!blocked.Solvable && blocked.Errors.Any(error => error.Contains("No valid push-block")), "Reject an impossible push-block route");

        ProceduralDungeonPlan route = new ProceduralDungeonPlan { StartingItems = new List<string>() };
        route.Rooms.Add(new DungeonRoomPlan { Index = 0, GrantedItems = new List<string> { "Hookshot" } });
        route.Rooms.Add(new DungeonRoomPlan { Index = 1, TraversalConstraints = new List<DungeonTraversalConstraint> {
            new DungeonTraversalConstraint { Kind = "Hookshot", RequiredItem = "Hookshot", HorizontalDistance = 300, MaxDistance = 500 } } });
        DungeonTraversalProgressionReport routeReport = DungeonTraversalProgression.Check(route);
        Check(routeReport.Solvable && routeReport.AcquiredCapabilities.Contains("Hookshot"), "Carry earned traversal capability into a later room");
        route.Rooms[0].GrantedItems.Clear();
        routeReport = DungeonTraversalProgression.Check(route);
        Check(!routeReport.Solvable && routeReport.Errors.Any(error => error.Contains("requires Hookshot")), "Reject a traversal softlock before generation completes");
        route.Rooms[0].GrantedItems.Add("Hookshot");
        route.TraversalCalibration.Hookshot = 250;
        routeReport = DungeonTraversalProgression.Check(route);
        Check(!routeReport.Solvable && routeReport.Errors.Any(error => error.Contains("OoT conservative authoring limit")), "Reject a traversal span beyond the selected calibration profile");
        ProceduralDungeonPlan branchedRoute = new ProceduralDungeonPlan();
        branchedRoute.Rooms.Add(new DungeonRoomPlan { Index = 0 });
        branchedRoute.Rooms.Add(new DungeonRoomPlan { Index = 1, TraversalConstraints = new List<DungeonTraversalConstraint> { new DungeonTraversalConstraint { Kind = "Hookshot", RequiredItem = "Hookshot", HorizontalDistance = 100 } } });
        branchedRoute.Rooms.Add(new DungeonRoomPlan { Index = 2, GrantedItems = new List<string> { "Hookshot" } });
        branchedRoute.Connections.Add(new DungeonConnectionPlan { FromRoom = 0, ToRoom = 2 });
        branchedRoute.Connections.Add(new DungeonConnectionPlan { FromRoom = 2, ToRoom = 1 });
        Check(DungeonTraversalProgression.Check(branchedRoute).Solvable, "Validate traversal requirements using reachable item state rather than room index");
        DungeonTraversalCalibration fitted = DungeonTraversalCalibration.FromTraces("OoT N64 1.0 playtest", new[] {
            new DungeonTraversalTrace { Kind = "Hookshot", HorizontalDistance = 420, Success = true },
            new DungeonTraversalTrace { Kind = "Hookshot", HorizontalDistance = 530, Success = false },
            new DungeonTraversalTrace { Kind = "JumpGap", HorizontalDistance = 160, Success = true },
            new DungeonTraversalTrace { Kind = "JumpGap", HorizontalDistance = 210, Success = false }
        });
        Check(fitted.Name == "OoT N64 1.0 playtest" && fitted.Hookshot >= 420 && fitted.Hookshot < 530 && fitted.JumpGap >= 160 && fitted.JumpGap < 210,
            "Fit traversal calibration limits from successful and failed playtest traces");
        DungeonTraversalConstraint blockedRoute = new DungeonTraversalConstraint
        {
            Kind = "JumpGap", Start = new Vector3d(0, 0, 0), End = new Vector3d(100, 0, 0),
            Obstacles = new List<DungeonTraversalObstacle> { new DungeonTraversalObstacle { Name = "solid wall", Min = new Vector3d(40, -20, -20), Max = new Vector3d(60, 20, 20) } }
        };
        route.Rooms[1].BoundsMin = new Vector3d(-100, -100, -100); route.Rooms[1].BoundsMax = new Vector3d(200, 100, 100);
        route.Rooms[1].TraversalConstraints = new List<DungeonTraversalConstraint> { blockedRoute };
        routeReport = DungeonTraversalProgression.Check(route);
        Check(!routeReport.Solvable && routeReport.Errors.Any(error => error.Contains("intersects obstacle")), "Reject traversal path through a collision obstacle");
        blockedRoute.Obstacles.Clear(); blockedRoute.End = new Vector3d(400, 0, 0);
        routeReport = DungeonTraversalProgression.Check(route);
        Check(!routeReport.Solvable && routeReport.Errors.Any(error => error.Contains("outside the room collision bounds")), "Reject traversal landing outside collision bounds");
        route.TraversalCalibration.MaxRampSlope = 0.5;
        route.Rooms[1].TraversalConstraints = new List<DungeonTraversalConstraint> {
            new DungeonTraversalConstraint { Kind = "Stairs", Start = new Vector3d(0, 0, 0), End = new Vector3d(100, 100, 0), HorizontalDistance = 100, VerticalDistance = 100 }
        };
        routeReport = DungeonTraversalProgression.Check(route);
        Check(!routeReport.Solvable && routeReport.Errors.Any(error => error.Contains("slope")), "Reject ramp geometry beyond the calibrated rise-to-run limit");
        string tracePath = Path.Combine(Path.GetTempPath(), "archarina64-traversal-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            DungeonTraversalTraceSet traceSet = new DungeonTraversalTraceSet { RomFingerprint = "test-rom", GameVersion = "N64 1.0", CapturedBy = "test" };
            traceSet.Samples.Add(new DungeonTraversalTrace { Kind = "Hookshot", HorizontalDistance = 420, Success = true });
            DungeonTraversalTraceStore.Save(tracePath, traceSet);
            Check(DungeonTraversalTraceStore.Load(tracePath).Samples.Count == 1, "Persist and reload traversal playtest traces");
        }
        finally { if (File.Exists(tracePath)) File.Delete(tracePath); }
    }

    private static ObjFile MakeAnalysisTriangle()
    {
        ObjFile model = new ObjFile();
        model.Vertices.AddRange(new[] { new ObjFile.Vertex(0, 0, 0), new ObjFile.Vertex(100, 0, 0), new ObjFile.Vertex(0, 0, 100) });
        ObjFile.Group group = new ObjFile.Group { Name = "analysis_floor" };
        group.Triangles.Add(new ObjFile.Triangle { VertIndex = new[] { 0, 2, 1 } });
        model.Groups.Add(group);
        return model;
    }

    private static void TestProceduralDungeon()
    {
        ZScene scene = new ZScene { SceneNumber = 42, Scale = 1 };
        for (int i = 0; i < 2; i++)
        {
            ObjFile visual = MakeAnalysisTriangle();
            scene.Rooms.Add(new ZScene.ZRoom { ObjModel = visual, TrueGroups = visual.Groups });
        }
        scene.ColModel = MakeAnalysisTriangle();
        ZColPolyType traversalSurface = new ZColPolyType(0) { IsHookshotable = true, ClimbingCrawlingFlags = 1, IsSteep = true, EponaBlock = true };
        scene.PolyTypes.Add(traversalSurface);
        scene.Transitions.Add(new ZActor { Number = 0x0009, Variable = 0x42, FrontSwitchTo = 0, BackSwitchTo = 1, YRot = 0x4000 });
        scene.SpawnPoints.Add(new ZActor { SpawnRoom = 0, XPos = 10, YPos = 0, ZPos = 10 });
        scene.Rooms[1].ZActors.Add(new ZActor { Number = 0x01A0, Variable = 0, XPos = 20, YPos = 0, ZPos = 20 });
        RomAssetCatalog catalog = new RomAssetCatalog();
        catalog.Assets.Add(new RomPaletteAsset { Id = 0x0009, Category = "Connections", Name = "One-way Door", RequirementText = "one-way" });
        catalog.Assets.Add(new RomPaletteAsset { Id = 0x01A0, Category = "Chests", Name = "Test chest" });
        catalog.Assets.Add(new RomPaletteAsset { Id = 0x0200, Category = "Switches", Name = "Test pressure plate",
            LogicProperties = new List<RomActorLogicProperty> { new RomActorLogicProperty { Name = "Sets switch flag", Target = "Var", Mask = 0x003F } } });
        catalog.Assets.Add(new RomPaletteAsset { Id = 0x0201, Category = "Doors", Name = "Test shutter",
            LogicProperties = new List<RomActorLogicProperty> { new RomActorLogicProperty { Name = "Switch Flag", Target = "Var", Mask = 0x003F } } });
        catalog.Assets.Add(new RomPaletteAsset { Id = 0x0202, Category = "Props", Name = "Hookshot Target" });
        RomPaletteAsset descriptiveAsset = new RomPaletteAsset { Id = 0x0203, Category = "Props", Name = "Bombchu Target", RequirementText = "Supports bomb, bow, and hookshot use but does not require them" };
        RoomPackageActor descriptiveActor = new RoomPackageActor();
        RoomPackageBuilder.PopulateMetadata(descriptiveActor, descriptiveAsset, new ZActor { Number = descriptiveAsset.Id });
        Check(descriptiveActor.RequiredItems.Count == 0, "Ignore descriptive item mentions when capturing room requirements");
        descriptiveAsset.RequirementText = "Requires the Hookshot to activate";
        RoomPackageBuilder.PopulateMetadata(descriptiveActor, descriptiveAsset, new ZActor { Number = descriptiveAsset.Id });
        Check(descriptiveActor.RequiredItems.Count == 1 && descriptiveActor.RequiredItems[0] == "hookshot", "Capture explicit room item requirements");
        descriptiveAsset.RequirementText = "";
        descriptiveAsset.LogicProperties.Add(new RomActorLogicProperty { Name = "Requires item", Target = "Var", Mask = 0x0003, Dropdown = new List<RomActorDropdownValue> { new RomActorDropdownValue { Value = 1, Name = "Hookshot" } } });
        RoomPackageBuilder.PopulateMetadata(descriptiveActor, descriptiveAsset, new ZActor { Number = descriptiveAsset.Id, Variable = 1 });
        Check(descriptiveActor.RequiredItems.Count == 1 && descriptiveActor.RequiredItems[0] == "hookshot", "Read variable-driven requirements from the native actor instance");
        scene.Rooms[0].ZActors.Add(new ZActor { Number = 0x0200, Variable = 3 });
        scene.Rooms[1].ZActors.Add(new ZActor { Number = 0x0201, Variable = 3 });
        scene.Rooms[1].ZActors.Add(new ZActor { Number = 0x0202 });
        DungeonAnalysisReport report = ProceduralDungeonAnalyzer.AnalyzeScene(scene, catalog);
        Check(report.Rooms.Count == 2 && report.Connections.Count == 1, "Analyze decoded room graph and native transitions");
        Check(report.Rooms[0].Supports(DungeonRoomFeatures.Floor | DungeonRoomFeatures.Collision | DungeonRoomFeatures.Spawn), "Classify room geometry and spawn features");
        Check(report.Rooms[1].Supports(DungeonRoomFeatures.Chest) && report.ReachableRooms.Count == 2, "Classify actor assets and calculate reachability");
        Check(report.Connections[0].IsOneWay && report.Connections[0].TraversalMode == "OneWay",
            "Decode explicit one-way transition semantics from ROM metadata");
        Check(report.LogicCandidates.Count == 1 && report.LogicCandidates[0].Kind == "Opens" && report.LogicCandidates[0].Confidence > 0.7,
            "Infer a confidence-scored switch-to-door logic candidate");
        Check(report.Traversal.HookshotSurfaceCount == 1 && report.Traversal.ClimbableSurfaceCount == 1 && report.Traversal.EponaBlockSurfaceCount == 1 &&
            report.Rooms[1].HookshotAnchorCount == 1 && (report.Rooms[1].TraversalCapabilities & DungeonTraversalCapabilities.Hookshot) != 0,
            "Analyze native traversal flags and room-level Hookshot evidence");
        DungeonGeneratorSettings settings = new DungeonGeneratorSettings { Seed = 77, RoomCount = 2 };
        ProceduralDungeonPlan first = ProceduralDungeonAnalyzer.Generate(report, settings);
        ProceduralDungeonPlan second = ProceduralDungeonAnalyzer.Generate(report, settings);
        Check(first.Rooms.Count == 2 && first.Connections.Count == 1 && first.Connections[0].ReuseNativeTransition, "Generate a connected native-transition dungeon plan");
        Check(first.Rooms[0].SourceScene == second.Rooms[0].SourceScene && first.Rooms[1].SourceRoom == second.Rooms[1].SourceRoom, "Dungeon generation is deterministic for a seed");
        Check(first.Rooms[0].Mesh.TexturePolicy == "preserve-source-materials", "Dungeon mesh plan preserves source texture policy");
        Check(first.Progression != null && first.Progression.Solvable && first.Progression.ReachableRooms.Count == 2,
            "Certify generated room reachability and progression order");
        Check(first.Progression.Steps.Any(s => s.Action == "Opens" && s.Requirement == "Switch:3"),
            "Include inferred cause/effect in progression steps");
        Check(first.Assembly != null && first.Assembly.Ready && first.Assembly.RemappedReferences == 2,
            "Prepare native actor flag remapping for linked logic");
        Check(first.Topology != null && first.Topology.Nodes.Count == 2 && first.Topology.Nodes[0].Role == "Start" && first.Topology.Nodes[1].Role == "Boss",
            "Attach a mesh-independent dungeon topology contract");
        Check(first.Rooms[0].Actors.First(a => a.Number == 0x0200).Variable != 3 &&
            first.Rooms[1].Actors.First(a => a.Number == 0x0201).Variable == first.Rooms[0].Actors.First(a => a.Number == 0x0200).Variable,
            "Keep linked source and target flags synchronized after remapping");
        report.Rooms[1].RequiredItems.Add("hookshot");
        Reject(() => ProceduralDungeonAnalyzer.Generate(report, new DungeonGeneratorSettings { Seed = 77, RoomCount = 2 }),
            "Reject generation when starting inventory cannot satisfy a room requirement");
        ProceduralDungeonPlan equipped = ProceduralDungeonAnalyzer.Generate(report, new DungeonGeneratorSettings
        {
            Seed = 77, RoomCount = 2, StartingItems = new List<string> { "Hookshot" }
        });
        Check(equipped.Rooms.Count == 2 && equipped.StartingItems.Contains("Hookshot"),
            "Accept generation when the required item is in starting inventory");
        ProceduralDungeonPlan earned = new ProceduralDungeonPlan();
        earned.Rooms.Add(new DungeonRoomPlan { Index = 0, Actors = new List<DungeonActorPlacement> { new DungeonActorPlacement { SourceId = "spawn", Role = "Spawn" } }, GrantedItems = new List<string> { "Hookshot" } });
        earned.Rooms.Add(new DungeonRoomPlan { Index = 1, RequiredItems = new List<string> { "Hookshot" } });
        earned.Connections.Add(new DungeonConnectionPlan { FromRoom = 0, ToRoom = 1 });
        earned.Progression = DungeonProgressionSolver.Solve(null, earned);
        Check(earned.Progression.Solvable && earned.Progression.CollectedItems.Contains("Hookshot") && earned.Progression.ReachableRooms.Count == 2,
            "Allow earned items to unlock later rooms during progression");
        ProceduralDungeonPlan prefixedRequirement = new ProceduralDungeonPlan();
        prefixedRequirement.Rooms.Add(new DungeonRoomPlan { Index = 0, GrantedItems = new List<string> { "Hookshot" } });
        prefixedRequirement.Rooms.Add(new DungeonRoomPlan { Index = 1, RequiredItems = new List<string> { "DUNGEON Hookshot" } });
        prefixedRequirement.Connections.Add(new DungeonConnectionPlan { FromRoom = 0, ToRoom = 1 });
        Check(DungeonProgressionSolver.Solve(null, prefixedRequirement).Solvable, "Normalize DUNGEON-prefixed item requirements before solving");
        ProceduralDungeonPlan keyed = new ProceduralDungeonPlan();
        keyed.Rooms.Add(new DungeonRoomPlan { Index = 0, Actors = new List<DungeonActorPlacement> { new DungeonActorPlacement { SourceId = "spawn", Role = "Spawn" } }, GrantedItems = new List<string> { "DUNGEON Small Key" } });
        keyed.Rooms.Add(new DungeonRoomPlan { Index = 1, SmallKeyCost = 1 });
        keyed.Connections.Add(new DungeonConnectionPlan { FromRoom = 0, ToRoom = 1 });
        keyed.Progression = DungeonProgressionSolver.Solve(null, keyed);
        Check(keyed.Progression.Solvable && keyed.Progression.SmallKeysSpent == 1 && keyed.Progression.SmallKeysRemaining == 0,
            "Consume earned small keys to open locked rooms");
        ProceduralDungeonPlan softlocked = new ProceduralDungeonPlan();
        softlocked.Rooms.Add(new DungeonRoomPlan { Index = 0, Actors = new List<DungeonActorPlacement> { new DungeonActorPlacement { SourceId = "spawn", Role = "Spawn" } } });
        softlocked.Rooms.Add(new DungeonRoomPlan { Index = 1, SmallKeyCost = 1 });
        softlocked.Connections.Add(new DungeonConnectionPlan { FromRoom = 0, ToRoom = 1 });
        softlocked.Progression = DungeonProgressionSolver.Solve(null, softlocked);
        Check(!softlocked.Progression.Solvable && softlocked.Progression.MissingItems.Contains("Small Key"),
            "Report a small-key softlock instead of treating a locked room as reachable");
        ProceduralDungeonPlan bossLocked = new ProceduralDungeonPlan();
        bossLocked.Rooms.Add(new DungeonRoomPlan { Index = 0, Actors = new List<DungeonActorPlacement> { new DungeonActorPlacement { SourceId = "spawn", Role = "Spawn" } }, GrantedItems = new List<string> { "DUNGEON Boss Key" } });
        bossLocked.Rooms.Add(new DungeonRoomPlan { Index = 1, RequiresBossKey = true });
        bossLocked.Connections.Add(new DungeonConnectionPlan { FromRoom = 0, ToRoom = 1 });
        bossLocked.Progression = DungeonProgressionSolver.Solve(null, bossLocked);
        Check(bossLocked.Progression.Solvable && bossLocked.Progression.BossKeyAcquired,
            "Carry a boss key reward into a boss-door requirement");
        ProceduralDungeonPlan directed = new ProceduralDungeonPlan();
        directed.Rooms.Add(new DungeonRoomPlan { Index = 0, Actors = new List<DungeonActorPlacement> { new DungeonActorPlacement { SourceId = "spawn", Role = "Spawn" } } });
        directed.Rooms.Add(new DungeonRoomPlan { Index = 1 });
        directed.Connections.Add(new DungeonConnectionPlan { FromRoom = 0, ToRoom = 1, Bidirectional = false });
        directed.Progression = DungeonProgressionSolver.Solve(null, directed);
        Check(directed.Progression.Solvable && directed.Progression.ReachableRooms.Count == 2,
            "Traverse an explicitly one-way generated connection in its authored direction");
        ProceduralDungeonPlan gatedEdge = new ProceduralDungeonPlan();
        gatedEdge.Rooms.Add(new DungeonRoomPlan { Index = 0, Actors = new List<DungeonActorPlacement> { new DungeonActorPlacement { SourceId = "spawn", Role = "Spawn" } } });
        gatedEdge.Rooms.Add(new DungeonRoomPlan { Index = 1 });
        gatedEdge.Connections.Add(new DungeonConnectionPlan { FromRoom = 0, ToRoom = 1, RequiredItems = new List<string> { "Hookshot" } });
        gatedEdge.Progression = DungeonProgressionSolver.Solve(null, gatedEdge);
        Check(!gatedEdge.Progression.Solvable && gatedEdge.Progression.MissingItems.Contains("Hookshot"),
            "Reject a connection whose edge requirement is unavailable");
        ProceduralDungeonPlan itemArc = new ProceduralDungeonPlan { StartingItems = new List<string> { "Deku Shield" } };
        itemArc.Rooms.Add(new DungeonRoomPlan { Index = 0, Actors = new List<DungeonActorPlacement> { new DungeonActorPlacement { SourceId = "spawn", Role = "Spawn" } } });
        itemArc.Rooms.Add(new DungeonRoomPlan { Index = 1, GrantedItems = new List<string> { "Hookshot", "Iron Boots" } });
        itemArc.Rooms.Add(new DungeonRoomPlan { Index = 2, RequiredItems = new List<string> { "Hookshot", "Deku Shield" } });
        itemArc.Rooms.Add(new DungeonRoomPlan { Index = 3, RequiredItems = new List<string> { "Hookshot" }, TraversalConstraints = new List<DungeonTraversalConstraint> { new DungeonTraversalConstraint { Kind = "Underwater", RequiredItem = "Iron Boots" } } });
        itemArc.Connections.Add(new DungeonConnectionPlan { FromRoom = 0, ToRoom = 1 });
        itemArc.Connections.Add(new DungeonConnectionPlan { FromRoom = 1, ToRoom = 2 });
        itemArc.Connections.Add(new DungeonConnectionPlan { FromRoom = 2, ToRoom = 3 });
        DungeonItemArcReport itemArcReport = DungeonItemArcPlanner.Analyze(itemArc);
        Check(itemArcReport.Solvable && itemArcReport.HasMidDungeonAcquisition && itemArcReport.MidDungeonAcquisitionCount == 2 &&
            itemArcReport.Steps.Any(step => step.Item == "Hookshot" && step.FirstConsumerRoom == 2 && step.ConsumerRooms.Contains(3)),
            "Certify a mid-dungeon item arc with multiple item dependencies and later consumers");
        itemArc.Rooms[1].GrantedItems[0] = "DUNGEON Hookshot";
        itemArc.Progression = DungeonProgressionSolver.Solve(null, itemArc);
        Check(itemArc.Progression.Solvable && itemArc.Progression.CollectedItems.Contains("Hookshot"),
            "Normalize generated DUNGEON-prefixed item rewards before progression solving");
        itemArc.Rooms[1].GrantedItems[0] = "Hookshot";
        itemArc.Rooms[1].GrantedItems.Clear();
        itemArcReport = DungeonItemArcPlanner.Analyze(itemArc);
        Check(!itemArcReport.Solvable && itemArcReport.Errors.Any(error => error.Contains("Hookshot")),
            "Reject an item arc whose mid-dungeon acquisition is missing");
    }

    private static void TestProceduralSceneAggregation()
    {
        ZScene first = new ZScene { SceneNumber = 2 };
        first.Rooms.Add(new ZScene.ZRoom());
        ZScene second = new ZScene { SceneNumber = 7 };
        second.Rooms.Add(new ZScene.ZRoom());
        DungeonAnalysisReport report = ProceduralDungeonAnalyzer.AnalyzeScenes(new[] { first, second }, null);
        Check(report.SceneCount == 2 && report.AnalyzedScenes.SequenceEqual(new[] { 2, 7 }), "Aggregate procedural analysis tracks decoded ROM scenes");
        Check(report.Rooms.Count == 2 && report.Rooms[1].SceneId == 7, "Aggregate procedural analysis keeps cross-scene room identity");
        ZScene dungeon = new ZScene { SceneNumber = 3 };
        dungeon.Rooms.Add(new ZScene.ZRoom());
        dungeon.Rooms.Add(new ZScene.ZRoom());
        dungeon.Rooms[0].ZObjects.Add(new ZScene.ZUShort(0x0042));
        dungeon.Rooms[0].ZActors.Add(new ZActor { Number = 0x000A, Variable = (ushort)((1 << 5) | 2) });
        RomAssetCatalog catalog = new RomAssetCatalog();
        catalog.Assets.Add(new RomPaletteAsset { Category = "Object banks", Id = 0x0042, Name = "Dungeon object bank" });
        catalog.Assets.Add(new RomPaletteAsset { Category = "Chests", Id = 0x000A, Name = "Treasure Chest",
            LogicProperties = new List<RomActorLogicProperty>
            {
                new RomActorLogicProperty { Name = "Chest Flag", Target = "Var", Mask = 0x001F },
                new RomActorLogicProperty { Name = "Reward", Target = "Var", Mask = 0x0FE0, Position = 5,
                    Dropdown = new List<RomActorDropdownValue> { new RomActorDropdownValue { Value = 1, Name = "DUNGEON Small Key" } } }
            } });
        DungeonPatternReport patterns = ProceduralDungeonAnalyzer.AnalyzeDungeonPatterns(new[] { dungeon }, catalog);
        Check(patterns.Dungeons.Count == 1 && patterns.Dungeons[0].SceneId == 3 && patterns.Dungeons[0].RoomOwnership.Count == 2,
            "Profile dungeon room ownership and layout structure");
        Check(patterns.Dungeons[0].ObjectBanks.Contains("Dungeon object bank"), "Profile dungeon object-bank dependencies");
        Check(patterns.Dungeons[0].SmallKeyCount == 1 && patterns.Dungeons[0].ChestCount == 1 && patterns.Dungeons[0].ChestFlagOwnership.Count == 1,
            "Profile dungeon reward ownership and chest flags");
        Check(patterns.Dungeons[0].RoomRoles.ContainsKey("Reward") && patterns.Dungeons[0].RoomRoles.ContainsKey("DeadEnd") && patterns.Dungeons[0].AverageRewardDepth == 0,
            "Profile semantic room roles and reward depth");
        Check(patterns.Dungeons[0].ProgressionChain.Count == 2 && patterns.Comparison.AverageRoomCount == 2,
            "Compare dungeon progression chains across profiles");
        DungeonTopologyPlan branching = DungeonTopologyPlanner.Create(6, new DungeonPatternProfile { BranchingScore = 2.4 }, 9);
        Check(branching.Archetype == "Branching" && branching.Edges.Any(e => e.Purpose == "OptionalBranch"),
            "Choose a branching topology from learned dungeon metrics");
    }

    private static void TestTiles()
    {
        ZScene scene = new ZScene { Scale = 1 };
        ObjFile model = new ObjFile();
        model.Vertices.AddRange(new[] { new ObjFile.Vertex(0, 0, 0), new ObjFile.Vertex(100, 0, 0), new ObjFile.Vertex(0, 0, 100) });
        model.TextureCoordinates.AddRange(new[] { new ObjFile.TextureCoord(0, 0), new ObjFile.TextureCoord(1, 0), new ObjFile.TextureCoord(0, 1) });
        model.Normals.Add(new ObjFile.Normal(0, 1, 0)); model.VertexColors.Add(new ObjFile.VertexColor(1, 0, 0, 1));
        model.Materials.Add(new ObjFile.Material { Name = "floor", ForceRGBA = true });
        ObjFile.Group group = new ObjFile.Group { Name = "floor", BackfaceCulling = false, TileS = 2, TintAlpha = 0xFFEEDDCC };
        group.Triangles.Add(new ObjFile.Triangle { MaterialName = "floor", VertIndex = new[] { 0, 2, 1 }, TexCoordIndex = new[] { 0, 2, 1 }, NormalIndex = new[] { 0, 0, 0 }, VertColor = new[] { 1, 1, 1 } });
        model.Groups.Add(group);
        scene.Rooms.Add(new ZScene.ZRoom { ObjModel = model, TrueGroups = model.Groups });
        scene.ColModel = TileMesh.Copy(model, model.Groups).ToModel(); scene.PolyTypes.Add(new ZColPolyType(0));
        scene.Rooms[0].ZActors.Add(new ZActor(0x0095, 25, 0, 25, 0, 0, 0, 0));
        SectionTile tile = SectionTile.Capture(scene, 0, new HashSet<int> { 0 }, new HashSet<int> { 0 }, new OpenTK.Vector3d(0, 0, 0), "Test floor", "synthetic", 100);
        SectionTile clone = TileStorage.Clone(tile);
        Check(clone.Visual.Groups[0].TileS == 2 && !clone.Visual.Groups[0].BackfaceCulling && clone.Visual.Groups[0].TintAlpha == 0xFFEEDDCC,
            "Tile XML preserves render properties normally ignored by scene XML");
        Check(clone.Visual.UVs[1].U == 1 && clone.Visual.Groups[0].Triangles[0].TexCoordIndex[1] == 2 && clone.Visual.Materials[0].ForceRGBA,
            "Tile preserves UVs, face indices and material format");
        SectionTile gameplayTile = SectionTile.Capture(scene, 0, new HashSet<int> { 0 }, new HashSet<int> { 0 }, OpenTK.Vector3d.Zero, "Gameplay floor", "synthetic", 100,
            null, null, null, true);
        SectionTile gameplayClone = TileStorage.Clone(gameplayTile);
        Check(gameplayClone.Gameplay != null && gameplayClone.Gameplay.Actors.Count == 1 && gameplayClone.Gameplay.Actors[0].Number == 0x0095,
            "Tile captures and serializes room gameplay actors without ROM data");
        TileSceneGeometry geometry = TileSceneGeometry.Capture(scene);
        geometry.Place(gameplayTile, 0, new OpenTK.Vector3d(500, 0, 0), 1);
        geometry.Place(tile, 0, new OpenTK.Vector3d(-500, 0, 0), 0);
        Check(geometry.Rooms[0].Groups.Count == 3 && geometry.Collision.Groups.Count == 3, "Two copies append independent visual and collision groups");
        Check(geometry.Rooms[0].Groups[1].Triangles[0].VertColor.All(i => i == 2) && geometry.Rooms[0].Groups[2].Triangles[0].VertColor.All(i => i == 4),
            "Tile placement remaps one-based native vertex colors");
        Check(geometry.Rooms[0].Vertices[4].X == 600 && geometry.Rooms[0].Vertices[4].Z == 0,
            "Quarter-turn placement transforms pivot-relative vertices");
        geometry.Rooms[0].Vertices[3].X = 999;
        Check(tile.Visual.Vertices[0].X == 0 && model.Vertices[0].X == 0 && geometry.Rooms[0].Vertices[6].X == -500,
            "Editing one placement does not change tile, source, or second copy");
        int before = geometry.Rooms[0].Groups.Count;
        Reject(() => geometry.Place(tile, 0, new OpenTK.Vector3d(32767, 0, 32767), 0), "Reject out-of-range tile before destination mutation");
        Check(geometry.Rooms[0].Groups.Count == before, "Failed tile transform leaves destination unchanged");
        scene.PolyTypes[0].ExitNumber = 1;
        SectionTile linked = SectionTile.Capture(scene, 0, new HashSet<int> { 0 }, new HashSet<int> { 0 }, OpenTK.Vector3d.Zero, "linked", "synthetic", 100);
        geometry.SceneId = linked.SourceScene + 1;
        Reject(() => geometry.Place(linked, 0, OpenTK.Vector3d.Zero, 0), "Reject cross-scene collision references without explicit mapping");
        geometry.Place(linked, 0, OpenTK.Vector3d.Zero, 0, 0);
        Check(geometry.Collision.Groups.Last().PolyType == 0, "Explicit destination surface mapping replaces source references");
        var picker = typeof(MainForm).GetMethod("PickSandboxFace", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        int hit = (int)picker.Invoke(null, new object[] { model, model.Groups, new OpenTK.Vector3d(25, 100, 25), new OpenTK.Vector3d(25, -100, 25), 1f });
        Check(hit == 0, "Viewport face ray picks the floor triangle");
    }

    private static void TestTileAssembly()
    {
        Check(TileAssembly.RoomDoorFacing(90, true) == 0 && TileAssembly.RoomDoorFacing(90, false) == -180 &&
            TileAssembly.NativeDoorYaw(0) == 90,
            "Room doorway facing converts native +Z yaw into tile +X socket space");
        Check(Math.Abs(TileAssembly.FacingVector(0).X - 1) < 0.000001 && Math.Abs(TileAssembly.FacingVector(0).Z) < 0.000001 &&
            Math.Abs(TileAssembly.FacingVector(90).X) < 0.000001 && Math.Abs(TileAssembly.FacingVector(90).Z - 1) < 0.000001,
            "Doorway facing vectors use the native +X/+Z convention");
        TileSocket rotated = new TileSocket { Id = "rotated", X = 500, Y = 0, Z = 0, Facing = 0, Width = 100, Height = 180 };
        TileSocket placed = rotated.Placed(OpenTK.Vector3d.Zero, 1, 0, "placed");
        Check(Math.Abs(placed.X) < 0.000001 && Math.Abs(placed.Z + 500) < 0.000001 && Math.Abs(placed.Facing + 90) < 0.000001,
            "Placed doorway position and facing use the same clockwise quarter-turn");
        var source = new TileSocket { Id = "source", X = 0, Y = 0, Z = 0, Facing = 0, Width = 100, Height = 180 };
        var target = new TileSocket { Id = "target", X = 400, Y = 0, Z = 0, Facing = 180, Width = 100, Height = 180 };
        TileSnap snap = TileAssembly.FindSnap(new[] { source }, new[] { target }, new OpenTK.Vector3d(390, 0, 0), 0, 50);
        Check(snap != null && snap.SourceSocket == 0 && snap.Turns == 0 && snap.Position.X == 400, "Magnetic socket alignment chooses opposed doorway faces");
        target.Width = 120;
        Check(TileAssembly.FindSnap(new[] { source }, new[] { target }, OpenTK.Vector3d.Zero, 0, 50) == null, "Magnetic socket alignment rejects incompatible doorway widths");

        ObjFile model = new ObjFile();
        model.Vertices.AddRange(new[] { new ObjFile.Vertex(0, 0, 0), new ObjFile.Vertex(10, 0, 0), new ObjFile.Vertex(0, 0, 10),
            new ObjFile.Vertex(10, 0, 0), new ObjFile.Vertex(20, 0, 0), new ObjFile.Vertex(10, 0, 10) });
        ObjFile.Group group = new ObjFile.Group { Name = "selection" };
        group.Triangles.Add(new ObjFile.Triangle { VertIndex = new[] { 0, 1, 2 } });
        group.Triangles.Add(new ObjFile.Triangle { VertIndex = new[] { 3, 4, 5 } });
        model.Groups.Add(group);
        int crossing;
        Check(TileAssembly.FacesInBox(model, model.Groups, new OpenTK.Vector3d(-1, -1, -1), new OpenTK.Vector3d(11, 1, 11), out crossing).Count == 1 && crossing == 1,
            "Box selection keeps whole faces and reports boundary overlaps");
        Check(TileAssembly.ConnectedFaces(model, model.Groups, new HashSet<int> { 0 }).Count == 1,
            "Connected-face growth stops at nonmatching section boundaries");
        model.Vertices[3] = model.Vertices[1]; model.Vertices[5] = model.Vertices[2];
        Check(TileAssembly.ConnectedFaces(model, model.Groups, new HashSet<int> { 0 }).Count == 2,
            "Connected-face growth follows shared geometry edges");

        ZScene tiledScene = new ZScene { Scale = 1, SceneNumber = 7 };
        tiledScene.Rooms.Add(new ZScene.ZRoom { ObjModel = model, TrueGroups = model.Groups });
        tiledScene.Rooms.Add(new ZScene.ZRoom { ObjModel = model, TrueGroups = model.Groups });
        tiledScene.ColModel = TileMesh.Copy(model, model.Groups).ToModel();
        tiledScene.Transitions.Add(new ZActor(0, 255, 1, 255, 0x2E, 5, 0, 5, 0, 0));
        var plans = DungeonRoomTiling.Analyze(tiledScene, 7, 0, new[] { new DungeonTilingRule { Scene = 7, Room = 0, Transition = 0, Width = 100, Depth = 100, Height = 100 } });
        Check(plans.Count == 1 && plans[0].Transition == 0 && plans[0].Socket.Room == 0 && plans[0].VisualFaces.Count > 0,
            "Entrance subdivision assigns a generated tile to the correct room");
        var corridor = new SquareTubeCorridor { Start = new OpenTK.Vector3d(0, 0, 0), End = new OpenTK.Vector3d(500, 0, 0), Width = 200, Height = 240 };
        corridor.Validate(); Check(corridor.Direction.X == 1, "Square corridor validates and exposes connection direction");
        ObjFile sweepVisual = new ObjFile(), sweepCollision = new ObjFile();
        RoomGeometryBuilder.AppendSquareTubePath(sweepVisual, sweepCollision,
            new[] { new OpenTK.Vector3d(0, 0, 0), new OpenTK.Vector3d(300, 0, 0), new OpenTK.Vector3d(300, 0, 300) },
            100, 180, 100, "floor", "ceiling", "wall", 0);
        Check(sweepVisual.Groups.Count == 8 && sweepCollision.Groups.Count == 8 &&
            sweepVisual.Groups.Any(g => g.Name.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0),
            "Square corridor paths use a continuous swept mesh with a sealed low-poly elbow");
        ObjFile rampVisual = new ObjFile(), rampCollision = new ObjFile();
        RoomGeometryBuilder.AppendLowPolyRamp(rampVisual, rampCollision, new OpenTK.Vector3d(0, 0, 0), new OpenTK.Vector3d(400, 200, 0),
            120, 180, "stairs", "ceiling", "wall", 0);
        Check(rampVisual.Groups.Count == 5 && rampCollision.Groups.Count == 5 &&
            rampVisual.Groups.Sum(g => g.Triangles.Count) == 10 && rampCollision.Groups.Sum(g => g.Triangles.Count) == 10 &&
            rampVisual.Groups.SelectMany(g => g.Triangles).All(t => t.MaterialName == "stairs" || t.MaterialName == "ceiling" || t.MaterialName == "wall"),
            "Vertical connections use a five-sided ten-triangle ramp primitive with stairs material");
        Reject(() => RoomGeometryBuilder.AppendLowPolyRamp(new ObjFile(), new ObjFile(),
            new OpenTK.Vector3d(0, 0, 0), new OpenTK.Vector3d(0, 200, 0), 120, 180, "stairs", "ceiling", "wall", 0),
            "Reject pure vertical corridor geometry without horizontal ramp run");
        var package = new RoomPackage { Scene = 7, Room = 0, Visual = new TileMesh(), Collision = new TileMesh() };
        package.Actors.Add(new RoomPackageActor { Id = "plate", Role = "Pressure plate" });
        package.Actors.Add(new RoomPackageActor { Id = "door", Role = "Door" });
        LogicLinkGraph.Add(package, "plate", "door", LogicLinkKind.Opens);
        Check(package.Validate().Count == 0 && package.Links.Count == 1, "Room package accepts a validated no-code cause/effect link");

        ZScene nativeScene = new ZScene { SceneNumber = 7, Scale = 1 };
        nativeScene.Rooms.Add(new ZScene.ZRoom()); nativeScene.Rooms.Add(new ZScene.ZRoom());
        package.DestinationRoom = 0;
        package.Actors.Add(new RoomPackageActor { Id = "enemy", Number = 0x0037, Position = new OpenTK.Vector3d(10, 20, 30), RequiredObjects = new List<ushort> { 0x0009 } });
        package.Spawns.Add(new RoomPackageActor { Id = "spawn", Number = 0x0000, Position = new OpenTK.Vector3d(0, 0, 0) });
        package.Actors.Add(new RoomPackageActor { Id = "native-door", Name = "Door", Number = 0x0009, IsTransition = true,
            FrontRoom = 0, BackRoom = 1, FrontCamera = 255, BackCamera = 255, Position = new OpenTK.Vector3d(100, 0, 0) });
        nativeScene.SandboxGameplayPackages.Add(package);
        Check(NativeGameplayMaterializer.Apply(nativeScene).Count == 0 && nativeScene.Rooms[0].ZActors.Count == 3 &&
            nativeScene.SpawnPoints.Count == 1 && nativeScene.Transitions.Count == 1 && nativeScene.Rooms[0].ZObjects.Any(o => o.Value == 9),
            "Materialize tile actors, spawns, objects and native doors");
        NativeGameplayMaterializer.Apply(nativeScene);
        Check(nativeScene.Rooms[0].ZActors.Count == 3 && nativeScene.SpawnPoints.Count == 1 && nativeScene.Transitions.Count == 1,
            "Native gameplay materialization is idempotent");
        foreach (ZActor persisted in nativeScene.Rooms[0].ZActors.Concat(nativeScene.SpawnPoints).Concat(nativeScene.Transitions)) persisted.SandboxGameplayId = null;
        Check(NativeGameplayMaterializer.Apply(nativeScene).Count == 0 && nativeScene.Rooms[0].ZActors.Count == 3 &&
            nativeScene.SpawnPoints.Count == 1 && nativeScene.Transitions.Count == 1,
            "Recover materialized gameplay actors after scene reopen");
        var setter = new DungeonFlagReference { Kind = "Switch", Target = "Var", Mask = 0x000F, Position = 0, Value = 2, CanSet = true };
        var checker = new DungeonFlagReference { Kind = "Switch", Target = "Var", Mask = 0x000F, Position = 0, Value = 7, CanCheck = true };
        package.Actors.First(a => a.Id == "plate").Flags.Add(setter); package.Actors.First(a => a.Id == "door").Flags.Add(checker);
        nativeScene.Rooms[0].ZActors.First(a => a.SandboxGameplayId == "plate").SandboxGameplayId = "plate";
        nativeScene.Rooms[0].ZActors.First(a => a.SandboxGameplayId == "door").SandboxGameplayId = "door";
        Check(NativeLogicLinkMaterializer.Apply(nativeScene, null).Count == 0 &&
            nativeScene.Rooms[0].ZActors.First(a => a.SandboxGameplayId == "plate").Variable == nativeScene.Rooms[0].ZActors.First(a => a.SandboxGameplayId == "door").Variable,
            "Allocate and apply a shared native flag for a gameplay link");
        ushort linkedFlag = nativeScene.Rooms[0].ZActors.First(a => a.SandboxGameplayId == "plate").Variable;
        Check(NativeLogicLinkMaterializer.Apply(nativeScene, null).Count == 0 &&
            nativeScene.Rooms[0].ZActors.First(a => a.SandboxGameplayId == "plate").Variable == linkedFlag,
            "Reuse the same native flag value on repeated export");
    }

    private static void TestSharedDocumentLifecycle()
    {
        ZScene scene = new ZScene();
        ArcharinaSceneDocument document = new ArcharinaSceneDocument(scene);
        int notifications = 0;
        document.Changed += delegate { notifications++; };
        using (document.BeginUpdate("compound dungeon edit"))
        {
            document.MarkChanged("place room");
            document.MarkChanged("place doorway");
        }
        Check(document.IsDirty && document.Revision == 1 && notifications == 1,
            "Shared document batches editor changes into one revision");
        document.MarkSaved();
        Check(!document.IsDirty, "Shared document clears dirty state after save");
        TileSceneGeometry sidecar = new TileSceneGeometry();
        document.SetSidecarState(sidecar);
        Check(object.ReferenceEquals(document.SidecarState, sidecar) && !document.IsDirty,
            "Sidecar synchronization does not manufacture an edit");

        string root = Path.Combine(Path.GetTempPath(), "archarina-sidecar-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string sourcePath = Path.Combine(root, "scene.xml");
            string sidecarPath = Path.Combine(root, "sidecar.xml");
            File.WriteAllText(sidecarPath, "not a tile scene");
            ZScene relativeScene = new ZScene { SandboxGeometryFile = "sidecar.xml" };
            ArcharinaSceneDocument relativeDocument = new ArcharinaSceneDocument(relativeScene, null, sourcePath);
            Check(Path.IsPathRooted(relativeScene.SandboxGeometryFile) &&
                string.Equals(Path.GetFullPath(sidecarPath), relativeScene.SandboxGeometryFile, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(relativeDocument.SidecarLoadError),
                "Sidecar paths are normalized and failed loads remain diagnosable");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void TestSceneCloneOwnsDungeonState()
    {
        ZScene scene = new ZScene();
        ZScene.ZRoom room = new ZScene.ZRoom();
        room.ObjModel = new ObjFile();
        room.ObjModel.Vertices.Add(new ObjFile.Vertex(1, 2, 3));
        room.ObjModel.Vertices.Add(new ObjFile.Vertex(4, 5, 6));
        room.ObjModel.Vertices.Add(new ObjFile.Vertex(7, 8, 9));
        ObjFile.Group group = new ObjFile.Group { Name = "room-group" };
        group.Triangles.Add(new ObjFile.Triangle { VertIndex = new[] { 0, 1, 2 }, TexCoordIndex = new[] { 0, 0, 0 }, NormalIndex = new[] { 0, 0, 0 } });
        room.ObjModel.Groups.Add(group);
        room.TrueGroups = room.ObjModel.Groups;
        room.GroupSettings.TileS[0] = 7;
        room.MeshEdited = true;
        scene.Rooms.Add(room);
        scene.TileSockets.Add(new TileSocket { Id = "socket-a", X = 100 });
        RoomPackage package = new RoomPackage { Name = "package-a" };
        package.Actors.Add(new RoomPackageActor { Id = "actor-a", Name = "Actor A" });
        scene.SandboxGameplayPackages.Add(package);
        scene.Cutscene.Add(new ZCutscene { Marker = 0x13, Data = new ushort[] { 1 } });
        scene.SegmentFunctions.Add(new ZSegmentFunction { Functions = new List<ZTextureAnim> { new ZTextureAnim() } });

        ZScene clone = scene.Clone();
        clone.TileSockets[0].X = 200;
        clone.SandboxGameplayPackages[0].Actors[0].Id = "actor-b";
        clone.Rooms[0].ObjModel.Vertices[0].X = 99;
        clone.Rooms[0].GroupSettings.TileS[0] = 11;
        clone.Cutscene[0].Data[0] = 9;
        clone.SegmentFunctions[0].Functions[0].Preview = true;
        Check(scene.TileSockets[0].X == 100 && scene.SandboxGameplayPackages[0].Actors[0].Id == "actor-a" &&
            scene.Rooms[0].ObjModel.Vertices[0].X == 1 && scene.Rooms[0].GroupSettings.TileS[0] == 7 &&
            clone.Rooms[0].MeshEdited &&
            scene.Cutscene[0].Data[0] == 1 && !scene.SegmentFunctions[0].Functions[0].Preview &&
            !object.ReferenceEquals(scene.TileSockets, clone.TileSockets) &&
            !object.ReferenceEquals(scene.SandboxGameplayPackages, clone.SandboxGameplayPackages),
            "Scene clones own room, animation, socket, and gameplay state");
    }

    private static void TestSharedEditorServices()
    {
        string root = Path.Combine(Path.GetTempPath(), "archarina64-shared-assets-" + Guid.NewGuid().ToString("N"));
        string legacy = Path.Combine(root, "legacy");
        string settings = Path.Combine(root, "settings");
        Directory.CreateDirectory(legacy);
        string asset = Path.Combine(legacy, "rooms", "hub.sog.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(asset));
        File.WriteAllText(asset, "shared asset");
        string emulator = Path.Combine(root, "Project64.exe");
        string rom = Path.Combine(root, "playtest.z64");
        File.WriteAllText(emulator, "test executable");
        File.WriteAllText(rom, "test rom");
        try
        {
            SharedAssetRepository repository = new SharedAssetRepository(Path.Combine(root, "authored_geometry"), legacy);
            int changeNotifications = 0;
            repository.AssetsChanged += delegate { changeNotifications++; };
            Check(repository.EnumerateAuthoredMeshes().Count == 1 && File.Exists(Path.Combine(root, "authored_geometry", "rooms", "hub.sog.xml")),
                "Shared asset repository migrates and enumerates authored meshes");
            repository.NotifyAssetChanged(Path.Combine(root, "authored_geometry", "rooms", "hub.sog.xml"));
            Check(changeNotifications == 1, "Shared asset repository broadcasts authored asset changes");
            Project64PlaytestService service = new Project64PlaytestService(settings);
            service.SetExecutable(emulator);
            Project64PlaytestService reloaded = new Project64PlaytestService(settings);
            System.Diagnostics.ProcessStartInfo start = reloaded.CreateStartInfo(rom);
            Check(reloaded.GetConfiguredExecutable() == Path.GetFullPath(emulator) && start.FileName == Path.GetFullPath(emulator) &&
                start.Arguments.Contains(Path.GetFileName(rom)), "Project64 playtest service persists configuration and builds launch state");

            string richAsset = Path.Combine(root, "authored_geometry", "rooms", "rich.sog.xml");
            AuthoredMeshAssetDocument richDocument = new AuthoredMeshAssetDocument
            {
                Name = "Rich shared asset",
                SourceSceneScale = 2.0,
                HasSourceSceneScale = true,
                HasMirrorModifier = true,
                MirrorModifier = new AuthoredMeshAssetMirrorModifier { MirrorX = true }
            };
            richDocument.Vertices.Add(new AuthoredMeshAssetVertex { X = 1, HasColor = true, R = 0.25, G = 0.5, B = 0.75, A = 1.0 });
            richDocument.Vertices.Add(new AuthoredMeshAssetVertex { X = 2 });
            richDocument.Vertices.Add(new AuthoredMeshAssetVertex { X = 3 });
            richDocument.Faces.Add(new AuthoredMeshAssetFace
            {
                A = 0, B = 1, C = 2, HasUv = true, UA = 0.1, VA = 0.2, UB = 0.3, VB = 0.4, UC = 0.5, VC = 0.6
            });
            richDocument.Collections.Add(new AuthoredMeshAssetCollection { Name = "Collection 1" });
            Directory.CreateDirectory(Path.GetDirectoryName(richAsset));
            XmlSerializer assetSerializer = new XmlSerializer(typeof(AuthoredMeshAssetDocument));
            using (FileStream stream = File.Create(richAsset)) assetSerializer.Serialize(stream, richDocument);
            AuthoredMeshAssetDocument roundTripped;
            using (FileStream stream = File.OpenRead(richAsset)) roundTripped = (AuthoredMeshAssetDocument)assetSerializer.Deserialize(stream);
            Check(roundTripped.HasSourceSceneScale && Math.Abs(roundTripped.SourceSceneScale - 2.0) < 0.000001 &&
                roundTripped.HasMirrorModifier && roundTripped.MirrorModifier.MirrorX &&
                roundTripped.Vertices[0].HasColor && roundTripped.Faces[0].HasUv &&
                roundTripped.Collections.Count == 1,
                "Archarina preserves and reads rich modeling-editor authored assets");
            AuthoredMeshAssetManifest manifest;
            Check(AuthoredMeshAssetManifestReader.TryRead(richAsset, out manifest) && manifest.VertexCount == 3 &&
                manifest.FaceCount == 1 && manifest.CollectionCount == 1 && manifest.HasUvData &&
                manifest.HasVertexColors && manifest.HasMirrorModifier &&
                Math.Abs(manifest.SourceSceneScale - 2.0) < 0.000001,
                "Shared authored-asset manifest describes rich assets without renderer coupling");

            RoomPackage gameplay = new RoomPackage { Name = "Shared logic package", Scene = 3, Room = 4 };
            gameplay.Actors.Add(new RoomPackageActor
            {
                Id = "switch-a", Role = "Switch", Number = 0x1234, Variable = 0x0042,
                Position = new OpenTK.Vector3d(10, 20, 30), Rotation = new OpenTK.Vector3(1, 2, 3),
                RequiredItems = new List<string> { "bow" }, GrantedItems = new List<string> { "small key" },
                Flags = new List<DungeonFlagReference> { new DungeonFlagReference { Kind = "Switch", Target = "Var", Mask = 0x00FF, CanSet = true } }
            });
            gameplay.Actors.Add(new RoomPackageActor { Id = "door-b", IsTransition = true });
            gameplay.Links.Add(new LogicLink { SourceId = "switch-a", TargetId = "door-b", Kind = LogicLinkKind.Opens, Parameter = "flag" });
            SharedGameplayPackage sharedGameplay = RoomPackageSharedAdapter.Export(gameplay);
            RoomPackage gameplayRoundTrip = RoomPackageSharedAdapter.Import(sharedGameplay);
            Check(sharedGameplay.Actors.Count == 2 && sharedGameplay.Links[0].Kind == SharedLogicLinkKind.Opens &&
                gameplayRoundTrip.Actors[0].Position.X == 10 && gameplayRoundTrip.Actors[0].Flags[0].Mask == 0x00FF &&
                gameplayRoundTrip.Actors[0].RequiredItems.Contains("bow") && gameplayRoundTrip.Links[0].Kind == LogicLinkKind.Opens,
                "Shared gameplay contract round-trips actors, flags, rewards, and links");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void TestSharedAssemblyBoundary()
    {
        Assembly sharedAssembly = typeof(SharedAssetRepository).Assembly;
        Assembly archarinaAssembly = typeof(ArcharinaEditorHost).Assembly;
        Check(sharedAssembly.GetName().Name == "SharpOcarina.Shared",
            "Shared editor contracts live in one dedicated assembly");
        Check(archarinaAssembly.GetReferencedAssemblies().Any(reference => reference.Name == "SharpOcarina.Shared"),
            "Archarina references the canonical shared editor assembly");
        Check(typeof(ProjectDocument).Assembly == typeof(SharedGameplayPackage).Assembly,
            "Document and gameplay contracts resolve from the same runtime assembly");
    }

    private static void TestSharedSceneHandoff()
    {
        string root = Path.Combine(Path.GetTempPath(), "archarina-handoff-" + Guid.NewGuid().ToString("N"));
        try
        {
            SharedEditorWorkspace workspace = new SharedEditorWorkspace(
                Path.Combine(root, "authored_geometry"), Path.Combine(root, "project64"));
            ZScene scene = new ZScene { Name = "Handoff Scene", SceneNumber = 7 };
            scene.Rooms.Add(new ZScene.ZRoom { ModelShortFilename = "Hub" });
            scene.Rooms.Add(new ZScene.ZRoom { ModelShortFilename = "Exit" });
            scene.Transitions.Add(new ZActor(0, 255, 1, 255, 0x09, 10, 20, 30, 0, 0));
            ArcharinaSceneDocument document = new ArcharinaSceneDocument(scene, new TileSceneGeometry(), "handoff.zscene");
            document.MarkSaved();

            bool raised = false;
            workspace.SceneHandoffPublished += delegate(object sender, SharedSceneHandoffEventArgs args)
            {
                raised = args != null && args.Handoff != null && args.Handoff.Publisher == "Archarina64";
            };
            SharedSceneHandoff handoff = ArcharinaSceneHandoffAdapter.Export(document, workspace);
            workspace.PublishSceneHandoff(handoff);
            SharedSceneHandoff stored = null;
            Check(raised && workspace.TryGetSceneHandoff(out stored) && stored.Rooms.Count == 2 &&
                stored.Connections.Count == 1 && stored.SceneName == "Handoff Scene",
                "Publish a neutral scene handoff through the shared workspace");
            Check(Math.Abs(stored.Connections[0].X - 10.0) < 0.000001 &&
                Math.Abs(stored.Connections[0].Y - 20.0) < 0.000001 &&
                Math.Abs(stored.Connections[0].Z - 30.0) < 0.000001,
                "Shared scene handoff preserves sub-unit transition coordinates");
            stored.Rooms[0].Name = "mutated consumer copy";
            Check(workspace.LastSceneHandoff.Rooms[0].Name == "Hub",
                "Shared scene handoff snapshots are isolated from consumer mutation");

            SharedSceneHandoff sharpSnapshot = handoff.Clone();
            sharpSnapshot.Publisher = "SharpOcarina";
            sharpSnapshot.DocumentId = Guid.NewGuid();
            sharpSnapshot.Revision = 5;
            sharpSnapshot.Gameplay.Add(new SharedGameplayPackage
            {
                PlacementId = "shared-package",
                Actors = new List<SharedGameplayActor>
                {
                    new SharedGameplayActor
                    {
                        Id = "switch", RequiredItems = new List<string> { "bow" },
                        Flags = new List<SharedGameplayFlag> { new SharedGameplayFlag { Kind = "Switch", Mask = 0x00FF } }
                    }
                }
            });
            workspace.PublishSceneHandoff(sharpSnapshot);
            SharedSceneHandoff publisherCopy;
            Check(workspace.TryGetSceneHandoff("Archarina64", out publisherCopy) &&
                workspace.TryGetSceneHandoff("SharpOcarina", out publisherCopy),
                "Shared workspace retains handoffs independently by publisher");
            publisherCopy.Gameplay[0].Actors[0].RequiredItems[0] = "hookshot";
            Check(workspace.TryGetSceneHandoff("SharpOcarina", out publisherCopy) &&
                publisherCopy.Gameplay[0].Actors[0].RequiredItems[0] == "bow",
                "Shared gameplay handoff clones nested actor state");
            SharedSceneHandoff staleSnapshot = sharpSnapshot.Clone();
            staleSnapshot.Revision = 4;
            workspace.PublishSceneHandoff(staleSnapshot);
            Check(workspace.TryGetSceneHandoff("SharpOcarina", out publisherCopy) && publisherCopy.Revision == 5,
                "Shared workspace rejects stale publisher revisions");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && (args[0] == "--ui-smoke" || args[0] == "--sandbox-integration"))
            {
                typeof(ZScene).Assembly.GetType("SharpOcarina.ArcharinaWorkspace")
                    .GetMethod("Initialize", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null)
                    .Invoke(null, null);
                System.Windows.Forms.Application.EnableVisualStyles();
                using (MainForm form = new MainForm(new string[0]))
                {
                    typeof(ZScene).Assembly.GetType("SharpOcarina.Program").GetField("MF").SetValue(null, form);
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    Check(form.Text.StartsWith("Archarina64"), "Archarina64 window identity");
                    Check(form.Controls.ContainsKey("SandboxWorkspace") && form.Controls["SandboxWorkspace"].Visible,
                        "New sandbox is the visible workspace");
                    Check(!form.Controls["LegacyBackendControls"].Visible, "Legacy UI is hidden");
                    if (args[0] == "--sandbox-integration")
                    {
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                        var task = (System.Threading.Tasks.Task)typeof(MainForm).GetMethod("OpenSandboxRomAsync", flags).Invoke(form, new object[] { args[1] });
                        DateTime deadline = DateTime.UtcNow.AddMinutes(3);
                        while (!task.IsCompleted && DateTime.UtcNow < deadline)
                        { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                        Check(task.IsCompleted, "ROM import completes");
                        task.GetAwaiter().GetResult();
                        RomAssetCatalog imported = (RomAssetCatalog)typeof(MainForm).GetField("sandboxCatalog", flags).GetValue(form);
                        Check(imported != null && imported.Assets.Count > 386, "ROM assets extracted into categorized library");
                        var categories = (System.Windows.Forms.TreeView)typeof(MainForm).GetField("sandboxCategories", flags).GetValue(form);
                        var palette = (System.Windows.Forms.ListBox)typeof(MainForm).GetField("sandboxPalette", flags).GetValue(form);
                        categories.SelectedNode = categories.Nodes.Cast<System.Windows.Forms.TreeNode>().First(node => node.Text == "Rooms");
                        var sourceButton = (System.Windows.Forms.Button)form.Controls.Find("OpenSandboxSourceRoom", true).Single();
                        RomPaletteAsset alternateRoom = palette.Items.Cast<RomPaletteAsset>().First(a => a.Room.SceneId == 1 && a.Room.RoomId == 1);
                        palette.SelectedItem = alternateRoom;
                        Check(sourceButton.Enabled, "Source-room open command is enabled for a room preset");
                        MainForm.savechanges = false;
                        sourceButton.PerformClick();
                        var sceneField = typeof(MainForm).GetField("CurrentScene", flags | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        ZScene switched = (ZScene)sceneField.GetValue(form);
                        var roomSelector = (System.Windows.Forms.ComboBox)typeof(MainForm).GetField("sandboxRooms", flags).GetValue(form);
                        Check(switched.SceneNumber == 1 && roomSelector.SelectedIndex == 1, "Open source room switches to another scene and requested room");
                        roomSelector.SelectedIndex = 0;
                        var legacyRoomList = (System.Windows.Forms.ListBox)typeof(MainForm).GetField("RoomList", flags).GetValue(form);
                        Check(legacyRoomList.SelectedIndex == 0, "Sandbox room selector updates renderer room selection");
                        palette.SelectedItem = palette.Items.Cast<RomPaletteAsset>().First(a => a.Room.SceneId == 0 && a.Room.RoomId == 0);
                        MainForm.savechanges = false;
                        sourceButton.PerformClick();
                        Check(((ZScene)sceneField.GetValue(form)).SceneNumber == 0 && roomSelector.SelectedIndex == 0, "Can switch back to the original scene");
                        categories.SelectedNode = categories.Nodes.Cast<System.Windows.Forms.TreeNode>().First(node => node.Text == "Bosses");
                        System.Windows.Forms.Application.DoEvents();
                        Check(palette.Items.Count > 0 && palette.Items.Cast<RomPaletteAsset>().All(item => item.Category == "Bosses"), "Boss category selection populates the palette");
                        for (int boss = 0; boss < palette.Items.Count; boss++)
                        {
                            palette.SelectedIndex = boss;
                            System.Windows.Forms.Application.DoEvents();
                            Check(!form.IsDisposed, "Boss selection " + boss + " keeps the editor alive");
                        }
                        Console.WriteLine("Extracted: " + imported.Assets.Count + " entries; objects: " + imported.AvailableObjects.Count);
                        ZScene loaded = (ZScene)typeof(MainForm).GetField("CurrentScene", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(form);
                        Check(loaded != null && loaded.Rooms.Count > 0, "SharpOcarina backend opens source scene");
                        ObjFile model = loaded.Rooms[0].ObjModel;
                        ObjFile.Group previewGroup = loaded.Rooms[0].TrueGroups.First();
                        bool savedCulling = previewGroup.BackfaceCulling;
                        previewGroup.BackfaceCulling = true;
                        model.Prepare(false, loaded.Rooms[0].TrueGroups);
                        Check(model.PreviewTwoSided, "Sandbox applies preview-only two-sided mode");
                        model.Render(previewGroup);
                        Check(!OpenTK.Graphics.OpenGL.GL.IsEnabled(OpenTK.Graphics.OpenGL.EnableCap.CullFace) && previewGroup.BackfaceCulling,
                            "Compiled mesh disables preview culling without changing export culling");
                        var twoSided = (System.Windows.Forms.CheckBox)typeof(MainForm).GetField("sandboxTwoSided", flags).GetValue(form);
                        twoSided.Checked = false;
                        model.Render(previewGroup);
                        Check(OpenTK.Graphics.OpenGL.GL.IsEnabled(OpenTK.Graphics.OpenGL.EnableCap.CullFace), "Preview toggle restores material backface culling");
                        twoSided.Checked = true;
                        previewGroup.BackfaceCulling = savedCulling;
                        model.Prepare(false, loaded.Rooms[0].TrueGroups);
                        int before = loaded.Rooms[0].ZActors.Count;
                        RomPaletteAsset asset = imported.Assets.First(a => a.Category == "Enemies" && a.Objects.All(id => id <= 3 || imported.AvailableObjects.Contains(id)));
                        typeof(MainForm).GetMethod("PlaceSandboxAsset", flags).Invoke(form, new object[] { asset, new OpenTK.Vector3d(100, 0, 100) });
                        Check(loaded.Rooms[0].ZActors.Count == before + 1, "Palette actor placement mutates destination room");
                        Check(asset.Objects.Where(id => id > 3).All(id => loaded.Rooms[0].ZObjects.Any(o => o.Value == id)), "Placement adds object dependencies");
                        object undo = typeof(MainForm).GetField("sandboxUndo", flags).GetValue(form);
                        object redo = typeof(MainForm).GetField("sandboxRedo", flags).GetValue(form);
                        typeof(MainForm).GetMethod("SandboxHistory", flags).Invoke(form, new[] { undo, redo });
                        Check(loaded.Rooms[0].ZActors.Count == before, "Undo removes palette placement");
                        typeof(MainForm).GetMethod("SandboxHistory", flags).Invoke(form, new[] { redo, undo });
                        Check(loaded.Rooms[0].ZActors.Count == before + 1, "Redo restores palette placement");
                        RomPaletteAsset door = imported.Assets.First(a => a.Transition && a.Variable <= 0x3FF && a.Objects.All(id => id <= 3 || imported.AvailableObjects.Contains(id)));
                        int connectionsBefore = loaded.Transitions.Count;
                        using (var cancelDialog = new System.Windows.Forms.Timer { Interval = 150 })
                        {
                            cancelDialog.Tick += delegate
                            {
                                var dialog = System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().FirstOrDefault(f => f.Text == "Room connection");
                                if (dialog == null) return;
                                dialog.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                                cancelDialog.Stop();
                            };
                            cancelDialog.Start();
                            typeof(MainForm).GetMethod("ShowSandboxConnectionDialog", flags).Invoke(form, new object[] { door, new OpenTK.Vector3d(100, 0, 100) });
                        }
                        Check(loaded.Transitions.Count == connectionsBefore, "Cancelling connection dialog leaves scene unchanged");
                        ushort[][] banksBefore = loaded.Rooms.Take(2).Select(r => r.ZObjects.Select(o => o.Value).ToArray()).ToArray();
                        typeof(MainForm).GetMethod("PlaceSandboxConnection", flags).Invoke(form, new object[] { door, new OpenTK.Vector3d(100, 0, 100), 0, 1, 90.0 });
                        Check(loaded.Transitions.Count == connectionsBefore + 1 && loaded.Transitions.Last().IsTransition, "Connection placement adds a transition, not a room actor");
                        Check(loaded.Rooms.Take(2).All(r => door.Objects.Where(id => id > 3).All(id => r.ZObjects.Any(o => o.Value == id))), "Both connected rooms receive required banks");
                        typeof(MainForm).GetMethod("SandboxHistory", flags).Invoke(form, new[] { undo, redo });
                        Check(loaded.Transitions.Count == connectionsBefore && Enumerable.Range(0, 2).All(i => loaded.Rooms[i].ZObjects.Select(o => o.Value).SequenceEqual(banksBefore[i])), "Undo restores connection and both object lists");
                        typeof(MainForm).GetMethod("SandboxHistory", flags).Invoke(form, new[] { redo, undo });
                        Check(loaded.Transitions.Count == connectionsBefore + 1 && loaded.Transitions.Last().BackSwitchTo == 1, "Redo restores connection mapping");
                        var visualSelection = (HashSet<int>)typeof(MainForm).GetField("sandboxVisualFaces", flags).GetValue(form);
                        var collisionSelection = (HashSet<int>)typeof(MainForm).GetField("sandboxCollisionFaces", flags).GetValue(form);
                        int visualIndex = 0;
                        foreach (ObjFile.Group g in loaded.Rooms[0].TrueGroups)
                        {
                            if (!g.Animated && !g.Custom && g.MultiTexMaterial < 0 && !g.Billboard && !g.TwoAxisBillboard && !g.LOD && !g.TexPointerPlus1 && g.Triangles.Count > 0)
                            { visualSelection.Add(visualIndex); break; }
                            visualIndex += g.Triangles.Count;
                        }
                        int collisionIndex = 0;
                        foreach (ObjFile.Group g in loaded.ColModel.Groups)
                        {
                            ZColPolyType p = loaded.PolyTypes[g.PolyType];
                            if ((p.Raw & 0x07FC0000UL) == 0 && g.Triangles.Count > 0)
                            { collisionSelection.Add(collisionIndex); break; }
                            collisionIndex += g.Triangles.Count;
                        }
                        string tilePath = (string)typeof(MainForm).GetMethod("SaveSandboxTile", flags).Invoke(form, new object[] { "Integration section", OpenTK.Vector3d.Zero, 100.0 });
                        Check(tilePath.StartsWith(TileStorage.Root) && File.Exists(tilePath), "Section tile saves in private LocalAppData library");
                        int visualBefore = loaded.Rooms[0].TrueGroups.Sum(g => g.Triangles.Count), collisionBefore = loaded.ColModel.Groups.Sum(g => g.Triangles.Count);
                        var placeTile = typeof(MainForm).GetMethod("PlaceSandboxTile", flags);
                        placeTile.Invoke(form, new object[] { tilePath, new OpenTK.Vector3d(500, 0, 0), 0 });
                        ((System.Windows.Forms.ComboBox)typeof(MainForm).GetField("sandboxTileSurface", flags).GetValue(form)).SelectedIndex = 2;
                        placeTile.Invoke(form, new object[] { tilePath, new OpenTK.Vector3d(-500, 0, 0), 1 });
                        Check(loaded.Rooms[0].TrueGroups.Sum(g => g.Triangles.Count) == visualBefore + 2 && loaded.ColModel.Groups.Sum(g => g.Triangles.Count) == collisionBefore + 2,
                            "Two real-ROM tile placements add both selected meshes");
                        typeof(MainForm).GetMethod("SandboxHistory", flags).Invoke(form, new[] { undo, redo });
                        Check(loaded.Rooms[0].TrueGroups.Sum(g => g.Triangles.Count) == visualBefore + 1 && loaded.ColModel.Groups.Sum(g => g.Triangles.Count) == collisionBefore + 1, "Tile undo removes matching visual and collision geometry");
                        typeof(MainForm).GetMethod("SandboxHistory", flags).Invoke(form, new[] { redo, undo });
                        Check(loaded.Rooms[0].TrueGroups.Sum(g => g.Triangles.Count) == visualBefore + 2, "Tile redo restores independent copy");
                        typeof(MainForm).GetMethod("PersistSandboxGeometry", flags).Invoke(form, null);
                        string project = Path.Combine(TileStorage.Root, "integration-scene.xml");
                        Type sceneIO = typeof(ZScene).Assembly.GetType("SharpOcarina.IO");
                        sceneIO.GetMethod("Export").MakeGenericMethod(typeof(ZScene)).Invoke(null, new object[] { loaded, project });
                        ZScene savedScene = (ZScene)sceneIO.GetMethod("Import").MakeGenericMethod(typeof(ZScene)).Invoke(null, new object[] { project });
                        Check(savedScene.SandboxGeometryFile == loaded.SandboxGeometryFile, "Scene XML retains private geometry snapshot reference");
                        TileSceneGeometry savedGeometry = TileStorage.Load<TileSceneGeometry>(savedScene.SandboxGeometryFile);
                        Check(savedGeometry.Rooms[0].Groups.Sum(g => g.Triangles.Count) == visualBefore + 2 && savedGeometry.Collision.Groups.Sum(g => g.Triangles.Count) == collisionBefore + 2,
                            "Saved scene geometry retains both tile copies and collision");
                        MainForm.savechanges = false;
                        typeof(MainForm).GetMethod("OpenSandboxSavedScene", flags).Invoke(form, new object[] { project });
                        loaded = (ZScene)sceneField.GetValue(form);
                        Check(loaded.Rooms[0].TrueGroups.Sum(g => g.Triangles.Count) == visualBefore + 2 && loaded.ColModel.Groups.Sum(g => g.Triangles.Count) == collisionBefore + 2,
                            "Editor reopens the saved scene with both tile copies and collision");
                        string nativeDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Archarina64", "native-test-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(nativeDirectory);
                        loaded.ConvertSave(nativeDirectory + Path.DirectorySeparatorChar, true, false, 0);
                        Check(Directory.GetFiles(nativeDirectory, "*.zscene").Length == 1 && Directory.GetFiles(nativeDirectory, "*.zmap").Length == loaded.Rooms.Count,
                            "Native scene and room package generated outside the repository");
                        byte[] nativeScene = File.ReadAllBytes(Directory.GetFiles(nativeDirectory, "*.zscene")[0]);
                        int collisionHeader = -1;
                        for (int at = 0; at + 8 <= nativeScene.Length && nativeScene[at] != 0x14; at += 8)
                            if (nativeScene[at] == 3) { collisionHeader = nativeScene[at + 5] << 16 | nativeScene[at + 6] << 8 | nativeScene[at + 7]; break; }
                        Check(collisionHeader >= 0, "Native scene contains collision header");
                        int polygonCount = nativeScene[collisionHeader + 0x14] << 8 | nativeScene[collisionHeader + 0x15];
                        Check(polygonCount == collisionBefore + 2, "Native collision includes both placed tile faces");
                        int polygonList = nativeScene[collisionHeader + 0x19] << 16 | nativeScene[collisionHeader + 0x1A] << 8 | nativeScene[collisionHeader + 0x1B];
                        int finalPolygon = polygonList + (polygonCount - 1) * 16;
                        Check((nativeScene[finalPolygon] << 8 | nativeScene[finalPolygon + 1]) == 1, "Native tile collision honors destination surface override");
                        int transitionHeader = -1;
                        for (int at = 0; at + 8 <= nativeScene.Length && nativeScene[at] != 0x14; at += 8)
                            if (nativeScene[at] == 0x0E) { transitionHeader = at; break; }
                        Check(transitionHeader >= 0 && nativeScene[transitionHeader + 1] == connectionsBefore + 1, "Native transition command contains the added connection");
                        int lastDoor = (nativeScene[transitionHeader + 5] << 16 | nativeScene[transitionHeader + 6] << 8 | nativeScene[transitionHeader + 7]) + connectionsBefore * 16;
                        Check(nativeScene[lastDoor] == 0 && nativeScene[lastDoor + 1] == 255 && nativeScene[lastDoor + 2] == 1 && nativeScene[lastDoor + 3] == 255
                            && (nativeScene[lastDoor + 4] << 8 | nativeScene[lastDoor + 5]) == door.Id
                            && (nativeScene[lastDoor + 12] << 8 | nativeScene[lastDoor + 13]) == 16384
                            && (nativeScene[lastDoor + 14] << 8 | nativeScene[lastDoor + 15]) == door.Variable,
                            "Native connection record preserves room sides, cameras, actor, facing and parameters");
                        using (var sha = System.Security.Cryptography.SHA256.Create())
                            Check(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[1]))).Replace("-", "").ToLowerInvariant() == imported.Rooms.RomFingerprint,
                                "Source ROM unchanged by import, placement and native export");
                        string preview = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Archarina64", "sandbox-preview.png");
                        typeof(MainForm).GetMethod("CaptureSandboxPreview", flags).Invoke(form, new object[] { preview });
                        File.Delete(tilePath);
                        MainForm.savechanges = false;
                    }
                    form.Close();
                }
                return 0;
            }
        TestSharedDocumentLifecycle();
        TestSceneCloneOwnsDungeonState();
        TestSharedEditorServices();
        TestSharedAssemblyBoundary();
            TestSharedSceneHandoff();
        TestTiles();
            TestTileAssembly();
        TestProceduralTileKit();
        TestRomMechanicsScanner();
        TestCollisionVolumeExtraction();
        TestWaterMechanics();
        TestGeneratedDungeonPlayability();
        TestPushBlockSolver();
        TestConnections();
        TestMeshEditorMaterialStateSnapshot();
        TestCapturedRoomVertexColors();
        TestProceduralDungeon();
        TestProceduralSceneAggregation();
            string objectDir = Path.Combine(Path.GetTempPath(), "archarina64-objects-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(objectDir);
            try
            {
                File.WriteAllBytes(Path.Combine(objectDir, "object_link_child.zobj"), new byte[] { 1 });
                Check(RomObjectResolver.Resolve(objectDir, "OBJECT-LINK_CHILD.zobj") != null,
                    "ROM object resolver matches case and separator differences");
                Check(RomObjectResolver.SafeAliases("DON'T USE THIS!\r\ngameplay_keep").Contains("gameplay_keep"),
                    "ROM object import extracts safe aliases from multiline definitions");
            }
            finally { if (Directory.Exists(objectDir)) Directory.Delete(objectDir, true); }
            byte[] candidate = new byte[40];
            candidate[0] = 0x01; candidate[8] = 0x05; candidate[16] = 0xDF;
            Check(ActorPreviewInference.FindCandidates(candidate, 1).Count == 1,
                "Actor preview inference recognizes a conservative F3DEX2 candidate");
            ActorPreviewOverride previewOverride = new ActorPreviewOverride { Actor = "0123", Object = "object_custom", Offset = "00000100", Bank = "06", Scale = "0.01", DListCount = "1" };
            ushort overrideActor; uint overrideOffset, overrideBank; float overrideScale; int overrideLists;
            Check(previewOverride.TryRead(out overrideActor, out overrideOffset, out overrideBank, out overrideScale, out overrideLists) && overrideActor == 0x123,
                "Actor preview override parses private descriptor values");
            byte[] rom = new byte[1024];
            Put(rom, 0, 0x80371240); Put(rom, 32, 128); Put(rom, 36, 256);
            rom[128] = 4; rom[129] = 1; Put(rom, 132, 0x02000020);
            Put(rom, 160, 256); Put(rom, 164, 512);
            SceneTileLibrary catalog = SceneTileLibrary.Read(rom, 32, 52);
            Check(catalog.Rooms.Count == 1 && catalog.Rooms[0].Start == 256, "Main-header room catalog");
            Check(catalog.RomFingerprint.Length == 64, "SHA256 source identity");
            using (MemoryStream stream = new MemoryStream())
            {
                XmlSerializer serializer = new XmlSerializer(typeof(SceneTileLibrary));
                serializer.Serialize(stream, catalog); stream.Position = 0;
                Check(((SceneTileLibrary)serializer.Deserialize(stream)).Rooms[0].End == 512, "Catalog serialization round trip");
            }
            Put(rom, 132, 0x03000020);
            Check(SceneTileLibrary.Read(rom, 32, 52).Rooms.Count == 0, "Reject wrong segment room pointer");
            Put(rom, 132, 0x02000020); Put(rom, 164, 2000);
            Check(SceneTileLibrary.Read(rom, 32, 52).Rooms.Count == 0, "Reject out-of-range room");
            bool rejected = false;
            try { SceneTileLibrary.Read(rom, 32, 53); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Reject malformed table size");
            ZScene scene = new ZScene();
            Check(SceneBuilderValidation.Check(scene).Any(x => x.Contains("No player spawn")), "Missing spawn diagnostic");
            scene.Transitions.Add(new ZActor { FrontSwitchTo = 3, BackSwitchTo = 255 });
            Check(SceneBuilderValidation.Check(scene).Any(x => x.Contains("Door front")), "Invalid transition room diagnostic");
            string scenePath = Path.Combine(Path.GetTempPath(), "archarina64-test-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Type io = typeof(ZScene).Assembly.GetType("SharpOcarina.IO");
                io.GetMethod("Export").MakeGenericMethod(typeof(ZScene)).Invoke(null, new object[] { scene, scenePath });
                Check(File.ReadAllText(scenePath).Contains("assembly=SharpOcarina"), "Export preserves modeling-fork XML namespace");
                ZScene restored = (ZScene)io.GetMethod("Import").MakeGenericMethod(typeof(ZScene)).Invoke(null, new object[] { scenePath });
                Check(restored.Transitions.Count == 1 && restored.Transitions[0].FrontSwitchTo == 3, "Cross-namespace scene round trip");
            }
            finally { if (File.Exists(scenePath)) File.Delete(scenePath); }
            if (args.Length > 0)
            {
                byte[] real = File.ReadAllBytes(args[0]);
                ROM version = MainForm.CheckVersion(new List<byte>(real));
                SceneTileLibrary realCatalog = SceneTileLibrary.Read(real, version.SceneTable, version.SceneTableEnd);
                Check(realCatalog.Rooms.Count > 0, "Local ROM room indexing");
                Console.WriteLine("ROM rooms: " + realCatalog.Rooms.Count + "; diagnostics: " + realCatalog.Diagnostics.Count);
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
