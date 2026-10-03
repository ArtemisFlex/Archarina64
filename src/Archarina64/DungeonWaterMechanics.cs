using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK;

namespace SharpOcarina
{
    [Serializable]
    public sealed class DungeonWaterVolume
    {
        public int Room;
        public Vector3d Min, Max;
        public double SurfaceY, EstimatedFloorY, Depth;
        public byte Environment, Camera;
        public ushort Properties;
    }

    [Serializable]
    public sealed class DungeonWaterProfile
    {
        public int VolumeCount;
        public List<double> SurfaceLevels = new List<double>();
        public List<DungeonWaterVolume> Volumes = new List<DungeonWaterVolume>();
        public bool HasSwimSpace, HasDeepWater, RequiresZoraTunic, RequiresIronBoots;
        public string Evidence;
    }

    public static class DungeonWaterMechanics
    {
        public static DungeonWaterProfile Analyze(ZScene scene, int roomId, double estimatedFloorY)
        {
            DungeonWaterProfile result = new DungeonWaterProfile();
            if (scene == null || scene.Waterboxes == null) return result;
            foreach (ZWaterbox waterbox in scene.Waterboxes.Where(w => w.Room == roomId || w.Room == 0x3F))
            {
                double surface = waterbox.YPos, floor = Math.Min(estimatedFloorY, surface);
                result.Volumes.Add(new DungeonWaterVolume
                {
                    Room = roomId,
                    Min = new Vector3d(waterbox.XPos, floor, waterbox.ZPos),
                    Max = new Vector3d(waterbox.XPos + waterbox.XSize, surface, waterbox.ZPos + waterbox.ZSize),
                    SurfaceY = surface, EstimatedFloorY = floor, Depth = Math.Max(0, surface - floor),
                    Environment = waterbox.Env, Camera = waterbox.Camera, Properties = waterbox.Properties
                });
            }
            result.VolumeCount = result.Volumes.Count;
            result.SurfaceLevels = result.Volumes.Select(v => v.SurfaceY).Distinct().OrderBy(v => v).ToList();
            result.HasSwimSpace = result.Volumes.Any(v => v.Depth >= 40 && v.Max.X > v.Min.X && v.Max.Z > v.Min.Z);
            result.HasDeepWater = result.Volumes.Any(v => v.Depth >= 120);
            result.RequiresZoraTunic = result.HasDeepWater;
            result.RequiresIronBoots = result.HasDeepWater;
            if (result.VolumeCount > 0)
                result.Evidence = string.Format("{0} water volume(s), {1} distinct surface level(s), deepest estimated depth {2}.", result.VolumeCount, result.SurfaceLevels.Count, Math.Round(result.Volumes.Max(v => v.Depth)));
            return result;
        }

        public static void AddTraversalContracts(DungeonRoomAnalysis room, DungeonWaterProfile profile)
        {
            if (room == null || profile == null || !profile.HasSwimSpace) return;
            DungeonWaterVolume volume = profile.Volumes.OrderByDescending(v => v.Depth).First();
            Vector3d center = new Vector3d((volume.Min.X + volume.Max.X) * 0.5, volume.SurfaceY, (volume.Min.Z + volume.Max.Z) * 0.5);
            room.TraversalCapabilities |= DungeonTraversalCapabilities.Swim;
            room.TraversalConstraints.Add(new DungeonTraversalConstraint
            {
                Kind = "Swim", Evidence = profile.Evidence, SourceRoom = room.RoomId,
                Start = center, End = center, HorizontalDistance = 0, VerticalDistance = 0, MaxDistance = double.PositiveInfinity,
                Confidence = 0.8, Optional = true
            });
            if (!profile.HasDeepWater) return;
            room.TraversalConstraints.Add(new DungeonTraversalConstraint
            {
                Kind = "Underwater", Evidence = "Deep water requires underwater breathing for sustained traversal.", RequiredItem = "Zora Tunic",
                SourceRoom = room.RoomId, Start = center, End = new Vector3d(center.X, volume.EstimatedFloorY, center.Z),
                HorizontalDistance = 0, VerticalDistance = volume.Depth, MaxDistance = volume.Depth, Confidence = 0.7
            });
            room.TraversalConstraints.Add(new DungeonTraversalConstraint
            {
                Kind = "Sink", Evidence = "Deep-water route can use Iron Boots to sink and walk on the floor.", RequiredItem = "Iron Boots",
                SourceRoom = room.RoomId, Start = center, End = new Vector3d(center.X, volume.EstimatedFloorY, center.Z),
                HorizontalDistance = 0, VerticalDistance = volume.Depth, MaxDistance = volume.Depth, Confidence = 0.7, Optional = true
            });
        }
    }
}
