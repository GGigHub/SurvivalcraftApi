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
                            Velocity = Normalize(player.Body.Velocity, decimalPlaces),
                            LookAngles = player.Body.LookAngles == null
                                ? null
                                : Normalize(player.Body.LookAngles, decimalPlaces),
                            Rotation = player.Body.Rotation == null
                                ? null
                                : Normalize(player.Body.Rotation, decimalPlaces)
                        },
                    Health = player.Health.HasValue
                        ? Round(player.Health.Value, decimalPlaces)
                        : null,
                    Level = Round(player.Level, decimalPlaces)
                })
                .OrderBy(player => player.PlayerIndex)
                .ThenBy(player => player.Name, StringComparer.Ordinal)
                .ToArray(),
            TerrainCells = snapshot.TerrainCells
                .OrderBy(cell => cell.X)
                .ThenBy(cell => cell.Y)
                .ThenBy(cell => cell.Z)
                .ToArray(),
            Entities = snapshot.Entities?
                .Select(entity => entity with {
                    Body = entity.Body == null
                        ? null
                        : entity.Body with {
                            Position = Normalize(entity.Body.Position, decimalPlaces),
                            Velocity = Normalize(entity.Body.Velocity, decimalPlaces),
                            Rotation = Normalize(entity.Body.Rotation, decimalPlaces)
                        },
                    Health = entity.Health.HasValue
                        ? Round(entity.Health.Value, decimalPlaces)
                        : null,
                    Hitpoints = entity.Hitpoints.HasValue
                        ? Round(entity.Hitpoints.Value, decimalPlaces)
                        : null
                })
                .OrderBy(entity => entity.EntityId)
                .ToArray(),
            Inventories = snapshot.Inventories?
                .Select(inventory => inventory with {
                    Slots = inventory.Slots
                        .OrderBy(slot => slot.SlotIndex)
                        .ToArray()
                })
                .OrderBy(inventory => inventory.InventoryId)
                .ToArray(),
            HealthStates = snapshot.HealthStates?
                .Select(state => state with {
                    Health = state.Health.HasValue
                        ? Round(state.Health.Value, decimalPlaces)
                        : null,
                    Hitpoints = state.Hitpoints.HasValue
                        ? Round(state.Hitpoints.Value, decimalPlaces)
                        : null
                })
                .OrderBy(state => state.EntityId)
                .ToArray(),
            RecentEvents = snapshot.RecentEvents
                .OrderBy(item => item.Sequence)
                .Select(item => item with { Data = ToSortedDictionary(item.Data) })
                .ToArray(),
            Diagnostics = ToSortedDictionary(snapshot.Diagnostics),
            TerrainStreaming = snapshot.TerrainStreaming == null
                ? null
                : snapshot.TerrainStreaming with {
                    Locations = snapshot.TerrainStreaming.Locations
                        .Select(location => location with {
                            Center = Normalize(location.Center, decimalPlaces),
                            VisibilityDistance = Round(
                                location.VisibilityDistance,
                                decimalPlaces),
                            ContentDistance = Round(location.ContentDistance, decimalPlaces)
                        })
                        .OrderBy(location => location.PlayerIndex)
                        .ToArray()
                }
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

    private static Vector2State Normalize(Vector2State value, int decimalPlaces) =>
        new(
            Round(value.X, decimalPlaces),
            Round(value.Y, decimalPlaces));

    private static QuaternionState Normalize(QuaternionState value, int decimalPlaces) {
        double length = Math.Sqrt(
            value.X * value.X
            + value.Y * value.Y
            + value.Z * value.Z
            + value.W * value.W);
        double x = value.X;
        double y = value.Y;
        double z = value.Z;
        double w = value.W;
        if (double.IsFinite(length) && length > double.Epsilon) {
            x /= length;
            y /= length;
            z /= length;
            w /= length;
        }
        bool negate = w < 0
            || (w == 0 && z < 0)
            || (w == 0 && z == 0 && y < 0)
            || (w == 0 && z == 0 && y == 0 && x < 0);
        if (negate) {
            x = -x;
            y = -y;
            z = -z;
            w = -w;
        }
        return new QuaternionState(
            Round(x, decimalPlaces),
            Round(y, decimalPlaces),
            Round(z, decimalPlaces),
            Round(w, decimalPlaces));
    }

    private static double Round(double value, int decimalPlaces) {
        double rounded = Math.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);
        return rounded == 0d ? 0d : rounded;
    }

    private static SortedDictionary<string, string> ToSortedDictionary(
        IReadOnlyDictionary<string, string> source) {
        SortedDictionary<string, string> result = new(StringComparer.Ordinal);
        foreach ((string key, string value) in source) {
            result.Add(key, value);
        }
        return result;
    }
}
