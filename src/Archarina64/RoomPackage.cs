using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using OpenTK;

namespace SharpOcarina
{
    public enum LogicLinkKind { Activates, Opens, Closes, Unlocks, Spawns, Enables, Disables, StartsPath, StartsCutscene, CompletesPuzzle, SetsFlag, ChecksFlag, GrantsItem }

    [Serializable]
    public sealed class RoomPackageActor
    {
        public string Id, Role, Name, RequirementText;
        public int SourceIndex, SourceRoom;
        public ushort Number, Variable;
        public Vector3d Position;
        public Vector3 Rotation;
        public bool IsTransition;
        public byte FrontRoom = 0xFF, BackRoom = 0xFF, FrontCamera = 0xFF, BackCamera = 0xFF;
        public List<ushort> RequiredObjects = new List<ushort>();
        public List<string> RequiredItems = new List<string>();
        public List<string> GrantedItems = new List<string>();
        public List<DungeonFlagReference> Flags = new List<DungeonFlagReference>();
    }

    [Serializable]
    public sealed class LogicLink
    {
        public string SourceId, TargetId, Parameter;
        public LogicLinkKind Kind;
    }

    [Serializable]
    public sealed class RoomPackage
    {
        public int Version = 3, Scene, Room, DestinationRoom = -1, SourceTransition = -1;
        public string PlacementId;
        public bool LogicOnly;
        public string RomFingerprint, Name;
        public TileMesh Visual, Collision;
        public List<TileSocket> Sockets = new List<TileSocket>();
        public List<RoomPackageActor> Actors = new List<RoomPackageActor>();
        public List<RoomPackageActor> Spawns = new List<RoomPackageActor>();
        public List<LogicLink> Links = new List<LogicLink>();
        public List<string> Warnings = new List<string>();
        public List<string> Suggestions = new List<string>();

        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(Actors.Concat(Spawns).Select(a => a.Id).Where(id => !string.IsNullOrEmpty(id)));
            foreach (LogicLink link in Links)
            {
                if (string.IsNullOrWhiteSpace(link.SourceId) || !ids.Contains(link.SourceId)) errors.Add("Link source is missing: " + link.SourceId);
                if (string.IsNullOrWhiteSpace(link.TargetId) || !ids.Contains(link.TargetId)) errors.Add("Link target is missing: " + link.TargetId);
                if (link.SourceId == link.TargetId) errors.Add("A logic link cannot target itself: " + link.SourceId);
            }
            foreach (RoomPackageActor actor in Actors.Concat(Spawns))
                if (string.IsNullOrWhiteSpace(actor.Id)) errors.Add("Every package actor needs an ID.");
            return errors;
        }

        public List<string> Review()
        {
            var result = new List<string>(Warnings ?? new List<string>());
            result.AddRange(Validate());
            if (Suggestions != null) result.AddRange(Suggestions.Select(s => "Suggestion: " + s));
            return result.Distinct().ToList();
        }
    }

    public static class RoomPackageReview
    {
        public static List<string> CheckScene(ZScene scene)
        {
            var issues = new List<string>();
            if (scene == null) { issues.Add("ERROR: No scene is loaded."); return issues; }
            List<RoomPackage> packages = scene.SandboxGameplayPackages ?? new List<RoomPackage>();
            if (packages.Count == 0)
            {
                issues.Add("REVIEW: No tile gameplay manifests are attached to this scene.");
                issues.Add("Suggestion: Place or extract a room tile with gameplay data before exporting a procedural dungeon.");
                return issues;
            }

            HashSet<ushort> availableObjects = new HashSet<ushort>(scene.ZObjects.Select(o => o.Value));
            foreach (ZScene.ZRoom room in scene.Rooms)
                foreach (ZScene.ZUShort objectId in room.ZObjects) availableObjects.Add(objectId.Value);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RoomPackage package in packages)
            {
                string label = string.IsNullOrWhiteSpace(package.Name) ? "Unnamed tile" : package.Name;
                foreach (string issue in package.Review()) issues.Add("REVIEW [" + label + "]: " + issue);
                if (package.LogicOnly) continue;
                foreach (RoomPackageActor actor in package.Actors.Concat(package.Spawns))
                {
                    if (!ids.Add(actor.Id)) issues.Add("ERROR: Duplicate gameplay actor ID across placed tiles: " + actor.Id);
                    if (!Fits(actor.Position.X) || !Fits(actor.Position.Y) || !Fits(actor.Position.Z))
                        issues.Add("ERROR [" + label + "]: Actor " + actor.Id + " is outside the N64 coordinate range.");
                    foreach (ushort objectId in actor.RequiredObjects.Distinct())
                        if (!availableObjects.Contains(objectId))
                            issues.Add("REVIEW [" + label + "]: Actor " + actor.Name + " needs object bank " + objectId.ToString("X4") + ".");
                }
                foreach (RoomPackageActor door in package.Actors.Where(a => a.IsTransition))
                {
                    bool nativeMatch = scene.Transitions.Any(nativeDoor => nativeDoor.Number == door.Number &&
                        Math.Abs(nativeDoor.XPos - door.Position.X) < 1.0 && Math.Abs(nativeDoor.YPos - door.Position.Y) < 1.0 && Math.Abs(nativeDoor.ZPos - door.Position.Z) < 1.0);
                    if (!nativeMatch)
                    {
                        issues.Add("REVIEW [" + label + "]: Door actor " + door.Name + " is present in the tile manifest but has no native scene transition.");
                        issues.Add("Suggestion: Connect its doorway socket or add a native transition before export.");
                    }
                }
            }
            foreach (TileSocket socket in scene.TileSockets)
            {
                if (string.IsNullOrEmpty(socket.ConnectedTo)) issues.Add("REVIEW: Unconnected doorway socket " + (socket.Name ?? socket.Id) + " remains in the assembled scene.");
                else if (!scene.TileSockets.Any(other => other.Id == socket.ConnectedTo))
                    issues.Add("ERROR: Doorway socket " + socket.Id + " points to a missing socket " + socket.ConnectedTo + ".");
            }
            return issues.Distinct().ToList();
        }

        private static bool Fits(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= short.MinValue && value <= short.MaxValue;
        }
    }

    public static class NativeGameplayMaterializer
    {
        public static List<string> Apply(ZScene scene)
        {
            var issues = new List<string>();
            if (scene == null) { issues.Add("ERROR: No scene is loaded."); return issues; }
            foreach (RoomPackage package in scene.SandboxGameplayPackages ?? new List<RoomPackage>())
            {
                if (package.LogicOnly) continue;
                int roomId = package.DestinationRoom >= 0 ? package.DestinationRoom : package.Room;
                if (roomId < 0 || roomId >= scene.Rooms.Count) { issues.Add("ERROR: Tile gameplay package targets missing room " + roomId + "."); continue; }
                ZScene.ZRoom room = scene.Rooms[roomId];
                foreach (RoomPackageActor actor in package.Actors.Concat(package.Spawns))
                {
                    foreach (ushort objectId in actor.RequiredObjects.Distinct())
                        if (!room.ZObjects.Any(o => o.Value == objectId)) room.ZObjects.Add(new ZScene.ZUShort(objectId));
                    if (actor.IsTransition)
                    {
                        if (package.Scene != scene.SceneNumber)
                        {
                            issues.Add("REVIEW: Door " + actor.Name + " from " + package.Name + " needs an explicit native room mapping before export.");
                            continue;
                        }
                        if (!ValidRoom(actor.FrontRoom, scene) || !ValidRoom(actor.BackRoom, scene))
                        {
                            issues.Add("REVIEW: Door " + actor.Name + " from " + package.Name + " has no valid native room endpoints.");
                            continue;
                        }
                        ZActor existingTransition = scene.Transitions.FirstOrDefault(existing => existing.SandboxGameplayId == actor.Id) ??
                            scene.Transitions.FirstOrDefault(existing => string.IsNullOrEmpty(existing.SandboxGameplayId) && SameNative(existing, actor));
                        if (existingTransition == null)
                        {
                            var transition = new ZActor(actor.FrontRoom, actor.FrontCamera, actor.BackRoom, actor.BackCamera,
                                actor.Number, (float)actor.Position.X, (float)actor.Position.Y, (float)actor.Position.Z,
                                actor.Rotation.Y, actor.Variable) { SandboxGameplayId = actor.Id };
                            scene.Transitions.Add(transition);
                        }
                        else existingTransition.SandboxGameplayId = actor.Id;
                        continue;
                    }
                    if (package.Spawns.Contains(actor))
                    {
                        ZActor existingSpawn = scene.SpawnPoints.FirstOrDefault(existing => existing.SandboxGameplayId == actor.Id) ??
                            scene.SpawnPoints.FirstOrDefault(existing => string.IsNullOrEmpty(existing.SandboxGameplayId) && SameNative(existing, actor));
                        if (existingSpawn == null)
                            scene.SpawnPoints.Add(new ZActor((byte)roomId, actor.Number, (float)actor.Position.X, (float)actor.Position.Y, (float)actor.Position.Z,
                                actor.Rotation.X, actor.Rotation.Y, actor.Rotation.Z, actor.Variable) { SandboxGameplayId = actor.Id });
                        else existingSpawn.SandboxGameplayId = actor.Id;
                    }
                    else
                    {
                        ZActor existingActor = room.ZActors.FirstOrDefault(existing => existing.SandboxGameplayId == actor.Id) ??
                            room.ZActors.FirstOrDefault(existing => string.IsNullOrEmpty(existing.SandboxGameplayId) && SameNative(existing, actor));
                        if (existingActor == null)
                            room.ZActors.Add(new ZActor(actor.Number, (float)actor.Position.X, (float)actor.Position.Y, (float)actor.Position.Z,
                                actor.Rotation.X, actor.Rotation.Y, actor.Rotation.Z, actor.Variable) { SandboxGameplayId = actor.Id });
                        else existingActor.SandboxGameplayId = actor.Id;
                    }
                }
            }
            return issues;
        }

        private static bool SameNative(ZActor native, RoomPackageActor actor)
        {
            return native.Number == actor.Number && native.Variable == actor.Variable &&
                Math.Abs(native.XPos - actor.Position.X) < 0.01 && Math.Abs(native.YPos - actor.Position.Y) < 0.01 && Math.Abs(native.ZPos - actor.Position.Z) < 0.01 &&
                Math.Abs(native.XRot - actor.Rotation.X) < 0.01 && Math.Abs(native.YRot - actor.Rotation.Y) < 0.01 && Math.Abs(native.ZRot - actor.Rotation.Z) < 0.01;
        }

        private static bool ValidRoom(byte room, ZScene scene)
        {
            return room != 0xFF && room < scene.Rooms.Count && room <= 127;
        }
    }

    public static class NativeLogicLinkMaterializer
    {
        public static List<string> Apply(ZScene scene, RomAssetCatalog catalog)
        {
            var issues = new List<string>();
            if (scene == null) { issues.Add("ERROR: No scene is loaded."); return issues; }
            List<RoomPackage> packages = scene.SandboxGameplayPackages ?? new List<RoomPackage>();
            Dictionary<string, RoomPackageActor> actors = new Dictionary<string, RoomPackageActor>(StringComparer.Ordinal);
            foreach (RoomPackage package in packages)
                foreach (RoomPackageActor actor in package.Actors.Concat(package.Spawns))
                    if (!actors.ContainsKey(actor.Id)) actors.Add(actor.Id, actor);
            Dictionary<string, ZActor> native = scene.Rooms.SelectMany(r => r.ZActors).Concat(scene.SpawnPoints).Concat(scene.Transitions)
                .Where(a => !string.IsNullOrEmpty(a.SandboxGameplayId)).GroupBy(a => a.SandboxGameplayId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            foreach (RoomPackage package in packages)
                foreach (RoomPackageActor actor in package.Actors.Concat(package.Spawns))
                    foreach (DungeonFlagReference flag in actor.Flags ?? new List<DungeonFlagReference>()) used.Add(FlagKey(flag.Kind, flag.Value));

            foreach (RoomPackage package in packages)
                foreach (LogicLink link in package.Links ?? new List<LogicLink>())
                {
                    RoomPackageActor source, target;
                    if (!actors.TryGetValue(link.SourceId, out source) || !actors.TryGetValue(link.TargetId, out target))
                    {
                        issues.Add("ERROR: Logic link references an actor that is not present in the assembled scene."); continue;
                    }
                    DungeonFlagReference sourceFlag = (source.Flags ?? new List<DungeonFlagReference>()).FirstOrDefault(f => f.CanSet &&
                        (target.Flags ?? new List<DungeonFlagReference>()).Any(t => t.CanCheck && string.Equals(t.Kind, f.Kind, StringComparison.OrdinalIgnoreCase)));
                    DungeonFlagReference targetFlag = sourceFlag == null ? null : target.Flags.FirstOrDefault(f => f.CanCheck && string.Equals(f.Kind, sourceFlag.Kind, StringComparison.OrdinalIgnoreCase));
                    if (sourceFlag == null || targetFlag == null)
                    {
                        issues.Add("ERROR: Link " + link.SourceId + " -> " + link.TargetId + " has no compatible native setter/checker flag pair."); continue;
                    }
                    if (sourceFlag.Mask == 0 || targetFlag.Mask == 0)
                    {
                        issues.Add("ERROR: Link " + link.SourceId + " -> " + link.TargetId + " has an unbounded native flag field."); continue;
                    }
                    int capacity = Math.Min(sourceFlag.Mask >> sourceFlag.Position, targetFlag.Mask >> targetFlag.Position);
                    bool existingPair = sourceFlag.Value == targetFlag.Value && sourceFlag.Value >= 0 && sourceFlag.Value <= capacity;
                    int value = existingPair ? sourceFlag.Value : 1;
                    while (!existingPair && value <= capacity && used.Contains(FlagKey(sourceFlag.Kind, value))) value++;
                    if (value > capacity)
                    {
                        issues.Add("ERROR: No free " + sourceFlag.Kind + " flag value is available for link " + link.SourceId + " -> " + link.TargetId + "."); continue;
                    }
                    used.Add(FlagKey(sourceFlag.Kind, value)); sourceFlag.Value = value; targetFlag.Value = value;
                    ApplyFlag(source, sourceFlag, value); ApplyFlag(target, targetFlag, value);
                    ZActor nativeSource, nativeTarget;
                    if (!native.TryGetValue(source.Id, out nativeSource) || !native.TryGetValue(target.Id, out nativeTarget))
                    {
                        issues.Add("ERROR: Link " + link.SourceId + " -> " + link.TargetId + " has no materialized native actor identity."); continue;
                    }
                    ApplyFlag(nativeSource, sourceFlag, value); ApplyFlag(nativeTarget, targetFlag, value);
                }
            return issues.Distinct().ToList();
        }

        private static string FlagKey(string kind, int value) { return (kind ?? "Unknown") + ":" + value; }

        private static void ApplyFlag(RoomPackageActor actor, DungeonFlagReference flag, int value)
        {
            ushort encoded = (ushort)((value << flag.Position) & flag.Mask);
            if (string.Equals(flag.Target, "Var", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(flag.Target))
                actor.Variable = (ushort)((actor.Variable & ~flag.Mask) | encoded);
            else if (string.Equals(flag.Target, "XRot", StringComparison.OrdinalIgnoreCase)) actor.Rotation.X = value;
            else if (string.Equals(flag.Target, "YRot", StringComparison.OrdinalIgnoreCase)) actor.Rotation.Y = value;
            else if (string.Equals(flag.Target, "ZRot", StringComparison.OrdinalIgnoreCase)) actor.Rotation.Z = value;
        }

        private static void ApplyFlag(ZActor actor, DungeonFlagReference flag, int value)
        {
            ushort encoded = (ushort)((value << flag.Position) & flag.Mask);
            if (string.Equals(flag.Target, "Var", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(flag.Target))
                actor.Variable = (ushort)((actor.Variable & ~flag.Mask) | encoded);
            else if (string.Equals(flag.Target, "XRot", StringComparison.OrdinalIgnoreCase)) actor.XRot = value;
            else if (string.Equals(flag.Target, "YRot", StringComparison.OrdinalIgnoreCase)) actor.YRot = value;
            else if (string.Equals(flag.Target, "ZRot", StringComparison.OrdinalIgnoreCase)) actor.ZRot = value;
        }
    }

    public static class RoomPackageBuilder
    {
        public static void PopulateMetadata(RoomPackageActor actor, RomPaletteAsset asset, ZActor native)
        {
            if (actor == null || asset == null || native == null) return;
            actor.RequiredItems = ReadRequiredItems(asset, native); actor.GrantedItems = ReadGrantedItems(asset, native); actor.Flags = ReadFlags(asset, native);
        }

        public static RoomPackage Capture(ZScene scene, int roomId, RomAssetCatalog catalog,
            Vector3d? min = null, Vector3d? max = null, bool includeWholeRoom = false)
        {
            if (scene == null || roomId < 0 || roomId >= scene.Rooms.Count) throw new InvalidOperationException("Choose an existing room.");
            ZScene.ZRoom room = scene.Rooms[roomId];
            RoomPackage package = new RoomPackage
            {
                Scene = scene.SceneNumber, Room = roomId, DestinationRoom = roomId, RomFingerprint = catalog == null || catalog.Rooms == null ? null : catalog.Rooms.RomFingerprint,
                Name = string.Format("Scene {0:X2} Room {1:D2} gameplay", scene.SceneNumber, roomId)
            };
            Dictionary<ushort, RomPaletteAsset> assets = (catalog == null ? Enumerable.Empty<RomPaletteAsset>() : catalog.Assets)
                .GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
            Func<Vector3d, bool> inside = p => includeWholeRoom || !min.HasValue || !max.HasValue ||
                (p.X >= min.Value.X && p.X <= max.Value.X && p.Y >= min.Value.Y && p.Y <= max.Value.Y && p.Z >= min.Value.Z && p.Z <= max.Value.Z);
            Func<ZActor, int, string, RoomPackageActor> make = (actor, index, role) =>
            {
                RomPaletteAsset asset;
                assets.TryGetValue(actor.Number, out asset);
                RoomPackageActor result = new RoomPackageActor
                {
                    Id = string.Format("scene_{0}_room_{1}_actor_{2}", scene.SceneNumber, roomId, index),
                    Role = role ?? (asset == null ? "Unknown" : asset.Category),
                    Name = asset == null ? "Actor " + actor.Number.ToString("X4") : asset.Name,
                    SourceIndex = index, SourceRoom = roomId, Number = actor.Number, Variable = actor.Variable,
                    Position = new Vector3d(actor.XPos, actor.YPos, actor.ZPos),
                    Rotation = new Vector3(actor.XRot, actor.YRot, actor.ZRot),
                    IsTransition = actor.IsTransition,
                    FrontRoom = actor.FrontSwitchTo, BackRoom = actor.BackSwitchTo, FrontCamera = actor.FrontCamera, BackCamera = actor.BackCamera,
                    RequirementText = asset == null ? null : asset.RequirementText,
                    RequiredObjects = asset == null ? new List<ushort>() : asset.Objects.ToList()
                };
                if (asset != null)
                {
                    PopulateMetadata(result, asset, actor);
                }
                return result;
            };
            for (int i = 0; i < room.ZActors.Count; i++)
            {
                ZActor actor = room.ZActors[i];
                if (inside(new Vector3d(actor.XPos, actor.YPos, actor.ZPos))) package.Actors.Add(make(actor, i, null));
            }
            for (int i = 0; i < scene.SpawnPoints.Count; i++)
            {
                ZActor actor = scene.SpawnPoints[i];
                if (actor.SpawnRoom == roomId && inside(new Vector3d(actor.XPos, actor.YPos, actor.ZPos))) package.Spawns.Add(make(actor, i, "Spawns"));
            }
            for (int i = 0; i < scene.Transitions.Count; i++)
            {
                ZActor actor = scene.Transitions[i];
                bool belongs = actor.FrontSwitchTo == roomId || actor.BackSwitchTo == roomId;
                if (belongs && inside(new Vector3d(actor.XPos, actor.YPos, actor.ZPos))) package.Actors.Add(make(actor, i, "Doors"));
            }
            foreach (ZScene.ZUShort objectId in room.ZObjects) foreach (RoomPackageActor actor in package.Actors.Concat(package.Spawns))
                if (!actor.RequiredObjects.Contains(objectId.Value)) actor.RequiredObjects.Add(objectId.Value);

            DungeonAnalysisReport analysis = ProceduralDungeonAnalyzer.AnalyzeScene(scene, catalog);
            DungeonRoomAnalysis roomAnalysis = analysis.FindRoom(scene.SceneNumber, roomId);
            if (roomAnalysis != null)
            {
                foreach (DungeonLogicCandidate candidate in roomAnalysis.LogicCandidates)
                {
                    RoomPackageActor source = package.Actors.FirstOrDefault(a => a.Id.EndsWith("_" + candidate.SourceId.Split('_').Last(), StringComparison.Ordinal));
                    RoomPackageActor target = package.Actors.FirstOrDefault(a => a.Id.EndsWith("_" + candidate.TargetId.Split('_').Last(), StringComparison.Ordinal));
                    if (source != null && target != null) package.Links.Add(new LogicLink { SourceId = source.Id, TargetId = target.Id, Kind = ParseLinkKind(candidate.Kind), Parameter = candidate.Parameter });
                    else package.Suggestions.Add(string.Format("Add or retain the actor pair for {0} so it can link {1} to {2}.", candidate.Kind, candidate.SourceId, candidate.TargetId));
                }
                if (roomAnalysis.EnemyCount > 0 && roomAnalysis.DoorCount > 0 && !package.Links.Any(l => l.Kind == LogicLinkKind.Opens || l.Kind == LogicLinkKind.Unlocks))
                {
                    package.Warnings.Add("Enemies and doors share this room, but no explicit clear-room or enemy-defeat door link was decoded.");
                    package.Suggestions.Add("Add an enemy-clear trigger and assign it to the target door with Opens or Unlocks.");
                }
                if (roomAnalysis.ChestCount > 0 && roomAnalysis.SwitchCount > 0 && !package.Links.Any(l => l.Kind == LogicLinkKind.Spawns || l.Kind == LogicLinkKind.GrantsItem))
                    package.Suggestions.Add("Check the chest reward and switch flag, then link the switch or clear-room event to the chest with Spawns or GrantsItem.");
            }
            foreach (ZScene.ZUShort objectId in room.ZObjects)
                if (!package.Actors.Concat(package.Spawns).Any(a => a.RequiredObjects.Contains(objectId.Value))) package.Warnings.Add("Room object bank " + objectId.Value.ToString("X4") + " is present but not claimed by a captured actor.");
            return package;
        }

        private static LogicLinkKind ParseLinkKind(string value)
        {
            LogicLinkKind result;
            return Enum.TryParse(value, true, out result) ? result : LogicLinkKind.SetsFlag;
        }

        private static List<string> ReadRequiredItems(RomPaletteAsset asset, ZActor native)
        {
            List<string> result = new List<string>();
            if (asset == null) return result;
            string text = ((asset.Name ?? "") + " " + (asset.RequirementText ?? "")).ToLowerInvariant();
            string[] names = { "bow", "hookshot", "longshot", "boomerang", "bomb", "bombchu", "lens of truth", "iron boots", "hover boots", "megaton hammer", "ocarina" };
            foreach (string item in names)
                if (text.Contains("requires " + item) || text.Contains("requires the " + item) || text.Contains("need " + item) || text.Contains("only with " + item))
                    result.Add(item);
            foreach (RomActorLogicProperty property in asset.LogicProperties ?? new List<RomActorLogicProperty>())
            {
                string propertyName = property.Name ?? "";
                if (propertyName.IndexOf("require", StringComparison.OrdinalIgnoreCase) < 0 &&
                    propertyName.IndexOf("need", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string selected = property.DropdownName(property.Read(native.Variable, (short)native.XRot, (short)native.YRot, (short)native.ZRot));
                string normalized = NormalizeRequiredItem(selected);
                if (!string.IsNullOrEmpty(normalized) && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase)) result.Add(normalized);
            }
            return result;
        }

        private static List<string> ReadGrantedItems(RomPaletteAsset asset, ZActor actor)
        {
            return asset.LogicProperties.Where(p => (p.Name ?? "").IndexOf("content", StringComparison.OrdinalIgnoreCase) >= 0 || (p.Name ?? "").IndexOf("reward", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.DropdownName(p.Read(actor.Variable, (short)actor.XRot, (short)actor.YRot, (short)actor.ZRot)))
                .Where(s => !string.IsNullOrWhiteSpace(s) && !IsEmptyRewardLabel(s)).ToList();
        }

        private static string NormalizeRequiredItem(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim().ToLowerInvariant();
            if (text.Contains("nothing") || text.Contains("none") || text.Contains("unknown") || text.Contains("small key") || text.Contains("boss key") || text.Contains("map") || text.Contains("compass")) return null;
            string[] names = { "bow", "hookshot", "longshot", "boomerang", "bomb", "bombchu", "lens of truth", "iron boots", "hover boots", "megaton hammer", "ocarina" };
            return names.FirstOrDefault(text.Contains);
        }

        private static bool IsEmptyRewardLabel(string value)
        {
            string text = (value ?? "").Trim();
            return text.Equals("Nothing", StringComparison.OrdinalIgnoreCase) || text.Equals("None", StringComparison.OrdinalIgnoreCase) || text.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static List<DungeonFlagReference> ReadFlags(RomPaletteAsset asset, ZActor actor)
        {
            List<DungeonFlagReference> result = new List<DungeonFlagReference>();
            if (asset == null || asset.LogicProperties == null) return result;
            foreach (RomActorLogicProperty property in asset.LogicProperties)
            {
                string kind = property.Kind;
                if (string.IsNullOrEmpty(kind)) continue;
                int value = property.Read(actor.Variable, (short)actor.XRot, (short)actor.YRot, (short)actor.ZRot);
                string name = property.Name ?? "";
                bool setter = name.IndexOf("set", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("destroy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("burn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("lit", StringComparison.OrdinalIgnoreCase) >= 0;
                bool checker = name.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("when", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("opens", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("rises", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("lowers", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("burns", StringComparison.OrdinalIgnoreCase) >= 0;
                result.Add(new DungeonFlagReference
                {
                    Kind = kind, Property = name, Target = property.Target, Value = value,
                    Mask = property.Mask, Position = property.Position,
                    CanSet = setter, CanCheck = checker || !setter,
                    Evidence = name + " on " + asset.Name
                });
            }
            return result;
        }
    }

    public static class LogicLinkGraph
    {
        public static void Add(RoomPackage package, string source, string target, LogicLinkKind kind, string parameter = null)
        {
            if (package == null) throw new InvalidOperationException("Choose a room package.");
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("Choose both a source and target actor.");
            if (source == target) throw new InvalidOperationException("A logic link cannot target itself.");
            if (!package.Actors.Concat(package.Spawns).Any(a => a.Id == source) || !package.Actors.Concat(package.Spawns).Any(a => a.Id == target)) throw new InvalidOperationException("Both link endpoints must belong to the package.");
            if (package.Links.Any(l => l.SourceId == source && l.TargetId == target && l.Kind == kind)) return;
            package.Links.Add(new LogicLink { SourceId = source, TargetId = target, Kind = kind, Parameter = parameter });
        }
    }
}
