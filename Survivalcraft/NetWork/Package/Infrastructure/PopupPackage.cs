

namespace Game.NetWork;

public class PopupPackage : IPackage {
    public override byte ID => (byte)PackageType.Popup;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public PopupPackage() {
    }

    public PopupPackage(string title, string message) {
        Title = title ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public override void Serialize(PackageSerializer archive) {
        string title = Title;
        string message = Message;
        archive.Value(ref title);
        archive.Value(ref message);
        Title = title;
        Message = message;
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }

        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var mainPlayer = subsystemPlayers?.MainPlayer;
        if (mainPlayer?.GameWidget?.GuiWidget == null) {
            return;
        }

        DialogsManager.Alert(Title, Message);
    }
}