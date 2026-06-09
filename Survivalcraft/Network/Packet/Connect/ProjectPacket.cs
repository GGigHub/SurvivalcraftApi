namespace Game.Network;

public class ProjectPacket : Packet {
    public override byte ID => (byte)PacketType.ProjectInfo;
    public override NetworkState MinNeedState => NetworkState.NotConnected;

    public byte[] m_projectData;
    public int m_playerIndex;

    public ProjectPacket() {
    }

    public ProjectPacket(byte[] projectData, int playerIndex = -1) {
        m_projectData = projectData;
        m_playerIndex = playerIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerIndex);
        int projectLength = archive.IsWriting ? m_projectData.Length : 0;
        archive.Value(ref projectLength);
        byte[] projectData = archive.IsWriting ? m_projectData : null;
        archive.Raw(ref projectData, projectLength);
        if (archive.IsReading) {
            m_projectData = projectData;
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        // NetworkClient.ClientSession.PlayerIndex = m_playerIndex;
        // NetworkClient.ClientSession.NetworkStage = NetworkState.Connected;
        ScreensManager.FindScreen<GameLoadingScreen>("GameLoading").ReplyCall(m_projectData);
    }
}

