using System.Net;
using Engine;
using Game.NetWork.Packages;
using GameEntitySystem;

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
            SendWriterFromPackage(new ConnectionRequestPackage(TokenId, "ProjectNet.Server_Version", string.Empty, onlineAccessToken, passwd, ModsManager.ModList), ep, false);
            listener.NetworkReceiveEvent += NetworkReceiveEvent;
            listener.NetworkReceiveUnconnectedEvent += HanldeUnconnectedEvent;
            listener.PeerDisconnectedEvent += PeerDisconnectedEvent;
            listener.NetworkErrorEvent += HandleNetworkError;
            CurrentStage = Stage.WaitForClientList;
            WorkType = WorkType.Client;
            Window.Frame += Update;
            Window.Closed += StopImmediate;
            OnClientStateChanged += ClientStateChanged;
            TurnOnPackageHandle();
        }
        catch {
            CurrentStage = Stage.NotConnected;
        }
    }

    private void ClientStateChanged(Client c) {
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();

        if (c.State != ClientStage.NotConnected) {
            return;
        }

        var playerData = subsystemPlayers.PlayersData.Find(pd => pd.PlayerGUID == c.PlayerGuid);
        if (playerData != null && !playerData.IsMainPlayer) {
            subsystemPlayers.MakePlayerOffline(playerData.PlayerGUID);
        }
        SettingsManager.VisibilityRange = Math.Min(256, SettingsManager.VisibilityRange);
        NetworkManager.QueuePackage(new ClientPackage(Self.ID, ClientStage.ProjectLoaded));
    }

    private void HandleNetworkError(byte[] _, IPEndPoint endPoint) {
        Log.Information("Connection error " + endPoint);
        Stop(endPoint?.ToString());
    }
}
