using System.Net;
using System.Net.NetworkInformation;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;
#pragma warning disable CA1416


namespace Game.Network;

public static class NetworkHelper {

    public static string GetInnerIp() {
        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?.Address.ToString();
    }

    public static PacketStreamReader GetReader(NetDataReader reader) {
        return new PacketStreamReader(reader.GetBytesWithLength());
    }

    public static byte[] BuildOutgoingPayload(IEnumerable<Packet> packets) {
        PacketStreamWriter writer = new();
        foreach (var packet in packets) {
            writer.Write(PacketFragment.VerifyByte);
            writer.Write(packet.ID);
            packet.Serialize(new PacketSerializer(writer));
        }
        return writer.Data();
    }

    public static bool TryDecodePackets(IEnumerable<ClientSession> clients, PacketFragment chunkTransport, NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out List<Packet> packets) {
        packets = null;
        if (chunkTransport.TryRead(reader, peer, request, endPoint, out byte[] payload, out bool consumedChunkFrame)) {
            if (payload == null) {
                return false;
            }
            packets = PacketManager.DecodePacketsFromPayload(clients, payload, peer, request, endPoint);
            return true;
        }

        if (consumedChunkFrame) {
            return false;
        }

        packets = PacketManager.DecodePackets(clients, reader, peer, request, endPoint);
        return true;
    }

    public static void HandleUnconnectedEvent(IEnumerable<ClientSession> clients, bool isServer, PacketFragment chunkTransport, IPEndPoint remoteEndPoint, NetPacketReader reader, bool fromBroadcast) {
        var dataReader = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
        if (!TryDecodePackets(clients, chunkTransport, dataReader, null, null, remoteEndPoint, out List<Packet> packets)) {
            return;
        }

        foreach (var packet in packets) {
            packet.Handle(isServer);
        }
    }

    public static void SendWriterFromPacket(PacketFragment chunkTransport, Packet packet, IPEndPoint endPoint, bool isSend = true) {
        SendWriterFromPackets(chunkTransport, [packet], null, null, endPoint, isSend);
    }

    public static void SendWriterFromPacket(PacketFragment chunkTransport, Packet packet, NetPeer netPeer, bool useDeliveryEvent = false) {
        SendWriterFromPackets(chunkTransport, [packet], netPeer, null, null, useDeliveryEvent);
    }

    public static void SendWriterFromPacket(PacketFragment chunkTransport, Packet packet, ConnectionRequest request, bool reject) {
        if (reject) request.Reject();
        SendWriterFromPackets(chunkTransport, [packet], null, request, null);
    }

    public static void SendWriterFromPackets(PacketFragment chunkTransport, IEnumerable<Packet> packets, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        byte[] payload = BuildOutgoingPayload(packets);
        chunkTransport.Send(payload, netPeer, request, iPEndPoint, useDeliveryEvent);
    }

    public static byte[] BuildPayloadForClient(IEnumerable<Packet> pendingPackets, ClientSession client, bool respectMinState, bool isServer) {
        List<Packet> packets = new List<Packet>();
        foreach (Packet packet in pendingPackets) {
            if (packet.To != null && packet.To != client) continue;
            if (packet.Except != null && packet.Except == client) continue;
            if (respectMinState && client.NetworkState < packet.MinNeedState && isServer) continue;
            packets.Add(packet);
        }
        return packets.Count > 0 ? BuildOutgoingPayload(packets) : null;
    }

    public static PlayerData GetPlayerData(this ClientSession client) {
        if (client == null) return null;
        return GameManager.Project?.FindSubsystem<SubsystemPlayers>()?.PlayersData.FirstOrDefault(p => p.PlayerIndex == client.PlayerIndex);
    }
}
