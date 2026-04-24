using Engine;

namespace Game.NetWork.Packages;

public class ComponentClothingPackage : IPackage {

    public byte ID => (byte)PackageType.ComponentClothing;

    public enum DataType {
        RequestSkin,
        ReplySkin,
        WhoHas,
        WhoHasReply
    }

    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public string SkinName;
    public byte[] m_skinData;

    public DataType m_type;

    public ComponentClothingPackage() {

    }
    public ComponentClothingPackage(string skinName, DataType type) {
        m_type = type;
        SkinName = skinName;
        if (CharacterSkinsManager.HasSkinRes(SkinName)) {
            using (Stream stream = Storage.OpenFile(Storage.CombinePaths(ModsManager.CharacterSkinsDirectoryName, skinName), OpenFileMode.Read)) {
                m_skinData = ModsManager.StreamToBytes(stream);
            }
        }
    }
    public void WriteData(PackageStreamWriter writer) {
        writer.WriteEnum(m_type);
        switch (m_type) {
            case DataType.WhoHas:
            case DataType.RequestSkin:
                writer.Write(SkinName);
                break;
            case DataType.WhoHasReply:
            case DataType.ReplySkin:
                writer.Write(SkinName);
                writer.Write(m_skinData.Length);
                writer.Write(m_skinData);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader) {
        m_type = reader.ReadEnum<DataType>();
        switch (m_type) {
            case DataType.WhoHas:
            case DataType.RequestSkin:
                SkinName = reader.ReadString();
                break;
            case DataType.WhoHasReply:
            case DataType.ReplySkin:
                SkinName = reader.ReadString();
                int len = reader.ReadInt32();
                m_skinData = reader.ReadBytes(len);
                break;

        }
    }

    public void Handle(bool isServer) {
        switch (m_type) {
            case DataType.RequestSkin:
                if (CharacterSkinsManager.HasSkinRes(SkinName)) {
                    NetworkManager.QueuePackage(new ComponentClothingPackage(SkinName, DataType.ReplySkin));
                }
                else {
                    if (!CharacterSkinsManager.waitReplyList.Contains(SkinName)) {
                        NetworkManager.QueuePackage(new ComponentClothingPackage(SkinName, DataType.WhoHas));
                        CharacterSkinsManager.waitReplyList.Add(SkinName);
                    }
                }
                break;
            case DataType.WhoHasReply:
            case DataType.ReplySkin:
                if (CharacterSkinsManager.waitReplyList.Contains(SkinName)) CharacterSkinsManager.waitReplyList.Remove(SkinName);
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
