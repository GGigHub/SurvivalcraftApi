using Engine;
using Game.Network;

namespace Game;

public enum SushiToolCompatNetOperation : ushort {
    AddChannelRequest = 1,
    ChannelStateSnapshot = 2,
    InventoryActionRequest = 3,
    ChannelMetadataRequest = 4,
    MachineControlRequest = 5,
    MachineConfigRequest = 6,
    TrainControlRequest = 7,
    TrainRecycleRequest = 8,
    TrainCallRequest = 9,
    TrainCallEvent = 10
}

public enum SushiToolInventoryActionKind : byte {
    Move = 1,
    DragDrop = 2,
    Drop = 3
}

public enum SushiToolChannelMetadataKind : byte {
    UpdateName = 1,
    Reorder = 2
}

public enum SushiToolMachineControlKind : byte {
    StartWork = 1,
    OverStop = 2
}

public sealed class SushiToolMachineControlRequest {
    public int MachineEntityId;
    public SushiToolMachineControlKind Kind;
    public bool Enabled;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref MachineEntityId);
        archive.Enum(ref Kind);
        archive.Value(ref Enabled);
        if (MachineEntityId <= 0
            || Kind is not SushiToolMachineControlKind.StartWork
                and not SushiToolMachineControlKind.OverStop) {
            throw new InvalidDataException("Invalid SushiTool machine control request.");
        }
    }
}

public sealed class SushiToolMachineConfigRequest {
    public int MachineEntityId;
    public bool StartWork;
    public string ConfigXml = string.Empty;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref MachineEntityId);
        archive.Value(ref StartWork);
        if (archive.IsWriting && ConfigXml == null) {
            ConfigXml = string.Empty;
        }
        archive.Value(ref ConfigXml);
        if (MachineEntityId <= 0
            || string.IsNullOrWhiteSpace(ConfigXml)
            || ConfigXml.Length > SushiToolCompatNetPayloads.MaximumMachineConfigLength) {
            throw new InvalidDataException("Invalid SushiTool machine config request.");
        }
    }
}

public sealed class SushiToolTrainControlRequest {
    public int TrainEntityId;
    public int SpeedDelta;
    public int TurnDelta;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref TrainEntityId);
        archive.Value(ref SpeedDelta);
        archive.Value(ref TurnDelta);
        if (TrainEntityId <= 0
            || SpeedDelta < -1
            || SpeedDelta > 1
            || TurnDelta < -1
            || TurnDelta > 1) {
            throw new InvalidDataException("Invalid SushiTool train control request.");
        }
    }
}

public sealed class SushiToolTrainRecycleRequest {
    public int TrainEntityId;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref TrainEntityId);
        if (TrainEntityId <= 0) {
            throw new InvalidDataException("Invalid SushiTool train recycle request.");
        }
    }
}

public sealed class SushiToolTrainCallRequest {
    public int TrainEntityId;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref TrainEntityId);
        if (TrainEntityId <= 0) {
            throw new InvalidDataException("Invalid SushiTool train call request.");
        }
    }
}

public sealed class SushiToolInventoryActionRequest {
    public SushiToolInventoryActionKind Kind;
    public int SourceChannel = -1;
    public int SourceSlot;
    public int TargetChannel = -1;
    public int TargetSlot;
    public DragMode DragMode;
    public bool ProcessingOnly;
    public int Count;
    public Vector3 HitPosition;

    public void Serialize(PacketSerializer archive) {
        archive.Enum(ref Kind);
        archive.Value(ref SourceChannel);
        archive.Value(ref SourceSlot);
        archive.Value(ref TargetChannel);
        archive.Value(ref TargetSlot);
        archive.Enum(ref DragMode);
        archive.Value(ref ProcessingOnly);
        archive.Value(ref Count);
        archive.Value(ref HitPosition);
    }
}

public sealed class SushiToolChannelMetadataRequest {
    public SushiToolChannelMetadataKind Kind;
    public int ChannelIndex = -1;
    public int SourceOrderIndex = -1;
    public int TargetOrderIndex = -1;
    public string ChannelName = string.Empty;
    public int ColorR;
    public int ColorG;
    public int ColorB;

    public void Serialize(PacketSerializer archive) {
        archive.Enum(ref Kind);
        archive.Value(ref ChannelIndex);
        archive.Value(ref SourceOrderIndex);
        archive.Value(ref TargetOrderIndex);
        if (archive.IsWriting && ChannelName == null) {
            ChannelName = string.Empty;
        }
        archive.Value(ref ChannelName);
        if (!archive.IsWriting && ChannelName?.Length > SushiToolCompatNetPayloads.MaximumChannelNameLength) {
            throw new InvalidDataException("Invalid SushiTool channel name length.");
        }
        archive.Value(ref ColorR);
        archive.Value(ref ColorG);
        archive.Value(ref ColorB);
    }
}

public sealed class SushiToolChannelSlotState {
    public int Value;
    public int Count;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Value);
        archive.Value(ref Count);
    }
}

public sealed class SushiToolChannelState {
    public int ChannelIndex;
    public int OrderIndex = -1;
    public string ChannelName = string.Empty;
    public int ColorR;
    public int ColorG;
    public int ColorB;
    public List<SushiToolChannelSlotState> Slots = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref ChannelIndex);
        archive.Value(ref OrderIndex);
        if (archive.IsWriting && ChannelName == null) {
            ChannelName = string.Empty;
        }
        archive.Value(ref ChannelName);
        if (!archive.IsWriting && ChannelName?.Length > SushiToolCompatNetPayloads.MaximumChannelNameLength) {
            throw new InvalidDataException("Invalid SushiTool channel name length.");
        }
        archive.Value(ref ColorR);
        archive.Value(ref ColorG);
        archive.Value(ref ColorB);
        int count = archive.IsWriting ? Slots.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > SushiToolCompatNetPayloads.MaximumSlotsPerChannel) {
            throw new InvalidDataException("Invalid SushiTool channel slot count.");
        }

        if (archive.IsReading) {
            Slots.Clear();
            for (int i = 0; i < count; i++) {
                SushiToolChannelSlotState slot = new();
                slot.Serialize(archive);
                Slots.Add(slot);
            }
        }
        else {
            foreach (SushiToolChannelSlotState slot in Slots) {
                slot.Serialize(archive);
            }
        }
    }
}

public sealed class SushiToolChannelStateBatch {
    public int Sequence;
    public bool FullRefresh;
    public List<SushiToolChannelState> Channels = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref FullRefresh);
        int count = archive.IsWriting ? Channels.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > SushiToolCompatNetPayloads.MaximumChannelsPerBatch) {
            throw new InvalidDataException("Invalid SushiTool channel count.");
        }

        if (archive.IsReading) {
            Channels.Clear();
            for (int i = 0; i < count; i++) {
                SushiToolChannelState channel = new();
                channel.Serialize(archive);
                Channels.Add(channel);
            }
        }
        else {
            foreach (SushiToolChannelState channel in Channels) {
                channel.Serialize(archive);
            }
        }
    }
}

public static class SushiToolCompatNetPayloads {
    public const int MaximumChannelsPerBatch = 63;
    public const int MaximumSlotsPerChannel = 128;

    public static byte[] Write(SushiToolChannelStateBatch batch) {
        ArgumentNullException.ThrowIfNull(batch);
        return CompatNetPayload.Write(batch.Serialize);
    }

    public static SushiToolChannelStateBatch Read(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SushiToolChannelStateBatch batch = new();
        CompatNetPayload.Read(payload, batch.Serialize);
        return batch;
    }

    public static byte[] WriteAction(SushiToolInventoryActionRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SushiToolInventoryActionRequest ReadAction(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SushiToolInventoryActionRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteMetadata(SushiToolChannelMetadataRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SushiToolChannelMetadataRequest ReadMetadata(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SushiToolChannelMetadataRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteMachineControl(
        SushiToolMachineControlRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SushiToolMachineControlRequest ReadMachineControl(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SushiToolMachineControlRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteMachineConfig(
        SushiToolMachineConfigRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SushiToolMachineConfigRequest ReadMachineConfig(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SushiToolMachineConfigRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteTrainControl(SushiToolTrainControlRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SushiToolTrainControlRequest ReadTrainControl(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SushiToolTrainControlRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteTrainRecycle(SushiToolTrainRecycleRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SushiToolTrainRecycleRequest ReadTrainRecycle(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SushiToolTrainRecycleRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteTrainCall(SushiToolTrainCallRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SushiToolTrainCallRequest ReadTrainCall(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SushiToolTrainCallRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public const int MaximumChannelNameLength = 64;
    public const int MaximumMachineConfigLength = 8192;
}
