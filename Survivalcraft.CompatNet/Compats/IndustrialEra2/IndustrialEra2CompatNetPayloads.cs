using Engine;
using Game.Network;

namespace Game;

public enum IndustrialEra2CompatNetOperation : ushort {
    TankStateSnapshot = 1
}

public sealed class IndustrialEra2TankSlotState {
    public int Value;
    public float Volume;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Value);
        archive.Value(ref Volume);
        if (!float.IsFinite(Volume) || Volume < 0f) {
            throw new InvalidDataException("Invalid IndustrialEra2 tank volume.");
        }
    }
}

public sealed class IndustrialEra2TankState {
    public int EntityId;
    public string ComponentTypeName = string.Empty;
    public List<IndustrialEra2TankSlotState> Tanks = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref EntityId);
        archive.Value(ref ComponentTypeName);
        if (string.IsNullOrEmpty(ComponentTypeName)
            || ComponentTypeName.Length > IndustrialEra2CompatNetPayloads.MaximumComponentTypeNameLength) {
            throw new InvalidDataException("Invalid IndustrialEra2 component type name.");
        }
        int count = archive.IsWriting ? Tanks.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > IndustrialEra2CompatNetPayloads.MaximumTanksPerComponent) {
            throw new InvalidDataException("Invalid IndustrialEra2 tank count.");
        }

        if (archive.IsReading) {
            Tanks.Clear();
            for (int i = 0; i < count; i++) {
                IndustrialEra2TankSlotState tank = new();
                tank.Serialize(archive);
                Tanks.Add(tank);
            }
        }
        else {
            foreach (IndustrialEra2TankSlotState tank in Tanks) {
                tank.Serialize(archive);
            }
        }
    }
}

public sealed class IndustrialEra2TankStateBatch {
    public int Sequence;
    public bool FullRefresh;
    public List<IndustrialEra2TankState> Records = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref FullRefresh);
        int count = archive.IsWriting ? Records.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > IndustrialEra2CompatNetPayloads.MaximumRecordsPerBatch) {
            throw new InvalidDataException("Invalid IndustrialEra2 tank record count.");
        }

        if (archive.IsReading) {
            Records.Clear();
            for (int i = 0; i < count; i++) {
                IndustrialEra2TankState record = new();
                record.Serialize(archive);
                Records.Add(record);
            }
        }
        else {
            foreach (IndustrialEra2TankState record in Records) {
                record.Serialize(archive);
            }
        }
    }
}

public static class IndustrialEra2CompatNetPayloads {
    public const int MaximumRecordsPerBatch = 64;
    public const int MaximumTanksPerComponent = 128;
    public const int MaximumComponentTypeNameLength = 256;

    public static byte[] Write(IndustrialEra2TankStateBatch batch) {
        ArgumentNullException.ThrowIfNull(batch);
        return CompatNetPayload.Write(batch.Serialize);
    }

    public static IndustrialEra2TankStateBatch Read(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        IndustrialEra2TankStateBatch batch = new();
        CompatNetPayload.Read(payload, batch.Serialize);
        return batch;
    }
}
