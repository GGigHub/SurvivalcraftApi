using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public abstract class ClientPacketBase : Packet {
        public static void SerializeClientId(PacketSerializer archive, ref byte id) {
            archive.Value(ref id);
        }

        public static void SerializeClient(PacketSerializer archive, Client to, ref Client client) {
            byte id = client?.ID ?? 0;
            Guid tokenId = client?.TokenId ?? Guid.Empty;
            Guid playerGuid = client?.PlayerGuid ?? Guid.Empty;
            string nickname = client?.Nickname ?? string.Empty;

            archive.Value(ref id);
            archive.Value(ref tokenId);
            archive.Value(ref playerGuid);
            archive.Value(ref nickname);

            if (archive.IsReading) {
                client = new Client(null, id, tokenId, playerGuid, nickname);
            }
        }
    }
}
