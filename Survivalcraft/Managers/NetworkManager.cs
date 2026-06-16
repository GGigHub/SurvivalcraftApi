using System;
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

    public static bool IsServerMode => NetworkServer?.IsRunning ?? false;
    public static bool IsNetworkMode => NetworkClient?.IsConnected ?? false;

    public static int PlayerIndex => NetworkClient?.MySession?.PlayerIndex ?? -1;
    public static Guid NetworkGuid => NetworkClient?.MySession?.NetworkGuid ?? Guid.Empty;
    public static NetworkState MyState => NetworkClient?.MySession?.NetworkState ?? NetworkState.NotConnected;

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
