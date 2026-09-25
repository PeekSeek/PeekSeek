using System;

namespace PeekSeekRaid.Core.Models;

/// A door keeps who may open it in its own state, owner then group then whether it is
/// open (InteractableDoor.updateState); the barricade's owner does not count. A locked
/// door with neither set is a door nobody opens, which is what every door was.
public static class DoorLock {
    public static byte[] State(ulong owner, ulong group) {
        byte[] state = new byte[17];
        BitConverter.GetBytes(owner).CopyTo(state, 0);
        BitConverter.GetBytes(group).CopyTo(state, 8);
        return state;
    }
}
