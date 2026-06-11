using Engine;

namespace Game.Network;

public class RequestSkinPacket : Packet {
    public override byte ID => (byte)PacketType.ClothingRequestSkin;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public string SkinName;
    public RequestSkinPacket() { }
    public RequestSkinPacket(string skinName) {
        SkinName = skinName;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref SkinName);
    }
    public override void Handle(bool isServer) {
        if (CharacterSkinsManager.HasSkinRes(SkinName)) {
            NetworkManager.Queue(new ReplySkinPacket(SkinName));
        }
        else if (!CharacterSkinsManager.waitReplyList.Contains(SkinName)) {
            NetworkManager.Queue(new WhoHasSkinPacket(SkinName));
            CharacterSkinsManager.waitReplyList.Add(SkinName);
        }
    }
}

