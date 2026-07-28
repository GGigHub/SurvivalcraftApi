using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Survivalcraft.Testing.Snapshots;

public static class SnapshotCanonicalizer {
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static StateSnapshot Normalize(StateSnapshot snapshot, int decimalPlaces = 5) {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (decimalPlaces is < 0 or > 12) {
            throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
        }

        return snapshot with {
            TimeSeconds = Round(snapshot.TimeSeconds, decimalPlaces),
            Players = snapshot.Players
                .Select(player => player with {
                    Position = Normalize(player.Position, decimalPlaces),
                    Velocity = Normalize(player.Velocity, decimalPlaces),
                    Health = player.Health.HasValue
                        ? Round(player.Health.Value, decimalPlaces)
                        : null
                })
                .OrderBy(player => player.PlayerIndex)
                .ThenBy(player => player.Name, StringComparer.Ordinal)
                .ToArray(),
            Entities = snapshot.Entities
                .Select(entity => entity with {
                    Position = Normalize(entity.Position, decimalPlaces),
                    Velocity = Normalize(entity.Velocity, decimalPlaces)
                })
                .OrderBy(entity => entity.EntityId)
                .ThenBy(entity => entity.Template, StringComparer.Ordinal)
                .ToArray(),
            TerrainCells = snapshot.TerrainCells
                .OrderBy(cell => cell.X)
                .ThenBy(cell => cell.Y)
                .ThenBy(cell => cell.Z)
                .ToArray(),
            Diagnostics = ToSortedDictionary(snapshot.Diagnostics)
        };
    }

    public static string ToCanonicalJson(StateSnapshot snapshot, int decimalPlaces = 5) =>
        JsonSerializer.Serialize(Normalize(snapshot, decimalPlaces), CanonicalJsonOptions);

    public static string ComputeSha256(StateSnapshot snapshot, int decimalPlaces = 5) {
        byte[] bytes = Encoding.UTF8.GetBytes(ToCanonicalJson(snapshot, decimalPlaces));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static Vector3State? Normalize(Vector3State? value, int decimalPlaces) =>
        value is null
            ? null
            : new Vector3State(
                Round(value.X, decimalPlaces),
                Round(value.Y, decimalPlaces),
                Round(value.Z, decimalPlaces));

    private static double Round(double value, int decimalPlaces) =>
        Math.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);

    private static SortedDictionary<string, string> ToSortedDictionary(
        IReadOnlyDictionary<string, string> source) {
        SortedDictionary<string, string> result = new(StringComparer.Ordinal);
        foreach ((string key, string value) in source) {
            result.Add(key, value);
        }
        return result;
    }
}
