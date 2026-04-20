using Engine;

namespace Game.NetWork.Packages;

/// <summary>
/// 基础包模板复制
/// </summary>
public class SignBlockPackage : IPackage
{
    public byte ID => (byte)PackageType.SignBlock;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }

    public ClientState MinNeedState => ClientState.ProjectLoaded;

    public Point3 m_point;
    public SignData m_signData;
    public SignBlockPackage() { }
    public SignBlockPackage(Point3 point, string[] lines, Color[] colors, string url)
    {
        m_point = point;
        m_signData = new SignData();
        m_signData.Lines = lines;
        m_signData.Colors = colors;
        m_signData.Url = url;
    }

        public void Handle(ProjectNet projectNet, bool isServer)
        {
            if (isServer)
            {
                if (From.PlayerData != null)
                {
                    projectNet.FindSubsystem<SubsystemSignBlockBehavior>().SetSignData(m_point, m_signData.Lines, m_signData.Colors, m_signData.Url);
                    NetworkManager.QueuePackage(this);
                }
            }
            else
            {
                projectNet.FindSubsystem<SubsystemSignBlockBehavior>().SetSignData(m_point, m_signData.Lines, m_signData.Colors, m_signData.Url);
            }
        }

    public void ReadData(PackageStreamReader reader)
    {
        m_point = reader.ReadPoint3();
        m_signData = new SignData();
        m_signData.Url = reader.ReadString();
        m_signData.Colors = new Color[reader.ReadByte()];
        for (byte i = 0; i < m_signData.Colors.Length; i++) m_signData.Colors[i] = reader.ReadColor();
        m_signData.Lines = new string[reader.ReadByte()];
        for (byte i = 0; i < m_signData.Colors.Length; i++) m_signData.Lines[i] = reader.ReadString();

    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(m_point);
        writer.Write(m_signData.Url);
        writer.Write((byte)m_signData.Colors.Length);
        foreach (var i in m_signData.Colors) writer.Write(i);
        writer.Write((byte)m_signData.Lines.Length);
        foreach (var i in m_signData.Lines) writer.Write(i);
    }

}
