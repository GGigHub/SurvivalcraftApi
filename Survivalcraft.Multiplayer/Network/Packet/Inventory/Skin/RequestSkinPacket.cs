using Engine;

namespace Game.Network;

public class RequestSkinPacket : Packet {
    private static readonly Dictionary<string, List<ClientSession>> m_pendingSessions = [];

    public override byte ID => (byte)PacketType.ClothingRequestSkin;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public string SkinName;
    public RequestSkinPacket() { }
    public RequestSkinPacket(string skinName) {
        SkinName = skinName;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref SkinName);
    }
    public override void Handle(bool isServer) {
        if (CharacterSkinsManager.HasSkinRes(SkinName)) {
            NetworkManager.Queue(new ReplySkinPacket(SkinName) { To = From });
        }
        else if (isServer && From != null) {
            AddPendingSession(SkinName, From);
            if (!CharacterSkinsManager.waitReplyList.Contains(SkinName)) {
                NetworkManager.Queue(new WhoHasSkinPacket(SkinName));
                CharacterSkinsManager.waitReplyList.Add(SkinName);
            }
        }
    }

    private static void AddPendingSession(string skinName, ClientSession session) {
        if (!m_pendingSessions.TryGetValue(skinName, out List<ClientSession> sessions)) {
            sessions = [];
            m_pendingSessions[skinName] = sessions;
        }
        if (!sessions.Contains(session)) {
            sessions.Add(session);
        }
    }

    public static void ReplyPendingSessions(string skinName, byte[] skinData, ClientSession except) {
        if (!m_pendingSessions.Remove(skinName, out List<ClientSession> sessions)) {
            return;
        }
        foreach (ClientSession session in sessions) {
            if (session != null && session != except) {
                NetworkManager.Queue(new ReplySkinPacket(skinName, skinData) { To = session });
            }
        }
    }
}

