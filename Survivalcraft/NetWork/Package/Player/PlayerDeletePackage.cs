using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerDeletePackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerDelete;
    public override ClientStage MinNeedState => ClientStage.Connected;

    public override void Serialize(PackageSerializer archive) { }

    public override void Handle(bool isServer) {
        if (isServer) {
            NetworkManager.Server.RemoveClient(From);
        }
    }
}