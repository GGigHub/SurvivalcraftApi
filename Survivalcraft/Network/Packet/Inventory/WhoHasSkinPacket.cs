using Engine;

namespace Game.Network;

public class WhoHasSkinPacket : Packet {
    public override byte ID => (byte)PacketType.ClothingWhoHas;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public string SkinName;

    public WhoHasSkinPacket() { }
    public WhoHasSkinPacket(string skinName) {
        SkinName = skinName;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref SkinName);
    }

    public override void Handle(bool isServer) {
        if (CharacterSkinsManager.HasSkinRes(SkinName)) {
            NetworkManager.Queue(new WhoHasSkinReplyPacket(SkinName));
        }
    }
}

