namespace Game.Network;

public class ProjectPacket : Packet {
    public override byte ID => (byte)PacketType.Project;
    public override ClientStage MinNeedState => ClientStage.NotConnected;

    public bool hasTexture;
    public byte[] m_textureData;
    public byte[] m_projectData;

    public ProjectPacket() {
    }

    public ProjectPacket(byte[] textureData, byte[] projectData) {
        m_textureData = textureData;
        hasTexture = m_textureData != null;
        m_projectData = projectData;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref hasTexture);
        if (hasTexture) {
            int textureLength = archive.IsWriting ? m_textureData.Length : 0;
            archive.Value(ref textureLength);
            byte[] textureData = archive.IsWriting ? m_textureData : null;
            archive.Raw(ref textureData, textureLength);
            if (archive.IsReading) {
                m_textureData = textureData;
            }
        }
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
        var s = ScreensManager.FindScreen<GameLoadingScreen>("GameLoading");
        s.ReplyCall(hasTexture, m_textureData, m_projectData);
    }
}

