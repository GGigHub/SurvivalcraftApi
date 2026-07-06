using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Survivalcraft.Test;

public static class InventoryTestTools {
    public const int DirtContents = 2;
    public const int CobblestoneContents = 5;

    public static async Task<int> WaitForClientPlayerIndexAsync(GameContext context) {
        for (int i = 0; i < 50; i++) {
            object? result = await context.PushCommandAsync("FUNC return Game.NetworkManager.PlayerIndex;");
            int playerIndex = result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            if (playerIndex >= 0) {
                return playerIndex;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException("Client player index was not assigned.");
    }

    public static async Task WaitForPlayerInventoryAsync(GameContext context, int playerIndex) {
        for (int i = 0; i < 50; i++) {
            InventorySnapshot snapshot = await GetInventorySnapshotAsync(context, playerIndex);
            if (snapshot.Exists) {
                return;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Player {playerIndex} inventory was not available on {context.RoleName}.");
    }

    public static async Task<int> EnsureServerPlayerEntityAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            $$"""
            FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            foreach (var playerData in players.PlayersData) {
                if (playerData.PlayerIndex == {{playerIndex}}) {
                    if (playerData.ComponentPlayer == null) {
                        playerData.SpawnPlayer(
                            players.GlobalSpawnPosition + new Engine.Vector3(4f, 0f, 4f),
                            Game.PlayerData.SpawnMode.InitialNoIntro);
                    }
                    return playerData.ComponentPlayer?.Entity.Id ?? 0;
                }
            }
            return 0;
            """);

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task SeedInventoryAsync(GameContext context, int playerIndex, string slotSpec, int activeSlotIndex = 0) {
        string result = (string?)await context.PushCommandAsync($$"""
        FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
        var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
        var inventory = playerData?.ComponentPlayer?.ComponentMiner?.Inventory;
        if (inventory == null) return "missing";

        for (int i = 0; i < inventory.SlotsCount; i++) {
            inventory.RemoveSlotItems(i, inventory.GetSlotCount(i));
        }

        foreach (string entry in "{{slotSpec}}".Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string[] parts = entry.Split(':');
            int slotIndex = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            int contents = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            int count = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            if (count > 0) {
                inventory.AddSlotItems(slotIndex, Game.Terrain.MakeBlockValue(contents), count);
            }
        }

        inventory.ActiveSlotIndex = {{activeSlotIndex}};
        return "seeded";
        """) ?? string.Empty;

        if (result != "seeded") {
            throw new InvalidOperationException($"Failed to seed inventory for player {playerIndex} on {context.RoleName}. Result={result}.");
        }
    }

    public static async Task MoveAsync(GameContext context, int playerIndex, int fromSlot, int toSlot, int count) {
        string result = (string?)await context.PushCommandAsync($$"""
        FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
        var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
        var inventory = playerData?.ComponentPlayer?.ComponentMiner?.Inventory;
        if (inventory == null) return "missing";
        var widget = new Game.InventorySlotWidget();
        bool moved = widget.HandleMoveItem(inventory, {{fromSlot}}, inventory, {{toSlot}}, {{count}});
        return "queued:" + moved.ToString(System.Globalization.CultureInfo.InvariantCulture);
        """) ?? string.Empty;

        if (!result.StartsWith("queued:", StringComparison.Ordinal)) {
            throw new InvalidOperationException($"Move action was not queued on {context.RoleName}. Result={result}.");
        }
    }

    public static async Task DragDropAsync(GameContext context, int playerIndex, int fromSlot, int toSlot, string dragMode, bool processingOnly = false) {
        string result = (string?)await context.PushCommandAsync($$"""
        FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
        var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
        var inventory = playerData?.ComponentPlayer?.ComponentMiner?.Inventory;
        if (inventory == null) return "missing";
        var widget = new Game.InventorySlotWidget();
        bool moved = widget.HandleDragDrop(
            inventory,
            {{fromSlot}},
            Game.DragMode.{{dragMode}},
            inventory,
            {{toSlot}},
            {{(processingOnly ? "true" : "false")}});
        return "queued:" + moved.ToString(System.Globalization.CultureInfo.InvariantCulture);
        """) ?? string.Empty;

        if (!result.StartsWith("queued:", StringComparison.Ordinal)) {
            throw new InvalidOperationException($"DragDrop action was not queued on {context.RoleName}. Result={result}.");
        }
    }

    public static async Task SetActiveSlotAsync(GameContext context, int playerIndex, int slotIndex) {
        string result = (string?)await context.PushCommandAsync($$"""
        FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
        var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
        var player = playerData?.ComponentPlayer;
        var inventory = player?.ComponentMiner?.Inventory;
        if (player == null || inventory == null) return "missing";
        inventory.ActiveSlotIndex = {{slotIndex}};
        player.Update(0f);
        return "queued";
        """) ?? string.Empty;

        if (result != "queued") {
            throw new InvalidOperationException($"Active slot action was not queued on {context.RoleName}. Result={result}.");
        }
    }

    public static async Task<int> DropActiveSlotToWorldAsync(GameContext context, int playerIndex, int count) {
        object? result = await context.PushCommandAsync(
            $$"""
            FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
            var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
            var player = playerData?.ComponentPlayer;
            var inventory = player?.ComponentMiner?.Inventory;
            if (player == null || inventory == null) return 0;
            int slotIndex = inventory.ActiveSlotIndex;
            int slotValue = inventory.GetSlotValue(slotIndex);
            int removedCount = inventory.RemoveSlotItems(slotIndex, Math.Min(inventory.GetSlotCount(slotIndex), {{count}}));
            if (slotValue == 0 || removedCount <= 0) return 0;
            Engine.Vector3 position = player.ComponentBody.Position + new Engine.Vector3(4f, player.ComponentBody.StanceBoxSize.Y * 0.66f, 4f);
            var pickable = Game.GameManager.Project.FindSubsystem<Game.SubsystemPickables>()
                .AddPickable(slotValue, removedCount, position, Engine.Vector3.Zero, null, player.Entity);
            pickable.MaxTimeExist = 9999f;
            return pickable.Id;
            """);

        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public static async Task AssertPickableEventuallyAsync(GameContext context, int pickableId, int contents, int count) {
        PickableSnapshot snapshot = default;
        for (int i = 0; i < 50; i++) {
            snapshot = await GetPickableSnapshotAsync(context, pickableId);
            if (snapshot.Exists && snapshot.Contents == contents && snapshot.Count == count) {
                return;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Pickable {pickableId} did not reach expected state on {context.RoleName}. Expected={contents}:{count}. Actual={snapshot}.");
    }

    public static async Task AssertInventoryEventuallyAsync(GameContext context, int playerIndex, string expectedSlotSpec) {
        InventoryExpectation expectation = InventoryExpectation.Parse(expectedSlotSpec);
        InventorySnapshot snapshot = default;
        for (int i = 0; i < 50; i++) {
            snapshot = await GetInventorySnapshotAsync(context, playerIndex);
            if (expectation.Matches(snapshot)) {
                return;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Inventory did not reach expected state on {context.RoleName}. Expected={expectedSlotSpec}. Actual={snapshot}.");
    }

    public static async Task AssertActiveSlotEventuallyAsync(GameContext context, int playerIndex, int expectedSlotIndex) {
        InventorySnapshot snapshot = default;
        for (int i = 0; i < 50; i++) {
            snapshot = await GetInventorySnapshotAsync(context, playerIndex);
            if (snapshot.Exists && snapshot.ActiveSlotIndex == expectedSlotIndex) {
                return;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Active slot did not reach {expectedSlotIndex} on {context.RoleName}. Actual={snapshot}.");
    }

    public static async Task<InventorySnapshot> GetInventorySnapshotAsync(GameContext context, int playerIndex) {
        string result = (string?)await context.PushCommandAsync($$"""
        FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
        var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
        var inventory = playerData?.ComponentPlayer?.ComponentMiner?.Inventory;
        if (inventory == null) return "missing";
        var parts = new List<string>();
        parts.Add("active=" + inventory.ActiveSlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (int i = 0; i < Math.Min(inventory.SlotsCount, 10); i++) {
            int value = inventory.GetSlotValue(i);
            int contents = inventory.GetSlotCount(i) > 0 ? Game.Terrain.ExtractContents(value) : 0;
            int count = inventory.GetSlotCount(i);
            parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:{2}", i, contents, count));
        }
        return string.Join(";", parts);
        """) ?? string.Empty;

        return InventorySnapshot.Parse(result);
    }

    public static async Task<PickableSnapshot> GetPickableSnapshotAsync(GameContext context, int pickableId) {
        string result = (string?)await context.PushCommandAsync(
            $$"""
            FUNC var pickable = Game.GameManager.Project.FindSubsystem<Game.SubsystemPickables>()
                .Pickables
                .FirstOrDefault(p => p.Id == {{pickableId}});
            if (pickable == null) return "missing";
            int contents = pickable.Count > 0 ? Game.Terrain.ExtractContents(pickable.Value) : 0;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "id={0};contents={1};count={2}",
                pickable.Id,
                contents,
                pickable.Count);
            """) ?? string.Empty;

        return PickableSnapshot.Parse(result);
    }
}

public readonly record struct InventorySlotSnapshot(int Contents, int Count) {
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Contents={Contents},Count={Count}");
}

public readonly record struct InventorySnapshot(bool Exists, int ActiveSlotIndex, IReadOnlyDictionary<int, InventorySlotSnapshot> Slots, string Raw) {
    public static InventorySnapshot Parse(string value) {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new InventorySnapshot(false, -1, new Dictionary<int, InventorySlotSnapshot>(), value);
        }

        Dictionary<int, InventorySlotSnapshot> slots = new();
        int activeSlotIndex = -1;
        foreach (string entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            if (entry.StartsWith("active=", StringComparison.OrdinalIgnoreCase)) {
                activeSlotIndex = int.Parse(entry["active=".Length..], CultureInfo.InvariantCulture);
                continue;
            }

            string[] parts = entry.Split(':');
            if (parts.Length != 3) {
                throw new FormatException($"Invalid inventory slot snapshot entry: '{entry}'.");
            }

            slots[int.Parse(parts[0], CultureInfo.InvariantCulture)] = new InventorySlotSnapshot(
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        return new InventorySnapshot(true, activeSlotIndex, slots, value);
    }

    public override string ToString() =>
        Raw;
}

public readonly record struct PickableSnapshot(bool Exists, int Id, int Contents, int Count, string Raw) {
    public static PickableSnapshot Parse(string value) {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new PickableSnapshot(false, 0, 0, 0, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new PickableSnapshot(
            true,
            int.Parse(values["id"], CultureInfo.InvariantCulture),
            int.Parse(values["contents"], CultureInfo.InvariantCulture),
            int.Parse(values["count"], CultureInfo.InvariantCulture),
            value);
    }

    public override string ToString() =>
        Raw;
}

public sealed class InventoryExpectation {
    private readonly Dictionary<int, InventorySlotSnapshot> slots;

    private InventoryExpectation(Dictionary<int, InventorySlotSnapshot> slots) {
        this.slots = slots;
    }

    public static InventoryExpectation Parse(string value) {
        Dictionary<int, InventorySlotSnapshot> slots = new();
        foreach (string entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string[] parts = entry.Split(':');
            if (parts.Length != 3) {
                throw new FormatException($"Invalid inventory expectation entry: '{entry}'.");
            }

            slots[int.Parse(parts[0], CultureInfo.InvariantCulture)] = new InventorySlotSnapshot(
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        return new InventoryExpectation(slots);
    }

    public bool Matches(InventorySnapshot snapshot) {
        if (!snapshot.Exists) {
            return false;
        }

        foreach (KeyValuePair<int, InventorySlotSnapshot> pair in slots) {
            if (!snapshot.Slots.TryGetValue(pair.Key, out InventorySlotSnapshot actual) || actual != pair.Value) {
                return false;
            }
        }

        return true;
    }
}
