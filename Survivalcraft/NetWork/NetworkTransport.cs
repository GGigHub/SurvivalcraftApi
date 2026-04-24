using System.Net;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.NetWork;

internal sealed class NetPacketTransport {
    private readonly NetworkBase m_node;
    private readonly NetworkFragment m_chunkTransport;

    public NetPacketTransport(NetworkBase node, NetworkFragment chunkTransport) {
        m_node = node;
        m_chunkTransport = chunkTransport;
    }

    public void HandleUnconnectedEvent(IPEndPoint remoteEndPoint, NetPacketReader reader, bool fromBroadcast) {
        if (m_node.CurrentStage == NetworkBase.Stage.NotConnected) return;
        var dataReader = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
        try {
            if (!m_node.TryDecodePackages(dataReader, null, null, remoteEndPoint, out List<IPackage> packages)) {
                return;
            }

            foreach (var package in packages) {
                package.From.IsLocalRemote = fromBroadcast;
                try {
                    package.Handle(m_node.IsServer);
                }
                catch (Exception e) {
                    Log.Error(string.Format("[{0}]{1}", package.GetType().Name, e.Message));
                }
            }
        }
        finally {
            reader.Recycle();
        }
    }

    public void HandlePeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        string reason = disconnectInfo.Reason.ToString();
        if (disconnectInfo.AdditionalData != null && disconnectInfo.AdditionalData.TryGetString(out string extra)) {
            Log.Information("----Additional message----");
            Log.Information(extra);
            Log.Information("----End of message----");
            reason = extra;
        }

        if (m_node.WorkType == WorkType.Client) {
            m_node.Stop(reason);
            return;
        }

        Client client = peer.Tag as Client;
        if (client == null) {
            return;
        }

        client.State = ClientStage.NotConnected;
        m_node.RemoveClientImmediate(client);
        m_node.OnClientStateChanged?.Invoke(client);
        if (m_node is NetworkServer server) {
            if (peer == server.pendingPeer) {
                server.pendingPeer = null;
                server.agreeOnPendingPeer.Clear();
                server.pendingStartUtc = null;
                return;
            }

            server.agreeOnPendingPeer.Remove(client.ID);
            server.DeliveryEvent(null, null);
        }
    }

    public void AddPendingHandlePackage(IPackage package) {
        m_node.PendingHandlePackages.Add(package);
    }

    public void HandleNetworkReceive(NetPeer peer, NetDataReader reader) {
        if (m_node.CurrentStage == NetworkBase.Stage.NotConnected) return;
        Client fromClient = peer.Tag as Client;
        if (!m_node.TryDecodePackages(reader, peer, null, null, out List<IPackage> packages)) {
            return;
        }

        packages.InsertRange(0, m_node.PendingHandlePackages);
        m_node.PendingHandlePackages.Clear();
        try {
            if (!m_node.HasReceiveHandlers) {
                m_node.PendingHandlePackages.AddRange(packages);
            }
            else {
                m_node.DispatchReceivedPackages(packages);
            }
        }
        catch (Exception e) {
            Log.Error(e);
            if (m_node is NetworkServer server) {
                server.RemoveClient(fromClient, "Error On RecieveEvent:\n" + e);
            }
        }
    }

    public void TurnOnPackageHandle(ProjectNet projectNet) {
        NetworkManager.OnRecieve += list => {
            foreach (var package in list) {
                try {
                    package.Handle(m_node.IsServer);
                }
                catch (Exception e) {
                    Log.Error(string.Format("[{0}]{1}", package.GetType().Name, e.Message));
                }
            }
        };
    }

    public void SendWriterFromPackage(IPackage package, IPEndPoint endPoint, bool isSend = true) {
        SendWriterFromPackages([package], null, null, endPoint, isSend);
    }

    public void SendWriterFromPackage(IPackage package, NetPeer netPeer, bool useDeliveryEvent = false) {
        SendWriterFromPackages([package], netPeer, null, null, useDeliveryEvent);
    }

    public void SendWriterFromPackage(IPackage package, ConnectionRequest request, bool reject) {
        if (reject) request.Reject();
        SendWriterFromPackages([package], null, request, null);
    }

    public void SendWriterFromPackages(IEnumerable<IPackage> packages, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        byte[] payload = BuildOutgoingPayload(packages, m_node.IsServer);
        m_chunkTransport.SendPayload(payload, netPeer, request, iPEndPoint, useDeliveryEvent);
    }

    public byte[] BuildOutgoingPayload(IEnumerable<IPackage> packages, bool isServer) {
        PackageStreamWriter writer = new PackageStreamWriter {
            IsServer = isServer,
            Project = GameManager.Project
        };
        foreach (var package in packages) {
            writer.Write(NetworkFragment.VerifyByte);
            writer.Write(package.ID);
            package.WriteData(writer);
        }
        return writer.Data();
    }

    public bool TryDecodePackages(NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out List<IPackage> packages) {
        packages = null;
        if (m_chunkTransport.TryReadPayload(reader, peer, request, endPoint, out byte[] payload, out bool consumedChunkFrame)) {
            if (payload == null) {
                return false;
            }
            packages = PackageManager.DecodePackagesFromPayload(m_node, payload, peer, request, endPoint);
            return true;
        }

        if (consumedChunkFrame) {
            return false;
        }

        packages = PackageManager.DecodePackages(m_node, reader, peer, request, endPoint);
        return true;
    }

    public static int SendWriterFromPackage(NetManager netManager, IEnumerable<IPackage> packages, IPEndPoint endPoint) {
        PackageStreamWriter writer = new PackageStreamWriter {
            IsServer = false,
            Project = GameManager.Project
        };
        foreach (var packet in packages) {
            writer.Write(NetworkFragment.VerifyByte);
            writer.Write(packet.ID);
            packet.WriteData(writer);
        }
        byte[] payload = writer.Data();
        NetworkFragment.SendPayload(netManager, payload, endPoint);
        return payload.Length;
    }
}
