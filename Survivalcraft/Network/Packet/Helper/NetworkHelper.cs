using System.Net;
using System.Net.NetworkInformation;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;
#pragma warning disable CA1416


namespace Game.Network;

public static class NetworkHelper {
    public static PlayerData GetPlayerData(this ClientSession client) {
        if (client == null) return null;
        return GameManager.Project?.FindSubsystem<SubsystemPlayers>()?.PlayersData.FirstOrDefault(p => p.PlayerIndex == client.PlayerIndex);
    }
}
