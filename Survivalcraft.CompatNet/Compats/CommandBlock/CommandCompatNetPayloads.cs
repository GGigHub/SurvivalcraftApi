using Engine;
using Game.Network;

namespace Game;

public enum CompatNetPacketKind : byte {
    Request,
    Broadcast
}

public enum CommandCompatNetOperation : ushort {
    EditBlock = 1,
    ExecuteCommand = 2,
    ElectricResult = 3,
    RequestElectricSnapshot = 4,
    ElectricSnapshot = 5,
    ExecuteResult = 6,
    PlayerState = 7
}

public sealed class CommandExecuteRequest {
    public int RequestId;
    public Point3 Position;
    public bool Judge;
    public string Line = string.Empty;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref RequestId);
        archive.Value(ref Position);
        archive.Value(ref Judge);
        archive.Value(ref Line);
        if ((Line?.Length ?? 0) > CommandCompatNetRuntime.MaximumCommandLineChars) {
            throw new InvalidDataException("Command line exceeds the maximum length.");
        }
    }
}

public sealed class CommandExecuteResult {
    public int RequestId;
    public int ResultCode;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref RequestId);
        archive.Value(ref ResultCode);
    }
}

public sealed class CommandEditRequest {
    public Point3 Position;
    public string Line = string.Empty;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Position);
        archive.Value(ref Line);
        if ((Line?.Length ?? 0) > CommandCompatNetRuntime.MaximumCommandLineChars) {
            throw new InvalidDataException("Command line exceeds the maximum length.");
        }
    }
}

public sealed class CommandBlockSync {
    public Point3 Position;
    public string Line = string.Empty;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Position);
        archive.Value(ref Line);
        if ((Line?.Length ?? 0) > CommandCompatNetRuntime.MaximumCommandLineChars) {
            throw new InvalidDataException("Command line exceeds the maximum length.");
        }
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

public sealed class CommandPlayerStateSync {
    public int Sequence;
    public int PlayerIndex;
    public Vector3 SpawnPosition;
    public float Health;
    public float Food;
    public float Sleep;
    public float Stamina;
    public float Wetness;
    public float Temperature;
    public float AttackPower;
    public float AttackResilience;
    public float FallResilience;
    public float FireResilience;
    public float WalkSpeed;
    public float JumpSpeed;
    public float LadderSpeed;
    public float SwimSpeed;
    public float FlySpeed;
    public float CreativeFlySpeed;
    public float Air;
    public float AirCapacity;
    public bool IsInvulnerable;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref PlayerIndex);
        archive.Value(ref SpawnPosition);
        archive.Value(ref Health);
        archive.Value(ref Food);
        archive.Value(ref Sleep);
        archive.Value(ref Stamina);
        archive.Value(ref Wetness);
        archive.Value(ref Temperature);
        archive.Value(ref AttackPower);
        archive.Value(ref AttackResilience);
        archive.Value(ref FallResilience);
        archive.Value(ref FireResilience);
        archive.Value(ref WalkSpeed);
        archive.Value(ref JumpSpeed);
        archive.Value(ref LadderSpeed);
        archive.Value(ref SwimSpeed);
        archive.Value(ref FlySpeed);
        archive.Value(ref CreativeFlySpeed);
        archive.Value(ref Air);
        archive.Value(ref AirCapacity);
        archive.Value(ref IsInvulnerable);
    }
}
