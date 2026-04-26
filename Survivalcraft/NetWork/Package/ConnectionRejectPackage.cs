namespace Game.NetWork.Packages;

public class ConnectionRejectPackage : IPackage {
    public byte ID => (byte)PackageType.ConnectReject;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.NotConnected;

    public string reason;

    public ConnectionRejectPackage() { }

    public ConnectionRejectPackage(string r) { reason = r; }

    public void Handle(bool isServer) {
        NetworkManager.Stop();
    }

    public void ReadData(PackageStreamReader reader) {
        reason = reader.ReadString();
    }

    public void WriteData(PackageStreamWriter writer) {
        writer.Write(reason);
    }

}
