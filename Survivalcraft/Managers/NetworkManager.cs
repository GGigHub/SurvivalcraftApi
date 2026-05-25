using System;
using System.IO;
using System.Net;
using Engine.Graphics;
using Game;

namespace Game;

public enum NetworkState {
    NotConnected,
    Connected,
    ProjectLoaded,
    LoadTerrain,
    Playing
}

public static class NetworkHelper {
    public static bool IsValidIPAddress(string ip) => false;
    public static bool IsValidPort(int port) => false;
}

public static class NetworkManager {
    public static bool IsNetworkMode { get; set; }
    public static Guid NetworkGuid { get; set; }
    public static NetworkState SelfState { get; set; }

    public static void Initialize() { }
    public static void Update() { }
    public static void Stop() { }
    public static bool StartServer() => false;
    public static bool ConnectServer(IPEndPoint iPEndPoint, string passWord) => false;
}
