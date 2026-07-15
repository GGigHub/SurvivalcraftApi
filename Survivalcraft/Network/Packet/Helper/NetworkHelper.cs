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

    public static async Task<IPAddress> TryGetIPAddress(string host) {
        if (!IPAddress.TryParse(host, out IPAddress address)) {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);
            address = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault();
        }
        return address;
    }
}
