using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpOcarina.Shared
{
    /// <summary>
    /// Neutral scene/layout data exchanged by the editors. Native ZScene
    /// instances stay owned by their editor; this contract carries only the
    /// pieces another editor can safely interpret.
    /// </summary>
    [Serializable]
    public sealed class SharedSceneHandoff
    {
        public int Version = 1;
        public Guid WorkspaceId;
        public Guid DocumentId;
        public string Publisher, SceneName, SourcePath;
        public int Revision;
        public List<SharedRoomHandoff> Rooms = new List<SharedRoomHandoff>();
        public List<SharedConnectionHandoff> Connections = new List<SharedConnectionHandoff>();
        public List<SharedAssetReference> Assets = new List<SharedAssetReference>();
        public List<SharedGameplayPackage> Gameplay = new List<SharedGameplayPackage>();

        public void Validate(Guid expectedWorkspaceId)
        {
            if (Version != 1) throw new InvalidOperationException("Unsupported shared scene handoff version.");
            if (expectedWorkspaceId == Guid.Empty || WorkspaceId != expectedWorkspaceId)
                throw new InvalidOperationException("The scene handoff belongs to a different editor workspace.");
            if (DocumentId == Guid.Empty) throw new InvalidOperationException("The scene handoff has no document identity.");
            if (string.IsNullOrWhiteSpace(Publisher)) throw new InvalidOperationException("The scene handoff has no publisher.");
            if (Rooms == null || Connections == null || Assets == null || Gameplay == null)
                throw new InvalidOperationException("The scene handoff contains a missing collection.");

            HashSet<string> roomIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> doorwayIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> connectionIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> roomIndexes = new HashSet<int>();
            foreach (SharedRoomHandoff room in Rooms ?? Enumerable.Empty<SharedRoomHandoff>())
            {
                if (room == null || string.IsNullOrWhiteSpace(room.Id) || !roomIds.Add(room.Id))
                    throw new InvalidOperationException("The scene handoff contains a duplicate or missing room id.");
                if (room.RoomIndex < 0 || double.IsNaN(room.X) || double.IsNaN(room.Y) || double.IsNaN(room.Z) || double.IsNaN(room.Scale) ||
                    double.IsInfinity(room.X) || double.IsInfinity(room.Y) || double.IsInfinity(room.Z) || double.IsInfinity(room.Scale) || room.Scale <= 0)
                    throw new InvalidOperationException("The scene handoff contains an invalid room transform.");
                roomIndexes.Add(room.RoomIndex);
                foreach (SharedDoorwayHandoff doorway in room.Doorways ?? Enumerable.Empty<SharedDoorwayHandoff>())
                    if (doorway == null || string.IsNullOrWhiteSpace(doorway.Id) ||
                        !doorwayIds.Add(doorway.Id) ||
                        new[] { doorway.X, doorway.Y, doorway.Z, doorway.Facing, doorway.Width, doorway.Height }.Any(double.IsNaN) ||
                        new[] { doorway.X, doorway.Y, doorway.Z, doorway.Facing, doorway.Width, doorway.Height }.Any(double.IsInfinity) ||
                        doorway.Width <= 0 || doorway.Height <= 0)
                        throw new InvalidOperationException("The scene handoff contains an invalid doorway.");
            }
            foreach (SharedConnectionHandoff connection in Connections ?? Enumerable.Empty<SharedConnectionHandoff>())
                if (connection == null || string.IsNullOrWhiteSpace(connection.Id) || !connectionIds.Add(connection.Id) ||
                    connection.FromRoom < 0 || connection.ToRoom < 0 || connection.FromRoom == connection.ToRoom ||
                    !roomIndexes.Contains(connection.FromRoom) || !roomIndexes.Contains(connection.ToRoom) ||
                    double.IsNaN(connection.X) || double.IsNaN(connection.Y) || double.IsNaN(connection.Z) || double.IsNaN(connection.Yaw) ||
                    double.IsInfinity(connection.X) || double.IsInfinity(connection.Y) || double.IsInfinity(connection.Z) || double.IsInfinity(connection.Yaw))
                    throw new InvalidOperationException("The scene handoff contains an invalid connection.");
        }

        public SharedSceneHandoff Clone()
        {
            SharedSceneHandoff clone = new SharedSceneHandoff
            {
                Version = Version, WorkspaceId = WorkspaceId, DocumentId = DocumentId,
                Publisher = Publisher, SceneName = SceneName, SourcePath = SourcePath, Revision = Revision
            };
            foreach (SharedRoomHandoff room in Rooms ?? new List<SharedRoomHandoff>()) clone.Rooms.Add(room == null ? null : room.Clone());
            foreach (SharedConnectionHandoff connection in Connections ?? new List<SharedConnectionHandoff>()) clone.Connections.Add(connection == null ? null : connection.Clone());
            foreach (SharedAssetReference asset in Assets ?? new List<SharedAssetReference>()) clone.Assets.Add(asset == null ? null : asset.Clone());
            foreach (SharedGameplayPackage package in Gameplay ?? new List<SharedGameplayPackage>()) clone.Gameplay.Add(package == null ? null : package.Clone());
            return clone;
        }
    }

    [Serializable]
    public sealed class SharedRoomHandoff
    {
        public string Id, Name, GeometryAssetPath;
        public int RoomIndex, Turns, SurfaceOverride = -1;
        public double X, Y, Z, Scale = 1.0;
        public bool IsStart, IsFinal;
        public List<SharedDoorwayHandoff> Doorways = new List<SharedDoorwayHandoff>();

        public SharedRoomHandoff Clone()
        {
            SharedRoomHandoff clone = (SharedRoomHandoff)MemberwiseClone();
            clone.Doorways = (Doorways ?? new List<SharedDoorwayHandoff>()).Select(x => x == null ? null : x.Clone()).ToList();
            return clone;
        }
    }

    [Serializable]
    public sealed class SharedDoorwayHandoff
    {
        public string Id, Name, ConnectedTo;
        public int RoomIndex;
        public double X, Y, Z, Facing, Width = 100, Height = 180;

        public SharedDoorwayHandoff Clone() { return (SharedDoorwayHandoff)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class SharedConnectionHandoff
    {
        public string Id, SourceDoorwayId, TargetDoorwayId;
        public int FromRoom, ToRoom;
        public ushort ActorNumber, Variable;
        public byte FrontCamera = 0xFF, BackCamera = 0xFF;
        public double X, Y, Z, Yaw;
        public bool OneWay;

        public SharedConnectionHandoff Clone() { return (SharedConnectionHandoff)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class SharedAssetReference
    {
        public string Path, Kind, Name;

        public SharedAssetReference Clone() { return (SharedAssetReference)MemberwiseClone(); }
    }

    public sealed class SharedSceneHandoffEventArgs : EventArgs
    {
        public SharedSceneHandoffEventArgs(SharedSceneHandoff handoff) { Handoff = handoff; }
        public SharedSceneHandoff Handoff { get; private set; }
    }

}
