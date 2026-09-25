using Engine;
using Game.Network;

namespace Game;

public enum NeorxnaCompatNetOperation : ushort {
    HomeStateSnapshot = 1,
    SetHomeRequest = 2,
    DeleteHomeRequest = 3,
    SetBackPositionRequest = 4
}

public sealed class NeorxnaHomeRequest {
    public string Name = string.Empty;

    public void Serialize(PacketSerializer archive) {
        if (archive.IsWriting && Name == null) {
            Name = string.Empty;
        }
        archive.Value(ref Name);
        if (string.IsNullOrWhiteSpace(Name)
            || Name.Length > NeorxnaCompatNetPayloads.MaximumHomeNameLength) {
            throw new InvalidDataException("Invalid Neorxna home name.");
        }
    }
}

public sealed class NeorxnaHomeEntry {
    public string Name = string.Empty;
    public Vector3 Position;

    public void Serialize(PacketSerializer archive) {
        if (archive.IsWriting && Name == null) {
            Name = string.Empty;
        }
        archive.Value(ref Name);
        archive.Value(ref Position);
        if (string.IsNullOrWhiteSpace(Name)
            || Name.Length > NeorxnaCompatNetPayloads.MaximumHomeNameLength
            || !NeorxnaCompatNetPayloads.IsFinite(Position)) {
            throw new InvalidDataException("Invalid Neorxna home entry.");
        }
    }
}

public sealed class NeorxnaPlayerHomeState {
    public int PlayerIndex;
    public bool HasBackPosition;
    public Vector3 BackPosition;
    public List<NeorxnaHomeEntry> Homes = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref PlayerIndex);
        archive.Value(ref HasBackPosition);
        archive.Value(ref BackPosition);
        if (PlayerIndex < 0 || !NeorxnaCompatNetPayloads.IsFinite(BackPosition)) {
            throw new InvalidDataException("Invalid Neorxna player home state.");
        }

        int count = archive.IsWriting ? Homes.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > NeorxnaCompatNetPayloads.MaximumHomesPerPlayer) {
            throw new InvalidDataException("Invalid Neorxna home count.");
        }

        if (archive.IsReading) {
            Homes.Clear();
            for (int i = 0; i < count; i++) {
                NeorxnaHomeEntry home = new();
                home.Serialize(archive);
                Homes.Add(home);
            }
        }
        else {
            foreach (NeorxnaHomeEntry home in Homes) {
                home.Serialize(archive);
            }
        }
    }
}

public sealed class NeorxnaPlayerHomeStateBatch {
    public int Sequence;
    public bool FullRefresh;
    public List<NeorxnaPlayerHomeState> Records = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref FullRefresh);
        int count = archive.IsWriting ? Records.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > NeorxnaCompatNetPayloads.MaximumRecordsPerBatch) {
            throw new InvalidDataException("Invalid Neorxna home state count.");
        }

        if (archive.IsReading) {
            Records.Clear();
            for (int i = 0; i < count; i++) {
                NeorxnaPlayerHomeState state = new();
                state.Serialize(archive);
                Records.Add(state);
            }
        }
        else {
            foreach (NeorxnaPlayerHomeState state in Records) {
                state.Serialize(archive);
            }
        }
    }
}

public static class NeorxnaCompatNetPayloads {
    public const int MaximumHomeNameLength = 64;
    public const int MaximumHomesPerPlayer = 64;
    public const int MaximumRecordsPerBatch = 32;

    public static byte[] Write(NeorxnaPlayerHomeStateBatch batch) {
        ArgumentNullException.ThrowIfNull(batch);
        return CompatNetPayload.Write(batch.Serialize);
    }

    public static NeorxnaPlayerHomeStateBatch Read(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        NeorxnaPlayerHomeStateBatch batch = new();
        CompatNetPayload.Read(payload, batch.Serialize);
        return batch;
    }

    public static byte[] WriteHomeRequest(NeorxnaHomeRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static NeorxnaHomeRequest ReadHomeRequest(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        NeorxnaHomeRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static bool IsFinite(Vector3 value) {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }
}
