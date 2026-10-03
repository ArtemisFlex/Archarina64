using System;
using System.Collections.Generic;

namespace SharpOcarina
{
    [Serializable]
    public sealed class DungeonRomMechanicsProfile
    {
        public string RomFingerprint;
        public string GameVersion;
        public string Detection;
        public double HookshotSpeed;
        public int HookshotFrames;
        public int LongshotFrames;
        public double HookshotRange;
        public double LongshotRange;
        public double JumpVelocity;
        public double JumpGravity;
        public double RunSpeed;
        public double JumpEnvelope;
        public bool JumpPhysicsDerived;
        public List<string> Evidence = new List<string>();

        public bool IsEngineDerived
        {
            get { return Detection != null && Detection.IndexOf("ROM signature matched", StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        public static DungeonRomMechanicsProfile Conservative(string fingerprint, string version)
        {
            DungeonRomMechanicsProfile result = new DungeonRomMechanicsProfile
            {
                RomFingerprint = fingerprint, GameVersion = version, Detection = "Conservative fallback",
                HookshotSpeed = 20, HookshotFrames = 13, LongshotFrames = 26,
                HookshotRange = 260, LongshotRange = 520,
                JumpVelocity = 20, JumpGravity = -1.2, RunSpeed = 8, JumpEnvelope = 180
            };
            result.Evidence.Add("OoT Hookshot baseline: 20 units/frame for 13 frames; Longshot: 26 frames.");
            result.Evidence.Add("Verify modified ROMs with runtime traces when gameplay code or reach multipliers change.");
            return result;
        }
    }

    public static class DungeonRomMechanicsScanner
    {
        private const uint HookshotSpeedFloat = 0x41A00000; // IEEE-754 20.0f, big-endian ROM bytes.
        private const uint JumpGravityFloat = 0xBF99999A; // IEEE-754 -1.2f, big-endian ROM bytes.
        private const uint RunSpeedFloat = 0x41000000; // IEEE-754 8.0f, big-endian ROM bytes.

        public static DungeonRomMechanicsProfile Scan(byte[] rom, string fingerprint, string version)
        {
            DungeonRomMechanicsProfile result = DungeonRomMechanicsProfile.Conservative(fingerprint, version);
            if (rom == null || rom.Length < 4) { result.Detection = "Fallback: ROM bytes unavailable"; return result; }
            int speedOffset = FindBigEndianWord(rom, HookshotSpeedFloat);
            if (speedOffset < 0)
            {
                result.Detection = "Fallback: Hookshot speed signature not found";
                result.Evidence.Add("The ROM may be patched or use a different code layout.");
                return result;
            }
            int start = Math.Max(0, speedOffset - 256), end = Math.Min(rom.Length - 1, speedOffset + 256);
            if (ContainsByte(rom, start, end, 13) && ContainsByte(rom, start, end, 26))
            {
                result.Detection = "ROM signature matched OoT Hookshot movement code";
                result.Evidence.Add("Found big-endian float 20.0f at ROM offset 0x" + speedOffset.ToString("X8") + ".");
                result.Evidence.Add("Found nearby Hookshot/Longshot timer bytes 13 and 26.");
            }
            else
            {
                result.Detection = "Fallback: partial Hookshot signature";
                result.Evidence.Add("Found 20.0f, but not the expected nearby timer pair; limits remain conservative.");
            }
            int gravityOffset = FindBigEndianWord(rom, JumpGravityFloat);
            int runSpeedOffset = FindBigEndianWord(rom, RunSpeedFloat);
            if (gravityOffset >= 0 && runSpeedOffset >= 0 && Math.Abs(gravityOffset - runSpeedOffset) <= 0x20000)
            {
                result.JumpPhysicsDerived = true;
                result.JumpVelocity = 20;
                result.JumpGravity = -1.2;
                result.RunSpeed = 8;
                // Full flight distance is reduced for takeoff, landing, input,
                // collision clearance, and the fact that the player cannot use
                // the outermost frame as a reliable landing frame.
                result.JumpEnvelope = Math.Floor((2 * result.JumpVelocity / Math.Abs(result.JumpGravity)) * result.RunSpeed * 0.68);
                result.Evidence.Add("Found candidate jump constants 20.0f, -1.2f, and 8.0f; jump envelope is a collision-authoring estimate.");
            }
            else result.Evidence.Add("Jump constants were not confidently paired; jump-gap validation remains conservative.");
            return result;
        }

        private static int FindBigEndianWord(byte[] data, uint value)
        {
            byte a = (byte)(value >> 24), b = (byte)(value >> 16), c = (byte)(value >> 8), d = (byte)value;
            for (int i = 0; i <= data.Length - 4; i++)
                if (data[i] == a && data[i + 1] == b && data[i + 2] == c && data[i + 3] == d) return i;
            return -1;
        }

        private static bool ContainsByte(byte[] data, int start, int end, byte value)
        {
            for (int i = start; i <= end; i++) if (data[i] == value) return true;
            return false;
        }
    }
}
