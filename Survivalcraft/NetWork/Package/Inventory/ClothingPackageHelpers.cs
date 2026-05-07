using Engine;

namespace Game.NetWork;

public static class ClothingPackageHelpers {
    public static byte[] LoadSkinData(string skinName) {
        if (!CharacterSkinsManager.HasSkinRes(skinName)) {
            return null;
        }
        using Stream stream = Storage.OpenFile(Storage.CombinePaths(ModsManager.CharacterSkinsDirectoryName, skinName), OpenFileMode.Read);
        return ModsManager.StreamToBytes(stream);
    }

    public static void SerializeSkinData(PackageSerializer archive, ref byte[] skinData) {
        int len = archive.IsWriting ? skinData.Length : 0;
        archive.Value(ref len);
        byte[] data = archive.IsWriting ? skinData : null;
        archive.Raw(ref data, len);
        if (archive.IsReading) {
            skinData = data;
        }
    }

    public static void SaveSkin(string skinName, byte[] skinData) {
        if (CharacterSkinsManager.waitReplyList.Contains(skinName)) {
            CharacterSkinsManager.waitReplyList.Remove(skinName);
        }
        CharacterSkinsManager.SaveSkinToFile(skinName, skinData);
    }
}