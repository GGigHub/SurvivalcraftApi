using Engine;
using Game.NetWork.Packages;

namespace Game.NetWork;

internal sealed class NetRuntimeController {
    private readonly INetworkNode m_node;
    private readonly NetworkFragment m_chunkTransport;
    private readonly NetPacketTransport m_packetTransport;

    public NetRuntimeController(INetworkNode node, NetworkFragment chunkTransport, NetPacketTransport packetTransport) {
        m_node = node;
        m_chunkTransport = chunkTransport;
        m_packetTransport = packetTransport;
    }

    public void StopImmediate() {
        try {
            m_node.PendingPackages.Clear();
            if (m_node.WorkType == WorkType.Server) {
                ((NetworkServer)m_node).RemoveAllClients("Server is shutting down.");
            }
            if (m_node.NetManager.IsRunning) {
                m_node.NetManager.Stop(true);
            }
        }
        catch (Exception e) {
            Log.Error(e);
        }
        finally {
            m_node.CleanupAfterStop();
        }
    }

    public void Update() {
        m_chunkTransport.CleanupStates();
        SubsystemInventories.flushSyncItems();
        FlushClientsToRemove();
        FlushPendingPackages();

        if (m_node.isStopping) {
            StopImmediate();
            return;
        }

        try {
            m_node.NetManager.PollEvents();
        }
        catch (Exception ex) {
            Log.Error(ex);
        }
    }

    private void FlushClientsToRemove() {
        if (m_node is not NetworkServer server) {
            return;
        }
        foreach (var pair in server.ClientsToRemove) {
            server.RemoveClientImmediate(pair.Key, pair.Value);
        }
        server.ClientsToRemove.Clear();
    }

    private void FlushPendingPackages() {
        if (m_node.PendingPackages.Count == 0 || !Time.PeriodicEvent(0.05, 0.0)) {
            return;
        }

        lock (m_node.PendingPackages) {
            if (m_node.WorkType == WorkType.Client) {
                FlushClientPackages();
            }
            else {
                FlushServerPackages();
            }
            m_node.PendingPackages.Clear();
        }
    }

    private void FlushClientPackages() {
        if (m_node.Clients.Count <= 0) {
            return;
        }

        Client client = m_node.Clients[0];
        byte[] payload = BuildPayloadForClient(client, false);
        if (payload == null) {
            return;
        }
        m_node.SendPayload(payload, client.Peer);
    }

    private void FlushServerPackages() {
        foreach (Client client in m_node.Clients.Values) {
            if (!client.IsConnected) {
                continue;
            }

            byte[] payload = BuildPayloadForClient(client, true);
            if (payload == null) {
                continue;
            }
            m_node.SendPayload(payload, client.Peer);
        }
    }

    private byte[] BuildPayloadForClient(Client client, bool respectMinState) {
        List<IPackage> packages = new List<IPackage>();
        foreach (IPackage packet in m_node.PendingPackages) {
            if (packet.To != null && packet.To != client) continue;
            if (packet.Except != null && packet.Except == client) continue;
            if (respectMinState && client.State < packet.MinNeedState && m_node.WorkType != WorkType.Client) continue;
            packages.Add(packet);
        }
        return packages.Count > 0 ? m_packetTransport.BuildOutgoingPayload(packages, m_node.IsServer) : null;
    }
}
