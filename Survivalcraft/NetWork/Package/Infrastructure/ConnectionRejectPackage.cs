

namespace Game.NetWork;

public class ConnectionRejectPackage : IPackage {
    public override byte ID => (byte)PackageType.ConnectReject;
    public override ClientStage MinNeedState => ClientStage.NotConnected;

    public string reason;

    public ConnectionRejectPackage() {
    }

    public ConnectionRejectPackage(string r) {
        reason = r;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref reason);
    }

    public override void Handle(bool isServer) {
        NetworkManager.Stop();
    }
}