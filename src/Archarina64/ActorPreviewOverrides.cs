using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;

namespace SharpOcarina
{
    [XmlRoot("ActorPreviewOverrides")]
    public sealed class ActorPreviewOverrideFile
    {
        [XmlElement("Actor")] public List<ActorPreviewOverride> Actors = new List<ActorPreviewOverride>();

        public static ActorPreviewOverrideFile Load(string path, IList<string> diagnostics)
        {
            if (!File.Exists(path)) return new ActorPreviewOverrideFile();
            try
            {
                using (FileStream stream = File.OpenRead(path))
                    return (ActorPreviewOverrideFile)new XmlSerializer(typeof(ActorPreviewOverrideFile)).Deserialize(stream);
            }
            catch (Exception error)
            {
                diagnostics.Add("Actor preview override file could not be loaded: " + error.Message);
                return new ActorPreviewOverrideFile();
            }
        }
    }

    public sealed class ActorPreviewOverride
    {
        [XmlAttribute("Actor")] public string Actor = "";
        [XmlAttribute("Object")] public string Object = "";
        [XmlAttribute("Offset")] public string Offset = "";
        [XmlAttribute("Bank")] public string Bank = "06";
        [XmlAttribute("Scale")] public string Scale = "0.01";
        [XmlAttribute("Variable")] public string Variable = "....";
        [XmlAttribute("DListCount")] public string DListCount = "1";

        public bool TryRead(out ushort actor, out uint offset, out uint bank, out float scale, out int dlistCount)
        {
            actor = 0; offset = 0; bank = 0; scale = 0; dlistCount = 0;
            return ushort.TryParse(Actor, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out actor) &&
                uint.TryParse(Offset, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset) &&
                uint.TryParse(Bank, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bank) &&
                float.TryParse(Scale, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) &&
                int.TryParse(DListCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out dlistCount) &&
                dlistCount > 0 && scale > 0 && !string.IsNullOrWhiteSpace(Object);
        }
    }
}
