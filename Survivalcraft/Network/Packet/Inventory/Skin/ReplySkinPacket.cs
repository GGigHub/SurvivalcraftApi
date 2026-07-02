using Engine;

namespace Game.Network;

public class ReplySkinPacket : SkinPacketBase {
    public override byte ID => (byte)PacketType.ClothingReplySkin;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public string SkinName;
    public byte[] m_skinData;

    public ReplySkinPacket() { }
    public ReplySkinPacket(string skinName) {
        SkinName = skinName;
        m_skinData = LoadSkinData(skinName);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref SkinName);
        SerializeSkinData(archive, ref m_skinData);
    }

    public override void Handle(bool isServer) {
        SaveSkin(SkinName, m_skinData);
    }
}

