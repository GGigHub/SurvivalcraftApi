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

namespace Game;

public enum NetworkState {
    NotConnected, Connected, ProjectLoaded, LoadTerrain, Playing
}

public static class NetworkManager {
    public static NetworkClient NetworkClient { get; set; } = null;
    public static NetworkServer NetworkServer { get; set; } = null;

    public static bool IsServerRunning => NetworkServer?.IsRunning ?? false;
    public static bool IsClientRunning => NetworkClient?.IsConnected ?? false;

    public static ClientSession MySession => NetworkClient?.MySession;
    public static int PlayerIndex => MySession?.PlayerIndex ?? -1;
    public static Guid NetworkGuid => MySession?.NetworkGuid ?? Guid.Empty;
    public static NetworkState MyState => MySession?.NetworkState ?? NetworkState.NotConnected;
    public static void SetPlayerIndex(int index) { if (MySession != null) MySession.PlayerIndex = index; }
    public static void SetState(NetworkState state) { if (MySession != null) MySession.NetworkState = state; }
    public static IEnumerable<ClientSession> ServerSessions =>
        NetworkServer?.ClientSessions.Values ?? Enumerable.Empty<ClientSession>();

    public static void Initialize() {
        RootManager.Initialize();
        PacketManager.Initialize();
    }
    public static void Update() {
        NetworkServer?.Update();
        NetworkClient?.Update();
    }
    public static void Stop() {
        NetworkServer?.Stop();
        NetworkClient?.Disconnect();
        NetworkServer = null;
        NetworkClient = null;
    }
    public static bool StartServer() {
        NetworkServer = new();
        return NetworkServer.Start();
    }
    public static bool ConnectServer(IPEndPoint iPEndPoint) {
        NetworkClient = new();
        return NetworkClient.Connect(iPEndPoint);
    }
    public static void Queue(Packet packet) {
        NetworkClient?.Send(packet);
        NetworkServer?.Board(packet);
    }
}
