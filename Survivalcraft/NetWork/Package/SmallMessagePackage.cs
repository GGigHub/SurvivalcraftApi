using Engine;

namespace Game.NetWork.Packages;

/// <summary>
/// 小消息提示包，用于同步 DisplaySmallMessage 到客户端
/// </summary>
public class SmallMessagePackage : IPackage
{
    public byte ID => (byte)PackageType.SmallMessage;

    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.Playing;

    public string Message { get; set; } = string.Empty;
    public Color MessageColor { get; set; }
    public bool Blinking { get; set; }
    public bool PlayNotificationSound { get; set; }

    public SmallMessagePackage()
    {
    }

    public SmallMessagePackage(string message, Color color, bool blinking, bool playNotificationSound)
    {
        Message = message;
        MessageColor = color;
        Blinking = blinking;
        PlayNotificationSound = playNotificationSound;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(Message);
        writer.Write(MessageColor);
        writer.Write(Blinking);
        writer.Write(PlayNotificationSound);
    }

    public void ReadData(PackageStreamReader reader)
    {
        Message = reader.ReadString();
        MessageColor = reader.ReadColor();
        Blinking = reader.ReadBoolean();
        PlayNotificationSound = reader.ReadBoolean();
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        if (!isServer)
        {
            // 客户端接收到小消息提示
            var subsystemPlayers = projectNet.FindSubsystem<SubsystemPlayers>();
            var mainPlayer = subsystemPlayers.MainPlayer;

            mainPlayer?.ComponentGui?.DisplaySmallMessage(Message, MessageColor, Blinking, PlayNotificationSound);
        }
    }
}
