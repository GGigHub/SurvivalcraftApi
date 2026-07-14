using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network;

public class IndexPacket : Packet {
    public override byte ID => (byte)PacketType.IndexInfo;
    public int Index;

    public IndexPacket() { }

    public IndexPacket(int index) {
        Index = index;
    }

    public override void Handle(bool isServer) {
        NetworkManager.SetPlayerIndex(Index);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref Index);
    }
}
