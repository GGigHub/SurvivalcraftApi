using Engine;

namespace Game.Network;

public class RequestSkinPacket : Packet {
    public override byte ID => (byte)PacketType.ClothingRequestSkin;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;
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
            NetworkManager.QueuePacket(new ReplySkinPacket(SkinName));
        }
        else if (!CharacterSkinsManager.waitReplyList.Contains(SkinName)) {
            NetworkManager.QueuePacket(new WhoHasSkinPacket(SkinName));
            CharacterSkinsManager.waitReplyList.Add(SkinName);
        }
    }
}

