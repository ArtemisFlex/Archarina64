using System;
using System.Collections.Generic;

namespace SharpOcarina
{
    public static class ActorPreviewInference
    {
        public static IList<uint> FindCandidates(byte[] objectData, int maximum)
        {
            List<uint> result = new List<uint>();
            if (objectData == null || maximum <= 0) return result;
            for (int offset = 0; offset + 8 <= objectData.Length && result.Count < maximum; offset += 8)
            {
                if (!LooksLikeCommand(objectData[offset])) continue;
                bool vertex = false, triangle = false, end = false;
                for (int at = offset, commands = 0; at + 8 <= objectData.Length && commands < 256; at += 8, commands++)
                {
                    byte id = objectData[at];
                    if (id == 0x01) vertex = true;
                    if (id == 0x05 || id == 0x06) triangle = true;
                    if (id == 0xDF) { end = true; break; }
                    if (!LooksLikeCommand(id) && id != 0x00) break;
                }
                if (vertex && triangle && end) result.Add((uint)offset);
            }
            return result;
        }

        private static bool LooksLikeCommand(byte id)
        {
            return id == 0x01 || id == 0x03 || id == 0x04 || id == 0x05 || id == 0x06 ||
                id == 0xDA || id == 0xDB || id == 0xDC || id == 0xDE || id == 0xDF ||
                (id >= 0xE4 && id <= 0xE7) || (id >= 0xF0 && id <= 0xFF);
        }
    }
}
