using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SharpOcarina
{
    [Serializable]
    public sealed class RomActorDropdownValue
    {
        public int Value;
        public string Name;
    }

    [Serializable]
    public sealed class RomActorLogicProperty
    {
        public string Name;
        public string Target;
        public ushort Mask;
        public int Position;
        public List<RomActorDropdownValue> Dropdown = new List<RomActorDropdownValue>();

        public string Kind
        {
            get
            {
                string value = Name ?? "";
                if (value.IndexOf("Switch Flag", StringComparison.OrdinalIgnoreCase) >= 0) return "Switch";
                if (value.IndexOf("Chest Flag", StringComparison.OrdinalIgnoreCase) >= 0) return "Chest";
                if (value.IndexOf("Collectible Flag", StringComparison.OrdinalIgnoreCase) >= 0) return "Collectible";
                if (value.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0 && value.IndexOf("ID", StringComparison.OrdinalIgnoreCase) >= 0) return "Path";
                return null;
            }
        }

        public int Read(ushort variable, short xRotation, short yRotation, short zRotation)
        {
            ushort raw = variable;
            if (string.Equals(Target, "XRot", StringComparison.OrdinalIgnoreCase)) raw = unchecked((ushort)xRotation);
            else if (string.Equals(Target, "YRot", StringComparison.OrdinalIgnoreCase)) raw = unchecked((ushort)yRotation);
            else if (string.Equals(Target, "ZRot", StringComparison.OrdinalIgnoreCase)) raw = unchecked((ushort)zRotation);
            return (raw & Mask) >> Position;
        }

        public string DropdownName(int value)
        {
            RomActorDropdownValue match = Dropdown.FirstOrDefault(x => x.Value == value);
            return match == null ? null : match.Name;
        }
    }

    public sealed class RomPaletteAsset
    {
        public string Category;
        public string Name;
        public ushort Id;
        public ushort Variable;
        public ushort[] Objects = new ushort[0];
        public SceneTileSource Room;
        public string FilePath;
        public string TilePath;
        public string ThumbnailPath;
        public string RequirementText;
        public int RecommendationScore;
        public string RecommendationReason;
        public int TileSourceScene = -1;
        public bool GeneratedTile;
        public bool ImportedTile;
        public bool Transition;
        public short XRotation, YRotation, ZRotation;
        public List<RomActorLogicProperty> LogicProperties = new List<RomActorLogicProperty>();
        public override string ToString() { return Name; }
    }

    public sealed class RomAssetCatalog
    {
        public readonly List<RomPaletteAsset> Assets = new List<RomPaletteAsset>();
        public readonly List<string> Diagnostics = new List<string>();
        public readonly HashSet<ushort> AvailableObjects = new HashSet<ushort>();
        public SceneTileLibrary Rooms;
        public string DirectoryPath;

        public static RomAssetCatalog Import(byte[] rom, ROM version, string xmlDirectory, string cacheDirectory)
        {
            if (version.Game != "OOT") throw new InvalidDataException("Only OoT is supported by the sandbox.");
            if (version.ObjectTable == 0) version.ObjectTable = LocateN12ObjectTable(rom, version.Prefix);
            RomAssetCatalog result = new RomAssetCatalog();
            result.Rooms = SceneTileLibrary.Read(rom, version.SceneTable, version.SceneTableEnd);
            result.DirectoryPath = Path.Combine(cacheDirectory, result.Rooms.RomFingerprint);
            Directory.CreateDirectory(result.DirectoryPath);
            result.Rooms.Save(cacheDirectory);
            result.Diagnostics.AddRange(result.Rooms.Diagnostics);
            Dictionary<ushort, XElement> definitions = XDocument.Load(Path.Combine(xmlDirectory, "ActorNames.xml"))
                .Descendants("Actor").GroupBy(x => Hex((string)x.Attribute("Key"))).ToDictionary(x => x.Key, x => x.First());
            string[] categories = { "Switches", "Scenery", "Spawns", "Explosives", "NPCs", "Enemies", "Props", "Items", "Miscellaneous", "Bosses", "Doors", "Chests" };
            HashSet<string> presets = new HashSet<string>();
            HashSet<ushort> referencedObjects = new HashSet<ushort>();
            foreach (SceneTileSource room in result.Rooms.Rooms)
            {
                string path = result.Extract(rom, room.Start, room.End, string.Format("rooms/scene_{0:X2}_room_{1:D2}.zroom", room.SceneId, room.RoomId));
                result.Assets.Add(new RomPaletteAsset { Category = "Rooms", Name = room.ToString(), Room = room, FilePath = path });
                foreach (ushort objectId in ReadObjectIds(rom, room.Start, room.End, room.ToString(), result.Diagnostics))
                    referencedObjects.Add(objectId);
                foreach (int command in Commands(rom, room.Start, room.End))
                {
                    if (rom[command] != 1) continue;
                    int count = rom[command + 1];
                    long address = room.Start + (Read32(rom, command + 4) & 0xFFFFFF);
                    if (rom[command + 4] != 3 || !Within(address, count * 16L, room.Start, room.End))
                    { result.Diagnostics.Add(room + ": invalid actor list."); continue; }
                    for (int i = 0; i < count; i++)
                    {
                        int at = (int)address + i * 16;
                        ushort id = Read16(rom, at), variable = Read16(rom, at + 14);
                        short rx = (short)Read16(rom, at + 8), ry = (short)Read16(rom, at + 10), rz = (short)Read16(rom, at + 12);
                        string key = id + ":" + variable + ":" + rx + ":" + rz;
                        if (!presets.Add(key)) continue;
                        XElement definition;
                        definitions.TryGetValue(id, out definition);
                        int category;
                        int.TryParse((string)(definition == null ? null : definition.Attribute("Category")), out category);
                        string name = definition == null ? "Actor " + id.ToString("X4") : (string)definition.Attribute("Name");
                        string objects = definition == null ? null : (string)definition.Attribute("Object");
                        result.Assets.Add(new RomPaletteAsset { Category = category >= 0 && category < categories.Length ? categories[category] : "Actors",
                            Id = id, Variable = variable, Name = string.Format("{0} [{1:X4}:{2:X4}]", name, id, variable),
                            XRotation = rx, YRotation = ry, ZRotation = rz,
                            LogicProperties = ReadLogicProperties(definition),
                            RequirementText = ReadRequirementText(definition),
                            Objects = string.IsNullOrEmpty(objects) ? new ushort[0] : objects.Split(',').Select(x => Hex(x.Trim())).ToArray() });
                    }
                }
            }
            foreach (int sceneId in result.Rooms.Rooms.Select(x => x.SceneId).Distinct())
            {
                int row = (int)version.SceneTable + sceneId * 20;
                uint start = Read32(rom, row), end = Read32(rom, row + 4);
                result.Extract(rom, start, end, string.Format("scenes/scene_{0:X2}.zscene", sceneId));
                foreach (ushort objectId in ReadObjectIds(rom, start, end, "Scene " + sceneId.ToString("X2"), result.Diagnostics))
                    referencedObjects.Add(objectId);
                foreach (RomPaletteAsset connection in ReadTransitions(rom, start, end, definitions, result.Diagnostics))
                    if (presets.Add("transition:" + connection.Id + ":" + connection.Variable)) result.Assets.Add(connection);
            }
            Dictionary<ushort, XElement> objectDefinitions = XDocument.Load(Path.Combine(xmlDirectory, "ObjectData.xml"))
                .Descendants("Object").GroupBy(x => Hex((string)x.Attribute("Key"))).ToDictionary(x => x.Key, x => x.First());
            HashSet<ushort> importedObjects = new HashSet<ushort>();
            foreach (XElement definition in objectDefinitions.Values)
            {
                ushort id = Hex((string)definition.Attribute("Key"));
                ImportObjectBank(rom, version, id, definition, importedObjects, result);
            }
            foreach (ushort id in referencedObjects)
            {
                XElement definition;
                objectDefinitions.TryGetValue(id, out definition);
                ImportObjectBank(rom, version, id, definition, importedObjects, result);
            }
            result.Assets.Add(new RomPaletteAsset { Category = "Spawns", Name = "Player spawn", Id = 0, Variable = 0x0FFF });
            File.WriteAllLines(Path.Combine(result.DirectoryPath, "diagnostics.txt"), result.Diagnostics);
            return result;
        }

        private string Extract(byte[] rom, uint start, uint end, string relative)
        {
            string path = Path.Combine(DirectoryPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path) || new FileInfo(path).Length != end - start)
                using (FileStream output = File.Create(path)) output.Write(rom, checked((int)start), checked((int)(end - start)));
            return path;
        }

        private static void ImportObjectBank(byte[] rom, ROM version, ushort id, XElement definition,
            ISet<ushort> imported, RomAssetCatalog result)
        {
            if (!imported.Add(id)) return;
            long row = version.ObjectTable + id * 8L;
            if (!Within(row, 8, 0, rom.Length))
            {
                result.Diagnostics.Add("Object " + id.ToString("X4") + ": object-table row is outside the ROM.");
                return;
            }
            uint start = Read32(rom, (int)row), end = Read32(rom, (int)row + 4);
            if (start == 0 || start >= end || end > rom.Length)
            {
                result.Diagnostics.Add("Object " + id.ToString("X4") + ": invalid object range.");
                return;
            }
            result.AvailableObjects.Add(id);
            string description = definition == null ? "ROM-discovered object bank" : definition.Value.Trim();
            string path = result.Extract(rom, start, end, "objects/" + id.ToString("X4") + ".zobj");
            if (definition != null)
                foreach (string objectName in RomObjectResolver.SafeAliases(definition.Value))
                    result.Extract(rom, start, end, "objects/" + objectName + ".zobj");
            result.Assets.Add(new RomPaletteAsset { Category = "Object banks", Id = id,
                Name = id.ToString("X4") + " - " + description, FilePath = path });
        }

        private static IEnumerable<ushort> ReadObjectIds(byte[] rom, uint start, uint end, string source, IList<string> diagnostics)
        {
            foreach (int command in Commands(rom, start, end))
            {
                if (rom[command] != 0x0B) continue;
                int count = rom[command + 1];
                uint pointer = Read32(rom, command + 4);
                long address = start + (pointer & 0xFFFFFF);
                byte segment = (byte)(pointer >> 24);
                if ((segment != 2 && segment != 3) || !Within(address, count * 2L, start, end))
                {
                    diagnostics.Add(source + ": invalid object-list pointer.");
                    continue;
                }
                for (int i = 0; i < count; i++) yield return Read16(rom, (int)address + i * 2);
            }
        }

        public static List<RomPaletteAsset> ReadTransitions(byte[] rom, uint start, uint end,
            IDictionary<ushort, XElement> definitions, IList<string> diagnostics)
        {
            List<RomPaletteAsset> result = new List<RomPaletteAsset>();
            if (start >= end || end > rom.Length) throw new InvalidDataException("Invalid scene range.");
            foreach (int command in Commands(rom, start, end))
            {
                if (rom[command] != 0x0E) continue;
                int count = rom[command + 1];
                long address = start + (Read32(rom, command + 4) & 0xFFFFFF);
                if (rom[command + 4] != 2 || !Within(address, count * 16L, start, end))
                { diagnostics.Add("Scene " + start.ToString("X8") + ": invalid transition list."); continue; }
                for (int i = 0; i < count; i++)
                {
                    int at = (int)address + i * 16;
                    ushort id = Read16(rom, at + 4), variable = Read16(rom, at + 14);
                    XElement definition;
                    definitions.TryGetValue(id, out definition);
                    string name = definition == null ? "Actor " + id.ToString("X4") : (string)definition.Attribute("Name");
                    string objects = definition == null ? null : (string)definition.Attribute("Object");
                    result.Add(new RomPaletteAsset { Category = "Connections", Transition = true, Id = id, Variable = variable,
                        Name = string.Format("{0} [{1:X4}:{2:X4}]", name, id, variable), YRotation = (short)Read16(rom, at + 12),
                        RequirementText = ReadRequirementText(definition),
                        LogicProperties = ReadLogicProperties(definition),
                        Objects = string.IsNullOrEmpty(objects) ? new ushort[0] : objects.Split(',').Select(x => Hex(x.Trim())).ToArray() });
                }
            }
            return result;
        }

        private static uint LocateN12ObjectTable(byte[] rom, string prefix)
        {
            if (prefix != "N12") throw new InvalidDataException("This ROM version has no verified object table metadata.");
            // NTSC 1.2 decomp metadata: dmadata at 0x7960; common banks are entries 498-500.
            uint[] ranges = new uint[6];
            for (int i = 0; i < 3; i++)
            {
                int dma = 0x7960 + (498 + i) * 16;
                ranges[i * 2] = Read32(rom, dma); ranges[i * 2 + 1] = Read32(rom, dma + 4);
                if (ranges[i * 2] == 0 || ranges[i * 2 + 1] <= ranges[i * 2] || ranges[i * 2 + 1] > rom.Length)
                    throw new InvalidDataException("The common object DMA ranges are invalid.");
            }
            uint found = 0;
            for (int at = 8; at + 24 <= rom.Length; at += 4)
            {
                if (Read32(rom, at) != ranges[0]) continue;
                bool match = true;
                for (int i = 1; i < 6; i++) if (Read32(rom, at + i * 4) != ranges[i]) { match = false; break; }
                if (!match) continue;
                if (found != 0) throw new InvalidDataException("Ambiguous object table; import stopped.");
                found = (uint)(at - 8);
            }
            if (found == 0) throw new InvalidDataException("Could not identify the NTSC 1.2 object table.");
            return found;
        }

        private static IEnumerable<int> Commands(byte[] rom, uint start, uint end)
        {
            for (long at = start; at + 8 <= end && at < start + 2048L; at += 8)
            {
                if (rom[(int)at] == 0x14) yield break;
                yield return (int)at;
            }
        }
        private static bool Within(long start, long length, long lower, long upper) { return start >= lower && length >= 0 && start <= upper - length; }
        private static ushort Hex(string value) { return ushort.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture); }

        private static List<RomActorLogicProperty> ReadLogicProperties(XElement definition)
        {
            List<RomActorLogicProperty> result = new List<RomActorLogicProperty>();
            if (definition == null) return result;
            string masks = (string)definition.Attribute("Properties");
            string names = (string)definition.Attribute("PropertiesNames");
            string targets = (string)definition.Attribute("PropertiesTarget");
            string dropdowns = (string)definition.Attribute("Dropdown");
            string dropdownNames = (string)definition.Attribute("DropdownNames");
            if (string.IsNullOrWhiteSpace(masks) || string.IsNullOrWhiteSpace(names)) return result;
            string[] maskValues = masks.Split(',');
            string[] nameValues = names.Split(',');
            string[] targetValues = string.IsNullOrWhiteSpace(targets) ? new string[0] : targets.Split(',');
            string[] dropdownValues = string.IsNullOrWhiteSpace(dropdowns) ? new string[0] : dropdowns.Split(',');
            string[] dropdownNameValues = string.IsNullOrWhiteSpace(dropdownNames) ? new string[0] : dropdownNames.Split(',');
            for (int i = 0; i < maskValues.Length && i < nameValues.Length; i++)
            {
                ushort mask;
                if (!ushort.TryParse(maskValues[i].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask) || mask == 0) continue;
                int position = 0;
                ushort shifted = mask;
                while ((shifted & 1) == 0) { shifted = (ushort)(shifted >> 1); position++; }
                RomActorLogicProperty property = new RomActorLogicProperty { Mask = mask, Position = position,
                    Name = nameValues[i].Trim(), Target = i < targetValues.Length ? targetValues[i].Trim() : "Var" };
                if (i < dropdownValues.Length && i < dropdownNameValues.Length && !string.IsNullOrWhiteSpace(dropdownValues[i]))
                {
                    string[] values = dropdownValues[i].Split(';');
                    string[] labels = dropdownNameValues[i].Split(';');
                    for (int j = 0; j < values.Length && j < labels.Length; j++)
                    {
                        ushort dropdownValue;
                        if (ushort.TryParse(values[j].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out dropdownValue) && !string.IsNullOrWhiteSpace(labels[j]))
                            property.Dropdown.Add(new RomActorDropdownValue { Value = dropdownValue, Name = labels[j].Trim() });
                    }
                }
                result.Add(property);
            }
            return result;
        }

        private static string ReadRequirementText(XElement definition)
        {
            if (definition == null) return "";
            return string.Join(" ", definition.Descendants().Where(x => x.Name.LocalName == "Notes" || x.Name.LocalName == "Variable").Select(x => x.Value).ToArray());
        }
        private static ushort Read16(byte[] data, int at) { return (ushort)(data[at] << 8 | data[at + 1]); }
        private static uint Read32(byte[] data, int at) { return (uint)data[at] << 24 | (uint)data[at + 1] << 16 | (uint)data[at + 2] << 8 | data[at + 3]; }
    }
}
