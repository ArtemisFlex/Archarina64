using System;

namespace SharpOcarina
{
    internal static class OoTSceneNames
    {
        internal static string Get(int scene)
        {
            switch (scene)
            {
                case 0x00: return "Great Deku Tree"; case 0x01: return "Dodongo's Cavern"; case 0x02: return "Jabu-Jabu's Belly";
                case 0x03: return "Forest Temple"; case 0x04: return "Fire Temple"; case 0x05: return "Water Temple";
                case 0x06: return "Spirit Temple"; case 0x07: return "Shadow Temple"; case 0x08: return "Bottom of the Well";
                case 0x09: return "Ice Cavern"; case 0x0A: return "Ganon's Tower"; case 0x0B: return "Gerudo Training Ground";
                case 0x0C: return "Thieves' Hideout"; case 0x0D: return "Inside Ganon's Castle"; case 0x0E: return "Ganon's Tower Collapse";
                case 0x0F: return "Ganon's Castle Collapse"; case 0x10: return "Treasure Chest Game";
                case 0x11: return "Deku Tree Boss"; case 0x12: return "Dodongo's Cavern Boss"; case 0x13: return "Jabu-Jabu Boss";
                case 0x14: return "Forest Temple Boss"; case 0x15: return "Fire Temple Boss"; case 0x16: return "Water Temple Boss";
                case 0x17: return "Spirit Temple Boss"; case 0x18: return "Shadow Temple Boss"; case 0x19: return "Ganondorf Boss";
                case 0x1A: return "Ganon Boss"; case 0x1B: return "Market Entrance Day"; case 0x1C: return "Market Entrance Night";
                case 0x1D: return "Market Entrance Ruins"; case 0x1E: return "Market Back Alley Day"; case 0x1F: return "Market Back Alley Night";
                case 0x20: return "Market Day"; case 0x21: return "Market Night"; case 0x22: return "Market Ruins";
                case 0x23: return "Temple of Time Exterior Day"; case 0x24: return "Temple of Time Exterior Night"; case 0x25: return "Temple of Time Exterior Ruins";
                case 0x2A: return "Kakariko Guest House"; case 0x2B: return "Kakariko House"; case 0x2C: return "Kakariko Bazaar";
                case 0x2D: return "Kokiri Shop"; case 0x2E: return "Goron Shop"; case 0x2F: return "Zora Shop";
                case 0x30: return "Kakariko Potion Shop"; case 0x31: return "Market Potion Shop"; case 0x32: return "Bombchu Shop";
                case 0x33: return "Happy Mask Shop"; case 0x34: return "Link's House"; case 0x35: return "Dog Lady's House";
                case 0x36: return "Lon Lon Ranch Stable"; case 0x37: return "Impa's House"; case 0x38: return "Lake Hylia Laboratory";
                case 0x39: return "Carpenter's Tent"; case 0x3A: return "Gravekeeper's Hut"; case 0x3B: return "Great Fairy Fountain";
                case 0x3C: return "Fairy's Fountain"; case 0x3D: return "Great Fairy Fountain"; case 0x3E: return "Grottos";
                case 0x3F: return "Redead Grave"; case 0x40: return "Fairy Fountain Grave"; case 0x41: return "Royal Family's Tomb";
                case 0x42: return "Shooting Gallery"; case 0x43: return "Temple of Time"; case 0x44: return "Chamber of Sages";
                case 0x45: return "Hyrule Castle Courtyard Day"; case 0x46: return "Hyrule Castle Courtyard Night";
                case 0x48: return "Windmill and Dampe's Grave"; case 0x49: return "Fishing Pond"; case 0x4A: return "Zelda's Courtyard";
                case 0x4B: return "Bombchu Bowling"; case 0x4C: return "Lon Lon Ranch Buildings"; case 0x4D: return "Market Guard House";
                case 0x4E: return "Granny's Potion Shop"; case 0x4F: return "Ganon Boss"; case 0x50: return "House of Skulltula";
                case 0x51: return "Hyrule Field"; case 0x52: return "Kakariko Village"; case 0x53: return "Graveyard";
                case 0x54: return "Zora's River"; case 0x55: return "Kokiri Forest"; case 0x56: return "Sacred Forest Meadow";
                case 0x57: return "Lake Hylia"; case 0x58: return "Zora's Domain"; case 0x59: return "Zora's Fountain";
                case 0x5A: return "Gerudo Valley"; case 0x5B: return "Lost Woods"; case 0x5C: return "Desert Colossus";
                case 0x5D: return "Gerudo's Fortress"; case 0x5E: return "Haunted Wasteland"; case 0x5F: return "Hyrule Castle";
                case 0x60: return "Death Mountain Trail"; case 0x61: return "Death Mountain Crater"; case 0x62: return "Goron City";
                case 0x63: return "Lon Lon Ranch"; case 0x64: return "Outside Ganon's Castle";
                default: return "Other / Scene " + scene.ToString("X2");
            }
        }
    }
}
