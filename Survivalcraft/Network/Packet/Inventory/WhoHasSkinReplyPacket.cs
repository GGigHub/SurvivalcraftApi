using Engine;

namespace Game.Network;

public class WhoHasSkinReplyPacket : Packet {
    public override byte ID => (byte)PacketType.ClothingWhoHasReply;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public string SkinName;
    public byte[] m_skinData;

    public WhoHasSkinReplyPacket() { }
    public WhoHasSkinReplyPacket(string skinName) {
        SkinName = skinName;
        m_skinData = ClothingHelpers.LoadSkinData(skinName);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref SkinName);
        ClothingHelpers.SerializeSkinData(archive, ref m_skinData);
    }

    public override void Handle(bool isServer) {
        ClothingHelpers.SaveSkin(SkinName, m_skinData);
    }
}

