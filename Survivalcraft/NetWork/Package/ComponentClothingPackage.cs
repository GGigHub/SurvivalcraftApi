using Engine;

namespace Game.NetWork.Packages;

internal static class ClothingPackageHelpers {
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

public class ReplySkinPackage : IPackage {
    public override byte ID => (byte)PackageType.ClothingReplySkin;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public string SkinName;
    public byte[] m_skinData;

    public ReplySkinPackage() { }
    public ReplySkinPackage(string skinName) {
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
