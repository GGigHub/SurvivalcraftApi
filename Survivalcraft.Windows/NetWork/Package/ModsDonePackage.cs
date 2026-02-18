using Engine;
using Game;
using Game.NetWork;
using Game.NetWork.Packages;

namespace Game.NetWork.Packages;

public class ModsDonePackage : IPackage
{
    public byte ID => (byte)PackageType.ModsDone;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.NotConnected;

    public bool Ok;
    public string Error;

    public ModsDonePackage() { }

    public ModsDonePackage(bool ok, string error)
    {
        Ok = ok;
        Error = error;
    }

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        if (isServer)
        {
            if (From?.Peer != null)
            {
                From.Peer.Disconnect();
            }
            return;
        }

        if (!Ok)
        {
            CommonLib.ModTransferState.Error = Error ?? string.Empty;
            return;
        }

        CommonLib.ModTransferState.InProgress = false;
        SettingsManager.WillEnterServer = CommonLib.ModTransferState.TargetServer ?? string.Empty;
        SettingsManager.WillEnterServerPwd = CommonLib.ModTransferState.TargetPassword ?? string.Empty;
        SettingsManager.SaveSettings();
        GameRestarter.RestartGame();
    }

    public void ReadData(PackageStreamReader reader)
    {
        Ok = reader.ReadBoolean();
        Error = reader.ReadString();
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(Ok);
        writer.Write(Error ?? string.Empty);
    }
}
