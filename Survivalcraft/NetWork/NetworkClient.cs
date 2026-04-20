using System;
using System.Net;
using Engine;
using Game.NetWork.Packages;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.NetWork;

public class NetworkClient : NetworkBase {
    public void ConnectServer(IPEndPoint ep, string passwd = "", string? onlineAccessToken = null) {
        try {
            isStopping = false;
            Log.Information($"connecting to server at {ep.Address}");
            Clients.Clear();
            netManager.Start();
            TokenId = Guid.NewGuid();
            onlineAccessToken ??= SettingsManager.OnlineAccessToken;
            SendWriterFromPackage(new ConnectionRequestPackage(TokenId, ProjectNet.Server_Version, string.Empty, onlineAccessToken, passwd, ModsManager.ModList), ep, false);
            listener.NetworkReceiveEvent += NetworkReceiveEvent;
            listener.NetworkReceiveUnconnectedEvent += HanldeUnconnectedEvent;
            listener.PeerDisconnectedEvent += PeerDisconnectedEvent;
            listener.NetworkErrorEvent += HandleNetworkError;
            CurrentStage = Stage.WaitForClientList;
            WorkType = WorkType.Client;
            Window.Frame += Update;
            Window.Closed += StopImmediate;
        }
        catch {
            CurrentStage = Stage.NotConnected;
        }
    }

    private void HandleNetworkError(byte[] _, IPEndPoint endPoint) {
        Log.Information("Connection error " + endPoint);
        Stop(endPoint?.ToString());
    }
}
