using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SharpOcarina
{
    [Serializable]
    public sealed class DungeonTraversalCalibration
    {
        public string Name = "OoT conservative authoring";
        public double JumpGap = 180;
        public double Hookshot = 500;
        public double Longshot = 800;
        public double VineRise = 800;
        public double FallDrop = 300;
        public double PushBlockTravel = 400;
        // Rise divided by horizontal run. Projects can lower this after
        // ROM/runtime traces are available.
        public double MaxRampSlope = 1.0;
        public List<string> Evidence = new List<string>();

        public static DungeonTraversalCalibration ConservativeOoT()
        {
            DungeonTraversalCalibration result = new DungeonTraversalCalibration();
            result.Evidence.Add("Authoring heuristic; calibrate against injected runtime traces before shipping a ROM.");
            return result;
        }

        public static DungeonTraversalCalibration FromRomMechanics(DungeonRomMechanicsProfile profile)
        {
            DungeonTraversalCalibration result = ConservativeOoT();
            if (profile == null) return result;
            result.Name = profile.Detection + " | " + (string.IsNullOrWhiteSpace(profile.GameVersion) ? "OoT" : profile.GameVersion);
            if (profile.IsEngineDerived)
            {
                result.Hookshot = profile.HookshotRange;
                result.Longshot = profile.LongshotRange;
            }
            if (profile.JumpPhysicsDerived) result.JumpGap = profile.JumpEnvelope;
            if (profile.Evidence != null) result.Evidence.AddRange(profile.Evidence);
            return result;
        }

        public double LimitFor(string kind, string requiredItem)
        {
            if (string.Equals(kind, "JumpGap", StringComparison.OrdinalIgnoreCase)) return JumpGap;
            if (string.Equals(kind, "Hookshot", StringComparison.OrdinalIgnoreCase))
                return string.Equals(requiredItem, "Longshot", StringComparison.OrdinalIgnoreCase) ? Longshot : Hookshot;
            if (string.Equals(kind, "VineClimb", StringComparison.OrdinalIgnoreCase)) return VineRise;
            if (string.Equals(kind, "FallDrop", StringComparison.OrdinalIgnoreCase)) return FallDrop;
            if (string.Equals(kind, "PushBlock", StringComparison.OrdinalIgnoreCase)) return PushBlockTravel;
            return double.PositiveInfinity;
        }

        public static DungeonTraversalCalibration FromTraces(string name, IEnumerable<DungeonTraversalTrace> traces)
        {
            DungeonTraversalCalibration result = ConservativeOoT();
            result.Name = string.IsNullOrWhiteSpace(name) ? "OoT trace-calibrated" : name.Trim();
            List<DungeonTraversalTrace> samples = (traces ?? new List<DungeonTraversalTrace>()).Where(IsUsable).ToList();
            foreach (IGrouping<string, DungeonTraversalTrace> group in samples.GroupBy(t => t.Kind ?? "", StringComparer.OrdinalIgnoreCase))
            {
                double successful = group.Where(t => t.Success).Select(t => t.HorizontalDistance).DefaultIfEmpty(0).Max();
                double failed = group.Where(t => !t.Success).Select(t => t.HorizontalDistance).Where(value => value > 0).DefaultIfEmpty(double.PositiveInfinity).Min();
                if (successful <= 0) continue;
                double upper = double.IsInfinity(failed) ? successful * 1.1 : Math.Min(failed * 0.95, successful * 1.1);
                double fitted = Math.Max(successful, upper);
                if (group.Key.Equals("JumpGap", StringComparison.OrdinalIgnoreCase)) result.JumpGap = fitted;
                else if (group.Key.Equals("Hookshot", StringComparison.OrdinalIgnoreCase)) result.Hookshot = fitted;
                else if (group.Key.Equals("Longshot", StringComparison.OrdinalIgnoreCase)) result.Longshot = fitted;
                else if (group.Key.Equals("VineClimb", StringComparison.OrdinalIgnoreCase)) result.VineRise = fitted;
                else if (group.Key.Equals("FallDrop", StringComparison.OrdinalIgnoreCase)) result.FallDrop = fitted;
                else if (group.Key.Equals("PushBlock", StringComparison.OrdinalIgnoreCase)) result.PushBlockTravel = fitted;
                result.Evidence.Add(group.Key + ": fitted from " + group.Count() + " trace samples; successful max " + Math.Round(successful) + ", nearest failure " + (double.IsInfinity(failed) ? "none" : Math.Round(failed).ToString()) + ".");
            }
            return result;
        }

        private static bool IsUsable(DungeonTraversalTrace trace)
        {
            return trace != null && !string.IsNullOrWhiteSpace(trace.Kind) && trace.HorizontalDistance > 0 &&
                !double.IsNaN(trace.HorizontalDistance) && !double.IsInfinity(trace.HorizontalDistance);
        }
    }

    [Serializable]
    public sealed class DungeonTraversalTrace
    {
        public string RomFingerprint, GameVersion, Scene, Room, Kind, RequiredItem, Source;
        public double HorizontalDistance, VerticalDistance;
        public bool Success;
        public string Notes;
    }

    [Serializable]
    public sealed class DungeonTraversalTraceSet
    {
        public string RomFingerprint, GameVersion, CapturedBy;
        public List<DungeonTraversalTrace> Samples = new List<DungeonTraversalTrace>();
    }

    public static class DungeonTraversalTraceStore
    {
        public static void Save(string path, DungeonTraversalTraceSet traces)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("A traversal trace path is required.");
            if (traces == null) throw new InvalidOperationException("Traversal traces are required.");
            using (FileStream stream = File.Create(path)) new XmlSerializer(typeof(DungeonTraversalTraceSet)).Serialize(stream, traces);
        }

        public static DungeonTraversalTraceSet Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("Traversal trace file was not found.", path);
            using (FileStream stream = File.OpenRead(path)) return (DungeonTraversalTraceSet)new XmlSerializer(typeof(DungeonTraversalTraceSet)).Deserialize(stream);
        }
    }
}
