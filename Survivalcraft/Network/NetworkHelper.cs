using System.Net;
using System.Net.NetworkInformation;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;
#pragma warning disable CA1416


namespace Game.Network {
    public static class NetworkHelper {

        public static string GetInnerIp() {
            try {
                var validTypes = new List<NetworkInterfaceType>
                {
                    NetworkInterfaceType.Ethernet,
                    NetworkInterfaceType.Ethernet3Megabit,
                    NetworkInterfaceType.FastEthernetFx,
                    NetworkInterfaceType.FastEthernetT,
                    NetworkInterfaceType.GigabitEthernet,
                    NetworkInterfaceType.Wireless80211
                };

                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    // 物理网卡类型检测
                    if (validTypes.Contains(ni.NetworkInterfaceType)) {
                        foreach (var ip in ni.GetIPProperties().UnicastAddresses) {
                            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }

                // 备用方案：获取首个非虚拟IPv4地址
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(ip => ip.Address.ToString())
                    .FirstOrDefault() ?? "127.0.0.1";
            }
            catch {
                return "127.0.0.1";
            }
        }

        public static bool IsValidIPAddress(string ip) {
            return IPAddress.TryParse(ip, out _);
        }

        public static bool IsValidPort(int port) {
            return port > 0 && port <= 65535;
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

        public static bool TryDecodePackets(IEnumerable<Client> clients, PacketFragment chunkTransport, NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out List<Packet> packets) {
            packets = null;
            if (chunkTransport.TryReadPayload(reader, peer, request, endPoint, out byte[] payload, out bool consumedChunkFrame)) {
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

        public static void HandleUnconnectedEvent(IEnumerable<Client> clients, bool isServer, PacketFragment chunkTransport, IPEndPoint remoteEndPoint, NetPacketReader reader, bool fromBroadcast) {
            var dataReader = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
            try {
                if (!TryDecodePackets(clients, chunkTransport, dataReader, null, null, remoteEndPoint, out List<Packet> packets)) {
                    return;
                }

                foreach (var packet in packets) {
                    packet.From.IsLocalRemote = fromBroadcast;
                    try {
                        packet.Handle(isServer);
                    }
                    catch (Exception e) {
                        Log.Error(string.Format("[{0}]{1}", packet.GetType().Name, e.Message));
                    }
                }
            }
            finally {
                reader.Recycle();
            }
        }

        public static void HandlePeerDisconnected(NetworkClient node, NetPeer peer, DisconnectInfo disconnectInfo) {
            HandlePeerDisconnectedCore(node, peer, disconnectInfo);
        }

        public static void HandlePeerDisconnected(NetworkServer node, NetPeer peer, DisconnectInfo disconnectInfo) {
            HandlePeerDisconnectedCore(node, peer, disconnectInfo);
        }

        private static void HandlePeerDisconnectedCore(dynamic node, NetPeer peer, DisconnectInfo disconnectInfo) {
            string reason = disconnectInfo.Reason.ToString();
            if (disconnectInfo.AdditionalData != null && disconnectInfo.AdditionalData.TryGetString(out string extra)) {
                Log.Information("----Additional message----");
                Log.Information(extra);
                Log.Information("----End of message----");
                reason = extra;
            }

            if (node.WorkType == WorkType.Client) {
                node.Stop();
                return;
            }

            Client client = peer.Tag as Client;
            if (client == null) {
                return;
            }

            client.State = ClientStage.NotConnected;
            node.RemoveClientImmediate(client, reason);
            node.OnClientStateChanged?.Invoke(client);
            if (node is NetworkServer server) {
                server.HandleDeliveryEvent(null, null);
            }
        }

        public static void HandleNetworkReceive(NetworkClient node, PacketFragment chunkTransport, NetPeer peer, NetDataReader reader) {
            HandleNetworkReceiveCore(node, chunkTransport, peer, reader);
        }

        public static void HandleNetworkReceive(NetworkServer node, PacketFragment chunkTransport, NetPeer peer, NetDataReader reader) {
            HandleNetworkReceiveCore(node, chunkTransport, peer, reader);
        }

        private static void HandleNetworkReceiveCore(dynamic node, PacketFragment chunkTransport, NetPeer peer, NetDataReader reader) {
            if (node.CurrentStage == NetworkStage.NotConnected) return;
            if (!TryDecodePackets(node.Clients.Values, chunkTransport, reader, peer, null, null, out List<Packet> packets)) {
                return;
            }

            packets.InsertRange(0, node.PendingHandlePackets);
            foreach (var packet in packets) {
                try {
                    packet.Handle(node.IsServer);
                }
                catch (Exception e) {
                    Log.Error(string.Format("[{0}]{1}", packet.GetType().Name, e.Message));
                }
            }
        }

        public static void SendWriterFromPacket(NetworkClient node, PacketFragment chunkTransport, Packet packet, IPEndPoint endPoint, bool isSend = true) {
            SendWriterFromPackets(node, chunkTransport, [packet], null, null, endPoint, isSend);
        }

        public static void SendWriterFromPacket(NetworkClient node, PacketFragment chunkTransport, Packet packet, NetPeer netPeer, bool useDeliveryEvent = false) {
            SendWriterFromPackets(node, chunkTransport, [packet], netPeer, null, null, useDeliveryEvent);
        }

        public static void SendWriterFromPacket(NetworkClient node, PacketFragment chunkTransport, Packet packet, ConnectionRequest request, bool reject) {
            if (reject) request.Reject();
            SendWriterFromPackets(node, chunkTransport, [packet], null, request, null);
        }

        public static void SendWriterFromPacket(NetworkServer node, PacketFragment chunkTransport, Packet packet, IPEndPoint endPoint, bool isSend = true) {
            SendWriterFromPackets(node, chunkTransport, [packet], null, null, endPoint, isSend);
        }

        public static void SendWriterFromPacket(NetworkServer node, PacketFragment chunkTransport, Packet packet, NetPeer netPeer, bool useDeliveryEvent = false) {
            SendWriterFromPackets(node, chunkTransport, [packet], netPeer, null, null, useDeliveryEvent);
        }

        public static void SendWriterFromPacket(NetworkServer node, PacketFragment chunkTransport, Packet packet, ConnectionRequest request, bool reject) {
            if (reject) request.Reject();
            SendWriterFromPackets(node, chunkTransport, [packet], null, request, null);
        }

        public static void SendWriterFromPackets(NetworkClient node, PacketFragment chunkTransport, IEnumerable<Packet> packets, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
            byte[] payload = BuildOutgoingPayload(packets);
            chunkTransport.SendPayload(payload, netPeer, request, iPEndPoint, useDeliveryEvent);
        }

        public static void SendWriterFromPackets(NetworkServer node, PacketFragment chunkTransport, IEnumerable<Packet> packets, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
            byte[] payload = BuildOutgoingPayload(packets);
            chunkTransport.SendPayload(payload, netPeer, request, iPEndPoint, useDeliveryEvent);
        }

        public static byte[] BuildPayloadForClient(IEnumerable<Packet> pendingPackets, Client client, bool respectMinState, bool isServer) {
            List<Packet> packets = new List<Packet>();
            foreach (Packet packet in pendingPackets) {
                if (packet.To != null && packet.To != client) continue;
                if (packet.Except != null && packet.Except == client) continue;
                if (respectMinState && client.State < packet.MinNeedState && isServer) continue;
                packets.Add(packet);
            }
            return packets.Count > 0 ? BuildOutgoingPayload(packets) : null;
        }
    }
}
