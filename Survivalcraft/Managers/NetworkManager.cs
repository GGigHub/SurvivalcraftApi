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

    public static bool IsServerRunning => (NetworkServer?.IsRunning ?? false) || (RelayHost?.IsConnected ?? false);
    public static bool IsClientRunning => (NetworkClient?.IsConnected ?? false) || (RelayClient?.IsConnected ?? false);
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
        NetworkFaultInjector.Update();
        NetworkServer?.Update();
        NetworkClient?.Update();
        RelayHost?.Update();
        RelayClient?.Update();
        PackageFragment.CleanupStates();
        NetworkCompatibilityRuntime.UpdateClientHandshake(MySession);
        if (NetworkCompatibilityRuntime.TryTakePendingClientRejection(
                out NetworkCompatibilityResult rejection)) {
            StopClient();
            GameManager.DisposeProject();
            ScreensManager.SwitchScreen("MainMenu");
            DialogsManager.Alert(
                "Incompatible multiplayer",
                rejection.ToDisplayMessage());
        }
    }
    public static void Stop() {
        NetworkServer?.Stop();
        RelayHost?.Disconnect();
        NetworkServer = null;
        RelayHost = null;
        StopClient();
        NetworkBodyInterpolation.Clear();
        PlayerBodyUpdatePacket.ClearSnapshots();
        RelayHostCodeStatus = RelayHostCodeStatus.Inactive;
        NetworkFaultInjector.ClearPending();
    }

    public static void StopClient() {
        NetworkClient?.Disconnect();
        RelayClient?.Disconnect();
        NetworkClient = null;
        RelayClient = null;
        LatencyProbe.Reset();
    }
    public static void StartServer() {
        NetworkServer = new();
        NetworkServer.Start();
    }
    public static void ConnectServer(IPEndPoint iPEndPoint) {
        NetworkClient = new();
        NetworkClient.Connect(iPEndPoint);
    }

    public static async Task StartRelayHostAsync() {
        try {
            RelayHostCodeStatus = RelayHostCodeStatus.Creating;
            IPEndPoint endPoint = await CenterServerClient.GetBestRelayEndPointAsync();
            RelayHost = new RelayHost();
            RelayHost.Start(endPoint);
        }
        catch (Exception ex) {
            Log.Warning($"Failed to start relay host: {ex.Message}");
            RelayHostCodeStatus = RelayHostCodeStatus.Failed;
        }
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
