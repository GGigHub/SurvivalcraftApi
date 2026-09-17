using Engine;
using Game.Network;

namespace Game;

/// <summary>
/// Operations shared by the conservative state snapshot path. The adapter ID
/// still scopes the payload to one optional mod.
/// </summary>
public enum CompatNetStateOperation : ushort {
    StateSnapshot = 1
}

public enum CompatNetStateValueKind : byte {
    Boolean,
    Byte,
    Integer,
    Single,
    Double,
    Point3,
    Vector3,
    Quaternion
}

public sealed class CompatNetStateValue {
    public string Name = string.Empty;
    public CompatNetStateValueKind Kind;
    public bool BooleanValue;
    public byte ByteValue;
    public int IntegerValue;
    public float SingleValue;
    public double DoubleValue;
    public Point3 Point3Value;
    public Vector3 Vector3Value;
    public Quaternion QuaternionValue;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Name);
        archive.Enum(ref Kind);
        switch (Kind) {
            case CompatNetStateValueKind.Boolean:
                archive.Value(ref BooleanValue);
                break;
            case CompatNetStateValueKind.Byte:
                archive.Value(ref ByteValue);
                break;
            case CompatNetStateValueKind.Integer:
                archive.Value(ref IntegerValue);
                break;
            case CompatNetStateValueKind.Single:
                archive.Value(ref SingleValue);
                break;
            case CompatNetStateValueKind.Double:
                archive.Value(ref DoubleValue);
                break;
            case CompatNetStateValueKind.Point3:
                archive.Value(ref Point3Value);
                break;
            case CompatNetStateValueKind.Vector3:
                archive.Value(ref Vector3Value);
                break;
            case CompatNetStateValueKind.Quaternion:
                archive.Value(ref QuaternionValue);
                break;
            default:
                throw new InvalidDataException($"Unknown CompatNet state value kind: {Kind}");
        }
    }
}

public sealed class CompatNetStateRecord {
    public int EntityId;
    public string ComponentTypeName = string.Empty;
    public bool HasEntityPosition;
    public Vector3 EntityPosition;
    public bool HasBodyState;
    public Vector3 BodyPosition;
    public Vector3 BodyVelocity;
    public Quaternion BodyRotation;
    public List<CompatNetStateValue> Values = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref EntityId);
        archive.Value(ref ComponentTypeName);
        archive.Value(ref HasEntityPosition);
        if (HasEntityPosition) {
            archive.Value(ref EntityPosition);
        }
        archive.Value(ref HasBodyState);
        if (HasBodyState) {
            archive.Value(ref BodyPosition);
            archive.Value(ref BodyVelocity);
            archive.Value(ref BodyRotation);
        }
        int count = archive.IsWriting ? Values.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > CompatNetStatePayloads.MaximumValuesPerRecord) {
            throw new InvalidDataException("Invalid CompatNet state value count.");
        }

        if (archive.IsReading) {
            Values.Clear();
            for (int i = 0; i < count; i++) {
                CompatNetStateValue value = new();
                value.Serialize(archive);
                Values.Add(value);
            }
        }
        else {
            foreach (CompatNetStateValue value in Values) {
                value.Serialize(archive);
            }
        }
    }
}

public sealed class CompatNetStateBatch {
    public int Sequence;
    public List<CompatNetStateRecord> Records = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        int count = archive.IsWriting ? Records.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > CompatNetStatePayloads.MaximumRecordsPerBatch) {
            throw new InvalidDataException("Invalid CompatNet state record count.");
        }

        if (archive.IsReading) {
            Records.Clear();
            for (int i = 0; i < count; i++) {
                CompatNetStateRecord record = new();
                record.Serialize(archive);
                Records.Add(record);
            }
        }
        else {
            foreach (CompatNetStateRecord record in Records) {
                record.Serialize(archive);
            }
        }
    }
}

public static class CompatNetStatePayloads {
    public const int MaximumRecordsPerBatch = 128;
    public const int MaximumValuesPerRecord = 24;

    public static byte[] Write(CompatNetStateBatch batch) {
        ArgumentNullException.ThrowIfNull(batch);
        return CompatNetPayload.Write(batch.Serialize);
    }

    public static CompatNetStateBatch Read(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        CompatNetStateBatch batch = new();
        CompatNetPayload.Read(payload, batch.Serialize);
        return batch;
    }
}
