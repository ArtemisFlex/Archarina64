using System;
using System.Xml.Serialization;
using OpenTK;

namespace SharpOcarina
{
    [Serializable]
    public sealed class SandboxAuthoringMarker
    {
        public string Id, Name, Kind, ConnectedTo;
        public int Room;
        public double X, Y, Z, Facing, Width = 100, Height = 180;

        [XmlIgnore]
        public Vector3d Position
        {
            get { return new Vector3d(X, Y, Z); }
            set { X = value.X; Y = value.Y; Z = value.Z; }
        }

        public SandboxAuthoringMarker Clone()
        {
            return (SandboxAuthoringMarker)MemberwiseClone();
        }

        [XmlIgnore]
        public string MarkerLabel { get { return Name; } set { Name = value; } }
        [XmlIgnore]
        public string MarkerType { get { return Kind; } set { Kind = value; } }
        [XmlIgnore]
        public double MarkerFacing { get { return Facing; } set { Facing = value; } }
        [XmlIgnore]
        public double MarkerX { get { return X; } set { X = value; } }
        [XmlIgnore]
        public double MarkerY { get { return Y; } set { Y = value; } }
        [XmlIgnore]
        public double MarkerZ { get { return Z; } set { Z = value; } }

        public TileSocket ToSocket()
        {
            return new TileSocket
            {
                Id = "marker:" + Id,
                Name = string.IsNullOrWhiteSpace(Name) ? Kind + " marker" : Name,
                Position = Position,
                Facing = Facing,
                Width = Width,
                Height = Height,
                Room = Room,
                ConnectedTo = ConnectedTo
            };
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Kind)) throw new InvalidOperationException("Authoring markers need an id and kind.");
            ToSocket().Validate();
        }
    }
}
