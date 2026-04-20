using Engine;
using Game.NetWork.Packages;
using GameEntitySystem;
using LiteNetLib;
using System;
using System.Collections.Generic;

namespace Game.NetWork;

internal sealed class NetRuntimeController
{
    private readonly NetworkBase m_node;
    private readonly NetworkFragment m_chunkTransport;
    private readonly NetPacketTransport m_packetTransport;

    public NetRuntimeController(NetworkBase node, NetworkFragment chunkTransport, NetPacketTransport packetTransport)
    {
        m_node = node;
        m_chunkTransport = chunkTransport;
        m_packetTransport = packetTransport;
    }

    public void StopImmediate()
    {
        try
        {
            m_node.pendingPeer = null;
            m_node.agreeOnPendingPeer.Clear();
            m_node.pendingStartUtc = null;
            m_node.PendingPackages.Clear();
            if (m_node.WorkType == WorkType.Server)
            {
                m_node.RemoveAllClients("Server is shutting down.");
            }
            if (m_node.netManager.IsRunning)
            {
                m_node.netManager.Stop(true);
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
        finally
        {
            m_node.CleanupAfterStop();
        }
    }

    public void Update()
    {
        m_chunkTransport.CleanupStates();
        SubsystemInventories.flushSyncItems();
        UpdatePendingJoinState();
        FlushClientsToRemove();
        FlushPendingPackages();

        if (m_node.isStopping)
        {
            StopImmediate();
            return;
        }

        try
        {
            m_node.netManager.PollEvents();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private void UpdatePendingJoinState()
    {
        if (m_node.pendingPeer == null)
        {
            return;
        }

        var toRemoveIds = new List<byte>();
        foreach (byte id in m_node.agreeOnPendingPeer)
        {
            if (!m_node.Clients.ContainsKey(id) || !m_node.Clients[id].IsConnected)
            {
                toRemoveIds.Add(id);
            }
        }
        foreach (byte id in toRemoveIds)
        {
            m_node.agreeOnPendingPeer.Remove(id);
        }

        if (!m_node.pendingStartUtc.HasValue)
        {
            m_node.pendingStartUtc = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - m_node.pendingStartUtc.Value).TotalSeconds > NetworkBase.PendingJoinTimeoutSeconds)
        {
            CompletePendingJoin();
            m_node.agreeOnPendingPeer.Clear();
            return;
        }

        if (m_node.agreeOnPendingPeer.Count == 0 && m_node.PendingClient != null)
        {
            CompletePendingJoin();
        }
    }

    private void CompletePendingJoin()
    {
        if (m_node.PendingClient != null)
        {
            m_node.AddClient(m_node.PendingClient);
            m_node.QueuePackage(new ClientPackage(m_node.Clients.Values) { To = m_node.PendingClient });
        }
        m_node.pendingPeer = null;
        m_node.pendingStartUtc = null;
    }

    private void FlushClientsToRemove()
    {
        foreach (var pair in m_node.ClientsToRemove)
        {
            m_node.RemoveClientImmediate(pair.Key, pair.Value);
        }
        m_node.ClientsToRemove.Clear();
    }

    private void FlushPendingPackages()
    {
        if (m_node.PendingPackages.Count == 0 || !Time.PeriodicEvent(0.05, 0.0))
        {
            return;
        }

        lock (m_node.PendingPackages)
        {
            if (m_node.WorkType == WorkType.Client)
            {
                FlushClientPackages();
            }
            else
            {
                FlushServerPackages();
            }
            m_node.PendingPackages.Clear();
        }
    }

    private void FlushClientPackages()
    {
        if (m_node.Clients.Count <= 0)
        {
            return;
        }

        Client client = m_node.Clients[0];
        byte[] payload = BuildPayloadForClient(client, false);
        if (payload == null)
        {
            return;
        }
        m_node.SendPayload(payload, client.Peer);
        m_node.sec_length += payload.Length;
    }

    private void FlushServerPackages()
    {
        foreach (Client client in m_node.Clients.Values)
        {
            if (!client.IsConnected)
            {
                continue;
            }

            byte[] payload = BuildPayloadForClient(client, true);
            if (payload == null)
            {
                continue;
            }
            m_node.SendPayload(payload, client.Peer);
            m_node.sec_length += payload.Length;
        }
    }

    private byte[] BuildPayloadForClient(Client client, bool respectMinState)
    {
        List<IPackage> packages = new List<IPackage>();
        foreach (IPackage packet in m_node.PendingPackages)
        {
            if (packet.To != null && packet.To != client) continue;
            if (packet.Except != null && packet.Except == client) continue;
            if (respectMinState && client.State < packet.MinNeedState && m_node.WorkType != WorkType.Client) continue;
            packages.Add(packet);
        }
        return packages.Count > 0 ? m_packetTransport.BuildOutgoingPayload(packages, m_node.IsServer) : null;
    }
}
