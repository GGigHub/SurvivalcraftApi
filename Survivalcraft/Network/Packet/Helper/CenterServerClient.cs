using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Engine;
using Game;

namespace Game.Network;

public static class CenterServerClient {
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<RelayJoinInfo> ResolveRelayJoinInfoAsync(string globalCode) {
        if (string.IsNullOrWhiteSpace(globalCode)) throw new ArgumentException("Global code is empty.", nameof(globalCode));

        string centerUrl = NormalizeCenterUrl(SettingsManager.CenterServerUrl);
        if (string.IsNullOrWhiteSpace(centerUrl)) throw new InvalidOperationException("Center server URL is not configured.");

        byte[] data = await WebManager.GetAsync($"{centerUrl}/api/v1/rooms/{Uri.EscapeDataString(NormalizeCode(globalCode))}");
        RoomLookupResponse response = JsonSerializer.Deserialize<RoomLookupResponse>(data, JsonOptions)
            ?? throw new InvalidOperationException("Center server returned an empty response.");
        RoomRelayResponse relay = response.Relays?.OrderBy(r => r.Priority).FirstOrDefault()
            ?? throw new InvalidOperationException("Center server did not return a relay endpoint.");

        IPAddress address = await ResolveAddressAsync(relay.Host);
        return new RelayJoinInfo(new IPEndPoint(address, relay.UdpPort), relay.RoomCode);
    }

    public static async Task<RelayCandidateInfo[]> GetRelayCandidatesAsync() {
        string centerUrl = NormalizeCenterUrl(SettingsManager.CenterServerUrl);
        if (string.IsNullOrWhiteSpace(centerUrl)) return [];

        byte[] data = await WebManager.GetAsync($"{centerUrl}/api/v1/relays/candidates");
        RelayCandidatesResponse response = JsonSerializer.Deserialize<RelayCandidatesResponse>(data, JsonOptions);
        return response?.Relays ?? [];
    }

    private static async Task<IPAddress> ResolveAddressAsync(string host) {
        if (IPAddress.TryParse(host, out IPAddress address)) return address;

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);
        address = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault();
        return address ?? throw new InvalidOperationException($"Could not resolve relay host: {host}");
    }

    public static async Task<IPEndPoint> GetBestRelayEndPointAsync(int bestRank = 1) {
        RelayCandidateInfo candidate = await GetBestRelayCandidateAsync(bestRank);
        IPAddress address = await ResolveAddressAsync(candidate.Host);
        return new IPEndPoint(address, candidate.UdpPort);
    }

    public static async Task<RelayCandidateInfo> GetBestRelayCandidateAsync(int bestRank = 1) {
        if (bestRank < 1)
            throw new ArgumentOutOfRangeException(nameof(bestRank), "Relay rank is 1-based.");

        return (await GetRelayCandidatesAsync())
            .Where(c => !c.IsBusy)
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.RoomCount)
            .ThenBy(c => c.PeerCount)
            .ThenBy(c => c.LastSeenSecondsAgo)
            .Skip(bestRank - 1)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Center server did not return relay candidate rank {bestRank}.");
    }

    private static string NormalizeCenterUrl(string url) => string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');

    private static string NormalizeCode(string code) => code.Trim().Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    private sealed record RoomLookupResponse(string GlobalCode, DateTimeOffset ExpiresAtUtc, RoomRelayResponse[] Relays);
    private sealed record RoomRelayResponse(string RelayId, string Host, int UdpPort, string RoomCode, string Region, string Version, int Priority);
    private sealed record RelayCandidatesResponse(RelayCandidateInfo[] Relays);
}
