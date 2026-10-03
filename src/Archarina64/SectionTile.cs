using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;
using OpenTK;

namespace SharpOcarina
{
    public sealed class TileMesh
    {
        public List<ObjFile.Vertex> Vertices = new List<ObjFile.Vertex>();
        public List<ObjFile.TextureCoord> UVs = new List<ObjFile.TextureCoord>();
        public List<ObjFile.Normal> Normals = new List<ObjFile.Normal>();
        public List<ObjFile.VertexColor> Colors = new List<ObjFile.VertexColor>();
        public List<ObjFile.Material> Materials = new List<ObjFile.Material>();
        public List<ObjFile.Group> Groups = new List<ObjFile.Group>();

        public static ObjFile.Triangle CopyTriangle(ObjFile.Triangle t)
        {
            return new ObjFile.Triangle { MaterialName = t.MaterialName, VertIndex = (int[])t.VertIndex.Clone(),
                TexCoordIndex = t.TexCoordIndex == null ? null : (int[])t.TexCoordIndex.Clone(),
                NormalIndex = t.NormalIndex == null ? null : (int[])t.NormalIndex.Clone(),
                VertColor = t.VertColor == null ? null : (int[])t.VertColor.Clone() };
        }

        public static TileMesh Copy(ObjFile model, IList<ObjFile.Group> groups)
        {
            if (model == null) return new TileMesh();
            TileMesh mesh = new TileMesh();
            mesh.Vertices.AddRange(model.Vertices.Select(v => v.Clone()));
            mesh.UVs.AddRange(model.TextureCoordinates.Select(v => v.Clone()));
            mesh.Normals.AddRange(model.Normals.Select(v => v.Clone()));
            mesh.Colors.AddRange(model.VertexColors.Select(v => v.Clone()));
            foreach (ObjFile.Material original in model.Materials)
            {
                ObjFile.Material mat = original.Clone();
                mat.Ka = (float[])original.Ka.Clone(); mat.Kd = (float[])original.Kd.Clone(); mat.Ks = (float[])original.Ks.Clone();
                mat.GLID = 0; mat.TexImage = null;
                foreach (string field in TextureFields)
                {
                    FieldInfo info = typeof(ObjFile.Material).GetField(field);
                    string path = (string)info.GetValue(mat);
                    if (!string.IsNullOrEmpty(path)) info.SetValue(mat, Path.GetFullPath(Path.Combine(model.BasePath ?? "", path)));
                }
                mesh.Materials.Add(mat);
            }
            foreach (ObjFile.Group original in groups)
            {
                ObjFile.Group group = original.Clone(); group.GLID = 0;
                group.CustomDL = (ulong[])original.CustomDL.Clone();
                group.Triangles = original.Triangles.Select(CopyTriangle).ToList();
                mesh.Groups.Add(group);
            }
            return mesh;
        }

        internal static readonly string[] TextureFields = { "map_Ka", "map_Kd", "map_Ks", "map_d", "map_bump" };

        public static TileMesh Select(ObjFile source, IList<ObjFile.Group> groups, ISet<int> faces, Vector3d pivot)
        {
            TileMesh mesh = Copy(source, groups);
            int index = 0;
            foreach (ObjFile.Group group in mesh.Groups)
                group.Triangles = group.Triangles.Where(t => faces.Contains(index++)).ToList();
            mesh.Groups.RemoveAll(g => g.Triangles.Count == 0);
            if (mesh.Groups.Count == 0) throw new InvalidOperationException("Select at least one face.");
            var vertices = mesh.Vertices;
            var used = mesh.Groups.SelectMany(g => g.Triangles).SelectMany(t => t.VertIndex).Distinct().ToList();
            var map = used.Select((old, next) => new { old, next }).ToDictionary(x => x.old, x => x.next);
            mesh.Vertices = used.Select(i => vertices[i]).ToList();
            foreach (ObjFile.Vertex v in mesh.Vertices) { v.X -= pivot.X; v.Y -= pivot.Y; v.Z -= pivot.Z; }
            foreach (ObjFile.Triangle triangle in mesh.Groups.SelectMany(g => g.Triangles))
                triangle.VertIndex = triangle.VertIndex.Select(i => map[i]).ToArray();
            var materials = new HashSet<string>(mesh.Groups.SelectMany(g => g.Triangles).Select(t => t.MaterialName));
            mesh.Materials.RemoveAll(m => !materials.Contains(m.Name));
            return mesh;
        }

        public ObjFile ToModel()
        {
            ObjFile model = new ObjFile();
            model.Vertices.AddRange(Vertices); model.TextureCoordinates.AddRange(UVs);
            model.Normals.AddRange(Normals); model.VertexColors.AddRange(Colors);
            model.Materials.AddRange(Materials); model.Groups = Groups;
            return model;
        }

        public void Transform(Vector3d position, int turns, double scale = 1.0)
        {
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) throw new ArgumentOutOfRangeException("scale");
            foreach (ObjFile.Vertex vertex in Vertices)
            {
                Vector3d p = Rotate(vertex.ToVector3d() * scale, turns) + position;
                if (new[] { p.X, p.Y, p.Z }.Any(x => double.IsNaN(x) || double.IsInfinity(x) || x < short.MinValue || x > short.MaxValue))
                    throw new InvalidOperationException("Tile is outside the N64 coordinate range.");
                vertex.X = p.X; vertex.Y = p.Y; vertex.Z = p.Z; vertex.VN = Rotate(vertex.VN, turns);
            }
            foreach (ObjFile.Normal normal in Normals)
            {
                Vector3d n = Rotate(new Vector3d(normal.X, normal.Y, normal.Z), turns);
                normal.X = n.X; normal.Y = n.Y; normal.Z = n.Z;
            }
        }

        private static Vector3d Rotate(Vector3d p, int turns)
        {
            for (int i = 0; i < ((turns % 4) + 4) % 4; i++) p = new Vector3d(p.Z, p.Y, -p.X);
            return p;
        }

        public void Append(TileMesh other, string prefix)
        {
            int v = Vertices.Count, uv = UVs.Count, n = Normals.Count, c = Colors.Count;
            // SharpOcarina uses one-based vertex-color indices, unlike its vertex/UV indices.
            int fallbackColor = other.Colors.Count + 1;
            other.Colors.Add(new ObjFile.VertexColor(1, 1, 1, 1));
            foreach (ObjFile.Material mat in other.Materials) { mat.Name = prefix + mat.Name; Materials.Add(mat); }
            foreach (ObjFile.Group group in other.Groups)
            {
                group.Name = prefix + group.Name;
                foreach (ObjFile.Triangle t in group.Triangles)
                {
                    t.VertIndex = t.VertIndex.Select(i => i + v).ToArray();
                    if (t.TexCoordIndex != null) t.TexCoordIndex = t.TexCoordIndex.Select(i => i >= 0 && i < other.UVs.Count ? i + uv : -1).ToArray();
                    if (t.NormalIndex != null) t.NormalIndex = t.NormalIndex.Select(i => i >= 0 && i < other.Normals.Count ? i + n : -1).ToArray();
                    t.VertColor = (t.VertColor ?? new int[3]).Select(i => i > 0 && i < fallbackColor ? i + c : fallbackColor + c).ToArray();
                    if (!string.IsNullOrEmpty(t.MaterialName)) t.MaterialName = prefix + t.MaterialName;
                }
                Groups.Add(group);
            }
            Vertices.AddRange(other.Vertices); UVs.AddRange(other.UVs); Normals.AddRange(other.Normals); Colors.AddRange(other.Colors);
        }
    }

    public sealed class SectionTile
    {
        public int Version = 1;
        public string Name, RomFingerprint;
        public int SourceScene;
        public string LibraryKind;
        public List<TileSocket> Sockets = new List<TileSocket>();
        public List<TileEntryFrame> EntryFrames = new List<TileEntryFrame>();
        public double Grid = 100;
        public TileMesh Visual, Collision;
        public List<ZColPolyType> Surfaces = new List<ZColPolyType>();
        public RoomPackage Gameplay;
        public DungeonTraversalCapabilities TraversalCapabilities;
        public List<TileTraversalContract> Traversal = new List<TileTraversalContract>();

        public static SectionTile Capture(ZScene scene, int room, ISet<int> visual, ISet<int> collision, Vector3d pivot, string name, string fingerprint, double grid)
        {
            if (scene.Scale != 1 || scene.ColModel == null || scene.Rooms[room].Type2)
                throw new InvalidOperationException("Capture requires a scale-1 room with ordinary geometry and explicit collision.");
            if (string.IsNullOrWhiteSpace(name) || grid < 1 || grid > 4096 || double.IsNaN(grid)) throw new InvalidOperationException("Set a tile name and grid between 1 and 4096.");
            SectionTile tile = new SectionTile { Name = name.Trim(), RomFingerprint = fingerprint, SourceScene = scene.SceneNumber, Grid = grid,
                Visual = TileMesh.Select(scene.Rooms[room].ObjModel, scene.Rooms[room].TrueGroups, visual, pivot),
                Collision = TileMesh.Select(scene.ColModel, scene.ColModel.Groups, collision, pivot) };
            foreach (ObjFile.Group group in tile.Visual.Groups)
                if (group.Animated || group.Custom || group.MultiTexMaterial >= 0 || group.Billboard || group.TwoAxisBillboard || group.LOD || group.TexPointerPlus1)
                    throw new InvalidOperationException("Animated, billboard, custom or multi-texture groups need a separate tile mapping and cannot be captured yet.");
            foreach (ObjFile.Group group in tile.Collision.Groups)
            {
                if (group.PolyType < 0 || group.PolyType >= scene.PolyTypes.Count) throw new InvalidOperationException("Collision surface type is missing.");
                ZColPolyType type = scene.PolyTypes[group.PolyType];
                // The legacy exporter interprets #Polytype/#Raw name tags ahead of PolyType.
                group.Name = "Collision" + tile.Surfaces.Count;
                group.PolyType = tile.Surfaces.Count;
                tile.Surfaces.Add(new ZColPolyType(type.Raw, type.PolyFlagA, type.PolyFlagB));
            }
            tile.Visual.Transform(Vector3d.Zero, 0); tile.Collision.Transform(Vector3d.Zero, 0);
            return tile;
        }

        public static SectionTile Capture(ZScene scene, int room, ISet<int> visual, ISet<int> collision, Vector3d pivot, string name, string fingerprint, double grid, RomAssetCatalog catalog, Vector3d? gameplayMin, Vector3d? gameplayMax, bool includeWholeRoom)
        {
            SectionTile tile = Capture(scene, room, visual, collision, pivot, name, fingerprint, grid);
            tile.Gameplay = RoomPackageBuilder.Capture(scene, room, catalog, gameplayMin, gameplayMax, includeWholeRoom);
            foreach (RoomPackageActor actor in tile.Gameplay.Actors.Concat(tile.Gameplay.Spawns)) actor.Position -= pivot;
            return tile;
        }
    }

    [Serializable]
    public sealed class TileEntryFrame
    {
        public string SocketId;
        public Vector3d SpawnPosition;
        public Vector3 SpawnRotation;
    }

    [Serializable]
    public sealed class TileTraversalContract
    {
        public string Id, Kind, Evidence, RequiredItem;
        public Vector3d Start, End;
        public double MaxDistance;
        public bool Optional;
        public DungeonPushBlockPuzzle PushBlockPuzzle;
    }

    public sealed class PlacedTile
    {
        public string Id, Name, TilePath;
        public int Room, Turns, SurfaceOverride = -1;
        public double Scale = 1.0;
        public double X, Y, Z;
        public int SourceSocket = -1;
        public string TargetSocket;
        [XmlIgnore] public Vector3d Position { get { return new Vector3d(X, Y, Z); } set { X = value.X; Y = value.Y; Z = value.Z; } }
    }

    public sealed class TileSceneGeometry
    {
        public int SceneId;
        public List<TileSocket> Sockets = new List<TileSocket>();
        public List<TileMesh> Rooms = new List<TileMesh>();
        public TileMesh Collision;
        public List<ZColPolyType> Surfaces = new List<ZColPolyType>();
        public List<RoomPackage> GameplayPackages = new List<RoomPackage>();
        public List<PlacedTile> PlacedTiles = new List<PlacedTile>();
        public List<TileMesh> BaseRooms = new List<TileMesh>();
        public TileMesh BaseCollision;
        public List<TileSocket> BaseSockets = new List<TileSocket>();
        public List<SandboxAuthoringMarker> AuthoringMarkers = new List<SandboxAuthoringMarker>();
        public List<SandboxAuthoringMarker> BaseAuthoringMarkers = new List<SandboxAuthoringMarker>();
        public List<ZColPolyType> BaseSurfaces = new List<ZColPolyType>();
        public List<RoomPackage> BaseGameplayPackages = new List<RoomPackage>();
        public string TextureThemeTilePath;
        [XmlIgnore] public SectionTile RuntimeTextureTheme;
        [XmlIgnore] public List<string> GameplayWarnings = new List<string>();

        public static TileSceneGeometry Capture(ZScene scene)
        {
            List<TileMesh> rooms = scene.Rooms.Select(r => TileMesh.Copy(r.ObjModel, r.TrueGroups)).ToList();
            TileMesh collision = TileMesh.Copy(scene.ColModel, scene.ColModel.Groups);
            List<ZColPolyType> surfaces = scene.PolyTypes.Select(p => new ZColPolyType(p.Raw, p.PolyFlagA, p.PolyFlagB)).ToList();
            List<RoomPackage> gameplay = scene.SandboxGameplayPackages == null ? new List<RoomPackage>() : scene.SandboxGameplayPackages.Select(TileStorage.Clone).ToList();
            return new TileSceneGeometry { SceneId = scene.SceneNumber, Sockets = scene.TileSockets.Select(s => s.Clone()).ToList(), Rooms = rooms,
                Collision = collision, Surfaces = surfaces, GameplayPackages = gameplay,
                BaseRooms = rooms.Select(TileStorage.Clone).ToList(), BaseCollision = TileStorage.Clone(collision),
                BaseSockets = scene.TileSockets.Select(s => s.Clone()).ToList(), BaseSurfaces = surfaces.Select(p => new ZColPolyType(p.Raw, p.PolyFlagA, p.PolyFlagB)).ToList(),
                BaseGameplayPackages = gameplay.Select(TileStorage.Clone).ToList(), AuthoringMarkers = new List<SandboxAuthoringMarker>(),
                BaseAuthoringMarkers = new List<SandboxAuthoringMarker>() };
        }

        public void Place(SectionTile tile, int room, Vector3d position, int turns, int surfaceOverride = -1, TileSnap snap = null)
        {
            Place(tile, room, position, turns, surfaceOverride, snap, null);
        }

        public void Place(SectionTile tile, int room, Vector3d position, int turns, int surfaceOverride, TileSnap snap, string tilePath)
        {
            Place(tile, room, position, turns, surfaceOverride, snap, tilePath, 1.0);
        }

        public void Place(SectionTile tile, int room, Vector3d position, int turns, int surfaceOverride, TileSnap snap, string tilePath, double scale)
        {
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale < 0.1 || scale > 4.0)
                throw new InvalidOperationException("Tile scale must be between 0.1 and 4.0.");
            if (surfaceOverride < -1 || surfaceOverride >= Surfaces.Count) throw new InvalidOperationException("Choose an existing destination collision surface.");
            if (surfaceOverride < 0 && tile.SourceScene != SceneId)
                throw new InvalidOperationException("This tile comes from another scene. Choose a destination collision surface instead of Source surfaces.");
            if (surfaceOverride < 0 && turns % 4 != 0 && tile.Surfaces.Any(s => (s.Raw & 0x07FC0000UL) != 0))
                throw new InvalidOperationException("Rotating conveyor surfaces needs an explicit destination surface mapping.");
            if (!string.IsNullOrEmpty(tilePath))
            {
                if (BaseRooms == null || BaseRooms.Count == 0) throw new InvalidOperationException("This tile workspace has no editable base geometry.");
                PlacedTile placement = new PlacedTile { Id = "placed_" + Guid.NewGuid().ToString("N"), Name = tile.Name, TilePath = tilePath, Room = room,
                    Position = position, Turns = turns, Scale = scale, SurfaceOverride = surfaceOverride,
                    SourceSocket = snap == null ? -1 : snap.SourceSocket, TargetSocket = snap == null ? null : snap.TargetId };
                PlacedTiles.Add(placement);
                RebuildPlacements();
                MarkAuthoringMarkerConnection(snap, placement.Id);
                return;
            }
            tile = TileStorage.Clone(tile);
            ApplyTextureTheme(tile, RuntimeTextureTheme);
            string prefix = "tile_" + Guid.NewGuid().ToString("N") + "_";
            var sockets = tile.Sockets.Select((s, i) => s.Placed(position, turns, room, prefix + i, scale)).ToList();
            if (snap != null && (snap.SourceSocket < 0 || snap.SourceSocket >= sockets.Count || Sockets.Any(s => s.Id == snap.TargetId && !string.IsNullOrEmpty(s.ConnectedTo)) || Sockets.Any(s => s.ConnectedTo == snap.TargetId)))
                throw new InvalidOperationException("That doorway socket is already connected or unavailable.");
            tile.Visual.Transform(position, turns, scale); tile.Collision.Transform(position, turns, scale);
            if (Collision.Vertices.Count + tile.Collision.Vertices.Count > 8192) throw new InvalidOperationException("Tile would exceed the 13-bit collision vertex index limit.");
            foreach (ObjFile.Group group in tile.Collision.Groups)
            {
                if (surfaceOverride >= 0) { group.PolyType = surfaceOverride; continue; }
                ZColPolyType type = tile.Surfaces[group.PolyType];
                int index = Surfaces.FindIndex(p => p.Raw == type.Raw && p.PolyFlagA == type.PolyFlagA && p.PolyFlagB == type.PolyFlagB);
                if (index < 0) { index = Surfaces.Count; Surfaces.Add(type); }
                group.PolyType = index;
            }
            Rooms[room].Append(tile.Visual, prefix); Collision.Append(tile.Collision, prefix);
            if (tile.Gameplay != null)
            {
                RoomPackage package = TileStorage.Clone(tile.Gameplay);
                package.DestinationRoom = room;
                foreach (RoomPackageActor actor in package.Actors.Concat(package.Spawns))
                {
                    actor.Position = Transform(actor.Position, position, turns, scale);
                    actor.Rotation = TransformRotation(actor.Rotation, turns);
                }
                string actorPrefix = prefix.TrimEnd('_');
                foreach (RoomPackageActor actor in package.Actors.Concat(package.Spawns)) actor.Id = actorPrefix + "_" + actor.Id;
                foreach (LogicLink link in package.Links) { link.SourceId = actorPrefix + "_" + link.SourceId; link.TargetId = actorPrefix + "_" + link.TargetId; }
                GameplayPackages.Add(package);
                GameplayWarnings.AddRange(package.Review().Select(w => tile.Name + ": " + w));
            }
            if (snap != null)
            {
                sockets[snap.SourceSocket].ConnectedTo = snap.TargetId;
                TileSocket target = Sockets.FirstOrDefault(s => s.Id == snap.TargetId);
                if (target != null) target.ConnectedTo = sockets[snap.SourceSocket].Id;
                MarkAuthoringMarkerConnection(snap, sockets[snap.SourceSocket].Id);
            }
            Sockets.AddRange(sockets);
        }

        private void MarkAuthoringMarkerConnection(TileSnap snap, string connectionId)
        {
            if (snap == null || string.IsNullOrEmpty(connectionId)) return;
            string markerId = snap.TargetId != null && snap.TargetId.StartsWith("marker:", StringComparison.Ordinal) ? snap.TargetId.Substring(7) : null;
            if (string.IsNullOrEmpty(markerId)) return;
            SandboxAuthoringMarker marker = (AuthoringMarkers ?? new List<SandboxAuthoringMarker>()).FirstOrDefault(m => m.Id == markerId);
            if (marker != null) marker.ConnectedTo = connectionId;
            SandboxAuthoringMarker baseMarker = (BaseAuthoringMarkers ?? new List<SandboxAuthoringMarker>()).FirstOrDefault(m => m.Id == markerId);
            if (baseMarker != null) baseMarker.ConnectedTo = connectionId;
        }

        public void RebuildPlacements()
        {
            if (BaseRooms == null || BaseRooms.Count == 0) throw new InvalidOperationException("This tile workspace has no editable base geometry.");
            Rooms = BaseRooms.Select(TileStorage.Clone).ToList();
            Collision = TileStorage.Clone(BaseCollision);
            Sockets = BaseSockets == null ? new List<TileSocket>() : BaseSockets.Select(s => s.Clone()).ToList();
            AuthoringMarkers = BaseAuthoringMarkers == null ? new List<SandboxAuthoringMarker>() : BaseAuthoringMarkers.Select(s => s.Clone()).ToList();
            Surfaces = BaseSurfaces == null ? new List<ZColPolyType>() : BaseSurfaces.Select(p => new ZColPolyType(p.Raw, p.PolyFlagA, p.PolyFlagB)).ToList();
            GameplayPackages = BaseGameplayPackages == null ? new List<RoomPackage>() : BaseGameplayPackages.Select(TileStorage.Clone).ToList();
            GameplayWarnings.Clear();
            if (RuntimeTextureTheme == null && !string.IsNullOrEmpty(TextureThemeTilePath) && File.Exists(TextureThemeTilePath))
                RuntimeTextureTheme = TileStorage.Load<SectionTile>(TextureThemeTilePath);
            if (RuntimeTextureTheme != null)
                foreach (TileMesh room in Rooms) ApplyTextureTheme(room, RuntimeTextureTheme);
            foreach (PlacedTile placement in PlacedTiles.ToList())
            {
                if (string.IsNullOrEmpty(placement.TilePath) || !File.Exists(placement.TilePath)) throw new FileNotFoundException("Placed tile source is missing.", placement.TilePath);
                SectionTile tile = TileStorage.Load<SectionTile>(placement.TilePath);
                TileSnap snap = placement.SourceSocket < 0 ? null : new TileSnap { SourceSocket = placement.SourceSocket, TargetId = placement.TargetSocket };
                int packageCount = GameplayPackages.Count;
                Place(tile, placement.Room, placement.Position, placement.Turns, placement.SurfaceOverride, snap, null, placement.Scale <= 0 ? 1.0 : placement.Scale);
                for (int i = packageCount; i < GameplayPackages.Count; i++) GameplayPackages[i].PlacementId = placement.Id;
            }
        }

        private static Vector3d Transform(Vector3d value, Vector3d position, int turns, double scale = 1.0)
        {
            int quarterTurns = ((turns % 4) + 4) % 4;
            Vector3d rotated = value * scale;
            for (int i = 0; i < quarterTurns; i++) rotated = new Vector3d(rotated.Z, rotated.Y, -rotated.X);
            return rotated + position;
        }

        private static Vector3 TransformRotation(Vector3 rotation, int turns)
        {
            int quarterTurns = ((turns % 4) + 4) % 4;
            if (quarterTurns == 0) return rotation;
            float yaw = rotation.Y - quarterTurns * 16384.0f;
            while (yaw > 32767.0f) yaw -= 65536.0f;
            while (yaw < -32768.0f) yaw += 65536.0f;
            return new Vector3(rotation.X, yaw, rotation.Z);
        }

        internal static string[] TextureThemeMaterialNames(SectionTile tile)
        {
            TextureTheme theme = BuildTextureTheme(tile);
            return new[] { theme.Floor.Name, theme.Ceiling.Name, theme.Wall.Name };
        }

        private sealed class TextureTheme
        {
            internal ObjFile.Material Floor, Ceiling, Wall;
        }

        private static TextureTheme BuildTextureTheme(SectionTile tile)
        {
            if (tile == null || tile.Visual == null || tile.Visual.Materials.Count == 0)
                throw new InvalidOperationException("The anchor room has no visual materials for a dungeon texture theme.");
            Dictionary<string, double> floor = new Dictionary<string, double>(StringComparer.Ordinal);
            Dictionary<string, double> ceiling = new Dictionary<string, double>(StringComparer.Ordinal);
            Dictionary<string, double> wall = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (ObjFile.Group group in tile.Visual.Groups)
                foreach (ObjFile.Triangle triangle in group.Triangles)
                {
                    if (triangle.VertIndex == null || triangle.VertIndex.Length < 3) continue;
                    if (group.Animated || group.Custom || group.MultiTexMaterial >= 0 || group.Billboard || group.TwoAxisBillboard || group.LOD || IsPreservedMaterial(triangle.MaterialName)) continue;
                    Vector3d a = tile.Visual.Vertices[triangle.VertIndex[0]].ToVector3d();
                    Vector3d b = tile.Visual.Vertices[triangle.VertIndex[1]].ToVector3d();
                    Vector3d c = tile.Visual.Vertices[triangle.VertIndex[2]].ToVector3d();
                    Vector3d normal = Vector3d.Cross(b - a, c - a);
                    double area = normal.Length;
                    if (area < 0.000001 || string.IsNullOrEmpty(triangle.MaterialName)) continue;
                    normal.Normalize();
                    Dictionary<string, double> bucket = normal.Y > 0.55 ? floor : normal.Y < -0.55 ? ceiling : wall;
                    double previous;
                    bucket.TryGetValue(triangle.MaterialName, out previous);
                    bucket[triangle.MaterialName] = previous + area;
                }
            string fallback = tile.Visual.Materials[0].Name;
            string floorName = DominantMaterial(floor, fallback);
            string ceilingName = DominantMaterial(ceiling, fallback);
            string wallName = DominantMaterial(wall, fallback);
            return new TextureTheme
            {
                Floor = CloneMaterial(tile.Visual, floorName, "__ArcharinaThemeFloor"),
                Ceiling = CloneMaterial(tile.Visual, ceilingName, "__ArcharinaThemeCeiling"),
                Wall = CloneMaterial(tile.Visual, wallName, "__ArcharinaThemeWall")
            };
        }

        private static string DominantMaterial(Dictionary<string, double> weights, string fallback)
        {
            return weights.Count == 0 ? fallback : weights.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
        }

        private static ObjFile.Material CloneMaterial(TileMesh mesh, string sourceName, string name)
        {
            ObjFile.Material source = mesh.Materials.FirstOrDefault(material => material.Name == sourceName) ?? mesh.Materials[0];
            ObjFile.Material clone = source.Clone();
            clone.Name = name;
            return clone;
        }

        private static void ApplyTextureTheme(SectionTile tile, SectionTile themeTile)
        {
            if (tile == null) return;
            ApplyTextureTheme(tile.Visual, themeTile);
        }

        private static void ApplyTextureTheme(TileMesh target, SectionTile themeTile)
        {
            if (target == null || themeTile == null || themeTile.Visual == null) return;
            TextureTheme theme = BuildTextureTheme(themeTile);
            target.Materials.RemoveAll(material => material.Name == theme.Floor.Name || material.Name == theme.Ceiling.Name || material.Name == theme.Wall.Name);
            target.Materials.Add(theme.Floor.Clone());
            target.Materials.Add(theme.Ceiling.Clone());
            target.Materials.Add(theme.Wall.Clone());
            foreach (ObjFile.Group group in target.Groups)
                foreach (ObjFile.Triangle triangle in group.Triangles)
                {
                    if (triangle.VertIndex == null || triangle.VertIndex.Length < 3) continue;
                    if (group.Animated || group.Custom || group.MultiTexMaterial >= 0 || group.Billboard || group.TwoAxisBillboard || group.LOD || IsPreservedMaterial(triangle.MaterialName)) continue;
                    Vector3d a = target.Vertices[triangle.VertIndex[0]].ToVector3d();
                    Vector3d b = target.Vertices[triangle.VertIndex[1]].ToVector3d();
                    Vector3d c = target.Vertices[triangle.VertIndex[2]].ToVector3d();
                    Vector3d normal = Vector3d.Cross(b - a, c - a);
                    if (normal.LengthSquared < 0.000001) continue;
                    normal.Normalize();
                    triangle.MaterialName = normal.Y > 0.55 ? theme.Floor.Name : normal.Y < -0.55 ? theme.Ceiling.Name : theme.Wall.Name;
                }
        }

        private static bool IsPreservedMaterial(string materialName)
        {
            string name = (materialName ?? "").ToLowerInvariant();
            string[] preserved =
            {
                "vine", "ivy", "grass", "leaf", "foliage", "plant", "flower", "moss", "tree", "wood",
                "water", "lava", "fire", "smoke", "ice", "carpet", "rug", "sand", "snow", "door", "gate",
                "chest", "pot", "jar", "crate", "barrel", "torch", "flame", "sign", "statue", "pillar",
                "chain", "rope", "fence", "bridge", "rail", "ladder", "switch", "target", "crystal", "gem"
            };
            return preserved.Any(token => name.Contains(token));
        }
    }

    public static class TileStorage
    {
        public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Archarina64", "tiles"); } }
        private static readonly Dictionary<Type, XmlSerializer> serializers = new Dictionary<Type, XmlSerializer>();
        private static XmlSerializer Serializer(Type type)
        {
            XmlSerializer serializer;
            if (serializers.TryGetValue(type, out serializer)) return serializer;
            XmlAttributeOverrides overrides = new XmlAttributeOverrides();
            foreach (Type modelType in new[] { typeof(ObjFile.Group), typeof(ObjFile.Material) })
                foreach (FieldInfo field in modelType.GetFields())
                    if (field.IsDefined(typeof(XmlIgnoreAttribute), false) && field.Name != "GLID" && field.Name != "TexImage" && field.Name != "Width" && field.Name != "Height")
                        overrides.Add(modelType, field.Name, new XmlAttributes { XmlIgnore = false });
            serializer = new XmlSerializer(type, overrides); serializers.Add(type, serializer); return serializer;
        }
        public static T Clone<T>(T data)
        {
            using (MemoryStream stream = new MemoryStream()) { Serializer(typeof(T)).Serialize(stream, data); stream.Position = 0; return (T)Serializer(typeof(T)).Deserialize(stream); }
        }
        public static T Load<T>(string path)
        {
            using (XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return (T)Serializer(typeof(T)).Deserialize(reader);
        }
        public static string Save<T>(T value, bool library, IEnumerable<TileMesh> meshes)
        {
            string directory = Path.Combine(Root, library ? "library" : "snapshots"); Directory.CreateDirectory(directory);
            string textures = Path.Combine(Root, "textures"); Directory.CreateDirectory(textures);
            foreach (ObjFile.Material mat in meshes.SelectMany(m => m.Materials))
                foreach (string field in TileMesh.TextureFields)
                {
                    FieldInfo info = typeof(ObjFile.Material).GetField(field); string source = (string)info.GetValue(mat);
                    if (string.IsNullOrEmpty(source)) continue;
                    if (!File.Exists(source)) throw new FileNotFoundException("A tile texture is missing.", source);
                    string hash;
                    using (var sha = SHA256.Create()) using (var stream = File.OpenRead(source)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                    string target = Path.Combine(textures, hash + Path.GetExtension(source));
                    if (!File.Exists(target)) File.Copy(source, target);
                    info.SetValue(mat, target);
                }
            string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".xml");
            using (FileStream stream = File.Create(path)) Serializer(typeof(T)).Serialize(stream, value);
            return path;
        }
    }
}
