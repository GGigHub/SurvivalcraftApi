using Engine;

namespace Game.NetWork;

public class WhoHasSkinReplyPackage : IPackage {
    public override byte ID => (byte)PackageType.ClothingWhoHasReply;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public string SkinName;
    public byte[] m_skinData;

    public WhoHasSkinReplyPackage() { }
    public WhoHasSkinReplyPackage(string skinName) {
        SkinName = skinName;
        m_skinData = ClothingPackageHelpers.LoadSkinData(skinName);
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref SkinName);
        ClothingPackageHelpers.SerializeSkinData(archive, ref m_skinData);
    }

    public override void Handle(bool isServer) {
        ClothingPackageHelpers.SaveSkin(SkinName, m_skinData);
    }
}