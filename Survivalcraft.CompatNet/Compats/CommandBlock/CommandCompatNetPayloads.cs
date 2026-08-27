using Engine;
using Game.Network;

namespace Game;

public enum CompatNetPacketKind : byte {
    Request,
    Broadcast
}

public enum CommandCompatNetOperation : ushort {
    EditBlock = 1,
    Execute = 2,
    ElectricResult = 3,
    RequestElectricSnapshot = 4,
    ElectricSnapshot = 5
}

public enum CommandExecutionSource : byte {
    Manual,
    CommandBlock
}

public sealed class CommandEditRequest {
    public Point3 Position;
    public string Line = string.Empty;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Position);
        archive.Value(ref Line);
    }
}

public sealed class CommandExecuteRequest {
    public Point3 Position;
    public string Line = string.Empty;
    public bool Judge;
    public CommandExecutionSource Source;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Position);
        archive.Value(ref Line);
        archive.Value(ref Judge);
        archive.Enum(ref Source);
    }
}

public sealed class CommandBlockSync {
    public Point3 Position;
    public int CellValue;
    public string Line = string.Empty;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Position);
        archive.Value(ref CellValue);
        archive.Value(ref Line);
    }
}

public sealed class CommandElectricResult {
    public Point3 Position;
    public float Voltage;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Position);
        archive.Value(ref Voltage);
    }
}

public sealed class CommandElectricSnapshot {
    public List<CommandElectricResult> Results = [];

    public void Serialize(PacketSerializer archive) {
        int count = archive.IsWriting ? Results.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > 4096) {
            throw new InvalidDataException("Invalid command electric snapshot count.");
        }

        if (archive.IsReading) {
            Results.Clear();
            for (int i = 0; i < count; i++) {
                CommandElectricResult result = new();
                result.Serialize(archive);
                Results.Add(result);
            }
        }
        else {
            foreach (CommandElectricResult result in Results) {
                result.Serialize(archive);
            }
        }
    }
}
