using System;
using System.IO;
using System.Net;
using Engine.Graphics;
using Game;

namespace Game;

public enum NetworkState {
    NotConnected, Connected, ProjectLoaded, LoadTerrain, Playing
}

public static class NetworkManager {
    public static bool IsNetworkMode => Network.NetworkService.IsClient;
    public static Guid NetworkGuid => Network.NetworkService.NetworkGuid;
    public static NetworkState SelfState => Network.NetworkService.SelfState;

    public static void Initialize() => Network.NetworkService.Initialize();
    public static void Update() => Network.NetworkService.Update();
    public static void Stop() => Network.NetworkService.Stop();
    public static bool StartServer() => Network.NetworkService.StartServer();
    public static bool ConnectServer(IPEndPoint iPEndPoint, string passWord)
                    => Network.NetworkService.ConnectServer(iPEndPoint, passWord);
}
