using Engine;

namespace Game.Network;

public class WhoHasSkinReplyPacket : SkinPacketBase {
    public override byte ID => (byte)PacketType.ClothingWhoHasReply;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;
    public string SkinName;
    public byte[] m_skinData;

    public WhoHasSkinReplyPacket() { }
    public WhoHasSkinReplyPacket(string skinName) {
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

