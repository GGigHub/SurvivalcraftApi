using Engine;

namespace Game.NetWork;

public class SmallMessagePackage : IPackage {
    public override byte ID => (byte)PackageType.SmallMessage;
    public override ClientStage MinNeedState => ClientStage.Playing;

    public string Message { get; set; } = string.Empty;
    public Color MessageColor { get; set; }
    public bool Blinking { get; set; }
    public bool PlayNotificationSound { get; set; }

    public SmallMessagePackage() {
    }

    public SmallMessagePackage(string message, Color color, bool blinking, bool playNotificationSound) {
        Message = message;
        MessageColor = color;
        Blinking = blinking;
        PlayNotificationSound = playNotificationSound;
    }

    public override void Serialize(PackageSerializer archive) {
        string message = Message;
        Color messageColor = MessageColor;
        bool blinking = Blinking;
        bool playNotificationSound = PlayNotificationSound;

        archive.Value(ref message);
        archive.Value(ref messageColor);
        archive.Value(ref blinking);
        archive.Value(ref playNotificationSound);

        Message = message;
        MessageColor = messageColor;
        Blinking = blinking;
        PlayNotificationSound = playNotificationSound;
    }

    public override void Handle(bool isServer) {
        if (!isServer) {
            // 瀹㈡埛绔帴鏀跺埌灏忔秷鎭彁绀?
            var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            var mainPlayer = subsystemPlayers.MainPlayer;

            mainPlayer?.ComponentGui?.DisplaySmallMessage(Message, MessageColor, Blinking, PlayNotificationSound);
        }
    }
}