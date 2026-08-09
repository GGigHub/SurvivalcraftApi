using Engine;

namespace Game.Network;

public class RequestSkinPacket : Packet {
    public const int MaximumPendingSkinNames = 128;
    public const int MaximumPendingSessionsPerSkin = 16;

    private static readonly Dictionary<string, List<ClientSession>> m_pendingSessions =
        new(StringComparer.OrdinalIgnoreCase);

    public override byte ID => (byte)PacketType.ClothingRequestSkin;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public string SkinName;
    public RequestSkinPacket() { }
    public RequestSkinPacket(string skinName) {
        SkinName = skinName;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref SkinName, SkinPacketBase.MaximumSkinNameUtf8Bytes);
    }
    public override void Handle(bool isServer) {
        if (!isServer
            || From == null
            || !SkinPacketBase.TryValidateSkinName(
                SkinName,
                out string normalizedName)) return;
        SkinName = normalizedName;
        byte[] localData = SkinPacketBase.LoadSkinData(SkinName);
        if (localData != null) {
            NetworkManager.Queue(new ReplySkinPacket(SkinName, localData) { To = From });
            return;
        }
        if (!AddPendingSession(SkinName, From)) {
            return;
        }
        if (!CharacterSkinsManager.waitReplyList.Contains(
                SkinName,
                StringComparer.OrdinalIgnoreCase)) {
            NetworkManager.Queue(new WhoHasSkinPacket(SkinName));
            CharacterSkinsManager.waitReplyList.Add(SkinName);
        }
    }

    private static bool AddPendingSession(string skinName, ClientSession session) {
        if (session.NetworkState == NetworkState.NotConnected) {
            return false;
        }
        if (!m_pendingSessions.TryGetValue(skinName, out List<ClientSession> sessions)) {
            if (m_pendingSessions.Count >= MaximumPendingSkinNames) {
                return false;
            }
            sessions = [];
            m_pendingSessions[skinName] = sessions;
        }
        if (sessions.Contains(session)) {
            return true;
        }
        if (sessions.Count >= MaximumPendingSessionsPerSkin) {
            return false;
        }
        sessions.Add(session);
        return true;
    }

    public static bool IsPending(string skinName) =>
        SkinPacketBase.TryValidateSkinName(skinName, out string normalizedName)
        && m_pendingSessions.ContainsKey(normalizedName);

    public static void ReplyPendingSessions(string skinName, byte[] skinData, ClientSession except) {
        if (!SkinPacketBase.TryValidateSkinName(skinName, out string normalizedName)
            || !SkinPacketBase.TryValidateSkinData(skinData)
            || !m_pendingSessions.Remove(normalizedName, out List<ClientSession> sessions)) {
            return;
        }
        foreach (ClientSession session in sessions) {
            if (session != null
                && session != except
                && session.NetworkState != NetworkState.NotConnected) {
                NetworkManager.Queue(new ReplySkinPacket(normalizedName, skinData) { To = session });
            }
        }
    }
}

