using Engine;

namespace Game.NetWork;

public class RequestSkinPackage : IPackage {
    public override byte ID => (byte)PackageType.ClothingRequestSkin;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public string SkinName;
    public RequestSkinPackage() { }
    public RequestSkinPackage(string skinName) {
        SkinName = skinName;
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref SkinName);
    }
    public override void Handle(bool isServer) {
        if (CharacterSkinsManager.HasSkinRes(SkinName)) {
            NetworkManager.QueuePackage(new ReplySkinPackage(SkinName));
        }
        else if (!CharacterSkinsManager.waitReplyList.Contains(SkinName)) {
            NetworkManager.QueuePackage(new WhoHasSkinPackage(SkinName));
            CharacterSkinsManager.waitReplyList.Add(SkinName);
        }
    }
}