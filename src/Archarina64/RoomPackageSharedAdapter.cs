using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    /// <summary>
    /// Converts Archarina's native gameplay package to the shared pure-data
    /// contract without exposing ZScene, OpenTK, or ROM materializers to the
    /// other editors.
    /// </summary>
    public static class RoomPackageSharedAdapter
    {
        public static SharedGameplayPackage Export(RoomPackage package)
        {
            if (package == null) throw new ArgumentNullException("package");
            SharedGameplayPackage result = new SharedGameplayPackage
            {
                Version = package.Version,
                Scene = package.Scene,
                Room = package.Room,
                DestinationRoom = package.DestinationRoom,
                SourceTransition = package.SourceTransition,
                PlacementId = package.PlacementId,
                LogicOnly = package.LogicOnly,
                RomFingerprint = package.RomFingerprint,
                Name = package.Name
            };
            foreach (RoomPackageActor actor in package.Actors ?? new List<RoomPackageActor>()) result.Actors.Add(ExportActor(actor));
            foreach (RoomPackageActor actor in package.Spawns ?? new List<RoomPackageActor>()) result.Spawns.Add(ExportActor(actor));
            foreach (LogicLink link in package.Links ?? new List<LogicLink>())
                result.Links.Add(new SharedGameplayLink
                {
                    SourceId = link.SourceId, TargetId = link.TargetId, Parameter = link.Parameter,
                    Kind = (SharedLogicLinkKind)Enum.Parse(typeof(SharedLogicLinkKind), link.Kind.ToString())
                });
            if (package.Warnings != null) result.Warnings.AddRange(package.Warnings);
            if (package.Suggestions != null) result.Suggestions.AddRange(package.Suggestions);
            return result;
        }

        public static RoomPackage Import(SharedGameplayPackage package)
        {
            if (package == null) throw new ArgumentNullException("package");
            RoomPackage result = new RoomPackage
            {
                Version = package.Version,
                Scene = package.Scene,
                Room = package.Room,
                DestinationRoom = package.DestinationRoom,
                SourceTransition = package.SourceTransition,
                PlacementId = package.PlacementId,
                LogicOnly = package.LogicOnly,
                RomFingerprint = package.RomFingerprint,
                Name = package.Name
            };
            foreach (SharedGameplayActor actor in package.Actors ?? new List<SharedGameplayActor>()) result.Actors.Add(ImportActor(actor));
            foreach (SharedGameplayActor actor in package.Spawns ?? new List<SharedGameplayActor>()) result.Spawns.Add(ImportActor(actor));
            foreach (SharedGameplayLink link in package.Links ?? new List<SharedGameplayLink>())
                result.Links.Add(new LogicLink
                {
                    SourceId = link.SourceId, TargetId = link.TargetId, Parameter = link.Parameter,
                    Kind = (LogicLinkKind)Enum.Parse(typeof(LogicLinkKind), link.Kind.ToString())
                });
            if (package.Warnings != null) result.Warnings.AddRange(package.Warnings);
            if (package.Suggestions != null) result.Suggestions.AddRange(package.Suggestions);
            return result;
        }

        private static SharedGameplayActor ExportActor(RoomPackageActor actor)
        {
            if (actor == null) return null;
            SharedGameplayActor result = new SharedGameplayActor
            {
                Id = actor.Id, Role = actor.Role, Name = actor.Name, RequirementText = actor.RequirementText,
                SourceIndex = actor.SourceIndex, SourceRoom = actor.SourceRoom, Number = actor.Number, Variable = actor.Variable,
                X = actor.Position.X, Y = actor.Position.Y, Z = actor.Position.Z,
                RotationX = actor.Rotation.X, RotationY = actor.Rotation.Y, RotationZ = actor.Rotation.Z,
                IsTransition = actor.IsTransition, FrontRoom = actor.FrontRoom, BackRoom = actor.BackRoom,
                FrontCamera = actor.FrontCamera, BackCamera = actor.BackCamera
            };
            if (actor.RequiredObjects != null) result.RequiredObjects.AddRange(actor.RequiredObjects);
            if (actor.RequiredItems != null) result.RequiredItems.AddRange(actor.RequiredItems);
            if (actor.GrantedItems != null) result.GrantedItems.AddRange(actor.GrantedItems);
            foreach (DungeonFlagReference flag in actor.Flags ?? new List<DungeonFlagReference>())
                result.Flags.Add(new SharedGameplayFlag
                {
                    Kind = flag.Kind, Property = flag.Property, Target = flag.Target, Evidence = flag.Evidence,
                    Value = flag.Value, Position = flag.Position, Mask = flag.Mask, CanSet = flag.CanSet, CanCheck = flag.CanCheck
                });
            return result;
        }

        private static RoomPackageActor ImportActor(SharedGameplayActor actor)
        {
            if (actor == null) return null;
            RoomPackageActor result = new RoomPackageActor
            {
                Id = actor.Id, Role = actor.Role, Name = actor.Name, RequirementText = actor.RequirementText,
                SourceIndex = actor.SourceIndex, SourceRoom = actor.SourceRoom, Number = actor.Number, Variable = actor.Variable,
                Position = new Vector3d(actor.X, actor.Y, actor.Z), Rotation = new Vector3((float)actor.RotationX, (float)actor.RotationY, (float)actor.RotationZ),
                IsTransition = actor.IsTransition, FrontRoom = actor.FrontRoom, BackRoom = actor.BackRoom,
                FrontCamera = actor.FrontCamera, BackCamera = actor.BackCamera
            };
            if (actor.RequiredObjects != null) result.RequiredObjects.AddRange(actor.RequiredObjects);
            if (actor.RequiredItems != null) result.RequiredItems.AddRange(actor.RequiredItems);
            if (actor.GrantedItems != null) result.GrantedItems.AddRange(actor.GrantedItems);
            foreach (SharedGameplayFlag flag in actor.Flags ?? new List<SharedGameplayFlag>())
                result.Flags.Add(new DungeonFlagReference
                {
                    Kind = flag.Kind, Property = flag.Property, Target = flag.Target, Evidence = flag.Evidence,
                    Value = flag.Value, Position = flag.Position, Mask = flag.Mask, CanSet = flag.CanSet, CanCheck = flag.CanCheck
                });
            return result;
        }
    }
}
