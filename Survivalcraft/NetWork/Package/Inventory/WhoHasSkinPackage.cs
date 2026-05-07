using Engine;

namespace Game.NetWork;

public class WhoHasSkinPackage : IPackage {
    public override byte ID => (byte)PackageType.ClothingWhoHas;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public string SkinName;

    public WhoHasSkinPackage() { }
    public WhoHasSkinPackage(string skinName) {
        SkinName = skinName;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref SkinName);
    }

    public override void Handle(bool isServer) {
        if (CharacterSkinsManager.HasSkinRes(SkinName)) {
            NetworkManager.QueuePackage(new WhoHasSkinReplyPackage(SkinName));
        }
    }
}