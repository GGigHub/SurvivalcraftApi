using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.Testing.Snapshots;

public static class SnapshotV2Canonicalizer {
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(ControlProtocol.JsonOptions) {
        WriteIndented = false
    };

    public static StateSnapshotV2 Normalize(StateSnapshotV2 snapshot, int decimalPlaces = 5) {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != AutomationProtocol.CurrentSnapshotSchemaVersion) {
            throw new ArgumentException(
                $"Snapshot schema must be {AutomationProtocol.CurrentSnapshotSchemaVersion}.",
                nameof(snapshot));
        }
        if (decimalPlaces is < 0 or > 12) {
            throw new ArgumentOutOfRangeException(nameof(decimalPlaces));
        }

        return snapshot with {
            TimeSeconds = Round(snapshot.TimeSeconds, decimalPlaces),
            Players = snapshot.Players
                .Select(player => player with {
                    Body = player.Body == null
                        ? null
                        : player.Body with {
                            Position = Normalize(player.Body.Position, decimalPlaces),
                            Velocity = Normalize(player.Body.Velocity, decimalPlaces)
                        },
                    Health = player.Health.HasValue
                        ? Round(player.Health.Value, decimalPlaces)
                        : null,
                    Level = Round(player.Level, decimalPlaces)
                })
                .OrderBy(player => player.PlayerIndex)
                .ThenBy(player => player.Name, StringComparer.Ordinal)
                .ToArray(),
            RecentEvents = snapshot.RecentEvents
                .OrderBy(item => item.Sequence)
                .Select(item => item with { Data = ToSortedDictionary(item.Data) })
                .ToArray(),
            Diagnostics = ToSortedDictionary(snapshot.Diagnostics)
        };
    }

    public static string ToCanonicalJson(StateSnapshotV2 snapshot, int decimalPlaces = 5) =>
        JsonSerializer.Serialize(Normalize(snapshot, decimalPlaces), CanonicalJsonOptions);

    public static string ComputeSha256(StateSnapshotV2 snapshot, int decimalPlaces = 5) {
        byte[] bytes = Encoding.UTF8.GetBytes(ToCanonicalJson(snapshot, decimalPlaces));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static Vector3State Normalize(Vector3State value, int decimalPlaces) =>
        new(
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
