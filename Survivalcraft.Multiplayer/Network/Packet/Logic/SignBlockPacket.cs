using Engine;

namespace Game.Network;

public class SignBlockPacket : Packet {
    public override byte ID => (byte)PacketType.SignBlock;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public Point3 m_point;
    public SignData m_signData;

    public SignBlockPacket() {
    }

    public SignBlockPacket(Point3 point, string[] lines, Color[] colors, string url) {
        m_point = point;
        m_signData = new SignData();
        m_signData.Lines = lines;
        m_signData.Colors = colors;
        m_signData.Url = url;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_point);
        m_signData ??= new SignData();
        string url = m_signData.Url;
        archive.Value(ref url);
        m_signData.Url = url;

        byte colorCount = archive.IsWriting ? (byte)m_signData.Colors.Length : (byte)0;
        archive.Value(ref colorCount);
        if (archive.IsReading) {
            m_signData.Colors = new Color[colorCount];
        }
        for (int i = 0; i < colorCount; i++) {
            Color color = archive.IsWriting ? m_signData.Colors[i] : default;
            archive.Value(ref color);
            if (archive.IsReading) {
                m_signData.Colors[i] = color;
            }
        }

        byte lineCount = archive.IsWriting ? (byte)m_signData.Lines.Length : (byte)0;
        archive.Value(ref lineCount);
        if (archive.IsReading) {
            m_signData.Lines = new string[lineCount];
        }
        for (int i = 0; i < lineCount; i++) {
            string line = archive.IsWriting ? m_signData.Lines[i] : string.Empty;
            archive.Value(ref line);
            if (archive.IsReading) {
                m_signData.Lines[i] = line;
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            if (From?.GetPlayerData() != null) {
                GameManager.Project.FindSubsystem<SubsystemSignBlockBehavior>().SetSignData(m_point, m_signData.Lines, m_signData.Colors, m_signData.Url);
                NetworkManager.Queue(this);
            }
        }
        else {
            GameManager.Project.FindSubsystem<SubsystemSignBlockBehavior>().SetSignData(m_point, m_signData.Lines, m_signData.Colors, m_signData.Url);
        }
    }
}

