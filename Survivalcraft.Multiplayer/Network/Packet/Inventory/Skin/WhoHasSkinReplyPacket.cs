using Engine;

namespace Game.Network;

public class WhoHasSkinReplyPacket : SkinPacketBase {
    public override byte ID => (byte)PacketType.ClothingWhoHasReply;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public string SkinName;
    public byte[] m_skinData;

    public WhoHasSkinReplyPacket() { }
    public WhoHasSkinReplyPacket(string skinName) {
        SkinName = skinName;
        m_skinData = LoadSkinData(skinName);
    }
    public WhoHasSkinReplyPacket(string skinName, byte[] skinData) {
        SkinName = skinName;
        m_skinData = skinData;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref SkinName, MaximumSkinNameUtf8Bytes);
        SerializeSkinData(archive, ref m_skinData);
    }

    public override void Handle(bool isServer) {
        if (isServer
            && From != null
            && RequestSkinPacket.IsPending(SkinName)
            && SaveSkin(SkinName, m_skinData)) {
            RequestSkinPacket.ReplyPendingSessions(SkinName, m_skinData, From);
        }
    }
}

