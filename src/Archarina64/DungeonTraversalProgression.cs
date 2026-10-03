using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpOcarina
{
    [Serializable]
    public sealed class DungeonTraversalProgressionReport
    {
        public bool Solvable;
        public int CheckedRooms;
        public List<string> AcquiredCapabilities = new List<string>();
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    public static class DungeonTraversalProgression
    {
        public static DungeonTraversalProgressionReport Check(ProceduralDungeonPlan plan)
        {
            DungeonTraversalProgressionReport result = new DungeonTraversalProgressionReport();
            if (plan == null) { result.Errors.Add("A generated dungeon plan is required."); return result; }
            HashSet<string> items = new HashSet<string>(plan.StartingItems ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            DungeonTraversalCalibration calibration = plan.TraversalCalibration ?? DungeonTraversalCalibration.ConservativeOoT();
            HashSet<DungeonTraversalCapabilities> capabilities = new HashSet<DungeonTraversalCapabilities>();
            capabilities.Add(DungeonTraversalCapabilities.Walk);
            DungeonProgressionReport progression = plan.Connections != null && plan.Connections.Count > 0 ? DungeonProgressionSolver.Solve(null, plan) : null;
            for (int index = 0; index < plan.Rooms.Count; index++)
            {
                DungeonRoomPlan room = plan.Rooms[index];
                result.CheckedRooms++;
                HashSet<string> roomItems = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
                if (progression != null)
                {
                    List<string> reachableItems;
                    if (progression.ItemsAtRoom.TryGetValue(index, out reachableItems))
                        foreach (string item in reachableItems) roomItems.Add(NormalizeItem(item));
                }
                foreach (DungeonTraversalConstraint constraint in room.TraversalConstraints ?? new List<DungeonTraversalConstraint>())
                {
                    string geometryError;
                    if (!DungeonTraversalGeometry.Validate(constraint, room.BoundsMin, room.BoundsMax, room.CollisionVolumes, out geometryError))
                    {
                        result.Errors.Add("Room " + index + " has an invalid " + constraint.Kind + " geometry: " + geometryError);
                        continue;
                    }
                    string requiredItem = NormalizeItem(constraint.RequiredItem);
                    if (!string.IsNullOrEmpty(requiredItem) && !roomItems.Contains(requiredItem))
                    {
                        result.Errors.Add("Room " + index + " requires " + constraint.RequiredItem + " before its " + constraint.Kind + " route is usable.");
                        continue;
                    }
                    double limit = calibration.LimitFor(constraint.Kind, constraint.RequiredItem);
                    if (limit > 0 && !double.IsInfinity(limit) && constraint.HorizontalDistance > limit && constraint.Kind != "PushBlock")
                        result.Errors.Add("Room " + index + " has a " + constraint.Kind + " span of " + Math.Round(constraint.HorizontalDistance) + " beyond the " + calibration.Name + " limit of " + Math.Round(limit) + ".");
                    if ((constraint.Kind.Equals("Stairs", StringComparison.OrdinalIgnoreCase) || constraint.Kind.Equals("Ramp", StringComparison.OrdinalIgnoreCase)) &&
                        constraint.HorizontalDistance > 0 && constraint.VerticalDistance > 0)
                    {
                        double slope = constraint.VerticalDistance / constraint.HorizontalDistance;
                        if (slope > calibration.MaxRampSlope)
                            result.Errors.Add("Room " + index + " has a " + constraint.Kind + " slope of " + Math.Round(slope, 2) + " beyond the " + calibration.Name + " maximum of " + Math.Round(calibration.MaxRampSlope, 2) + ".");
                    }
                    AddCapability(capabilities, constraint.Kind);
                }
                if (progression == null)
                    foreach (string item in room.GrantedItems ?? new List<string>()) items.Add(NormalizeItem(item));
                AddCapabilities(capabilities, room.TraversalCapabilities);
            }
            result.AcquiredCapabilities.AddRange(capabilities.Select(value => value.ToString()).OrderBy(value => value));
            result.Solvable = result.Errors.Count == 0;
            return result;
        }

        private static void AddCapabilities(HashSet<DungeonTraversalCapabilities> capabilities, DungeonTraversalCapabilities value)
        {
            foreach (DungeonTraversalCapabilities capability in Enum.GetValues(typeof(DungeonTraversalCapabilities)))
                if (capability != DungeonTraversalCapabilities.None && (value & capability) == capability) capabilities.Add(capability);
        }

        private static void AddCapability(HashSet<DungeonTraversalCapabilities> capabilities, string kind)
        {
            string value = kind ?? "";
            if (value.Equals("JumpGap", StringComparison.OrdinalIgnoreCase)) capabilities.Add(DungeonTraversalCapabilities.JumpGap);
            else if (value.Equals("VineClimb", StringComparison.OrdinalIgnoreCase)) capabilities.Add(DungeonTraversalCapabilities.ClimbVines);
            else if (value.Equals("Hookshot", StringComparison.OrdinalIgnoreCase)) capabilities.Add(DungeonTraversalCapabilities.Hookshot);
            else if (value.Equals("PushBlock", StringComparison.OrdinalIgnoreCase)) capabilities.Add(DungeonTraversalCapabilities.PushBlock);
            else if (value.Equals("Stairs", StringComparison.OrdinalIgnoreCase)) capabilities.Add(DungeonTraversalCapabilities.Stairs);
            else if (value.Equals("FallDrop", StringComparison.OrdinalIgnoreCase)) capabilities.Add(DungeonTraversalCapabilities.FallDrop);
            else if (value.Equals("Swim", StringComparison.OrdinalIgnoreCase)) capabilities.Add(DungeonTraversalCapabilities.Swim);
        }

        private static string NormalizeItem(string value)
        {
            string result = (value ?? "").Trim();
            if (result.StartsWith("DUNGEON ", StringComparison.OrdinalIgnoreCase)) result = result.Substring("DUNGEON ".Length).Trim();
            return result;
        }
    }
}
