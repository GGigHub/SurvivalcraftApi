using Engine;

namespace Game.NetWork.Packages;

public class PopupPackage : IPackage
{
    public byte ID => (byte)PackageType.Popup;

    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public PopupPackage()
    {
    }

    public PopupPackage(string title, string message)
    {
        Title = title ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(Title ?? string.Empty);
        writer.Write(Message ?? string.Empty);
    }

    public void ReadData(PackageStreamReader reader)
    {
        Title = reader.ReadString();
        Message = reader.ReadString();
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        if (isServer)
        {
            return;
        }

        var subsystemPlayers = projectNet.FindSubsystem<SubsystemPlayers>();
        var mainPlayer = subsystemPlayers?.MainPlayer;
        if (mainPlayer?.GameWidget?.GuiWidget == null)
        {
            return;
        }

        DialogsManager.Alert(Title, Message);
    }
}
