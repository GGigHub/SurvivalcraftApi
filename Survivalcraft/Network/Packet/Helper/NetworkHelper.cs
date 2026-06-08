using System.Net;
using System.Net.NetworkInformation;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;
#pragma warning disable CA1416


namespace Game.Network;

public static class NetworkHelper {

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
        string sourceKey = peer?.Address.ToString() ?? request?.RemoteEndPoint.ToString() ?? endPoint?.ToString() ?? "unknown";
        if (chunkTransport.TryProcessIncoming(reader, sourceKey, out byte[] payload)) {
            packets = PacketManager.DecodePacketsFromPayload(clients, payload, peer, request, endPoint);
            return true;
        }

        return false;
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

    public static void SendWriterFromPacket(NetManager netManager, Packet packet, IPEndPoint endPoint, bool isSend = true) {
        SendWriterFromPackets(netManager, [packet], null, null, endPoint, isSend);
    }

    public static void SendWriterFromPacket(NetManager netManager, Packet packet, NetPeer netPeer, bool useDeliveryEvent = false) {
        SendWriterFromPackets(netManager, [packet], netPeer, null, null, useDeliveryEvent);
    }

    public static void SendWriterFromPacket(NetManager netManager, Packet packet, ConnectionRequest request, bool reject) {
        if (reject) request.Reject();
        SendWriterFromPackets(netManager, [packet], null, request, null);
    }

    public static void SendWriterFromPackets(NetManager netManager, IEnumerable<Packet> packets, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        byte[] payload = BuildOutgoingPayload(packets);
        foreach (var chunk in PacketFragment.FragmentPayload(payload)) {
            if (netPeer != null) {
                netPeer.Send(chunk, DeliveryMethod.ReliableOrdered);
            }
            else if (request != null) {
                netManager?.SendUnconnectedMessage(chunk, request.RemoteEndPoint);
            }
            else if (iPEndPoint != null) {
                if (!useDeliveryEvent && netManager != null) {
                    netManager.Connect(iPEndPoint, chunk);
                } else {
                    netManager?.SendUnconnectedMessage(chunk, iPEndPoint);
                }
            }
        }
    }

    public static byte[] BuildPayloadForClient(IEnumerable<Packet> pendingPackets, ClientSession client, bool respectMinState, bool isServer) {
        List<Packet> packets = new List<Packet>();
        foreach (Packet packet in pendingPackets) {
            if (packet.ToPlayerIndex != -1 && packet.ToPlayerIndex != client.PlayerIndex) continue;
            if (packet.ExceptPlayerIndex != -1 && packet.ExceptPlayerIndex == client.PlayerIndex) continue;
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
