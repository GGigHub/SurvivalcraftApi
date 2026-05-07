using Engine;

namespace Game.Network;

public static class ClientPacketSerialization {
    public static void SerializeClientId(PacketSerializer archive, ref byte id) {
        archive.Value(ref id);
    }

    public static void SerializeClient(PacketSerializer archive, Client to, ref Client client) {
        byte id = client?.ID ?? 0;
        Guid tokenId = client?.TokenId ?? Guid.Empty;
        Guid playerGuid = client?.PlayerGuid ?? Guid.Empty;
        string nickname = client?.Nickname ?? string.Empty;

        if (archive.IsWriting && to != null && client != null && to.PlayerGuid == client.PlayerGuid) {
            playerGuid = client.TokenId;
        }

        archive.Value(ref id);
        archive.Value(ref tokenId);
        archive.Value(ref playerGuid);
        archive.Value(ref nickname);

        if (archive.IsReading) {
            client = new Client(null, id, tokenId, playerGuid, nickname);
        }
    }
}

