using Engine;
using Game.Network;

namespace Game;

public sealed class IndustrialEra2VoltElementState {
    public Point3 Position;
    public string ElementTypeName = string.Empty;
    public List<CompatNetStateValue> Values = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Position);
        archive.Value(ref ElementTypeName);
        if (string.IsNullOrWhiteSpace(ElementTypeName)
            || ElementTypeName.Length > IndustrialEra2VoltPayloads.MaximumElementTypeNameLength) {
            throw new InvalidDataException("Invalid IndustrialEra2 Volt element type name.");
        }

        int count = archive.IsWriting ? Values.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > IndustrialEra2VoltPayloads.MaximumValuesPerElement) {
            throw new InvalidDataException("Invalid IndustrialEra2 Volt element value count.");
        }

        if (archive.IsReading) {
            Values.Clear();
            for (int i = 0; i < count; i++) {
                CompatNetStateValue value = new();
                value.Serialize(archive);
                if (string.IsNullOrWhiteSpace(value.Name)
                    || value.Name.Length > IndustrialEra2VoltPayloads.MaximumValueNameLength) {
                    throw new InvalidDataException("Invalid IndustrialEra2 Volt element value name.");
                }
                Values.Add(value);
            }
        }
        else {
            foreach (CompatNetStateValue value in Values) {
                if (value == null
                    || string.IsNullOrWhiteSpace(value.Name)
                    || value.Name.Length > IndustrialEra2VoltPayloads.MaximumValueNameLength) {
                    throw new InvalidDataException("Invalid IndustrialEra2 Volt element value.");
                }
                value.Serialize(archive);
            }
        }
    }
}

public sealed class IndustrialEra2VoltStateBatch {
    public int Sequence;
    public bool FullRefresh;
    public List<IndustrialEra2VoltElementState> Records = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref FullRefresh);
        int count = archive.IsWriting ? Records.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > IndustrialEra2VoltPayloads.MaximumRecordsPerBatch) {
            throw new InvalidDataException("Invalid IndustrialEra2 Volt record count.");
        }

        if (archive.IsReading) {
            Records.Clear();
            for (int i = 0; i < count; i++) {
                IndustrialEra2VoltElementState record = new();
                record.Serialize(archive);
                Records.Add(record);
            }
        }
        else {
            foreach (IndustrialEra2VoltElementState record in Records) {
                record.Serialize(archive);
            }
        }
    }
}

public static class IndustrialEra2VoltPayloads {
    public const int MaximumRecordsPerBatch = 96;
    public const int MaximumValuesPerElement = 24;
    public const int MaximumElementTypeNameLength = 256;
    public const int MaximumValueNameLength = 128;

    public static byte[] Write(IndustrialEra2VoltStateBatch batch) {
        ArgumentNullException.ThrowIfNull(batch);
        return CompatNetPayload.Write(batch.Serialize);
    }

    public static IndustrialEra2VoltStateBatch Read(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        IndustrialEra2VoltStateBatch batch = new();
        CompatNetPayload.Read(payload, batch.Serialize);
        return batch;
    }
}
