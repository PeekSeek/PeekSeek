using System.Collections.Generic;
using Rocket.API;
using UnityEngine;

namespace PeekSeekArena;

public class SerializableVector3 {
    public float x;
    public float y;
    public float z;

    public SerializableVector3() { }

    public SerializableVector3(float x, float y, float z) {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public Vector3 ToVector3() => new(x, y, z);
}

public class PeekSeekArenaConfiguration : IRocketPluginConfiguration {
    /// Where the teams come down each round. The teams rotate through these, one
    /// step per round, so nobody holds the good end of the map for the whole match.
    /// This has to be set per map; the default is nowhere in particular.
    public List<SerializableVector3> Spawns;

    public void LoadDefaults() {
        Spawns = [new SerializableVector3(0, 0, 0)];
    }
}
