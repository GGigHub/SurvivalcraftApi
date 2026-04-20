namespace Game.NetWork;

public interface IPackage
{
    byte ID { get;  }
    Client To { get; set; }
    Client Except { get; set; }
    Client From { get; set; }
    ClientState MinNeedState { get; }
    void WriteData(PackageStreamWriter writer);
    void ReadData(PackageStreamReader reader);
    void Handle(ProjectNet projectNet, NetworkBase networkBase,bool isServer);
}
