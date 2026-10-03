using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpOcarina
{
    [Serializable]
    public sealed class GeneratedDungeonProgressionReport
    {
        public bool Solvable;
        public int StartRoom = -1, FinaleRoom = -1, RoomCount, ReachableRoomCount;
        public int SmallKeysSpent, SmallKeysRemaining;
        public bool BossKeyAcquired;
        public DungeonProgressionReport SolverReport;
        public DungeonItemArcReport ItemArc;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();

        public string Summary
        {
            get
            {
                return string.Format("progression {0}, {1}/{2} rooms reachable, {3} small keys spent, boss key {4}, {5}",
                    Solvable ? "certified" : "blocked", ReachableRoomCount, RoomCount, SmallKeysSpent,
                    BossKeyAcquired ? "acquired" : "not acquired", ItemArc == null ? "item arc unavailable" : ItemArc.Summary);
            }
        }
    }

    /// <summary>
    /// Adapts the native scene/package representation produced by the editor to
    /// the existing inventory-aware OoT progression solver. This keeps generation
    /// validation tied to the data that will actually be exported.
    /// </summary>
    public static class GeneratedDungeonProgression
    {
        public static GeneratedDungeonProgressionReport Check(ZScene scene, IEnumerable<string> startingItems)
        {
            GeneratedDungeonProgressionReport result = new GeneratedDungeonProgressionReport();
            if (scene == null)
            {
                result.Errors.Add("No native scene is available for progression analysis.");
                return result;
            }

            ProceduralDungeonPlan plan = BuildPlan(scene, startingItems, result);
            if (plan == null) return result;
            try
            {
                result.SolverReport = DungeonProgressionSolver.Solve(null, plan);
            plan.ItemArc = DungeonItemArcPlanner.Analyze(plan);
            }
            catch (Exception error)
            {
                result.Errors.Add("Native progression analysis failed: " + error.Message);
                return result;
            }

            result.StartRoom = result.SolverReport.StartRoom;
            result.RoomCount = plan.Rooms.Count;
            result.ReachableRoomCount = result.SolverReport.ReachableRooms.Count;
            result.SmallKeysSpent = result.SolverReport.SmallKeysSpent;
            result.SmallKeysRemaining = result.SolverReport.SmallKeysRemaining;
            result.BossKeyAcquired = result.SolverReport.BossKeyAcquired;
            result.ItemArc = plan.ItemArc;
            result.FinaleRoom = FindFinaleRoom(plan);
            result.Errors.AddRange(result.SolverReport.Errors ?? new List<string>());
            result.Warnings.AddRange(result.SolverReport.Warnings ?? new List<string>());
            result.Errors.AddRange(result.ItemArc.Errors.Select(error => "Item arc: " + error));
            result.Warnings.AddRange(result.ItemArc.Warnings.Select(warning => "Item arc: " + warning));
            if (result.FinaleRoom < 0) result.Errors.Add("No finale actor is present in the native progression plan.");
            else if (!result.SolverReport.ReachableRooms.Contains(result.FinaleRoom)) result.Errors.Add("The finale room is not reachable in the item-aware progression plan.");
            result.Solvable = result.Errors.Count == 0 && result.SolverReport.Solvable;
            return result;
        }

        private static ProceduralDungeonPlan BuildPlan(ZScene scene, IEnumerable<string> startingItems, GeneratedDungeonProgressionReport result)
        {
            result.RoomCount = scene.Rooms == null ? 0 : scene.Rooms.Count;
            if (result.RoomCount == 0)
            {
                result.Errors.Add("The native progression plan contains no rooms.");
                return null;
            }

            ProceduralDungeonPlan plan = new ProceduralDungeonPlan
            {
                Seed = 0,
                StartingItems = (startingItems ?? new List<string>()).Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            List<RoomPackage> packages = scene.SandboxGameplayPackages ?? new List<RoomPackage>();
            for (int room = 0; room < result.RoomCount; room++)
            {
                DungeonRoomPlan roomPlan = new DungeonRoomPlan { Index = room };
                IEnumerable<RoomPackage> roomPackages = packages.Where(candidate => candidate != null && !candidate.LogicOnly &&
                    (candidate.DestinationRoom == room || (candidate.DestinationRoom < 0 && candidate.Room == room)));
                foreach (RoomPackage package in roomPackages)
                {
                    foreach (RoomPackageActor actor in Actors(package))
                    {
                        if (actor == null) continue;
                        string role = MapRole(actor);
                        roomPlan.Actors.Add(new DungeonActorPlacement
                        {
                            SourceId = actor.Id,
                            Role = role,
                            Number = actor.Number,
                            Variable = actor.Variable,
                            Position = actor.Position,
                            Rotation = actor.Rotation,
                            GrantedItems = (actor.GrantedItems ?? new List<string>()).ToList(),
                            Flags = (actor.Flags ?? new List<DungeonFlagReference>()).ToList()
                        });
                        foreach (string granted in actor.GrantedItems ?? new List<string>())
                            if (!roomPlan.GrantedItems.Contains(granted, StringComparer.OrdinalIgnoreCase)) roomPlan.GrantedItems.Add(granted);
                        if (IsGate(actor)) AddRequirements(roomPlan, actor.RequiredItems);
                    }
                    foreach (LogicLink link in package.Links ?? new List<LogicLink>())
                        if (link != null && !string.IsNullOrWhiteSpace(link.SourceId) && !string.IsNullOrWhiteSpace(link.TargetId))
                            roomPlan.Actors.Add(new DungeonActorPlacement { SourceId = "logic:" + link.SourceId + ":" + link.TargetId, Role = "Logic" });
                }
                plan.Rooms.Add(roomPlan);
            }

            foreach (ZActor transition in scene.Transitions ?? new List<ZActor>())
            {
                if (transition == null) continue;
                if (!ValidRoom(transition.FrontSwitchTo, result.RoomCount) || !ValidRoom(transition.BackSwitchTo, result.RoomCount))
                {
                    result.Warnings.Add("Skipped a native transition with an invalid progression endpoint.");
                    continue;
                }
                DungeonConnectionPlan connection = new DungeonConnectionPlan
                {
                    FromRoom = transition.FrontSwitchTo,
                    ToRoom = transition.BackSwitchTo,
                    Bidirectional = !IsOneWayTransition(packages, transition),
                    ReuseNativeTransition = true,
                    RequiredItems = FindTransitionRequirements(packages, transition)
                };
                plan.Connections.Add(connection);
            }
            return plan;
        }

        private static IEnumerable<RoomPackageActor> Actors(RoomPackage package)
        {
            return (package.Actors ?? new List<RoomPackageActor>()).Concat(package.Spawns ?? new List<RoomPackageActor>());
        }

        private static string MapRole(RoomPackageActor actor)
        {
            if (Contains(actor.Role, "spawn") || Contains(actor.Name, "spawn")) return "Spawn";
            if (Contains(actor.Role, "boss") || Contains(actor.Role, "finale") || Contains(actor.Name, "boss")) return "Bosses";
            if (Contains(actor.Role, "enemy") || Contains(actor.Name, "stalfos") || Contains(actor.Name, "moblin") || Contains(actor.Name, "lizalfos")) return "Enemies";
            if (Contains(actor.Role, "reward") || Contains(actor.Role, "chest") || Contains(actor.Name, "chest") ||
                (actor.GrantedItems != null && actor.GrantedItems.Count > 0)) return "Chests";
            return string.IsNullOrWhiteSpace(actor.Role) ? "Actors" : actor.Role;
        }

        private static bool IsGate(RoomPackageActor actor)
        {
            return actor.IsTransition || (actor.RequiredItems != null && actor.RequiredItems.Count > 0) ||
                Contains(actor.Role, "door") || Contains(actor.Role, "gate") || Contains(actor.Name, "door") || Contains(actor.Name, "gate");
        }

        private static void AddRequirements(DungeonRoomPlan room, IEnumerable<string> requirements)
        {
            foreach (string requirement in requirements ?? new List<string>())
            {
                if (IsSmallKey(requirement)) room.SmallKeyCost++;
                else if (IsBossKey(requirement)) room.RequiresBossKey = true;
                else
                {
                    string normalized = NormalizeItem(requirement);
                    if (!string.IsNullOrWhiteSpace(normalized) && !room.RequiredItems.Contains(normalized, StringComparer.OrdinalIgnoreCase)) room.RequiredItems.Add(normalized);
                }
            }
        }

        private static List<string> FindTransitionRequirements(IEnumerable<RoomPackage> packages, ZActor transition)
        {
            List<string> result = new List<string>();
            foreach (RoomPackage package in packages ?? new List<RoomPackage>())
                foreach (RoomPackageActor actor in Actors(package).Where(candidate => candidate != null && candidate.IsTransition))
                {
                    if (actor.Number != transition.Number) continue;
                    if (Math.Abs(actor.Position.X - transition.XPos) > 2.0 || Math.Abs(actor.Position.Y - transition.YPos) > 2.0 || Math.Abs(actor.Position.Z - transition.ZPos) > 2.0) continue;
                    foreach (string requirement in actor.RequiredItems ?? new List<string>())
                    {
                        string normalized = NormalizeItem(requirement);
                        if (!string.IsNullOrWhiteSpace(normalized) && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase)) result.Add(normalized);
                    }
                }
            return result;
        }

        private static bool IsOneWayTransition(IEnumerable<RoomPackage> packages, ZActor transition)
        {
            foreach (RoomPackage package in packages ?? new List<RoomPackage>())
                foreach (RoomPackageActor actor in Actors(package).Where(candidate => candidate != null && candidate.IsTransition))
                {
                    if (actor.Number != transition.Number) continue;
                    if (Math.Abs(actor.Position.X - transition.XPos) > 2.0 || Math.Abs(actor.Position.Y - transition.YPos) > 2.0 || Math.Abs(actor.Position.Z - transition.ZPos) > 2.0) continue;
                    string text = ((actor.Name ?? "") + " " + (actor.RequirementText ?? "")).ToLowerInvariant();
                    if (text.Contains("one-way") || text.Contains("one way") || text.Contains("entrance only") || text.Contains("no return") || text.Contains("does not return") || text.Contains("cannot return")) return true;
                }
            return false;
        }

        private static int FindFinaleRoom(ProceduralDungeonPlan plan)
        {
            for (int i = 0; i < plan.Rooms.Count; i++)
                if (plan.Rooms[i].Actors.Any(actor => actor.Role == "Bosses")) return i;
            return -1;
        }

        private static bool ValidRoom(int room, int roomCount) { return room >= 0 && room < roomCount && room <= 127; }
        private static bool IsSmallKey(string value) { return Contains(value, "small key"); }
        private static bool IsBossKey(string value) { return Contains(value, "boss key"); }
        private static string NormalizeItem(string value)
        {
            string result = (value ?? "").Trim();
            if (result.StartsWith("DUNGEON ", StringComparison.OrdinalIgnoreCase)) result = result.Substring("DUNGEON ".Length).Trim();
            return result;
        }
        private static bool Contains(string value, string part) { return !string.IsNullOrEmpty(value) && value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0; }
    }
}
