using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpOcarina
{
    [Serializable]
    public sealed class GeneratedDungeonPlayabilityReport
    {
        public bool Solvable;
        public int StartRoom = -1, FinaleRoom = -1, RoomCount, ConnectionCount, ReachableCount, GameplayActorCount, LogicLinkCount;
        public List<int> ReachableRooms = new List<int>();
        public List<int> UnreachableRooms = new List<int>();
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public GeneratedDungeonProgressionReport Progression;

        public string Summary
        {
            get
            {
                return string.Format("start room {0}, {1}/{2} rooms reachable, {3} native connections, {4} gameplay actors, {5} logic links, {6}",
                    StartRoom, ReachableCount, RoomCount, ConnectionCount, GameplayActorCount, LogicLinkCount,
                    Progression == null || Progression.ItemArc == null ? "item arc unavailable" : Progression.ItemArc.Summary);
            }
        }
    }

    /// <summary>
    /// Certifies the native scene assembled by the one-click randomizer. This is
    /// deliberately separate from ROM-wide analysis because it checks the scene
    /// and package identities that will actually be exported.
    /// </summary>
    public static class GeneratedDungeonPlayability
    {
        public static GeneratedDungeonPlayabilityReport Check(ZScene scene)
        {
            return Check(scene, null);
        }

        public static GeneratedDungeonPlayabilityReport Check(ZScene scene, IEnumerable<string> startingItems)
        {
            GeneratedDungeonPlayabilityReport result = new GeneratedDungeonPlayabilityReport();
            if (scene == null)
            {
                result.Errors.Add("No generated native scene is available.");
                return result;
            }

            result.RoomCount = scene.Rooms == null ? 0 : scene.Rooms.Count;
            if (result.RoomCount == 0)
            {
                result.Errors.Add("The generated dungeon contains no native rooms.");
                return result;
            }

            Dictionary<int, HashSet<int>> graph = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < result.RoomCount; i++) graph[i] = new HashSet<int>();
            foreach (ZActor transition in scene.Transitions ?? new List<ZActor>())
            {
                if (!ValidRoom(transition.FrontSwitchTo, result.RoomCount) || !ValidRoom(transition.BackSwitchTo, result.RoomCount))
                {
                    result.Errors.Add("A generated native transition has an invalid room endpoint.");
                    continue;
                }
                graph[transition.FrontSwitchTo].Add(transition.BackSwitchTo);
                graph[transition.BackSwitchTo].Add(transition.FrontSwitchTo);
                result.ConnectionCount++;
            }

            ZActor spawn = (scene.SpawnPoints ?? new List<ZActor>()).FirstOrDefault();
            result.StartRoom = spawn == null ? -1 : spawn.SpawnRoom;
            if (!ValidRoom(result.StartRoom, result.RoomCount)) result.Errors.Add("The generated dungeon has no valid player start room.");

            List<RoomPackage> packages = scene.SandboxGameplayPackages ?? new List<RoomPackage>();
            Dictionary<int, RoomPackage> roomPackages = new Dictionary<int, RoomPackage>();
            foreach (RoomPackage package in packages.Where(p => p != null && !p.LogicOnly))
            {
                int room = package.DestinationRoom >= 0 ? package.DestinationRoom : package.Room;
                if (!ValidRoom(room, result.RoomCount))
                {
                    result.Errors.Add("A gameplay package targets a missing generated room.");
                    continue;
                }
                if (roomPackages.ContainsKey(room)) result.Errors.Add("Multiple gameplay packages target room " + room + "; merge or remove the duplicate package before export.");
                else roomPackages.Add(room, package);
            }
            if (roomPackages.Count != result.RoomCount)
                result.Errors.Add("Every generated native room must have one gameplay package.");

            Dictionary<string, RoomPackageActor> actors = new Dictionary<string, RoomPackageActor>(StringComparer.Ordinal);
            foreach (RoomPackage package in roomPackages.Values)
                foreach (RoomPackageActor actor in package.Actors.Concat(package.Spawns))
                {
                    result.GameplayActorCount++;
                    if (actor == null || string.IsNullOrWhiteSpace(actor.Id)) result.Errors.Add("A generated gameplay actor has no stable ID.");
                    else if (!actors.ContainsKey(actor.Id)) actors.Add(actor.Id, actor);
                    else result.Errors.Add("Generated gameplay actor ID is duplicated: " + actor.Id + ".");
                }

            foreach (RoomPackage package in roomPackages.Values)
                foreach (LogicLink link in package.Links ?? new List<LogicLink>())
                {
                    result.LogicLinkCount++;
                    RoomPackageActor source, target;
                    if (!actors.TryGetValue(link.SourceId ?? "", out source) || !actors.TryGetValue(link.TargetId ?? "", out target))
                        result.Errors.Add("A generated logic link references a missing actor.");
                }

            HashSet<int> reachable = new HashSet<int>();
            if (ValidRoom(result.StartRoom, result.RoomCount))
            {
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(result.StartRoom); reachable.Add(result.StartRoom);
                while (queue.Count > 0)
                    foreach (int next in graph[queue.Dequeue()])
                        if (reachable.Add(next)) queue.Enqueue(next);
            }
            result.ReachableRooms = reachable.OrderBy(room => room).ToList();
            result.ReachableCount = result.ReachableRooms.Count;
            for (int i = 0; i < result.RoomCount; i++) if (!reachable.Contains(i)) result.UnreachableRooms.Add(i);
            if (result.UnreachableRooms.Count > 0)
                result.Errors.Add("Generated rooms are unreachable from the player start: " + string.Join(",", result.UnreachableRooms.ToArray()) + ".");

            List<RoomPackageActor> allActors = actors.Values.ToList();
            bool hasCombat = allActors.Any(IsEnemy) || allActors.Any(IsFinale);
            bool hasFinale = allActors.Any(IsFinale);
            bool hasReward = allActors.Any(IsReward);
            bool hasPuzzle = allActors.Any(IsPuzzle) && result.LogicLinkCount > 0;
            RoomPackageActor finale = allActors.FirstOrDefault(IsFinale);
            if (finale != null) result.FinaleRoom = FindActorRoom(finale, roomPackages);
            if (!hasCombat) result.Errors.Add("The generated dungeon has no combat encounter.");
            if (!hasFinale) result.Errors.Add("The generated dungeon has no finale enemy or boss.");
            if (!hasReward) result.Errors.Add("The generated dungeon has no reward actor.");
            if (!hasPuzzle) result.Errors.Add("The generated dungeon has no resolvable puzzle actor and logic link.");
            if (result.FinaleRoom >= 0 && !reachable.Contains(result.FinaleRoom)) result.Errors.Add("The finale room is not reachable from the player start.");

            result.Progression = GeneratedDungeonProgression.Check(scene, startingItems);
            result.Errors.AddRange(result.Progression.Errors.Select(error => "Progression: " + error));
            result.Warnings.AddRange(result.Progression.Warnings.Select(warning => "Progression: " + warning));

            result.Solvable = result.Errors.Count == 0;
            return result;
        }

        private static bool ValidRoom(int room, int roomCount)
        {
            return room >= 0 && room < roomCount && room <= 127;
        }

        private static int FindActorRoom(RoomPackageActor actor, IDictionary<int, RoomPackage> packages)
        {
            foreach (KeyValuePair<int, RoomPackage> pair in packages)
                if (pair.Value.Actors.Concat(pair.Value.Spawns).Any(candidate => candidate.Id == actor.Id)) return pair.Key;
            return -1;
        }

        private static bool IsFinale(RoomPackageActor actor)
        {
            return actor != null && (Contains(actor.Role, "boss") || Contains(actor.Role, "finale") || Contains(actor.Name, "boss"));
        }

        private static bool IsEnemy(RoomPackageActor actor)
        {
            return actor != null && (Contains(actor.Role, "enemy") || Contains(actor.Role, "boss") || Contains(actor.Name, "stalfos") || Contains(actor.Name, "moblin") || Contains(actor.Name, "lizalfos"));
        }

        private static bool IsReward(RoomPackageActor actor)
        {
            return actor != null && (Contains(actor.Role, "reward") || Contains(actor.Role, "chest") || Contains(actor.Name, "chest") ||
                (actor.GrantedItems != null && actor.GrantedItems.Count > 0));
        }

        private static bool IsPuzzle(RoomPackageActor actor)
        {
            return actor != null && (Contains(actor.Role, "puzzle") || Contains(actor.Role, "switch") || Contains(actor.Name, "switch") || Contains(actor.Name, "target"));
        }

        private static bool Contains(string value, string part)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
