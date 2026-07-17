using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using Engine.Graphics;
using Game;
using Game.Network;
using GameEntitySystem;
using TemplatesDatabase;
using Engine;

namespace Game;

public enum NetworkState {
    NotConnected, Connected, ProjectLoaded, LoadTerrain, Playing
}

public enum RelayHostCodeStatus {
    Inactive, Creating, Ready, Failed
}

public class RelayJoinInfo(IPEndPoint endPoint, string roomCode) {
    public IPEndPoint EndPoint { get; } = endPoint;
    public string RoomCode { get; } = roomCode;
}

public static class NetworkManager {
    public static NetworkClient NetworkClient { get; set; } = null;
    public static NetworkServer NetworkServer { get; set; } = null;
    public static RelayClient RelayClient { get; set; } = null;
    public static RelayHost RelayHost { get; set; } = null;
    public static PacketFragment PackageFragment { get; } = new();
    public static RelayHostCodeStatus RelayHostCodeStatus { get; set; } = RelayHostCodeStatus.Inactive;

    public static CancellationTokenSource RelayCancelToken { get; private set; }
    public static bool IsServerRunning => (NetworkServer?.IsRunning ?? false) || (RelayHost?.IsConnected ?? false);
    public static bool IsClientRunning => (NetworkClient?.IsConnected ?? false) || (RelayClient?.IsConnected ?? false);
    public static bool IsClientStarting => (NetworkClient?.IsRunning ?? false) || (RelayClient?.IsRunning ?? false);
    public static int ClientLatency => IsClientRunning ? LatencyProbe.Latency : -1;
    public static ClientSession MySession => NetworkClient?.MySession ?? RelayClient?.MySession;
    public static int PlayerIndex => MySession?.PlayerIndex ?? -1;
    public static Guid NetworkGuid => MySession?.NetworkGuid ?? Guid.Empty;
    public static NetworkState MyState => MySession?.NetworkState ?? NetworkState.NotConnected;
    public static void SetPlayerIndex(int index) => MySession?.PlayerIndex = index;
    public static void SetState(NetworkState state) => MySession?.NetworkState = state;
    public static IEnumerable<ClientSession> ServerSessions =>
        (NetworkServer?.ClientSessions.Values as IEnumerable<ClientSession> ?? [])
        .Concat(RelayHost?.Sessions ?? []);

    public static void Initialize() {
        RootManager.Initialize();
        PacketManager.Initialize();
    }
    public static void Update() {
        NetworkServer?.Update();
        NetworkClient?.Update();
        RelayHost?.Update();
        RelayClient?.Update();
        PackageFragment.CleanupStates();
    }
    public static void Stop() {
        NetworkServer?.Stop();
        NetworkClient?.Disconnect();
        RelayHost?.Disconnect();
        RelayClient?.Disconnect();
        NetworkServer = null;
        NetworkClient = null;
        RelayHost = null;
        RelayClient = null;
        RelayCancelToken?.Cancel();
        NetworkBodySmoothing.Clear();
        RelayHostCodeStatus = RelayHostCodeStatus.Inactive;
    }
    public static void StartServer() {
        NetworkServer = new();
        NetworkServer.Start();
    }
    public static void ConnectServer(IPEndPoint iPEndPoint) {
        NetworkClient = new();
        NetworkClient.Connect(iPEndPoint);
    }
    public static async Task<bool> StartRelayHostAsync() {
        try {
            RelayHostCodeStatus = RelayHostCodeStatus.Creating;
            CancellationTokenSource cts = new();
            RelayCancelToken = cts;

            RelayCandidateInfo[] candidates = await CenterServerClient.GetRelayCandidatesAsync();
            foreach (RelayCandidateInfo candidate in candidates
              .OrderBy(c => c.IsBusy).ThenBy(c => c.Priority).ThenBy(c => c.RoomCount).ThenBy(c => c.PeerCount).Take(3)) {
                try {
                    IPAddress address = await NetworkHelper.TryGetIPAddress(candidate.Host);
                    if (address == null) continue;

                    RelayHost relayHost = new();
                    relayHost.Start(new IPEndPoint(address, candidate.UdpPort));
                    for (int i = 0; i < 200; i++) {
                        if (relayHost?.IsDisconnected == true) break;
                        if (relayHost?.IsConnected == true) return true;
                        if (cts.IsCancellationRequested == true) {
                            RelayHostCodeStatus = RelayHostCodeStatus.Inactive;
                            relayHost?.Disconnect();
                            return false;
                        }
                        relayHost.Update();
                        await Task.Delay(50);
                    }
                    relayHost?.Disconnect();
                }
                catch (Exception ex) {
                    Log.Warning($"Failed to start relay host through candidate {candidate.RelayId}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) {
            Log.Warning($"Failed to get relay candidates from center server: {ex.Message}");
        }
        if (RelayHostCodeStatus == RelayHostCodeStatus.Creating) {
            RelayHostCodeStatus = RelayHostCodeStatus.Failed;
        }
        return false;
    }
    public static void StartRelayHost(IPEndPoint relayEndPoint) {
        RelayHost = new();
        RelayHost.Start(relayEndPoint);
    }
    public static void ConnectRelay(IPEndPoint relayEndPoint, string roomCode) {
        RelayClient = new(roomCode);
        RelayClient.Connect(relayEndPoint);
    }
    public static void Queue(Packet packet) {
        NetworkClient?.Send(packet);
        NetworkServer?.Board(packet);
        RelayClient?.Send(packet);
        RelayHost?.Board(packet);
    }
}
