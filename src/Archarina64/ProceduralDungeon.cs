using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK;

namespace SharpOcarina
{
    [Flags]
    public enum DungeonRoomFeatures
    {
        None = 0,
        Floor = 1,
        Collision = 2,
        Spawn = 4,
        Door = 8,
        Switch = 16,
        Chest = 32,
        Enemy = 64,
        Boss = 128,
        Npc = 256,
        Path = 512,
        Camera = 1024,
        Cutscene = 2048,
        Water = 4096,
        Prop = 8192
    }

    [Flags]
    public enum DungeonTraversalCapabilities
    {
        None = 0,
        Walk = 1,
        JumpGap = 2,
        ClimbVines = 4,
        Hookshot = 8,
        PushBlock = 16,
        Stairs = 32,
        FallDrop = 64,
        Swim = 128
    }

    [Serializable]
    public sealed class DungeonTraversalConstraint
    {
        public string Kind, Evidence, RequiredItem;
        public int SourceRoom = -1, TargetRoom = -1;
        public Vector3d Start, End;
        public double HorizontalDistance, VerticalDistance, MaxDistance;
        public double Confidence;
        public double ClearanceRadius = 35;
        public List<DungeonTraversalObstacle> Obstacles = new List<DungeonTraversalObstacle>();
        public bool Optional;
    }

    [Serializable]
    public sealed class DungeonTraversalAnalysis
    {
        public int HookshotSurfaceCount, ClimbableSurfaceCount, SteepSurfaceCount, EponaBlockSurfaceCount;
        public int JumpGapCount, VineRouteCount, HookshotAnchorCount, PushBlockCount, StairCount, FallDropCount;
        public List<string> Evidence = new List<string>();
        public List<DungeonTraversalConstraint> Constraints = new List<DungeonTraversalConstraint>();
    }

    [Serializable]
    public sealed class DungeonActorProfile
    {
        public string Id, Name, Category;
        public int SceneId, RoomId;
        public ushort Number, Variable;
        public Vector3d Position;
        public Vector3 Rotation;
        public bool IsSpawn;
        public List<DungeonFlagReference> Flags = new List<DungeonFlagReference>();
        public List<string> GrantedItems = new List<string>();
        public List<string> RequiredItems = new List<string>();
        public int SmallKeyCost;
        public bool RequiresBossKey;
    }

    [Serializable]
    public sealed class DungeonFlagReference
    {
        public string Kind, Property, Target;
        public int Value, Position;
        public ushort Mask;
        public bool CanSet, CanCheck;
        public string Evidence;
    }

    [Serializable]
    public sealed class DungeonLogicCandidate
    {
        public string SourceId, TargetId, Kind, Parameter, Evidence;
        public int SceneId, SourceRoom, TargetRoom;
        public double Confidence;
    }

    [Serializable]
    public sealed class DungeonConnectionAnalysis
    {
        public int SceneId, TransitionIndex, FrontRoom, BackRoom;
        public ushort ActorNumber, Variable;
        public Vector3d Position;
        public double Facing;
        public bool IsOneWay;
        public string TraversalMode, Evidence;
        public List<string> RequiredItems = new List<string>();
        public bool IsExternal { get { return FrontRoom == 0xFF || BackRoom == 0xFF; } }
    }

    [Serializable]
    public sealed class DungeonRoomAnalysis
    {
        public int SceneId, RoomId;
        public string Name;
        public Vector3d BoundsMin, BoundsMax;
        public int VisualTriangles, CollisionTriangles, FloorTriangles;
        public int SpawnCount, PathCount, CameraCount, CutsceneCount, WaterboxCount;
        public int EnemyCount, ChestCount, DoorCount, SwitchCount, TraversalDepth, TraversalDegree;
        public int SmallKeyCost;
        public bool RequiresBossKey;
        public DungeonRoomFeatures Features;
        public double GeometryScore;
        public string Role;
        public List<string> Warnings = new List<string>();
        public List<DungeonActorProfile> Actors = new List<DungeonActorProfile>();
        public List<DungeonConnectionAnalysis> Connections = new List<DungeonConnectionAnalysis>();
        public List<DungeonLogicCandidate> LogicCandidates = new List<DungeonLogicCandidate>();
        public List<string> RequiredItems = new List<string>();
        public List<string> GrantedItems = new List<string>();
        public DungeonTraversalCapabilities TraversalCapabilities;
        public int JumpGapCount, VineRouteCount, HookshotAnchorCount, PushBlockCount, StairCount, FallDropCount;
        public List<DungeonTraversalConstraint> TraversalConstraints = new List<DungeonTraversalConstraint>();
        public List<DungeonCollisionVolume> CollisionVolumes = new List<DungeonCollisionVolume>();
        public DungeonWaterProfile WaterProfile = new DungeonWaterProfile();

        public bool Supports(DungeonRoomFeatures required)
        {
            return (Features & required) == required;
        }
    }

    [Serializable]
    public sealed class DungeonAnalysisReport
    {
        public int Version = 1;
        public string RomFingerprint;
        public DateTime CreatedUtc = DateTime.UtcNow;
        public int SceneCount;
        public List<int> AnalyzedScenes = new List<int>();
        public List<DungeonRoomAnalysis> Rooms = new List<DungeonRoomAnalysis>();
        public List<DungeonConnectionAnalysis> Connections = new List<DungeonConnectionAnalysis>();
        public List<string> Diagnostics = new List<string>();
        public List<int> ReachableRooms = new List<int>();
        public List<int> UnreachableRooms = new List<int>();
        public List<string> ReachableRoomKeys = new List<string>();
        public List<string> UnreachableRoomKeys = new List<string>();
        public List<DungeonLogicCandidate> LogicCandidates = new List<DungeonLogicCandidate>();
        public DungeonPatternReport DungeonPatterns;
        public DungeonTraversalAnalysis Traversal = new DungeonTraversalAnalysis();

        public DungeonRoomAnalysis FindRoom(int scene, int room)
        {
            return Rooms.FirstOrDefault(x => x.SceneId == scene && x.RoomId == room);
        }
    }

    [Serializable]
    public sealed class DungeonGeneratorSettings
    {
        public int Seed = 1;
        public int RoomCount = 8;
        public DungeonRoomFeatures RequiredFeatures = DungeonRoomFeatures.Floor | DungeonRoomFeatures.Collision;
        public bool RequireBoss;
        public bool RequireChest;
        public bool AllowLoops = true;
        public bool GenerateCorridors = true;
        public int MaxEnemiesPerRoom = 4;
        public List<string> StartingItems = new List<string>();
        public bool AllowPartialPlan;
        public bool RequireTraversalMechanics = true;
        public bool RequireItemAcquisitionArc;
        public int MinimumItemAcquisitionArcs = 1;
        public DungeonRomMechanicsProfile RomMechanics;
        public double SafeJumpGap = 180;
        public double SafeHookshotDistance = 500;
        public double SafeVineRise = 800;
    }

    [Serializable]
    public sealed class DungeonMeshPlan
    {
        public int SceneId, RoomId;
        public string TexturePolicy = "preserve-source-materials";
        public bool GenerateCorridor;
        public int VisualTriangles, CollisionTriangles;
    }

    [Serializable]
    public sealed class DungeonActorPlacement
    {
        public string SourceId, Role;
        public ushort Number, Variable;
        public Vector3d Position;
        public Vector3 Rotation;
        public List<DungeonFlagReference> Flags = new List<DungeonFlagReference>();
        public List<string> GrantedItems = new List<string>();
    }

    [Serializable]
    public sealed class DungeonRoomPlan
    {
        public int Index;
        public int SourceScene, SourceRoom;
        public DungeonRoomFeatures Features;
        public Vector3d BoundsMin, BoundsMax;
        public List<DungeonCollisionVolume> CollisionVolumes = new List<DungeonCollisionVolume>();
        public DungeonWaterProfile WaterProfile = new DungeonWaterProfile();
        public DungeonMeshPlan Mesh = new DungeonMeshPlan();
        public List<DungeonActorPlacement> Actors = new List<DungeonActorPlacement>();
        public List<string> RequiredItems = new List<string>();
        public List<string> GrantedItems = new List<string>();
        public int SmallKeyCost;
        public bool RequiresBossKey;
        public DungeonTraversalCapabilities TraversalCapabilities;
        public List<DungeonTraversalConstraint> TraversalConstraints = new List<DungeonTraversalConstraint>();
    }

    [Serializable]
    public sealed class DungeonConnectionPlan
    {
        public int FromRoom, ToRoom;
        public int SourceScene, TransitionIndex;
        public bool GenerateCorridor;
        public bool ReuseNativeTransition;
        public bool Bidirectional = true;
        public List<string> RequiredItems = new List<string>();
    }

    [Serializable]
    public sealed class ProceduralDungeonPlan
    {
        public int Version = 1, Seed;
        public List<DungeonRoomPlan> Rooms = new List<DungeonRoomPlan>();
        public List<DungeonConnectionPlan> Connections = new List<DungeonConnectionPlan>();
        public List<string> Warnings = new List<string>();
        public DungeonProgressionReport Progression;
        public DungeonItemArcReport ItemArc;
        public DungeonNativeAssemblyReport Assembly;
        public DungeonTopologyPlan Topology;
        public DungeonTraversalProgressionReport TraversalProgression;
        public DungeonTraversalCalibration TraversalCalibration = DungeonTraversalCalibration.ConservativeOoT();
        public DungeonRomMechanicsProfile RomMechanics;
        public List<string> StartingItems = new List<string>();
    }

    [Serializable]
    public sealed class DungeonFlagRemap
    {
        public string Group, Kind;
        public int SourceValue, GeneratedValue;
        public List<int> RoomIndices = new List<int>();
    }

    [Serializable]
    public sealed class DungeonNativeAssemblyReport
    {
        public bool Ready;
        public int RemappedReferences;
        public List<DungeonFlagRemap> FlagRemaps = new List<DungeonFlagRemap>();
        public List<string> Warnings = new List<string>();
    }

    [Serializable]
    public sealed class DungeonPatternProfile
    {
        public int SceneId;
        public string Name;
        public int RoomCount, ConnectionCount, ReachableRoomCount, GateCount, RewardCount, BossRoomCount;
        public int DeadEndCount, HubCount, EnemyCount, ChestCount;
        public double LinearProgressionScore, BranchingScore, GeometryScore;
        public double AverageRewardDepth, EnemyPacingScore;
        public List<string> RewardItems = new List<string>();
        public Dictionary<string, int> RewardCounts = new Dictionary<string, int>();
        public List<string> RewardOwnership = new List<string>();
        public List<string> ChestFlagOwnership = new List<string>();
        public List<string> ObjectBanks = new List<string>();
        public List<string> RoomOwnership = new List<string>();
        public Dictionary<string, int> ActorCategories = new Dictionary<string, int>();
        public Dictionary<string, int> RoomRoles = new Dictionary<string, int>();
        public int SmallKeyCount, BossKeyCount, MapCount, CompassCount, LockedDoorCount, SwitchGateCount;
        public int JumpGapCount, VineRouteCount, HookshotAnchorCount, PushBlockCount, StairCount, FallDropCount;
        public int BossDepth = -1;
        public double RewardSpacingScore, GateRewardBalance;
        public List<DungeonProgressionMilestone> ProgressionChain = new List<DungeonProgressionMilestone>();
        public List<string> Diagnostics = new List<string>();
    }

    [Serializable]
    public sealed class DungeonProgressionMilestone
    {
        public int Depth, RoomId, GateCount, EnemyCount, ChestCount;
        public string Role;
        public List<string> Rewards = new List<string>();
    }

    [Serializable]
    public sealed class DungeonPatternComparison
    {
        public double AverageRoomCount, AverageRewardSpacing, AverageGateRewardBalance;
        public List<string> SharedPatterns = new List<string>();
        public List<string> Outliers = new List<string>();
    }

    [Serializable]
    public sealed class DungeonPatternReport
    {
        public int Version = 1;
        public string RomFingerprint;
        public DateTime CreatedUtc = DateTime.UtcNow;
        public List<DungeonPatternProfile> Dungeons = new List<DungeonPatternProfile>();
        public DungeonPatternComparison Comparison = new DungeonPatternComparison();
        public List<string> Diagnostics = new List<string>();
    }

    [Serializable]
    public sealed class DungeonTopologyNode
    {
        public int Index, Depth, Branch;
        public string Role;
    }

    [Serializable]
    public sealed class DungeonTopologyEdge
    {
        public int From, To;
        public string Purpose;
    }

    [Serializable]
    public sealed class DungeonTopologyPlan
    {
        public string Archetype;
        public int Seed;
        public List<DungeonTopologyNode> Nodes = new List<DungeonTopologyNode>();
        public List<DungeonTopologyEdge> Edges = new List<DungeonTopologyEdge>();
        public List<string> Warnings = new List<string>();
    }

    public static class DungeonTopologyPlanner
    {
        public static DungeonTopologyPlan Create(int roomCount, DungeonPatternProfile reference, int seed)
        {
            if (roomCount < 1) throw new InvalidOperationException("A topology needs at least one room.");
            string archetype = Classify(reference);
            DungeonTopologyPlan result = new DungeonTopologyPlan { Archetype = archetype, Seed = seed };
            for (int i = 0; i < roomCount; i++)
            {
                string role = i == 0 ? "Start" : i == roomCount - 1 ? "Boss" : (i % 3 == 0 ? "Reward" : "Puzzle");
                result.Nodes.Add(new DungeonTopologyNode { Index = i, Depth = i, Branch = 0, Role = role });
            }
            for (int i = 1; i < roomCount; i++) result.Edges.Add(new DungeonTopologyEdge { From = i - 1, To = i, Purpose = "Progression" });
            if (archetype == "Branching" && roomCount > 3)
            {
                int branch = Math.Max(1, roomCount / 3);
                result.Nodes[branch].Role = "Hub";
                result.Nodes[Math.Min(roomCount - 2, branch + 1)].Role = "Reward";
                result.Nodes[Math.Min(roomCount - 2, branch + 1)].Branch = 1;
                result.Edges.Add(new DungeonTopologyEdge { From = branch, To = Math.Min(roomCount - 1, branch + 2), Purpose = "OptionalBranch" });
            }
            else if (archetype == "HubLoop" && roomCount > 3)
            {
                result.Nodes[1].Role = "Hub";
                result.Edges.Add(new DungeonTopologyEdge { From = roomCount - 1, To = 1, Purpose = "ReturnLoop" });
            }
            if (reference == null) result.Warnings.Add("No decoded OoT pattern was available; using the neutral topology archetype.");
            return result;
        }

        private static string Classify(DungeonPatternProfile reference)
        {
            if (reference == null) return "Linear";
            if (reference.BranchingScore >= 2.0) return "Branching";
            if (reference.BranchingScore > 1.1 || reference.ConnectionCount >= reference.RoomCount) return "HubLoop";
            return "Linear";
        }
    }

    [Serializable]
    public sealed class DungeonProgressionStep
    {
        public int Order, RoomIndex;
        public string Action, SourceId, TargetId, Requirement;
    }

    [Serializable]
    public sealed class DungeonProgressionReport
    {
        public bool Solvable;
        public int StartRoom = -1;
        public int SmallKeysSpent, SmallKeysRemaining;
        public bool BossKeyAcquired;
        public List<int> ReachableRooms = new List<int>();
        public List<int> UnreachableRooms = new List<int>();
        public List<string> CollectedItems = new List<string>();
        public Dictionary<int, List<string>> ItemsAtRoom = new Dictionary<int, List<string>>();
        public List<string> MissingItems = new List<string>();
        public List<DungeonProgressionStep> Steps = new List<DungeonProgressionStep>();
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    public static class DungeonProgressionSolver
    {
        private sealed class State
        {
            internal int Room, SmallKeys, Spent;
            internal bool BossKey;
            internal HashSet<string> Items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal HashSet<int> CollectedRooms = new HashSet<int>();
        }

        public static DungeonProgressionReport Solve(DungeonAnalysisReport analysis, ProceduralDungeonPlan plan)
        {
            if (plan == null) throw new InvalidOperationException("A generated dungeon plan is required.");
            DungeonProgressionReport result = new DungeonProgressionReport();
            result.SmallKeysRemaining = int.MaxValue;
            if (plan.Rooms.Count == 0)
            {
                result.Errors.Add("The generated dungeon contains no rooms.");
                return result;
            }

            result.StartRoom = plan.Rooms.FindIndex(r => r.Actors.Any(a => a.Role == "Spawn"));
            if (result.StartRoom < 0)
            {
                result.StartRoom = 0;
                result.Warnings.Add("No explicit player spawn was copied; room 0 is being treated as the start.");
            }

            Dictionary<int, List<DungeonConnectionPlan>> graph = new Dictionary<int, List<DungeonConnectionPlan>>();
            for (int i = 0; i < plan.Rooms.Count; i++) graph[i] = new List<DungeonConnectionPlan>();
            foreach (DungeonConnectionPlan connection in plan.Connections)
            {
                if (!graph.ContainsKey(connection.FromRoom) || !graph.ContainsKey(connection.ToRoom))
                {
                    result.Errors.Add("Connection references a room outside the generated plan.");
                    continue;
                }
                graph[connection.FromRoom].Add(connection);
                if (connection.Bidirectional) graph[connection.ToRoom].Add(new DungeonConnectionPlan
                {
                    FromRoom = connection.ToRoom,
                    ToRoom = connection.FromRoom,
                    RequiredItems = connection.RequiredItems,
                    Bidirectional = false
                });
            }

            HashSet<string> visitedStates = new HashSet<string>(StringComparer.Ordinal);
            Queue<State> queue = new Queue<State>();
            queue.Enqueue(new State { Room = result.StartRoom, Items = new HashSet<string>(plan.StartingItems ?? new List<string>(), StringComparer.OrdinalIgnoreCase) });
            HashSet<int> visited = new HashSet<int>();
            while (queue.Count > 0)
            {
                State state = queue.Dequeue();
                int roomIndex = state.Room;
                DungeonRoomPlan room = plan.Rooms[roomIndex];
                if (!room.RequiredItems.All(item => state.Items.Contains(NormalizeReward(item))))
                {
                    if (!visited.Contains(roomIndex))
                        foreach (string item in room.RequiredItems.Where(item => !state.Items.Contains(NormalizeReward(item))))
                            if (!result.MissingItems.Contains(NormalizeReward(item), StringComparer.OrdinalIgnoreCase)) result.MissingItems.Add(NormalizeReward(item));
                    continue;
                }
                string stateKey = roomIndex + "|" + state.SmallKeys + "|" + state.BossKey + "|" + string.Join(",", state.Items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()) + "|" + string.Join(",", state.CollectedRooms.OrderBy(x => x).ToArray());
                if (!visitedStates.Add(stateKey)) continue;
                if (room.RequiresBossKey && !state.BossKey)
                {
                    if (!visited.Contains(roomIndex) && !result.MissingItems.Contains("Boss Key", StringComparer.OrdinalIgnoreCase)) result.MissingItems.Add("Boss Key");
                    continue;
                }
                if (state.SmallKeys < room.SmallKeyCost)
                {
                    if (!visited.Contains(roomIndex) && !result.MissingItems.Contains("Small Key", StringComparer.OrdinalIgnoreCase)) result.MissingItems.Add("Small Key");
                    continue;
                }
                visited.Add(roomIndex);
                if (!result.ReachableRooms.Contains(roomIndex)) result.ReachableRooms.Add(roomIndex);
                State nextState = new State { Room = roomIndex, SmallKeys = state.SmallKeys - room.SmallKeyCost, Spent = state.Spent + room.SmallKeyCost, BossKey = state.BossKey,
                    Items = new HashSet<string>(state.Items, StringComparer.OrdinalIgnoreCase), CollectedRooms = new HashSet<int>(state.CollectedRooms) };
                if (!nextState.CollectedRooms.Contains(roomIndex))
                {
                    nextState.CollectedRooms.Add(roomIndex);
                    foreach (string granted in room.GrantedItems ?? new List<string>())
                    {
                        string reward = NormalizeReward(granted);
                        if (IsSmallKey(reward)) nextState.SmallKeys++;
                        else if (IsBossKey(reward)) nextState.BossKey = true;
                        else nextState.Items.Add(reward);
                        if (!result.CollectedItems.Contains(reward, StringComparer.OrdinalIgnoreCase)) result.CollectedItems.Add(reward);
                    }
                }
                List<string> itemsAtRoom;
                if (!result.ItemsAtRoom.TryGetValue(roomIndex, out itemsAtRoom))
                {
                    itemsAtRoom = new List<string>();
                    result.ItemsAtRoom.Add(roomIndex, itemsAtRoom);
                }
                foreach (string item in nextState.Items)
                    if (!itemsAtRoom.Contains(item, StringComparer.OrdinalIgnoreCase)) itemsAtRoom.Add(item);
                result.SmallKeysSpent = Math.Max(result.SmallKeysSpent, nextState.Spent);
                result.SmallKeysRemaining = Math.Min(result.SmallKeysRemaining, nextState.SmallKeys);
                result.BossKeyAcquired |= nextState.BossKey;
                foreach (DungeonConnectionPlan edge in graph[roomIndex])
                {
                    if (!edge.RequiredItems.All(item => nextState.Items.Contains(NormalizeReward(item))))
                    {
                        if (!visited.Contains(edge.ToRoom))
                            foreach (string item in edge.RequiredItems.Where(item => !nextState.Items.Contains(NormalizeReward(item))))
                                if (!result.MissingItems.Contains(NormalizeReward(item), StringComparer.OrdinalIgnoreCase)) result.MissingItems.Add(NormalizeReward(item));
                        continue;
                    }
                    queue.Enqueue(new State { Room = edge.ToRoom, SmallKeys = nextState.SmallKeys, Spent = nextState.Spent,
                        BossKey = nextState.BossKey, Items = new HashSet<string>(nextState.Items, StringComparer.OrdinalIgnoreCase),
                        CollectedRooms = new HashSet<int>(nextState.CollectedRooms) });
                }
            }
            for (int i = 0; i < plan.Rooms.Count; i++) if (!visited.Contains(i)) result.UnreachableRooms.Add(i);
            if (result.UnreachableRooms.Count > 0)
                result.Errors.Add("The generated dungeon contains unreachable or item-locked room(s): " + string.Join(",", result.UnreachableRooms.ToArray()) + ".");
            foreach (string item in result.MissingItems)
                result.Errors.Add("No reachable progression state supplies required item: " + item + ".");

            int order = 0;
            foreach (int roomIndex in result.ReachableRooms)
            {
                result.Steps.Add(new DungeonProgressionStep { Order = order++, RoomIndex = roomIndex, Action = "EnterRoom" });
                DungeonRoomPlan room = plan.Rooms[roomIndex];
                foreach (DungeonActorPlacement actor in room.Actors.Where(a => a.Role == "Chests" || a.Role == "Bosses" || a.Role == "Enemies"))
                    result.Steps.Add(new DungeonProgressionStep { Order = order++, RoomIndex = roomIndex, Action = actor.Role == "Bosses" ? "DefeatBoss" : "ResolveEncounter", SourceId = actor.SourceId });
            }

            if (analysis != null)
                foreach (DungeonLogicCandidate candidate in analysis.LogicCandidates)
                {
                    int sourceRoom = FindPlanRoom(plan, candidate.SceneId, candidate.SourceRoom, candidate.SourceId);
                    int targetRoom = FindPlanRoom(plan, candidate.SceneId, candidate.TargetRoom, candidate.TargetId);
                    if (sourceRoom < 0 || targetRoom < 0)
                    {
                        result.Warnings.Add("Logic candidate was not copied into the generated plan: " + candidate.SourceId + " -> " + candidate.TargetId + ".");
                        continue;
                    }
                    if (!visited.Contains(sourceRoom) || !visited.Contains(targetRoom))
                    {
                        result.Errors.Add("Logic candidate crosses unreachable content: " + candidate.SourceId + " -> " + candidate.TargetId + ".");
                        continue;
                    }
                    result.Steps.Add(new DungeonProgressionStep
                    {
                        Order = order++, RoomIndex = targetRoom, Action = candidate.Kind,
                        SourceId = candidate.SourceId, TargetId = candidate.TargetId, Requirement = candidate.Parameter
                    });
                }

            result.Solvable = result.Errors.Count == 0 && result.ReachableRooms.Count == plan.Rooms.Count;
            if (!result.Solvable && result.Errors.Count == 0) result.Errors.Add("The progression graph could not be certified.");
            if (result.SmallKeysRemaining == int.MaxValue) result.SmallKeysRemaining = 0;
            return result;
        }

        private static string NormalizeReward(string reward)
        {
            string value = (reward ?? "").Trim();
            if (value.StartsWith("DUNGEON ", StringComparison.OrdinalIgnoreCase)) value = value.Substring("DUNGEON ".Length).Trim();
            return value;
        }
        private static bool IsSmallKey(string reward) { return reward.IndexOf("small key", StringComparison.OrdinalIgnoreCase) >= 0; }
        private static bool IsBossKey(string reward) { return reward.IndexOf("boss key", StringComparison.OrdinalIgnoreCase) >= 0; }

        private static int FindPlanRoom(ProceduralDungeonPlan plan, int scene, int room, string sourceId)
        {
            for (int i = 0; i < plan.Rooms.Count; i++)
            {
                DungeonRoomPlan candidate = plan.Rooms[i];
                if (candidate.SourceScene != scene || candidate.SourceRoom != room) continue;
                if (candidate.Actors.Any(a => a.SourceId == sourceId)) return i;
            }
            return -1;
        }
    }

    public static class DungeonNativeAssembler
    {
        public static DungeonNativeAssemblyReport Prepare(DungeonAnalysisReport analysis, ProceduralDungeonPlan plan)
        {
            DungeonNativeAssemblyReport result = new DungeonNativeAssemblyReport();
            if (plan == null) { result.Warnings.Add("A generated plan is required."); return result; }
            Dictionary<string, string> parent = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> kinds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DungeonRoomPlan room in plan.Rooms)
                foreach (DungeonActorPlacement actor in room.Actors)
                    foreach (DungeonFlagReference flag in actor.Flags)
                    {
                        string key = FlagKey(room.Index, flag);
                        parent[key] = key;
                        kinds[key] = flag.Kind;
                    }

            if (analysis != null)
                foreach (DungeonLogicCandidate candidate in analysis.LogicCandidates)
                {
                    int sourceRoom; DungeonActorPlacement source;
                    int targetRoom; DungeonActorPlacement target;
                    if (!FindPlacement(plan, candidate.SceneId, candidate.SourceRoom, candidate.SourceId, out sourceRoom, out source) ||
                        !FindPlacement(plan, candidate.SceneId, candidate.TargetRoom, candidate.TargetId, out targetRoom, out target))
                        continue;
                    DungeonFlagReference sourceFlag = source.Flags.FirstOrDefault(f => f.Kind + ":" + f.Value == candidate.Parameter);
                    DungeonFlagReference targetFlag = target.Flags.FirstOrDefault(f => f.Kind + ":" + f.Value == candidate.Parameter);
                    if (sourceFlag == null || targetFlag == null) continue;
                    Union(parent, FlagKey(sourceRoom, sourceFlag), FlagKey(targetRoom, targetFlag));
                }

            Dictionary<string, int> nextByKind = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> generatedByRoot = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, DungeonFlagRemap> remaps = new Dictionary<string, DungeonFlagRemap>(StringComparer.Ordinal);
            foreach (DungeonRoomPlan room in plan.Rooms)
                foreach (DungeonActorPlacement actor in room.Actors)
                    foreach (DungeonFlagReference flag in actor.Flags)
                    {
                        string key = FlagKey(room.Index, flag), root = Find(parent, key), kind = flag.Kind ?? "Unknown";
                        int generated;
                        if (!generatedByRoot.TryGetValue(root, out generated))
                        {
                            if (!nextByKind.ContainsKey(kind)) nextByKind.Add(kind, 0);
                            generated = nextByKind[kind]++;
                            int capacity = flag.Mask == 0 ? 0 : flag.Mask >> flag.Position;
                            if (capacity > 0 && generated > capacity)
                            {
                                result.Warnings.Add("No free " + kind + " flag value remains for generated group " + root + ".");
                                generated = flag.Value;
                            }
                            generatedByRoot.Add(root, generated);
                            remaps.Add(root, new DungeonFlagRemap { Group = root, Kind = kind, SourceValue = flag.Value, GeneratedValue = generated });
                        }
                        DungeonFlagRemap remap = remaps[root];
                        if (!remap.RoomIndices.Contains(room.Index)) remap.RoomIndices.Add(room.Index);
                        ApplyFlag(actor, flag, generated);
                        result.RemappedReferences++;
                    }
            result.FlagRemaps.AddRange(remaps.Values);
            result.Ready = result.Warnings.Count == 0;
            return result;
        }

        private static bool FindPlacement(ProceduralDungeonPlan plan, int scene, int room, string sourceId, out int roomIndex, out DungeonActorPlacement placement)
        {
            for (int i = 0; i < plan.Rooms.Count; i++)
            {
                DungeonRoomPlan candidate = plan.Rooms[i];
                if (candidate.SourceScene != scene || candidate.SourceRoom != room) continue;
                placement = candidate.Actors.FirstOrDefault(a => a.SourceId == sourceId);
                if (placement != null) { roomIndex = i; return true; }
            }
            roomIndex = -1; placement = null; return false;
        }

        private static string FlagKey(int roomIndex, DungeonFlagReference flag)
        {
            return roomIndex + "|" + flag.Kind + "|" + flag.Value;
        }

        private static string Find(Dictionary<string, string> parent, string value)
        {
            string root = value, next;
            while (parent.TryGetValue(root, out next) && next != root) root = next;
            string cursor = value;
            while (parent.TryGetValue(cursor, out next) && next != cursor) { parent[cursor] = root; cursor = next; }
            return root;
        }

        private static void Union(Dictionary<string, string> parent, string left, string right)
        {
            if (!parent.ContainsKey(left) || !parent.ContainsKey(right)) return;
            string a = Find(parent, left), b = Find(parent, right);
            if (a != b) parent[b] = a;
        }

        private static void ApplyFlag(DungeonActorPlacement actor, DungeonFlagReference flag, int value)
        {
            ushort encoded = (ushort)((value << flag.Position) & flag.Mask);
            if (string.Equals(flag.Target, "Var", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(flag.Target))
                actor.Variable = (ushort)((actor.Variable & ~flag.Mask) | encoded);
            else if (string.Equals(flag.Target, "XRot", StringComparison.OrdinalIgnoreCase)) actor.Rotation.X = value;
            else if (string.Equals(flag.Target, "YRot", StringComparison.OrdinalIgnoreCase)) actor.Rotation.Y = value;
            else if (string.Equals(flag.Target, "ZRot", StringComparison.OrdinalIgnoreCase)) actor.Rotation.Z = value;
        }
    }

    public static class ProceduralDungeonAnalyzer
    {
        public static DungeonAnalysisReport AnalyzeScenes(IEnumerable<ZScene> scenes, RomAssetCatalog catalog)
        {
            if (scenes == null) throw new InvalidOperationException("Decoded scenes are required for ROM analysis.");
            DungeonAnalysisReport aggregate = new DungeonAnalysisReport
            {
                RomFingerprint = catalog == null || catalog.Rooms == null ? null : catalog.Rooms.RomFingerprint
            };
            foreach (ZScene scene in scenes)
            {
                if (scene == null) continue;
                DungeonAnalysisReport report = AnalyzeScene(scene, catalog);
                aggregate.SceneCount++;
                aggregate.AnalyzedScenes.Add(scene.SceneNumber);
                aggregate.Rooms.AddRange(report.Rooms);
                aggregate.Connections.AddRange(report.Connections);
                aggregate.Diagnostics.AddRange(report.Diagnostics);
                aggregate.LogicCandidates.AddRange(report.LogicCandidates);
                aggregate.Traversal.HookshotSurfaceCount += report.Traversal.HookshotSurfaceCount;
                aggregate.Traversal.ClimbableSurfaceCount += report.Traversal.ClimbableSurfaceCount;
                aggregate.Traversal.SteepSurfaceCount += report.Traversal.SteepSurfaceCount;
                aggregate.Traversal.EponaBlockSurfaceCount += report.Traversal.EponaBlockSurfaceCount;
                aggregate.Traversal.JumpGapCount += report.Traversal.JumpGapCount;
                aggregate.Traversal.VineRouteCount += report.Traversal.VineRouteCount;
                aggregate.Traversal.HookshotAnchorCount += report.Traversal.HookshotAnchorCount;
                aggregate.Traversal.PushBlockCount += report.Traversal.PushBlockCount;
                aggregate.Traversal.StairCount += report.Traversal.StairCount;
                aggregate.Traversal.FallDropCount += report.Traversal.FallDropCount;
                aggregate.Traversal.Evidence.AddRange(report.Traversal.Evidence);
                aggregate.Traversal.Constraints.AddRange(report.Traversal.Constraints);
                foreach (int room in report.ReachableRooms)
                {
                    aggregate.ReachableRooms.Add(room);
                    aggregate.ReachableRoomKeys.Add(scene.SceneNumber + ":" + room);
                }
                foreach (int room in report.UnreachableRooms)
                {
                    aggregate.UnreachableRooms.Add(room);
                    aggregate.UnreachableRoomKeys.Add(scene.SceneNumber + ":" + room);
                }
            }
            return aggregate;
        }

        public static DungeonPatternReport AnalyzeDungeonPatterns(IEnumerable<ZScene> scenes, RomAssetCatalog catalog)
        {
            if (scenes == null) throw new InvalidOperationException("Decoded scenes are required for dungeon pattern analysis.");
            DungeonPatternReport result = new DungeonPatternReport
            {
                RomFingerprint = catalog == null || catalog.Rooms == null ? null : catalog.Rooms.RomFingerprint
            };
            Dictionary<ushort, RomPaletteAsset> objects = catalog == null ? new Dictionary<ushort, RomPaletteAsset>() :
                catalog.Assets.Where(a => a.Category == "Object banks").GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (ZScene scene in scenes.Where(s => s != null).OrderBy(s => s.SceneNumber))
            {
                if (scene.SceneNumber > 0x0D) continue;
                DungeonAnalysisReport analysis = AnalyzeScene(scene, catalog);
                DungeonPatternProfile profile = new DungeonPatternProfile
                {
                    SceneId = scene.SceneNumber,
                    Name = DungeonName(scene.SceneNumber),
                    RoomCount = analysis.Rooms.Count,
                    ConnectionCount = analysis.Connections.Count(c => !c.IsExternal),
                    ReachableRoomCount = analysis.ReachableRooms.Count,
                    GateCount = analysis.LogicCandidates.Count,
                    RewardCount = analysis.Rooms.Sum(r => r.GrantedItems.Count),
                    BossRoomCount = analysis.Rooms.Count(r => (r.Features & DungeonRoomFeatures.Boss) != 0),
                    GeometryScore = analysis.Rooms.Count == 0 ? 0 : analysis.Rooms.Average(r => r.GeometryScore),
                    DeadEndCount = analysis.Rooms.Count(r => r.Role == "DeadEnd"),
                    HubCount = analysis.Rooms.Count(r => r.Role == "Hub"),
                    EnemyCount = analysis.Rooms.Sum(r => r.EnemyCount),
                    ChestCount = analysis.Rooms.Sum(r => r.ChestCount),
                    JumpGapCount = analysis.Traversal.JumpGapCount,
                    VineRouteCount = analysis.Traversal.VineRouteCount,
                    HookshotAnchorCount = analysis.Traversal.HookshotAnchorCount,
                    PushBlockCount = analysis.Traversal.PushBlockCount,
                    StairCount = analysis.Traversal.StairCount,
                    FallDropCount = analysis.Traversal.FallDropCount,
                    LockedDoorCount = analysis.Rooms.Sum(r => r.SmallKeyCost),
                    AverageRewardDepth = analysis.Rooms.Where(r => r.GrantedItems.Count > 0 && r.TraversalDepth >= 0).Select(r => (double)r.TraversalDepth).DefaultIfEmpty(0).Average()
                };
                foreach (DungeonRoomAnalysis room in analysis.Rooms)
                {
                    profile.RoomOwnership.Add(string.Format("Scene {0:X2} Room {1:D2} | visual={2} collision={3} objects={4}",
                        scene.SceneNumber, room.RoomId, room.VisualTriangles, room.CollisionTriangles,
                        scene.Rooms[room.RoomId].ZObjects == null ? "" : string.Join(",", scene.Rooms[room.RoomId].ZObjects.Select(o => o.Value.ToString("X4")).ToArray())));
                    foreach (string item in room.GrantedItems) if (!profile.RewardItems.Contains(item, StringComparer.OrdinalIgnoreCase)) profile.RewardItems.Add(item);
                    foreach (DungeonActorProfile actor in room.Actors.Where(a => !a.IsSpawn))
                    {
                        foreach (string item in actor.GrantedItems)
                        {
                            string reward = ClassifyReward(item);
                            if (!profile.RewardCounts.ContainsKey(reward)) profile.RewardCounts.Add(reward, 0);
                            profile.RewardCounts[reward]++;
                            profile.RewardOwnership.Add(string.Format("Scene {0:X2} Room {1:D2} {2} -> {3}", scene.SceneNumber, room.RoomId, actor.Name, reward));
                            if (reward == "SmallKey") profile.SmallKeyCount++;
                            else if (reward == "BossKey") profile.BossKeyCount++;
                            else if (reward == "Map") profile.MapCount++;
                            else if (reward == "Compass") profile.CompassCount++;
                        }
                        if (actor.Category == "Chests")
                        {
                            foreach (DungeonFlagReference flag in actor.Flags.Where(f => f.Kind == "Chest"))
                                profile.ChestFlagOwnership.Add(string.Format("Scene {0:X2} Room {1:D2} {2} -> Chest:{3}", scene.SceneNumber, room.RoomId, actor.Name, flag.Value));
                        }
                        if (actor.Category == "Doors")
                        {
                            if (actor.Flags.Any(f => f.Kind == "Switch")) profile.SwitchGateCount++;
                        }
                    }
                    if (!profile.RoomRoles.ContainsKey(room.Role ?? "Unknown")) profile.RoomRoles.Add(room.Role ?? "Unknown", 0);
                    profile.RoomRoles[room.Role ?? "Unknown"]++;
                    foreach (DungeonActorProfile actor in room.Actors.Where(a => !a.IsSpawn))
                    {
                        string category = string.IsNullOrEmpty(actor.Category) ? "Unknown" : actor.Category;
                        if (!profile.ActorCategories.ContainsKey(category)) profile.ActorCategories.Add(category, 0);
                        profile.ActorCategories[category]++;
                    }
                    foreach (ZScene.ZUShort obj in scene.Rooms[room.RoomId].ZObjects)
                    {
                        RomPaletteAsset asset;
                        if (objects.TryGetValue(obj.Value, out asset)) profile.ObjectBanks.Add(asset.Name);
                        else profile.ObjectBanks.Add("Object " + obj.Value.ToString("X4"));
                    }
                }
                foreach (ZScene.ZUShort obj in scene.ZObjects)
                {
                    RomPaletteAsset asset;
                    profile.ObjectBanks.Add(objects.TryGetValue(obj.Value, out asset) ? asset.Name : "Object " + obj.Value.ToString("X4"));
                }
                profile.ObjectBanks = profile.ObjectBanks.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                double chain = Math.Max(1, profile.RoomCount - 1);
                profile.LinearProgressionScore = Math.Min(1.0, profile.ConnectionCount / chain);
                profile.BranchingScore = profile.RoomCount == 0 ? 0 : (double)(profile.ConnectionCount * 2) / profile.RoomCount;
                profile.EnemyPacingScore = profile.RoomCount == 0 ? 0 : (double)profile.EnemyCount / profile.RoomCount;
                foreach (DungeonRoomAnalysis room in analysis.Rooms.OrderBy(r => r.TraversalDepth < 0 ? int.MaxValue : r.TraversalDepth).ThenBy(r => r.RoomId))
                {
                    DungeonProgressionMilestone milestone = new DungeonProgressionMilestone
                    {
                        Depth = room.TraversalDepth,
                        RoomId = room.RoomId,
                        Role = room.Role,
                        GateCount = analysis.LogicCandidates.Count(c => c.TargetRoom == room.RoomId),
                        EnemyCount = room.EnemyCount,
                        ChestCount = room.ChestCount
                    };
                    foreach (DungeonActorProfile actor in room.Actors)
                        foreach (string reward in actor.GrantedItems)
                            milestone.Rewards.Add(ClassifyReward(reward));
                    profile.ProgressionChain.Add(milestone);
                }
                DungeonProgressionMilestone boss = profile.ProgressionChain.FirstOrDefault(m => m.Role == "Boss");
                profile.BossDepth = boss == null ? -1 : boss.Depth;
                List<int> rewardDepths = profile.ProgressionChain.Where(m => m.Rewards.Count > 0 && m.Depth >= 0).Select(m => m.Depth).ToList();
                profile.RewardSpacingScore = rewardDepths.Count < 2 ? rewardDepths.Count : 1.0 / (1.0 + rewardDepths.Zip(rewardDepths.Skip(1), (a, b) => b - a).Average());
                profile.GateRewardBalance = profile.RewardCount == 0 ? profile.GateCount : (double)profile.GateCount / profile.RewardCount;
                if (profile.ReachableRoomCount < profile.RoomCount) profile.Diagnostics.Add("One or more rooms are not reachable from a player spawn.");
                if (profile.BossRoomCount == 0) profile.Diagnostics.Add("No boss actor was identified by the current actor metadata.");
                if (profile.RewardCount == 0) profile.Diagnostics.Add("No item rewards were identified from decoded actor metadata.");
                result.Dungeons.Add(profile);
            }
            BuildPatternComparison(result);
            if (result.Dungeons.Count == 0) result.Diagnostics.Add("No scenes in the supported dungeon scene range were decoded.");
            return result;
        }

        private static void BuildPatternComparison(DungeonPatternReport report)
        {
            if (report.Dungeons.Count == 0) return;
            report.Comparison.AverageRoomCount = report.Dungeons.Average(d => d.RoomCount);
            report.Comparison.AverageRewardSpacing = report.Dungeons.Average(d => d.RewardSpacingScore);
            report.Comparison.AverageGateRewardBalance = report.Dungeons.Average(d => d.GateRewardBalance);
            if (report.Dungeons.Count(d => d.ProgressionChain.Any(m => m.Role == "Reward")) == report.Dungeons.Count)
                report.Comparison.SharedPatterns.Add("Every analyzed dungeon contains a detected reward room.");
            if (report.Dungeons.Count(d => d.BossDepth >= 0) >= Math.Max(1, report.Dungeons.Count / 2))
                report.Comparison.SharedPatterns.Add("Boss rooms are usually identifiable from actor metadata.");
            foreach (DungeonPatternProfile dungeon in report.Dungeons)
            {
                if (dungeon.RewardSpacingScore < report.Comparison.AverageRewardSpacing * 0.5)
                    report.Comparison.Outliers.Add(dungeon.Name + " has unusually sparse reward spacing.");
                if (dungeon.GateRewardBalance > report.Comparison.AverageGateRewardBalance * 2.0 && dungeon.GateCount > 0)
                    report.Comparison.Outliers.Add(dungeon.Name + " has unusually high gate density relative to rewards.");
            }
        }

        private static string DungeonName(int scene)
        {
            string[] names = { "Deku Tree", "Dodongo's Cavern", "Jabu-Jabu's Belly", "Forest Temple", "Fire Temple",
                "Water Temple", "Spirit Temple", "Shadow Temple", "Bottom of the Well", "Ice Cavern",
                "Gerudo Training Ground", "Ganon's Castle", "Treasure Chest Game", "Inside Ganon's Castle" };
            return scene >= 0 && scene < names.Length ? names[scene] : "Dungeon Scene " + scene.ToString("X2");
        }

        private static string ClassifyReward(string item)
        {
            string value = (item ?? "").ToLowerInvariant();
            if (value.Contains("small key")) return "SmallKey";
            if (value.Contains("boss key")) return "BossKey";
            if (value.Contains("compass")) return "Compass";
            if (value.Contains("map")) return "Map";
            if (value.Contains("dungeon")) return "DungeonReward";
            return item;
        }

        public static DungeonAnalysisReport AnalyzeScene(ZScene scene, RomAssetCatalog catalog)
        {
            if (scene == null) throw new InvalidOperationException("A decoded scene is required for dungeon analysis.");
            DungeonAnalysisReport report = new DungeonAnalysisReport();
            report.RomFingerprint = catalog == null || catalog.Rooms == null ? null : catalog.Rooms.RomFingerprint;
            Dictionary<ushort, RomPaletteAsset> assets = BuildAssetLookup(catalog);

            for (int roomId = 0; roomId < scene.Rooms.Count; roomId++)
            {
                ZScene.ZRoom room = scene.Rooms[roomId];
                DungeonRoomAnalysis analysis = AnalyzeRoom(scene, room, roomId, assets);
                report.Rooms.Add(analysis);
            }

            foreach (ZActor transition in scene.Transitions.Select((x, i) => x))
            {
                int index = scene.Transitions.IndexOf(transition);
                RomPaletteAsset transitionAsset;
                assets.TryGetValue(transition.Number, out transitionAsset);
                List<string> transitionRequirements = ReadRequiredItems(transitionAsset, transition);
                string transitionText = transitionAsset == null ? "" : ((transitionAsset.Name ?? "") + " " + (transitionAsset.RequirementText ?? ""));
                bool oneWay = IsExplicitOneWay(transitionText);
                DungeonConnectionAnalysis connection = new DungeonConnectionAnalysis
                {
                    SceneId = scene.SceneNumber,
                    TransitionIndex = index,
                    FrontRoom = transition.FrontSwitchTo,
                    BackRoom = transition.BackSwitchTo,
                    ActorNumber = transition.Number,
                    Variable = transition.Variable,
                    Position = new Vector3d(transition.XPos, transition.YPos, transition.ZPos),
                    Facing = transition.YRot * 360.0 / 65536.0,
                    IsOneWay = oneWay,
                    TraversalMode = transition.FrontSwitchTo == 0xFF || transition.BackSwitchTo == 0xFF ? "External" : (oneWay ? "OneWay" : "Bidirectional"),
                    Evidence = transitionAsset == null ? "No transition metadata" : transitionText.Trim(),
                    RequiredItems = transitionRequirements
                };
                report.Connections.Add(connection);
                DungeonRoomAnalysis front = report.FindRoom(scene.SceneNumber, connection.FrontRoom);
                DungeonRoomAnalysis back = report.FindRoom(scene.SceneNumber, connection.BackRoom);
                if (front != null) front.Connections.Add(connection);
                if (back != null && !ReferenceEquals(back, front)) back.Connections.Add(connection);
            }

            CalculateReachability(scene, report);
            AnalyzeLogic(scene, report, assets);
            AnalyzeRoomSemantics(scene, report);
            AnalyzeTraversal(scene, report, assets);
            return report;
        }

        private static void AnalyzeTraversal(ZScene scene, DungeonAnalysisReport report, IDictionary<ushort, RomPaletteAsset> assets)
        {
            if (scene.PolyTypes != null)
            {
                foreach (ZColPolyType surface in scene.PolyTypes)
                {
                    if (surface == null) continue;
                    if (surface.IsHookshotable) report.Traversal.HookshotSurfaceCount++;
                    if (surface.ClimbingCrawlingFlags != 0) report.Traversal.ClimbableSurfaceCount++;
                    if (surface.IsSteep) report.Traversal.SteepSurfaceCount++;
                    if (surface.EponaBlock) report.Traversal.EponaBlockSurfaceCount++;
                }
                if (report.Traversal.HookshotSurfaceCount > 0)
                    report.Traversal.Evidence.Add("Native collision surface types include Hookshotable polygons; verify their room association before reuse.");
                if (report.Traversal.ClimbableSurfaceCount > 0)
                    report.Traversal.Evidence.Add("Native collision surface types include climbing/crawling flags; treat these as climb-route evidence, not decoration.");
                if (report.Traversal.EponaBlockSurfaceCount > 0)
                    report.Traversal.Evidence.Add("Native collision surface types include Epona-blocking polygons; preserve this flag when remapping collision.");
            }

            foreach (DungeonRoomAnalysis room in report.Rooms)
            {
                ZScene.ZRoom source = room.RoomId >= 0 && room.RoomId < scene.Rooms.Count ? scene.Rooms[room.RoomId] : null;
                IEnumerable<string> names = source == null || source.TrueGroups == null
                    ? Enumerable.Empty<string>()
                    : source.TrueGroups.Select(g => g == null ? "" : g.Name ?? "");
                if (source != null && source.TrueGroups != null)
                    foreach (ObjFile.Group group in source.TrueGroups)
                    {
                        Vector3d min, max;
                        GetGroupBounds(source.ObjModel, group, out min, out max);
                        AddTraversalEvidence(room, report.Traversal, group == null ? "" : group.Name ?? "", "room geometry group", min, max);
                    }
                foreach (DungeonActorProfile actor in room.Actors)
                {
                    string text = (actor.Name ?? "") + " " + (actor.Category ?? "");
                    AddTraversalEvidence(room, report.Traversal, text, "actor metadata");
                }
                room.GeometryScore = ScoreGeometry(room);
            }
        }

        private static void AddTraversalEvidence(DungeonRoomAnalysis room, DungeonTraversalAnalysis report, string raw, string source)
        {
            AddTraversalEvidence(room, report, raw, source, room.BoundsMin, room.BoundsMax);
        }

        private static void AddTraversalEvidence(DungeonRoomAnalysis room, DungeonTraversalAnalysis report, string raw, string source, Vector3d min, Vector3d max)
        {
            string text = raw ?? "";
            string value = text.ToLowerInvariant();
            if (value.Contains("vine") || value.Contains("climb") || value.Contains("crawl"))
            {
                room.TraversalCapabilities |= DungeonTraversalCapabilities.ClimbVines;
                room.VineRouteCount++;
                report.VineRouteCount++;
                report.Evidence.Add(room.Name + ": climb/vine evidence from " + source + " ('" + text + "').");
                EnsureConstraint(room, "VineClimb", "Vines/climbable surface evidence", null, 800, 0.8, min, max);
            }
            if (value.Contains("hookshot") || value.Contains("hook shot") || value.Contains("target") || value.Contains("eye switch"))
            {
                room.TraversalCapabilities |= DungeonTraversalCapabilities.Hookshot;
                room.HookshotAnchorCount++;
                report.HookshotAnchorCount++;
                report.Evidence.Add(room.Name + ": Hookshot anchor evidence from " + source + " ('" + text + "').");
                EnsureConstraint(room, "Hookshot", "Hookshot anchor evidence", "Hookshot", 500, 0.8, min, max);
            }
            if (value.Contains("push") || value.Contains("block") || value.Contains("movable") || value.Contains("ice block"))
            {
                room.TraversalCapabilities |= DungeonTraversalCapabilities.PushBlock;
                room.PushBlockCount++;
                report.PushBlockCount++;
                report.Evidence.Add(room.Name + ": movable-block evidence from " + source + " ('" + text + "').");
                string required = value.Contains("silver") || value.Contains("golden") || value.Contains("gauntlet") ? "Strength" : null;
                EnsureConstraint(room, "PushBlock", "Movable-block evidence", required, 400, 0.75, min, max);
            }
            if (value.Contains("stair") || value.Contains("step") || value.Contains("ramp"))
            {
                room.TraversalCapabilities |= DungeonTraversalCapabilities.Stairs;
                room.StairCount++;
                report.StairCount++;
            }
            if (value.Contains("gap") || value.Contains("jump") || value.Contains("platform"))
            {
                room.TraversalCapabilities |= DungeonTraversalCapabilities.JumpGap;
                room.JumpGapCount++;
                report.JumpGapCount++;
                EnsureConstraint(room, "JumpGap", "Jump/platform evidence", null, 180, 0.7, min, max);
            }
            if (value.Contains("fall") || value.Contains("drop") || value.Contains("shaft"))
            {
                room.TraversalCapabilities |= DungeonTraversalCapabilities.FallDrop;
                room.FallDropCount++;
                report.FallDropCount++;
                EnsureConstraint(room, "FallDrop", "Fall/drop evidence", null, 300, 0.75, min, max);
            }
        }

        private static void EnsureConstraint(DungeonRoomAnalysis room, string kind, string evidence, string requiredItem, double maxDistance, double confidence, Vector3d min, Vector3d max)
        {
            if (room.TraversalConstraints.Any(c => c.Kind == kind)) return;
            double horizontal = Math.Sqrt(Math.Pow(max.X - min.X, 2) + Math.Pow(max.Z - min.Z, 2));
            room.TraversalConstraints.Add(new DungeonTraversalConstraint
            {
                Kind = kind, Evidence = evidence, RequiredItem = requiredItem,
                SourceRoom = room.RoomId, Start = min, End = max,
                HorizontalDistance = horizontal, VerticalDistance = Math.Abs(max.Y - min.Y),
                MaxDistance = maxDistance, Confidence = confidence
            });
        }

        private static void GetGroupBounds(ObjFile model, ObjFile.Group group, out Vector3d min, out Vector3d max)
        {
            min = Vector3d.Zero; max = Vector3d.Zero;
            if (model == null || group == null || group.Triangles == null) return;
            bool found = false;
            foreach (ObjFile.Triangle triangle in group.Triangles)
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

        private static void AnalyzeRoomSemantics(ZScene scene, DungeonAnalysisReport report)
        {
            Dictionary<int, List<int>> graph = new Dictionary<int, List<int>>();
            for (int i = 0; i < scene.Rooms.Count; i++) graph[i] = new List<int>();
            foreach (DungeonConnectionAnalysis edge in report.Connections.Where(c => !c.IsExternal))
            {
                if (!graph.ContainsKey(edge.FrontRoom) || !graph.ContainsKey(edge.BackRoom)) continue;
                if (!graph[edge.FrontRoom].Contains(edge.BackRoom)) graph[edge.FrontRoom].Add(edge.BackRoom);
                if (!edge.IsOneWay && !graph[edge.BackRoom].Contains(edge.FrontRoom)) graph[edge.BackRoom].Add(edge.FrontRoom);
            }
            int start = report.Rooms.FirstOrDefault(r => r.SpawnCount > 0) == null ? 0 : report.Rooms.First(r => r.SpawnCount > 0).RoomId;
            Dictionary<int, int> depths = new Dictionary<int, int> { { start, 0 } };
            Queue<int> queue = new Queue<int>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                foreach (int next in graph[room]) if (!depths.ContainsKey(next)) { depths.Add(next, depths[room] + 1); queue.Enqueue(next); }
            }
            foreach (DungeonRoomAnalysis room in report.Rooms)
            {
                room.TraversalDegree = graph.ContainsKey(room.RoomId) ? graph[room.RoomId].Count : 0;
                room.TraversalDepth = depths.ContainsKey(room.RoomId) ? depths[room.RoomId] : -1;
                room.EnemyCount = room.Actors.Count(a => a.Category == "Enemies" || a.Category == "Bosses");
                room.ChestCount = room.Actors.Count(a => a.Category == "Chests");
                room.DoorCount = room.Actors.Count(a => a.Category == "Doors");
                room.SwitchCount = room.Actors.Count(a => a.Category == "Switches" || a.Flags.Any(f => f.Kind == "Switch"));
                if ((room.Features & DungeonRoomFeatures.Boss) != 0) room.Role = "Boss";
                else if (room.SpawnCount > 0) room.Role = "Start";
                else if (room.GrantedItems.Count > 0) room.Role = "Reward";
                else if (room.TraversalDegree >= 3) room.Role = "Hub";
                else if (room.LogicCandidates.Count > 0 || room.SwitchCount > 0) room.Role = "Puzzle";
                else if (room.EnemyCount > 0) room.Role = "Combat";
                else if (room.TraversalDegree <= 1) room.Role = "DeadEnd";
                else room.Role = "Corridor";
            }
        }

        private static DungeonRoomAnalysis AnalyzeRoom(ZScene scene, ZScene.ZRoom room, int roomId,
            IDictionary<ushort, RomPaletteAsset> assets)
        {
            DungeonRoomAnalysis result = new DungeonRoomAnalysis
            {
                SceneId = scene.SceneNumber,
                RoomId = roomId,
                Name = string.Format("Scene {0:X2} / Room {1:D2}", scene.SceneNumber, roomId),
                BoundsMin = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue),
                BoundsMax = new Vector3d(double.MinValue, double.MinValue, double.MinValue)
            };
            if (room.ObjModel == null || room.ObjModel.Vertices.Count == 0)
                result.Warnings.Add("Room has no decoded visual vertices.");
            else
            {
                result.VisualTriangles = room.TrueGroups == null ? 0 : room.TrueGroups.Sum(g => g.Triangles.Count);
                foreach (ObjFile.Vertex vertex in room.ObjModel.Vertices) AddBounds(result, vertex.ToVector3d());
                result.FloorTriangles = CountFloorTriangles(room.ObjModel, room.TrueGroups);
            }
            if (scene.ColModel == null || scene.ColModel.Vertices.Count == 0)
                result.Warnings.Add("Scene has no decoded collision vertices.");
            else
            {
                result.CollisionTriangles = scene.ColModel.Groups.Sum(g => g.Triangles.Count);
                result.CollisionVolumes = DungeonCollisionVolumeExtractor.Extract(scene.ColModel, scene.ColModel.Groups, result.BoundsMin, result.BoundsMax);
            }

            result.SpawnCount = scene.SpawnPoints.Count(a => a.SpawnRoom == roomId || (a.SpawnRoom == 0xFF && roomId == 0));
            result.PathCount = scene.Pathways.Count;
            result.CameraCount = scene.Cameras.Count;
            result.CutsceneCount = scene.Cutscene.Count;
            result.WaterboxCount = scene.Waterboxes.Count;
            if (result.SpawnCount > 0) result.Features |= DungeonRoomFeatures.Spawn;
            if (result.FloorTriangles > 0) result.Features |= DungeonRoomFeatures.Floor;
            if (result.CollisionTriangles > 0) result.Features |= DungeonRoomFeatures.Collision;
            if (result.PathCount > 0) result.Features |= DungeonRoomFeatures.Path;
            if (result.CameraCount > 0) result.Features |= DungeonRoomFeatures.Camera;
            if (result.CutsceneCount > 0) result.Features |= DungeonRoomFeatures.Cutscene;
            if (result.WaterboxCount > 0) result.Features |= DungeonRoomFeatures.Water;

            for (int actorIndex = 0; actorIndex < room.ZActors.Count; actorIndex++)
            {
                ZActor actor = room.ZActors[actorIndex];
                RomPaletteAsset asset;
                assets.TryGetValue(actor.Number, out asset);
                string category = asset == null ? "Unknown" : asset.Category;
                DungeonActorProfile profile = new DungeonActorProfile
                {
                    Id = string.Format("room_{0}_actor_{1}", roomId, actorIndex),
                    Name = asset == null ? "Actor " + actor.Number.ToString("X4") : asset.Name,
                    Category = category,
                    SceneId = scene.SceneNumber,
                    RoomId = roomId,
                    Number = actor.Number,
                    Variable = actor.Variable,
                    Position = new Vector3d(actor.XPos, actor.YPos, actor.ZPos),
                    Rotation = new Vector3(actor.XRot, actor.YRot, actor.ZRot)
                };
                if (asset != null)
                {
                    profile.Flags = ReadFlagReferences(asset, actor);
                    profile.GrantedItems = ReadGrantedItems(asset, actor);
                    profile.RequiredItems = ReadRequiredItems(asset);
                    profile.SmallKeyCost = ReadSmallKeyCost(asset, category);
                    profile.RequiresBossKey = RequiresBossKey(asset, category);
                }
                result.Actors.Add(profile);
                result.GrantedItems.AddRange(profile.GrantedItems);
                result.RequiredItems.AddRange(profile.RequiredItems);
                result.SmallKeyCost += profile.SmallKeyCost;
                result.RequiresBossKey |= profile.RequiresBossKey;
                result.Features |= FeatureForCategory(category);
            }
            for (int spawnIndex = 0; spawnIndex < scene.SpawnPoints.Count; spawnIndex++)
            {
                ZActor spawn = scene.SpawnPoints[spawnIndex];
                if (!(spawn.SpawnRoom == roomId || (spawn.SpawnRoom == 0xFF && roomId == 0))) continue;
                result.Actors.Add(new DungeonActorProfile
                {
                    Id = string.Format("room_{0}_spawn_{1}", roomId, spawnIndex),
                    Name = "Player spawn", Category = "Spawns", Number = spawn.Number, Variable = spawn.Variable,
                    SceneId = scene.SceneNumber, RoomId = roomId,
                    Position = new Vector3d(spawn.XPos, spawn.YPos, spawn.ZPos),
                    Rotation = new Vector3(spawn.XRot, spawn.YRot, spawn.ZRot), IsSpawn = true
                });
            }
            if (result.Connections.Count > 0) result.Features |= DungeonRoomFeatures.Door;
            if (result.BoundsMin.X == double.MaxValue)
            {
                result.BoundsMin = Vector3d.Zero;
                result.BoundsMax = Vector3d.Zero;
            }
            if (result.VisualTriangles == 0) result.Warnings.Add("Room has no visual triangles.");
            if (result.CollisionTriangles == 0) result.Warnings.Add("Room has no collision triangles.");
            if (result.FloorTriangles == 0) result.Warnings.Add("No upward-facing floor triangles were detected.");
            result.WaterProfile = DungeonWaterMechanics.Analyze(scene, roomId, result.BoundsMin.Y);
            DungeonWaterMechanics.AddTraversalContracts(result, result.WaterProfile);
            result.GeometryScore = ScoreGeometry(result);
            return result;
        }

        private static List<string> ReadGrantedItems(RomPaletteAsset asset, ZActor actor)
        {
            List<string> result = new List<string>();
            if (asset == null || asset.LogicProperties == null) return result;
            foreach (RomActorLogicProperty property in asset.LogicProperties)
            {
                string propertyName = property.Name ?? "";
                if (!IsRewardProperty(propertyName)) continue;
                string item = property.DropdownName(property.Read(actor.Variable, (short)actor.XRot, (short)actor.YRot, (short)actor.ZRot));
                if (!string.IsNullOrWhiteSpace(item) && !string.Equals(item, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item, "Nothing", StringComparison.OrdinalIgnoreCase) && !string.Equals(item, "None", StringComparison.OrdinalIgnoreCase)) result.Add(item);
            }
            return result;
        }

        private static bool IsRewardProperty(string propertyName)
        {
            string value = propertyName ?? "";
            if (value.IndexOf("require", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("need", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return value.IndexOf("content", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("reward", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("item drop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("collectible", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> ReadRequiredItems(RomPaletteAsset asset, ZActor actor = null)
        {
            List<string> result = new List<string>();
            if (asset == null) return result;
            string text = ((asset.Name ?? "") + " " + (asset.RequirementText ?? "")).ToLowerInvariant();
            string[] items = { "bow", "hookshot", "longshot", "boomerang", "bomb", "bombchu", "hammer", "lens of truth",
                "iron boots", "hover boots", "slingshot", "ocarina", "fire arrow", "ice arrow", "light arrow",
                "megaton hammer", "mirror shield", "silver gauntlets", "golden gauntlets", "magic beans" };
            foreach (string item in items)
                if (text.Contains("requires " + item) || text.Contains("requires the " + item) || text.Contains("need " + item) || text.Contains("only with " + item))
                    result.Add(item);
            if (actor != null)
                foreach (RomActorLogicProperty property in asset.LogicProperties ?? new List<RomActorLogicProperty>())
                {
                    string propertyName = property.Name ?? "";
                    if (propertyName.IndexOf("require", StringComparison.OrdinalIgnoreCase) < 0 &&
                        propertyName.IndexOf("need", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string selected = property.DropdownName(property.Read(actor.Variable, (short)actor.XRot, (short)actor.YRot, (short)actor.ZRot));
                    string normalized = NormalizeItemName(selected);
                    if (!string.IsNullOrEmpty(normalized) && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase)) result.Add(normalized);
                }
            return result;
        }

        private static string NormalizeItemName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim();
            string lower = text.ToLowerInvariant();
            if (lower.Contains("small key") || lower.Contains("boss key") || lower.Contains("map") || lower.Contains("compass") || lower.Contains("nothing") || lower.Contains("unknown")) return null;
            string[] known = { "bow", "hookshot", "longshot", "boomerang", "bomb", "bombchu", "hammer", "lens of truth", "iron boots", "hover boots", "slingshot", "ocarina", "fire arrow", "ice arrow", "light arrow", "megaton hammer", "mirror shield", "silver gauntlets", "golden gauntlets", "magic beans" };
            return known.FirstOrDefault(item => lower.Contains(item));
        }

        private static bool IsExplicitOneWay(string text)
        {
            string value = (text ?? "").ToLowerInvariant();
            return value.Contains("one-way") || value.Contains("one way") || value.Contains("entrance only") ||
                value.Contains("no return") || value.Contains("does not return") || value.Contains("cannot return");
        }

        private static int ReadSmallKeyCost(RomPaletteAsset asset, string category)
        {
            if (asset == null || category != "Doors") return 0;
            string text = ((asset.Name ?? "") + " " + (asset.RequirementText ?? "")).ToLowerInvariant();
            return text.Contains("locked") || text.Contains("small key") ? 1 : 0;
        }

        private static bool RequiresBossKey(RomPaletteAsset asset, string category)
        {
            if (asset == null || category != "Doors") return false;
            string text = ((asset.Name ?? "") + " " + (asset.RequirementText ?? "")).ToLowerInvariant();
            return text.Contains("boss key") || text.Contains("boss door");
        }

        private static List<DungeonFlagReference> ReadFlagReferences(RomPaletteAsset asset, ZActor actor)
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

        private static void AnalyzeLogic(ZScene scene, DungeonAnalysisReport report, IDictionary<ushort, RomPaletteAsset> assets)
        {
            Dictionary<string, List<DungeonActorProfile>> references = new Dictionary<string, List<DungeonActorProfile>>(StringComparer.Ordinal);
            foreach (DungeonRoomAnalysis room in report.Rooms)
                foreach (DungeonActorProfile actor in room.Actors)
                    foreach (DungeonFlagReference flag in actor.Flags)
                    {
                        string key = flag.Kind + ":" + flag.Value;
                        List<DungeonActorProfile> matches;
                        if (!references.TryGetValue(key, out matches)) references.Add(key, matches = new List<DungeonActorProfile>());
                        matches.Add(actor);
                    }

            foreach (KeyValuePair<string, List<DungeonActorProfile>> group in references)
            {
                List<DungeonActorProfile> setters = group.Value.Where(a => a.Flags.Any(f => f.CanSet && group.Key == f.Kind + ":" + f.Value)).ToList();
                List<DungeonActorProfile> checkers = group.Value.Where(a => a.Flags.Any(f => f.CanCheck && group.Key == f.Kind + ":" + f.Value)).ToList();
                if (group.Value.Count > 1 && setters.Count == 0)
                    report.Diagnostics.Add("Scene " + scene.SceneNumber.ToString("X2") + " flag " + group.Key + " is shared but has no confidently identified setter.");
                foreach (DungeonActorProfile source in setters)
                    foreach (DungeonActorProfile target in checkers)
                    {
                        if (source.Id == target.Id) continue;
                        DungeonFlagReference sourceFlag = source.Flags.First(f => f.CanSet && group.Key == f.Kind + ":" + f.Value);
                        DungeonFlagReference targetFlag = target.Flags.First(f => f.CanCheck && group.Key == f.Kind + ":" + f.Value);
                        DungeonLogicCandidate candidate = new DungeonLogicCandidate
                        {
                            SourceId = source.Id,
                            TargetId = target.Id,
                            SceneId = scene.SceneNumber,
                            SourceRoom = source.RoomId,
                            TargetRoom = target.RoomId,
                            Kind = InferLogicKind(source, target, group.Key.StartsWith("Chest:", StringComparison.Ordinal) ? "Chest" : "Switch"),
                            Parameter = group.Key,
                            Evidence = sourceFlag.Evidence + " -> " + targetFlag.Evidence,
                            Confidence = LogicConfidence(source, target, sourceFlag, targetFlag)
                        };
                        report.LogicCandidates.Add(candidate);
                        DungeonRoomAnalysis room = report.FindRoom(scene.SceneNumber, source.RoomId);
                        if (room != null) room.LogicCandidates.Add(candidate);
                    }
            }
        }

        private static string InferLogicKind(DungeonActorProfile source, DungeonActorProfile target, string flagKind)
        {
            if (target.Category.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0) return "Opens";
            if (target.Category.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0 || target.Category.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0) return "Spawns";
            if (flagKind == "Chest") return "Enables";
            if (target.Category.IndexOf("NPC", StringComparison.OrdinalIgnoreCase) >= 0) return "Enables";
            return "Activates";
        }

        private static double LogicConfidence(DungeonActorProfile source, DungeonActorProfile target,
            DungeonFlagReference sourceFlag, DungeonFlagReference targetFlag)
        {
            double confidence = 0.55;
            string sourceName = source.Name ?? "";
            string targetName = target.Name ?? "";
            if (source.Category == "Switches" || sourceName.IndexOf("torch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sourceName.IndexOf("plate", StringComparison.OrdinalIgnoreCase) >= 0) confidence += 0.2;
            if (target.Category == "Doors" || target.Category == "Enemies" || target.Category == "Bosses") confidence += 0.15;
            if (sourceFlag.Property.IndexOf("set", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sourceFlag.Property.IndexOf("destroy", StringComparison.OrdinalIgnoreCase) >= 0) confidence += 0.1;
            if (targetFlag.Property.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                targetFlag.Property.IndexOf("opens", StringComparison.OrdinalIgnoreCase) >= 0) confidence += 0.1;
            return Math.Min(0.99, confidence);
        }

        public static ProceduralDungeonPlan Generate(DungeonAnalysisReport report, DungeonGeneratorSettings settings)
        {
            if (report == null) throw new InvalidOperationException("Analyze a ROM scene before generating a dungeon.");
            if (settings == null) settings = new DungeonGeneratorSettings();
            if (settings.RoomCount < 1 || settings.RoomCount > 128) throw new InvalidOperationException("Room count must be between 1 and 128.");
            Random random = new Random(settings.Seed);
            DungeonRoomFeatures required = settings.RequiredFeatures;
            if (settings.RequireBoss) required |= DungeonRoomFeatures.Boss;
            if (settings.RequireChest) required |= DungeonRoomFeatures.Chest;
            HashSet<string> startingItems = new HashSet<string>(settings.StartingItems ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> possibleItems = new HashSet<string>(startingItems, StringComparer.OrdinalIgnoreCase);
            foreach (string item in report.Rooms.SelectMany(r => r.GrantedItems)) possibleItems.Add(item);
            List<DungeonRoomAnalysis> candidates = report.Rooms.Where(r => r.Supports(required))
                .Where(r => r.RequiredItems.All(item => possibleItems.Contains(item)))
                .OrderByDescending(r => r.GeometryScore).ToList();
            if (candidates.Count == 0)
            {
                string missing = report.Rooms.SelectMany(r => r.RequiredItems).Where(item => !startingItems.Contains(item)).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                throw new InvalidOperationException(string.IsNullOrEmpty(missing) ? "No analyzed rooms satisfy the requested dungeon features." : "No compatible rooms satisfy the starting inventory. Missing item: " + missing + ".");
            }

            ProceduralDungeonPlan plan = new ProceduralDungeonPlan { Seed = settings.Seed, StartingItems = startingItems.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(), RomMechanics = settings.RomMechanics, TraversalCalibration = DungeonTraversalCalibration.FromRomMechanics(settings.RomMechanics) };
            DungeonRoomAnalysis start = candidates.FirstOrDefault(r => r.Supports(DungeonRoomFeatures.Spawn)) ?? candidates[0];
            AddRoom(plan, start, settings, 0);
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal) { RoomKey(start) };
            while (plan.Rooms.Count < settings.RoomCount)
            {
                DungeonRoomAnalysis parent = report.FindRoom(plan.Rooms[plan.Rooms.Count - 1].SourceScene, plan.Rooms[plan.Rooms.Count - 1].SourceRoom);
                DungeonConnectionAnalysis edge = parent == null ? null : parent.Connections.Where(c => !c.IsExternal)
                    .OrderBy(c => used.Contains(RoomKey(c.BackRoom == parent.RoomId ? report.FindRoom(c.SceneId, c.FrontRoom) : report.FindRoom(c.SceneId, c.BackRoom))) ? 1 : 0)
                    .FirstOrDefault();
                DungeonRoomAnalysis next = edge == null ? null : report.FindRoom(edge.SceneId, edge.BackRoom == parent.RoomId ? edge.FrontRoom : edge.BackRoom);
                bool connected = next != null && candidates.Contains(next) && next.Supports(required) && !used.Contains(RoomKey(next)) && TraversalRequirementsAvailable(next, plan);
                if (!connected)
                {
                    List<DungeonRoomAnalysis> remaining = candidates.Where(c => !used.Contains(RoomKey(c)) && TraversalRequirementsAvailable(c, plan)).ToList();
                    if (remaining.Count == 0)
                    {
                        remaining = candidates.Where(c => !used.Contains(RoomKey(c))).ToList();
                        if (remaining.Count > 0) plan.Warnings.Add("No traversal-valid next room was available; the generator used a fallback candidate and will report the route constraint.");
                    }
                    if (remaining.Count == 0) break;
                    next = remaining[random.Next(remaining.Count)];
                }
                int nextIndex = plan.Rooms.Count;
                AddRoom(plan, next, settings, nextIndex);
                used.Add(RoomKey(next));
                plan.Connections.Add(new DungeonConnectionPlan
                {
                    FromRoom = nextIndex - 1,
                    ToRoom = nextIndex,
                    SourceScene = edge == null ? next.SceneId : edge.SceneId,
                    TransitionIndex = edge == null ? -1 : edge.TransitionIndex,
                    GenerateCorridor = settings.GenerateCorridors && edge == null,
                    ReuseNativeTransition = edge != null,
                    Bidirectional = edge == null || !edge.IsOneWay,
                    RequiredItems = edge == null ? new List<string>() : edge.RequiredItems.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                });
                if (settings.AllowLoops && plan.Rooms.Count > 2 && random.NextDouble() < 0.2)
                    plan.Warnings.Add("Loop candidates are recorded for a later connection-placement pass.");
            }
            if (plan.Rooms.Count < settings.RoomCount)
            {
                string message = "Only " + plan.Rooms.Count + " compatible rooms were available for the requested " + settings.RoomCount + ".";
                if (!settings.AllowPartialPlan) throw new InvalidOperationException(message + " Generation stopped to avoid an incomplete dungeon.");
                plan.Warnings.Add(message);
            }
            DungeonPatternProfile reference = report.DungeonPatterns == null ? null : report.DungeonPatterns.Dungeons.FirstOrDefault();
            plan.Topology = DungeonTopologyPlanner.Create(plan.Rooms.Count, reference, settings.Seed);
            plan.Assembly = DungeonNativeAssembler.Prepare(report, plan);
            if (!plan.Assembly.Ready)
                plan.Warnings.Add("Native flag assembly was not certified; inspect flag remap warnings before export.");
            plan.Progression = DungeonProgressionSolver.Solve(report, plan);
            if (!plan.Progression.Solvable)
                plan.Warnings.Add("Progression was not certified; inspect the progression errors before export.");
            plan.TraversalProgression = DungeonTraversalProgression.Check(plan);
            if (!plan.TraversalProgression.Solvable)
                plan.Warnings.Add("Traversal progression was not certified; inspect the traversal route errors before export.");
            if (settings.RequireTraversalMechanics)
                foreach (DungeonRoomPlan room in plan.Rooms)
                    foreach (DungeonTraversalConstraint constraint in room.TraversalConstraints)
                        if (!string.IsNullOrEmpty(constraint.RequiredItem) && !startingItems.Contains(constraint.RequiredItem) &&
                            !plan.Rooms.Take(room.Index).SelectMany(r => r.GrantedItems).Contains(constraint.RequiredItem, StringComparer.OrdinalIgnoreCase))
                            plan.Warnings.Add("Room " + room.Index + " contains a " + constraint.Kind + " route that requires " + constraint.RequiredItem + ".");
            plan.ItemArc = DungeonItemArcPlanner.Analyze(plan);
            if (plan.ItemArc.MidDungeonAcquisitionCount < Math.Max(0, settings.MinimumItemAcquisitionArcs))
                plan.Warnings.Add("The generated dungeon has fewer mid-dungeon item acquisitions than requested.");
            if (settings.RequireItemAcquisitionArc && (!plan.ItemArc.Solvable || plan.ItemArc.MidDungeonAcquisitionCount < Math.Max(1, settings.MinimumItemAcquisitionArcs)))
                throw new InvalidOperationException("The generated dungeon failed item-arc certification.\n\n" + string.Join("\n", plan.ItemArc.Errors.Concat(plan.ItemArc.Warnings).Distinct().ToArray()));
            return plan;
        }

        private static void AddRoom(ProceduralDungeonPlan plan, DungeonRoomAnalysis source, DungeonGeneratorSettings settings, int index)
        {
            DungeonRoomPlan room = new DungeonRoomPlan
            {
                Index = index,
                SourceScene = source.SceneId,
                SourceRoom = source.RoomId,
                Features = source.Features,
                BoundsMin = source.BoundsMin,
                BoundsMax = source.BoundsMax,
                CollisionVolumes = source.CollisionVolumes == null ? new List<DungeonCollisionVolume>() : source.CollisionVolumes,
                WaterProfile = source.WaterProfile ?? new DungeonWaterProfile(),
                Mesh = new DungeonMeshPlan { SceneId = source.SceneId, RoomId = source.RoomId, VisualTriangles = source.VisualTriangles, CollisionTriangles = source.CollisionTriangles },
                RequiredItems = source.RequiredItems.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                GrantedItems = source.GrantedItems.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SmallKeyCost = source.SmallKeyCost,
                RequiresBossKey = source.RequiresBossKey,
                TraversalCapabilities = source.TraversalCapabilities,
                TraversalConstraints = source.TraversalConstraints.Select(c => new DungeonTraversalConstraint
                {
                    Kind = c.Kind, Evidence = c.Evidence, RequiredItem = c.RequiredItem,
                    SourceRoom = index, TargetRoom = c.TargetRoom, Start = c.Start, End = c.End, HorizontalDistance = c.HorizontalDistance,
                    VerticalDistance = c.VerticalDistance, MaxDistance = c.MaxDistance,
                    Confidence = c.Confidence, ClearanceRadius = c.ClearanceRadius,
                    Obstacles = c.Obstacles == null ? new List<DungeonTraversalObstacle>() : c.Obstacles,
                    Optional = c.Optional
                }).ToList()
            };
            int enemies = 0;
            foreach (DungeonActorProfile actor in source.Actors)
            {
                if (actor.IsSpawn || actor.Category == "Spawns")
                {
                    room.Actors.Add(ToPlacement(actor, "Spawn"));
                    continue;
                }
                if (actor.Category == "Enemies" && enemies++ >= settings.MaxEnemiesPerRoom) continue;
                room.Actors.Add(ToPlacement(actor, actor.Category));
            }
            plan.Rooms.Add(room);
        }

        private static bool TraversalRequirementsAvailable(DungeonRoomAnalysis candidate, ProceduralDungeonPlan plan)
        {
            if (candidate == null) return false;
            HashSet<string> available = new HashSet<string>(plan.StartingItems ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (DungeonRoomPlan room in plan.Rooms)
                foreach (string item in room.GrantedItems ?? new List<string>()) available.Add(item);
            return candidate.TraversalConstraints == null || candidate.TraversalConstraints.All(c => string.IsNullOrEmpty(c.RequiredItem) || available.Contains(c.RequiredItem));
        }

        private static DungeonActorPlacement ToPlacement(DungeonActorProfile actor, string role)
        {
            return new DungeonActorPlacement { SourceId = actor.Id, Role = role, Number = actor.Number, Variable = actor.Variable,
                Position = actor.Position, Rotation = actor.Rotation,
                GrantedItems = actor.GrantedItems.ToList(),
                Flags = actor.Flags.Select(f => new DungeonFlagReference { Kind = f.Kind, Property = f.Property, Target = f.Target,
                    Value = f.Value, Mask = f.Mask, Position = f.Position, CanSet = f.CanSet, CanCheck = f.CanCheck, Evidence = f.Evidence }).ToList() };
        }

        private static DungeonRoomFeatures FeatureForCategory(string category)
        {
            if (category == null) return DungeonRoomFeatures.None;
            if (category.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0) return DungeonRoomFeatures.Boss;
            if (category.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0) return DungeonRoomFeatures.Chest;
            if (category.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0) return DungeonRoomFeatures.Enemy;
            if (category.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0) return DungeonRoomFeatures.Switch;
            if (category.IndexOf("NPC", StringComparison.OrdinalIgnoreCase) >= 0) return DungeonRoomFeatures.Npc;
            if (category.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0) return DungeonRoomFeatures.Door;
            return category == "Props" || category == "Scenery" ? DungeonRoomFeatures.Prop : DungeonRoomFeatures.None;
        }

        private static Dictionary<ushort, RomPaletteAsset> BuildAssetLookup(RomAssetCatalog catalog)
        {
            Dictionary<ushort, RomPaletteAsset> result = new Dictionary<ushort, RomPaletteAsset>();
            if (catalog == null) return result;
            foreach (RomPaletteAsset asset in catalog.Assets.Where(a => a.Id != 0)) if (!result.ContainsKey(asset.Id)) result.Add(asset.Id, asset);
            return result;
        }

        private static int CountFloorTriangles(ObjFile model, IList<ObjFile.Group> groups)
        {
            if (model == null || groups == null) return 0;
            int count = 0;
            foreach (ObjFile.Triangle triangle in groups.SelectMany(g => g.Triangles))
            {
                if (triangle.VertIndex == null || triangle.VertIndex.Length < 3) continue;
                Vector3d a = model.Vertices[triangle.VertIndex[0]].ToVector3d();
                Vector3d b = model.Vertices[triangle.VertIndex[1]].ToVector3d();
                Vector3d c = model.Vertices[triangle.VertIndex[2]].ToVector3d();
                if (Vector3d.Cross(b - a, c - a).Y > 0.01) count++;
            }
            return count;
        }

        private static double ScoreGeometry(DungeonRoomAnalysis room)
        {
            double score = 0;
            if (room.VisualTriangles > 0) score += 0.35;
            if (room.CollisionTriangles > 0) score += 0.35;
            if (room.FloorTriangles > 0) score += 0.2;
            if (room.BoundsMax != room.BoundsMin) score += 0.1;
            return score;
        }

        private static void AddBounds(DungeonRoomAnalysis result, Vector3d value)
        {
            result.BoundsMin = new Vector3d(Math.Min(result.BoundsMin.X, value.X), Math.Min(result.BoundsMin.Y, value.Y), Math.Min(result.BoundsMin.Z, value.Z));
            result.BoundsMax = new Vector3d(Math.Max(result.BoundsMax.X, value.X), Math.Max(result.BoundsMax.Y, value.Y), Math.Max(result.BoundsMax.Z, value.Z));
        }

        private static string RoomKey(DungeonRoomAnalysis room)
        {
            return room == null ? "" : room.SceneId + ":" + room.RoomId;
        }

        private static string RoomKey(int room)
        {
            return room.ToString();
        }

        private static void CalculateReachability(ZScene scene, DungeonAnalysisReport report)
        {
            HashSet<int> reachable = new HashSet<int>();
            Queue<int> queue = new Queue<int>();
            for (int room = 0; room < scene.Rooms.Count; room++)
                if (scene.SpawnPoints.Any(s => s.SpawnRoom == room || (s.SpawnRoom == 0xFF && room == 0))) queue.Enqueue(room);
            if (queue.Count == 0 && scene.Rooms.Count > 0) queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                if (!reachable.Add(room)) continue;
                foreach (DungeonConnectionAnalysis edge in report.Connections)
                {
                    if (edge.FrontRoom == room && edge.BackRoom != 0xFF) queue.Enqueue(edge.BackRoom);
                    if (!edge.IsOneWay && edge.BackRoom == room && edge.FrontRoom != 0xFF) queue.Enqueue(edge.FrontRoom);
                }
            }
            report.ReachableRooms = report.Rooms.Where(r => reachable.Contains(r.RoomId)).Select(r => r.RoomId).Distinct().OrderBy(x => x).ToList();
            report.UnreachableRooms = report.Rooms.Where(r => !reachable.Contains(r.RoomId)).Select(r => r.RoomId).Distinct().OrderBy(x => x).ToList();
        }
    }
}
