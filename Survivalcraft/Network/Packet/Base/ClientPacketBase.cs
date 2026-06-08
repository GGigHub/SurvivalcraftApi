using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public abstract class ClientPacketBase : Packet {
        public static void SerializePlayerIndex(PacketSerializer archive, ref int playerIndex) {
            archive.Value(ref playerIndex);
        }

        public static void SerializePlayerInfo(PacketSerializer archive, ref int playerIndex, ref Guid networkGuid) {
            archive.Value(ref playerIndex);
            archive.Value(ref networkGuid);
        }
    }
}
