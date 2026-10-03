using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Serialization;

namespace SharpOcarina
{
    public sealed class SceneTileSource
    {
        public string RomFingerprint;
        public int SceneId;
        public int RoomId;
        public int Setup;
        public uint Start;
        public uint End;
        public override string ToString()
        {
            return string.Format("Scene {0:X2} - {1} / Room {2:D2}", SceneId, OoTSceneNames.Get(SceneId), RoomId);
        }
    }

    public sealed class SceneTileLibrary
    {
        public int Version = 1;
        public string RomFingerprint;
        public List<SceneTileSource> Rooms = new List<SceneTileSource>();
        public List<string> Diagnostics = new List<string>();

        public static SceneTileLibrary Read(byte[] rom, uint tableStart, uint tableEnd)
        {
            if (rom == null || rom.Length < 4 || Read32(rom, 0) != 0x80371240)
                throw new InvalidDataException("Select a big-endian, decompressed OoT ROM (.z64).");
            if (tableEnd <= tableStart || tableEnd > rom.Length || (tableEnd - tableStart) % 20 != 0)
                throw new InvalidDataException("The OoT scene table is outside this ROM or has an invalid length.");
            SceneTileLibrary library = new SceneTileLibrary();
            using (SHA256 hash = SHA256.Create())
                library.RomFingerprint = BitConverter.ToString(hash.ComputeHash(rom)).Replace("-", "").ToLowerInvariant();
            int scene = 0;
            for (long row = tableStart; row < tableEnd; row += 20, scene++)
            {
                uint start = Read32(rom, (int)row), end = Read32(rom, (int)row + 4);
                if (start == 0 && end == 0) continue;
                if (start >= end || end > rom.Length)
                {
                    library.Diagnostics.Add("Scene " + scene.ToString("X2") + ": invalid ROM range.");
                    continue;
                }
                bool found = false;
                for (long command = start; command + 8 <= end && command < start + 2048L; command += 8)
                {
                    int offset = (int)command;
                    if (rom[offset] == 0x14) break;
                    if (rom[offset] != 0x04) continue;
                    int count = rom[offset + 1];
                    uint pointer = Read32(rom, offset + 4);
                    long rooms = start + (pointer & 0xFFFFFF);
                    if ((pointer >> 24) != 2 || rooms < start || rooms + count * 8L > end)
                    {
                        library.Diagnostics.Add("Scene " + scene.ToString("X2") + ": invalid room table.");
                        break;
                    }
                    for (int room = 0; room < count; room++)
                    {
                        uint roomStart = Read32(rom, (int)rooms + room * 8);
                        uint roomEnd = Read32(rom, (int)rooms + room * 8 + 4);
                        // Room blobs are separate ROM files and normally sit outside
                        // the scene header range. Validate against the ROM, not the
                        // scene's own start/end range.
                        if (roomStart >= roomEnd || roomEnd > rom.Length)
                        {
                            library.Diagnostics.Add(string.Format("Scene {0:X2}, room {1}: invalid ROM range.", scene, room));
                            continue;
                        }
                        library.Rooms.Add(new SceneTileSource { RomFingerprint = library.RomFingerprint,
                            SceneId = scene, RoomId = room, Setup = 0, Start = roomStart, End = roomEnd });
                    }
                    found = true;
                    break;
                }
                if (!found) library.Diagnostics.Add("Scene " + scene.ToString("X2") + ": no supported main-header room list.");
            }
            return library;
        }

        public void Save(string directory)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, RomFingerprint + ".xml");
            using (FileStream stream = File.Create(path))
                new XmlSerializer(typeof(SceneTileLibrary)).Serialize(stream, this);
        }

        private static uint Read32(byte[] data, int offset)
        {
            return (uint)data[offset] << 24 | (uint)data[offset + 1] << 16 |
                (uint)data[offset + 2] << 8 | data[offset + 3];
        }
    }
}
