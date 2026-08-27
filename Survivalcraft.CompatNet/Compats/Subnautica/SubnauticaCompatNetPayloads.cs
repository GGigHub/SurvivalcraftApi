using Game.Network;

namespace Game;

public enum SubnauticaCompatNetOperation : ushort {
    MermaidFeedRequest = 1,
    MermaidQuestAcceptRequest = 2,
    MermaidQuestDeliverRequest = 3,
    MermaidStateSnapshot = 4
}

public enum SubnauticaMermaidFeedKind : byte {
    RawFish = 1,
    CookedFish = 2
}

public sealed class SubnauticaMermaidFeedRequest {
    public int MermaidEntityId;
    public SubnauticaMermaidFeedKind Kind;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref MermaidEntityId);
        archive.Enum(ref Kind);
        if (MermaidEntityId <= 0
            || Kind is not SubnauticaMermaidFeedKind.RawFish
                and not SubnauticaMermaidFeedKind.CookedFish) {
            throw new InvalidDataException("Invalid Subnautica mermaid feed request.");
        }
    }
}

public sealed class SubnauticaMermaidQuestAcceptRequest {
    public int MermaidEntityId;
    public string QuestId = string.Empty;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref MermaidEntityId);
        if (archive.IsWriting && QuestId == null) {
            QuestId = string.Empty;
        }
        archive.Value(ref QuestId);
        if (MermaidEntityId <= 0
            || string.IsNullOrWhiteSpace(QuestId)
            || QuestId.Length > SubnauticaCompatNetPayloads.MaximumQuestIdLength) {
            throw new InvalidDataException("Invalid Subnautica quest accept request.");
        }
    }
}

public sealed class SubnauticaMermaidQuestDeliverRequest {
    public int MermaidEntityId;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref MermaidEntityId);
        if (MermaidEntityId <= 0) {
            throw new InvalidDataException("Invalid Subnautica quest delivery request.");
        }
    }
}

public sealed class SubnauticaMermaidState {
    public int MermaidEntityId;
    public int TargetPlayerEntityId;
    public float AffectionPoints;
    public int RelationshipState = -1;
    public string ActiveQuestId = string.Empty;
    public int CurrentProgress;
    public int TargetProgress;
    public List<string> CompletedQuestIds = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref MermaidEntityId);
        archive.Value(ref TargetPlayerEntityId);
        archive.Value(ref AffectionPoints);
        archive.Value(ref RelationshipState);
        if (archive.IsWriting && ActiveQuestId == null) {
            ActiveQuestId = string.Empty;
        }
        archive.Value(ref ActiveQuestId);
        archive.Value(ref CurrentProgress);
        archive.Value(ref TargetProgress);

        if (!float.IsFinite(AffectionPoints)
            || MermaidEntityId <= 0
            || TargetPlayerEntityId < 0
            || RelationshipState < -1
            || RelationshipState > SubnauticaCompatNetPayloads.MaximumRelationshipState
            || ActiveQuestId.Length > SubnauticaCompatNetPayloads.MaximumQuestIdLength
            || CurrentProgress < 0
            || TargetProgress < 0) {
            throw new InvalidDataException("Invalid Subnautica mermaid state.");
        }

        int count = archive.IsWriting ? CompletedQuestIds.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > SubnauticaCompatNetPayloads.MaximumCompletedQuestIds) {
            throw new InvalidDataException("Invalid Subnautica completed quest count.");
        }

        if (archive.IsReading) {
            CompletedQuestIds.Clear();
            for (int i = 0; i < count; i++) {
                string questId = string.Empty;
                archive.Value(ref questId);
                if (string.IsNullOrWhiteSpace(questId)
                    || questId.Length > SubnauticaCompatNetPayloads.MaximumQuestIdLength) {
                    throw new InvalidDataException("Invalid Subnautica completed quest id.");
                }
                CompletedQuestIds.Add(questId);
            }
        }
        else {
            foreach (string questId in CompletedQuestIds) {
                string value = questId ?? string.Empty;
                archive.Value(ref value);
                if (string.IsNullOrWhiteSpace(value)
                    || value.Length > SubnauticaCompatNetPayloads.MaximumQuestIdLength) {
                    throw new InvalidDataException("Invalid Subnautica completed quest id.");
                }
            }
        }
    }
}

public sealed class SubnauticaMermaidStateBatch {
    public int Sequence;
    public bool FullRefresh;
    public List<SubnauticaMermaidState> Records = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref FullRefresh);
        int count = archive.IsWriting ? Records.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > SubnauticaCompatNetPayloads.MaximumRecordsPerBatch) {
            throw new InvalidDataException("Invalid Subnautica mermaid record count.");
        }

        if (archive.IsReading) {
            Records.Clear();
            for (int i = 0; i < count; i++) {
                SubnauticaMermaidState record = new();
                record.Serialize(archive);
                Records.Add(record);
            }
        }
        else {
            foreach (SubnauticaMermaidState record in Records) {
                record.Serialize(archive);
            }
        }
    }
}

public static class SubnauticaCompatNetPayloads {
    public const int MaximumRecordsPerBatch = 64;
    public const int MaximumCompletedQuestIds = 16;
    public const int MaximumQuestIdLength = 64;
    public const int MaximumRelationshipState = 16;

    public static byte[] Write(SubnauticaMermaidStateBatch batch) {
        ArgumentNullException.ThrowIfNull(batch);
        return CompatNetPayload.Write(batch.Serialize);
    }

    public static SubnauticaMermaidStateBatch Read(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SubnauticaMermaidStateBatch batch = new();
        CompatNetPayload.Read(payload, batch.Serialize);
        return batch;
    }

    public static byte[] WriteFeedRequest(SubnauticaMermaidFeedRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SubnauticaMermaidFeedRequest ReadFeedRequest(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SubnauticaMermaidFeedRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteQuestAcceptRequest(SubnauticaMermaidQuestAcceptRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SubnauticaMermaidQuestAcceptRequest ReadQuestAcceptRequest(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SubnauticaMermaidQuestAcceptRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteQuestDeliverRequest(SubnauticaMermaidQuestDeliverRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SubnauticaMermaidQuestDeliverRequest ReadQuestDeliverRequest(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SubnauticaMermaidQuestDeliverRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }
}
