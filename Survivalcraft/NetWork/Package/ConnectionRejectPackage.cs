namespace Game.NetWork.Packages;

public class ConnectionRejectPackage : IPackage
{
    public byte ID => (byte)PackageType.ConnectionReject;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.NotConnected;

    public string reason;

    public ConnectionRejectPackage() { }

    public ConnectionRejectPackage(string r) { reason = r; }

    public void Handle(ProjectNet projectNet, bool isServer) 
    {
        NetworkManager.Net.Stop($"[连接拒绝]{reason}");
    }

    public void ReadData(PackageStreamReader reader)
    {
        reason = reader.ReadString();
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(reason);
    }

}
