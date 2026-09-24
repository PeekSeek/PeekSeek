using System.Xml.Serialization;
using UnityEngine;

namespace PeekSeekRaid.Core.Models;

/// Unity's Vector3 is a struct with properties Rocket's XML serialiser will not
/// round-trip, so configured positions are held in this and converted on read.
public class SerializableVector3 {
    [XmlAttribute]
    public float X;

    [XmlAttribute]
    public float Y;

    [XmlAttribute]
    public float Z;

    public SerializableVector3() { }

    public SerializableVector3(float x, float y, float z) {
        X = x;
        Y = y;
        Z = z;
    }

    public Vector3 ToVector3() => new(X, Y, Z);
}
