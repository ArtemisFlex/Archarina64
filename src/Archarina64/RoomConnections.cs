using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK;

namespace SharpOcarina
{
    public static class RoomConnections
    {
        // OoT injects the transition list index into actor params bits 10-15.
        public const int MaximumConnections = 64;

        public static ZActor Prepare(ZScene scene, RomPaletteAsset preset, int front, int back,
            Vector3d position, double yawDegrees, ISet<ushort> availableObjects)
        {
            if (scene == null || preset == null || !preset.Transition)
                throw new InvalidOperationException("Choose a scene and a connection preset.");
            if (front < 0 || back < 0 || front > 127 || back > 127 || front >= scene.Rooms.Count || back >= scene.Rooms.Count || front == back)
                throw new InvalidOperationException("Choose two different existing rooms (indices 0-127).");
            if (scene.Transitions.Count >= MaximumConnections)
                throw new InvalidOperationException("OoT supports at most 64 indexed transition actors.");
            if (preset.Variable > 0x03FF)
                throw new InvalidOperationException("This preset uses reserved transition-index bits; it needs an explicit parameter mapping.");
            if (new[] { position.X, position.Y, position.Z }.Any(x => double.IsNaN(x) || double.IsInfinity(x) || x < short.MinValue || x > short.MaxValue))
                throw new InvalidOperationException("Connection position is outside the N64 coordinate range.");
            if (double.IsNaN(yawDegrees) || double.IsInfinity(yawDegrees))
                throw new InvalidOperationException("Connection facing must be a finite angle.");
            ushort[] required = preset.Objects.Where(id => id > 3).Distinct().ToArray();
            if (required.Length > 0 && (availableObjects == null || required.Any(id => !availableObjects.Contains(id))))
                throw new InvalidOperationException("A required object bank is unavailable in the source ROM.");
            foreach (int room in new[] { front, back })
                if (scene.Rooms[room].ZObjects.Count + required.Count(id => !scene.Rooms[room].ZObjects.Any(o => o.Value == id)) > 255)
                    throw new InvalidOperationException("A connected room would exceed its object-list limit.");
            short angle = unchecked((short)(int)Math.Round((yawDegrees % 360) * 65536 / 360));
            return new ZActor((byte)front, 255, (byte)back, 255, preset.Id,
                (float)Math.Round(position.X), (float)Math.Round(position.Y), (float)Math.Round(position.Z), angle, preset.Variable);
        }

        internal static void Add(ZScene scene, RomPaletteAsset preset, ZActor connection)
        {
            foreach (int index in new[] { (int)connection.FrontSwitchTo, (int)connection.BackSwitchTo })
                foreach (ushort id in preset.Objects.Where(id => id > 3).Distinct())
                    if (!scene.Rooms[index].ZObjects.Any(o => o.Value == id)) scene.Rooms[index].ZObjects.Add(new ZScene.ZUShort(id));
            scene.Transitions.Add(connection);
        }
    }
}
