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
    PlayerState = 7,
    TerrainSnapshot = 8,
    MovingBlockSnapshot = 9,
    CreatureSnapshot = 10,
    WorldState = 11
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
    public float Level;
    public bool IsCreativeFlyEnabled;
    public Vector3 BodyPosition;
    public Vector3 BodyVelocity;
    public Quaternion BodyRotation;
    public bool IsCrouching;
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
        archive.Value(ref Level);
        archive.Value(ref IsCreativeFlyEnabled);
        archive.Value(ref BodyPosition);
        archive.Value(ref BodyVelocity);
        archive.Value(ref BodyRotation);
        archive.Value(ref IsCrouching);
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

public sealed class CommandTerrainSnapshot {
    public Point3 Origin;
    public int SizeX;
    public int SizeY;
    public int SizeZ;
    public List<int> Indices = [];
    public List<int> Values = [];
    public List<int> ClimateIndices = [];
    public List<int> Temperatures = [];
    public List<int> Humidities = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Origin);
        archive.Value(ref SizeX);
        archive.Value(ref SizeY);
        archive.Value(ref SizeZ);

        int count = archive.IsWriting ? Values.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > 10000) {
            throw new InvalidDataException("Invalid command terrain snapshot count.");
        }

        if (archive.IsReading) {
            Indices.Clear();
            Values.Clear();
            for (int i = 0; i < count; i++) {
                int index = 0;
                archive.Value(ref index);
                int value = 0;
                archive.Value(ref value);
                Indices.Add(index);
                Values.Add(value);
            }
        }
        else {
            for (int i = 0; i < Values.Count; i++) {
                int index = Indices[i];
                int copy = Values[i];
                archive.Value(ref index);
                archive.Value(ref copy);
            }
        }

        int climateCount = archive.IsWriting ? ClimateIndices.Count : 0;
        archive.Value(ref climateCount);
        if (climateCount < 0 || climateCount > 10000) {
            throw new InvalidDataException("Invalid command climate snapshot count.");
        }
        if (archive.IsReading) {
            ClimateIndices.Clear();
            Temperatures.Clear();
            Humidities.Clear();
            for (int i = 0; i < climateCount; i++) {
                int index = 0;
                int temperature = 0;
                int humidity = 0;
                archive.Value(ref index);
                archive.Value(ref temperature);
                archive.Value(ref humidity);
                ClimateIndices.Add(index);
                Temperatures.Add(temperature);
                Humidities.Add(humidity);
            }
        }
        else {
            if (ClimateIndices.Count != Temperatures.Count
                || ClimateIndices.Count != Humidities.Count) {
                throw new InvalidDataException("Invalid command climate snapshot.");
            }
            for (int i = 0; i < ClimateIndices.Count; i++) {
                int index = ClimateIndices[i];
                int temperature = Temperatures[i];
                int humidity = Humidities[i];
                archive.Value(ref index);
                archive.Value(ref temperature);
                archive.Value(ref humidity);
            }
        }

        if (SizeX <= 0 || SizeY <= 0 || SizeZ <= 0
            || SizeX > 100 || SizeY > 100 || SizeZ > 100
            || Indices.Count != Values.Count
            || ClimateIndices.Count != Temperatures.Count
            || ClimateIndices.Count != Humidities.Count
            || Indices.Any(index => index < 0 || index >= (long)SizeX * SizeY * SizeZ)) {
            throw new InvalidDataException("Invalid command terrain snapshot dimensions.");
        }
        if (ClimateIndices.Any(index => index < 0 || index >= (long)SizeX * SizeZ)) {
            throw new InvalidDataException("Invalid command climate snapshot dimensions.");
        }
    }
}

public sealed class CommandMovingBlockState {
    public string Id = string.Empty;
    public bool Stopped;
    public Vector3 Position;
    public Vector3 StartPosition;
    public Vector3 TargetPosition;
    public Vector3 CurrentVelocity;
    public float Speed;
    public float Acceleration;
    public float Drag;
    public Vector2 Smoothness;
    public List<Point3> Offsets = [];
    public List<int> Values = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Id);
        archive.Value(ref Stopped);
        archive.Value(ref Position);
        archive.Value(ref StartPosition);
        archive.Value(ref TargetPosition);
        archive.Value(ref CurrentVelocity);
        archive.Value(ref Speed);
        archive.Value(ref Acceleration);
        archive.Value(ref Drag);
        archive.Value(ref Smoothness);
        int count = archive.IsWriting ? Values.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > 4096) {
            throw new InvalidDataException("Invalid moving block count.");
        }
        if (archive.IsReading) {
            Offsets.Clear();
            Values.Clear();
            for (int i = 0; i < count; i++) {
                Point3 offset = default;
                int value = 0;
                archive.Value(ref offset);
                archive.Value(ref value);
                Offsets.Add(offset);
                Values.Add(value);
            }
        }
        else {
            for (int i = 0; i < Values.Count; i++) {
                Point3 offset = Offsets[i];
                int value = Values[i];
                archive.Value(ref offset);
                archive.Value(ref value);
            }
        }
        if (Offsets.Count != Values.Count) {
            throw new InvalidDataException("Invalid moving block payload.");
        }
    }
}

public sealed class CommandMovingBlockSnapshot {
    public int Sequence;
    public List<CommandMovingBlockState> States = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        int count = archive.IsWriting ? States.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > 256) {
            throw new InvalidDataException("Invalid moving block snapshot count.");
        }
        if (archive.IsReading) {
            States.Clear();
            for (int i = 0; i < count; i++) {
                CommandMovingBlockState state = new();
                state.Serialize(archive);
                States.Add(state);
            }
        }
        else {
            foreach (CommandMovingBlockState state in States) {
                state.Serialize(archive);
            }
        }
    }
}

public sealed class CommandCreatureState {
    public string Identity = string.Empty;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Health;
    public bool IsOnFire;
    public float FireDuration;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Identity);
        archive.Value(ref Position);
        archive.Value(ref Velocity);
        archive.Value(ref Health);
        archive.Value(ref IsOnFire);
        archive.Value(ref FireDuration);
    }
}

public sealed class CommandCreatureSnapshot {
    public int Sequence;
    public List<CommandCreatureState> States = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        int count = archive.IsWriting ? States.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > 1024) {
            throw new InvalidDataException("Invalid creature snapshot count.");
        }
        if (archive.IsReading) {
            States.Clear();
            for (int i = 0; i < count; i++) {
                CommandCreatureState state = new();
                state.Serialize(archive);
                States.Add(state);
            }
        }
        else {
            foreach (CommandCreatureState state in States) {
                state.Serialize(archive);
            }
        }
    }
}

public sealed class CommandWorldStateSync {
    public int GameMode;
    public Color SkyColor;
    public Color RainColor;
    public float GameTimeFactor;
    public int DayDuration;
    public double PrecipitationStartTime;
    public double PrecipitationEndTime;
    public float PrecipitationRampTime;
    public float LightningIntensity;
    public double FogStartTime;
    public double FogEndTime;
    public float FogRampTime;
    public float FogProgress;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref GameMode);
        archive.Value(ref SkyColor);
        archive.Value(ref RainColor);
        archive.Value(ref GameTimeFactor);
        archive.Value(ref DayDuration);
        archive.Value(ref PrecipitationStartTime);
        archive.Value(ref PrecipitationEndTime);
        archive.Value(ref PrecipitationRampTime);
        archive.Value(ref LightningIntensity);
        archive.Value(ref FogStartTime);
        archive.Value(ref FogEndTime);
        archive.Value(ref FogRampTime);
        archive.Value(ref FogProgress);
    }
}
