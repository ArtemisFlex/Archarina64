using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpOcarina
{
    [Serializable]
    public sealed class DungeonItemArcStep
    {
        public string Item;
        public int AcquisitionRoom = -1;
        public int FirstConsumerRoom = -1;
        public bool StartingItem;
        public List<int> ConsumerRooms = new List<int>();
        public List<string> RequiredBefore = new List<string>();
        public string Evidence;
    }

    [Serializable]
    public sealed class DungeonItemArcReport
    {
        public bool Solvable;
        public bool HasMidDungeonAcquisition;
        public int MidDungeonAcquisitionCount;
        public List<DungeonItemArcStep> Steps = new List<DungeonItemArcStep>();
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();

        public string Summary
        {
            get
            {
                return string.Format("{0} item arc{1}, {2} mid-dungeon acquisition{3}", Steps.Count,
                    Steps.Count == 1 ? "" : "s", MidDungeonAcquisitionCount,
                    MidDungeonAcquisitionCount == 1 ? "" : "s");
            }
        }
    }

    public static class DungeonItemArcPlanner
    {
        private static readonly string[] NonArcRewards = { "small key", "boss key", "map", "compass", "heart piece", "heart container" };

        public static DungeonItemArcReport Analyze(ProceduralDungeonPlan plan)
        {
            DungeonItemArcReport result = new DungeonItemArcReport();
            if (plan == null || plan.Rooms == null || plan.Rooms.Count == 0)
            {
                result.Errors.Add("The dungeon has no rooms from which to build an item arc.");
                return result;
            }

            HashSet<string> starting = new HashSet<string>(plan.StartingItems ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<int>> acquisitions = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, HashSet<int>> consumers = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (DungeonRoomPlan room in plan.Rooms)
            {
                foreach (string reward in room.GrantedItems ?? new List<string>())
                {
                    string item = Normalize(reward);
                    if (!IsArcReward(item)) continue;
                    if (!acquisitions.ContainsKey(item)) acquisitions.Add(item, new List<int>());
                    if (!acquisitions[item].Contains(room.Index)) acquisitions[item].Add(room.Index);
                }
                AddConsumers(consumers, room.RequiredItems, room.Index);
                foreach (DungeonTraversalConstraint constraint in room.TraversalConstraints ?? new List<DungeonTraversalConstraint>())
                    if (!constraint.Optional) AddConsumer(consumers, constraint.RequiredItem, room.Index);
            }
            foreach (DungeonConnectionPlan connection in plan.Connections)
                AddConsumers(consumers, connection.RequiredItems, connection.ToRoom);

            HashSet<string> items = new HashSet<string>(acquisitions.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (string item in consumers.Keys) items.Add(item);
            foreach (string item in starting) items.Add(Normalize(item));
            foreach (string item in items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                List<int> acquired = acquisitions.ContainsKey(item) ? acquisitions[item].OrderBy(x => x).ToList() : new List<int>();
                List<int> usedBy = consumers.ContainsKey(item) ? consumers[item].OrderBy(x => x).ToList() : new List<int>();
                bool isStarting = starting.Contains(item);
                DungeonItemArcStep step = new DungeonItemArcStep
                {
                    Item = item,
                    AcquisitionRoom = acquired.Count == 0 ? (isStarting ? 0 : -1) : acquired[0],
                    FirstConsumerRoom = usedBy.Count == 0 ? -1 : usedBy[0],
                    StartingItem = isStarting,
                    ConsumerRooms = usedBy,
                    Evidence = acquired.Count > 0 ? "Room reward" : isStarting ? "Starting inventory" : "Required by generated content"
                };
                result.Steps.Add(step);
                if (usedBy.Count == 0) continue;
                if (!isStarting && acquired.Count == 0)
                {
                    result.Errors.Add("Item arc is missing an acquisition for " + item + ".");
                    continue;
                }
                if (!isStarting && acquired.All(room => usedBy.Any(consumer => consumer <= room)))
                    result.Warnings.Add("Item " + item + " is only acquired after an earlier consumer; check the intended alternate route.");
                if (!isStarting && acquired.Any(room => room > 0) && usedBy.Any(consumer => consumer > acquired.First()))
                {
                    result.HasMidDungeonAcquisition = true;
                    result.MidDungeonAcquisitionCount++;
                }
                foreach (string dependency in RequiredItemsBefore(plan, step.AcquisitionRoom))
                    if (!step.RequiredBefore.Contains(dependency, StringComparer.OrdinalIgnoreCase)) step.RequiredBefore.Add(dependency);
            }

            List<DungeonItemArcStep> consumedArcs = result.Steps.Where(step => step.ConsumerRooms.Count > 0).ToList();
            if (consumedArcs.Count == 0) result.Warnings.Add("No later room or connection consumes a non-key item.");
            if (!result.HasMidDungeonAcquisition) result.Warnings.Add("No item is acquired after the starting room and used later in the dungeon.");
            result.Solvable = result.Errors.Count == 0;
            return result;
        }

        private static IEnumerable<string> RequiredItemsBefore(ProceduralDungeonPlan plan, int room)
        {
            if (room < 0 || plan == null || plan.Rooms == null || room >= plan.Rooms.Count) yield break;
            foreach (string item in plan.Rooms[room].RequiredItems ?? new List<string>())
            {
                string normalized = Normalize(item);
                if (!string.IsNullOrWhiteSpace(normalized) && !IsKeyLike(normalized)) yield return normalized;
            }
            foreach (DungeonConnectionPlan connection in plan.Connections ?? new List<DungeonConnectionPlan>())
                if (connection.ToRoom == room)
                    foreach (string item in connection.RequiredItems ?? new List<string>())
                    {
                        string normalized = Normalize(item);
                        if (!string.IsNullOrWhiteSpace(normalized) && !IsKeyLike(normalized)) yield return normalized;
                    }
        }

        private static void AddConsumers(Dictionary<string, HashSet<int>> consumers, IEnumerable<string> items, int room)
        {
            foreach (string item in items ?? Enumerable.Empty<string>()) AddConsumer(consumers, item, room);
        }

        private static void AddConsumer(Dictionary<string, HashSet<int>> consumers, string item, int room)
        {
            item = Normalize(item);
            if (string.IsNullOrWhiteSpace(item) || IsKeyLike(item)) return;
            if (!consumers.ContainsKey(item)) consumers.Add(item, new HashSet<int>());
            consumers[item].Add(room);
        }

        private static bool IsArcReward(string item)
        {
            return !string.IsNullOrWhiteSpace(item) && !IsKeyLike(item) && !NonArcRewards.Any(x => item.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsKeyLike(string item)
        {
            return item.IndexOf("small key", StringComparison.OrdinalIgnoreCase) >= 0 || item.IndexOf("boss key", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Normalize(string item)
        {
            string value = (item ?? "").Trim();
            if (value.StartsWith("DUNGEON ", StringComparison.OrdinalIgnoreCase)) value = value.Substring("DUNGEON ".Length).Trim();
            return value;
        }
    }
}
