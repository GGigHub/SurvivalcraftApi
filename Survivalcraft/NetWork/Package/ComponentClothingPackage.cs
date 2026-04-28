using Engine;

namespace Game.NetWork.Packages;

public class ComponentClothingPackage : IPackage {
    public override byte ID => (byte)PackageType.Clothing;

    public enum DataType {
        RequestSkin,
        ReplySkin,
        WhoHas,
        WhoHasReply
    }

    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public string SkinName;
    public byte[] m_skinData;

    public DataType m_type;

    public ComponentClothingPackage() {
    }

    public ComponentClothingPackage(string skinName, DataType type) {
        m_type = type;
        SkinName = skinName;
        if (CharacterSkinsManager.HasSkinRes(SkinName)) {
            using Stream stream = Storage.OpenFile(Storage.CombinePaths(ModsManager.CharacterSkinsDirectoryName, skinName), OpenFileMode.Read);
            m_skinData = ModsManager.StreamToBytes(stream);
        }
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_type);
        archive.Value(ref SkinName);
        switch (m_type) {
            case DataType.WhoHas:
            case DataType.RequestSkin:
                break;
            case DataType.WhoHasReply:
            case DataType.ReplySkin:
                int len = archive.IsWriting ? m_skinData.Length : 0;
                archive.Value(ref len);
                byte[] skinData = archive.IsWriting ? m_skinData : null;
                archive.Raw(ref skinData, len);
                if (archive.IsReading) {
                    m_skinData = skinData;
                }
                break;
        }
    }

    public override void Handle(bool isServer) {
        switch (m_type) {
            case DataType.RequestSkin:
                if (CharacterSkinsManager.HasSkinRes(SkinName)) {
                    NetworkManager.QueuePackage(new ComponentClothingPackage(SkinName, DataType.ReplySkin));
                }
                else if (!CharacterSkinsManager.waitReplyList.Contains(SkinName)) {
                    NetworkManager.QueuePackage(new ComponentClothingPackage(SkinName, DataType.WhoHas));
                    CharacterSkinsManager.waitReplyList.Add(SkinName);
                }
                break;
            case DataType.WhoHasReply:
            case DataType.ReplySkin:
                if (CharacterSkinsManager.waitReplyList.Contains(SkinName)) {
                    CharacterSkinsManager.waitReplyList.Remove(SkinName);
                }
                CharacterSkinsManager.SaveSkinToFile(SkinName, m_skinData);
                break;
            case DataType.WhoHas:
                if (CharacterSkinsManager.HasSkinRes(SkinName)) {
                    NetworkManager.QueuePackage(new ComponentClothingPackage(SkinName, DataType.WhoHasReply));
                }
                break;
        }
    }
}
