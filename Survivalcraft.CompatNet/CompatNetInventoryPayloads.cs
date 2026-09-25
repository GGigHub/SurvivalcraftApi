using Game.Network;

namespace Game;

public enum CompatNetInventoryOperation : ushort {
    StateSnapshot = 2
}

public sealed class CompatNetInventorySlotState {
    public int Value;
    public int Count;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Value);
        archive.Value(ref Count);
        if (Value < 0 || Count < 0) {
            throw new InvalidDataException("Invalid CompatNet inventory slot.");
        }
    }
}

public sealed class CompatNetInventoryState {
    public int EntityId;
    public bool IsSubsystem;
    public string ComponentTypeName = string.Empty;
    public List<CompatNetInventorySlotState> Slots = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref EntityId);
        archive.Value(ref IsSubsystem);
        if (archive.IsWriting && ComponentTypeName == null) {
            ComponentTypeName = string.Empty;
        }
        archive.Value(ref ComponentTypeName);
        if ((!IsSubsystem && EntityId <= 0)
            || (IsSubsystem && EntityId != 0)
            || string.IsNullOrWhiteSpace(ComponentTypeName)
            || ComponentTypeName.Length > CompatNetInventoryPayloads.MaximumComponentTypeNameLength) {
            throw new InvalidDataException("Invalid CompatNet inventory state.");
        }

        int count = archive.IsWriting ? Slots.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > CompatNetInventoryPayloads.MaximumSlotsPerInventory) {
            throw new InvalidDataException("Invalid CompatNet inventory slot count.");
        }
        if (archive.IsReading) {
            Slots.Clear();
            for (int i = 0; i < count; i++) {
                CompatNetInventorySlotState slot = new();
                slot.Serialize(archive);
                Slots.Add(slot);
            }
        }
        else {
            foreach (CompatNetInventorySlotState slot in Slots) {
                slot.Serialize(archive);
            }
        }
    }
}

public sealed class CompatNetInventoryStateBatch {
    public int Sequence;
    public bool FullRefresh;
    public List<CompatNetInventoryState> Records = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref FullRefresh);
        int count = archive.IsWriting ? Records.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > CompatNetInventoryPayloads.MaximumRecordsPerBatch) {
            throw new InvalidDataException("Invalid CompatNet inventory record count.");
        }
        if (archive.IsReading) {
            Records.Clear();
            for (int i = 0; i < count; i++) {
                CompatNetInventoryState state = new();
                state.Serialize(archive);
                Records.Add(state);
            }
        }
        else {
            foreach (CompatNetInventoryState state in Records) {
                state.Serialize(archive);
            }
        }
    }
}

public static class CompatNetInventoryPayloads {
    public const int MaximumComponentTypeNameLength = 256;
    public const int MaximumSlotsPerInventory = 256;
    public const int MaximumRecordsPerBatch = 32;

    public static byte[] Write(CompatNetInventoryStateBatch batch) {
        ArgumentNullException.ThrowIfNull(batch);
        return CompatNetPayload.Write(batch.Serialize);
    }

    public static CompatNetInventoryStateBatch Read(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        CompatNetInventoryStateBatch batch = new();
        CompatNetPayload.Read(payload, batch.Serialize);
        return batch;
    }
}
