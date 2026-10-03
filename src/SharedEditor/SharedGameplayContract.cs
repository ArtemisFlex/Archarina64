using System;
using System.Collections.Generic;

namespace SharpOcarina.Shared
{
    /// <summary>
    /// Renderer- and ROM-neutral gameplay data shared by the dungeon and
    /// scene/modeling editors. Native materialization remains editor-owned.
    /// </summary>
    [Serializable]
    public enum SharedLogicLinkKind
    {
        Activates, Opens, Closes, Unlocks, Spawns, Enables, Disables,
        StartsPath, StartsCutscene, CompletesPuzzle, SetsFlag, ChecksFlag,
        GrantsItem
    }

    [Serializable]
    public sealed class SharedGameplayFlag
    {
        public string Kind, Property, Target, Evidence;
        public int Value, Position;
        public ushort Mask;
        public bool CanSet, CanCheck;

        public SharedGameplayFlag Clone()
        {
            return (SharedGameplayFlag)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class SharedGameplayActor
    {
        public string Id, Role, Name, RequirementText;
        public int SourceIndex, SourceRoom;
        public ushort Number, Variable;
        public double X, Y, Z, RotationX, RotationY, RotationZ;
        public bool IsTransition;
        public byte FrontRoom = 0xFF, BackRoom = 0xFF, FrontCamera = 0xFF, BackCamera = 0xFF;
        public List<ushort> RequiredObjects = new List<ushort>();
        public List<string> RequiredItems = new List<string>();
        public List<string> GrantedItems = new List<string>();
        public List<SharedGameplayFlag> Flags = new List<SharedGameplayFlag>();

        public SharedGameplayActor Clone()
        {
            SharedGameplayActor clone = (SharedGameplayActor)MemberwiseClone();
            clone.RequiredObjects = new List<ushort>(RequiredObjects ?? new List<ushort>());
            clone.RequiredItems = new List<string>(RequiredItems ?? new List<string>());
            clone.GrantedItems = new List<string>(GrantedItems ?? new List<string>());
            clone.Flags = new List<SharedGameplayFlag>();
            foreach (SharedGameplayFlag flag in Flags ?? new List<SharedGameplayFlag>())
                clone.Flags.Add(flag == null ? null : flag.Clone());
            return clone;
        }
    }

    [Serializable]
    public sealed class SharedGameplayLink
    {
        public string SourceId, TargetId, Parameter;
        public SharedLogicLinkKind Kind;

        public SharedGameplayLink Clone()
        {
            return (SharedGameplayLink)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class SharedGameplayPackage
    {
        public int Version = 1, Scene, Room, DestinationRoom = -1, SourceTransition = -1;
        public string PlacementId, RomFingerprint, Name;
        public bool LogicOnly;
        public List<SharedGameplayActor> Actors = new List<SharedGameplayActor>();
        public List<SharedGameplayActor> Spawns = new List<SharedGameplayActor>();
        public List<SharedGameplayLink> Links = new List<SharedGameplayLink>();
        public List<string> Warnings = new List<string>();
        public List<string> Suggestions = new List<string>();

        public SharedGameplayPackage Clone()
        {
            SharedGameplayPackage clone = (SharedGameplayPackage)MemberwiseClone();
            clone.Actors = new List<SharedGameplayActor>();
            foreach (SharedGameplayActor actor in Actors ?? new List<SharedGameplayActor>())
                clone.Actors.Add(actor == null ? null : actor.Clone());
            clone.Spawns = new List<SharedGameplayActor>();
            foreach (SharedGameplayActor actor in Spawns ?? new List<SharedGameplayActor>())
                clone.Spawns.Add(actor == null ? null : actor.Clone());
            clone.Links = new List<SharedGameplayLink>();
            foreach (SharedGameplayLink link in Links ?? new List<SharedGameplayLink>())
                clone.Links.Add(link == null ? null : link.Clone());
            clone.Warnings = new List<string>(Warnings ?? new List<string>());
            clone.Suggestions = new List<string>(Suggestions ?? new List<string>());
            return clone;
        }
    }
}
