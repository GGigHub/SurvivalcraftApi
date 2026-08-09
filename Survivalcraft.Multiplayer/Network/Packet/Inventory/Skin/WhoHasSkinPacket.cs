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
        archive.Value(ref SkinName, SkinPacketBase.MaximumSkinNameUtf8Bytes);
    }

    public override void Handle(bool isServer) {
        if (isServer
            || !SkinPacketBase.TryValidateSkinName(
                SkinName,
                out string normalizedName)) return;
        byte[] data = SkinPacketBase.LoadSkinData(normalizedName);
        if (data != null) {
            NetworkManager.Queue(new WhoHasSkinReplyPacket(normalizedName, data));
        }
    }
}

