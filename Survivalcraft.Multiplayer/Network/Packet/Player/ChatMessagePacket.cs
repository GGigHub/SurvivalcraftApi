using Engine;
using Survivalcraft.Multiplayer;

namespace Game.Network;

public enum ChatMessageType : byte {
    Player, Join, Leave
}

public class ChatMessagePacket : Packet {
    public override byte ID => (byte)PacketType.Chat;
    public override NetworkState MinNeedState => NetworkState.Connected;

    public ChatMessageType m_type;
    public int m_playerIndex;
    public string m_name;
    public string m_text;

    public const int MaxTextLength = 100;

    public ChatMessagePacket() { }

    public ChatMessagePacket(ChatMessageType type, int playerIndex, string name, string text) {
        m_type = type;
        m_playerIndex = playerIndex;
        m_name = name ?? string.Empty;
        m_text = text ?? string.Empty;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Enum(ref m_type);
        archive.Value(ref m_playerIndex);
        archive.Value(ref m_name);
        archive.Value(ref m_text);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            // Player chat originates from a client; system messages are only ever
            // generated server-side, so a client-sent system packet is dropped here.
            if (m_type != ChatMessageType.Player || From == null) {
                return;
            }

            // Trust the session identity, not the payload, to prevent name/index spoofing.
            m_playerIndex = From.PlayerIndex;
            string serverName = From.GetPlayerData()?.Name;
            m_name = string.IsNullOrEmpty(serverName) ? $"Player {From.PlayerIndex}" : serverName;
            string text = m_text ?? string.Empty;
            if (text.Length > MaxTextLength) {
                text = text.Substring(0, MaxTextLength);
            }
            m_text = text;
            if (string.IsNullOrWhiteSpace(m_text)) {
                return;
            }

            // Fan out to every client (including the sender, so its echo matches
            // everyone else). 只有房主未以客户端身份加入时才需要本地回显，
            // 否则房主会通过自己的客户端收到广播，导致重复显示。
            NetworkManager.Queue(new ChatMessagePacket(m_type, m_playerIndex, m_name, m_text));
            if (!NetworkManager.IsClientRunning) {
                ChatManager.Add(m_type, m_playerIndex, m_name, m_text);
            }
            return;
        }

        ChatManager.Add(m_type, m_playerIndex, m_name, m_text);
    }
}
