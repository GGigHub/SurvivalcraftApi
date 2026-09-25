using Engine;
using Game.Network;

namespace Game;

public enum SubnauticaCompatNetOperation : ushort {
    MermaidFeedRequest = 1,
    MermaidQuestAcceptRequest = 2,
    MermaidQuestDeliverRequest = 3,
    MermaidStateSnapshot = 4,
    MermaidGiftRequest = 5,
    AchievementStateSnapshot = 6,
    CreatureKillRequest = 7,
    KrakenStateSnapshot = 8
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

public sealed class SubnauticaMermaidGiftRequest {
    public int MermaidEntityId;
    public int GiftType;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref MermaidEntityId);
        archive.Value(ref GiftType);
        if (MermaidEntityId <= 0 || GiftType < 0 || GiftType > 3) {
            throw new InvalidDataException("Invalid Subnautica mermaid gift request.");
        }
    }
}

public sealed class SubnauticaCreatureKillRequest {
    public int VictimEntityId;
    public string CreatureName = string.Empty;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref VictimEntityId);
        if (archive.IsWriting && CreatureName == null) {
            CreatureName = string.Empty;
        }
        archive.Value(ref CreatureName);
        if (VictimEntityId <= 0
            || string.IsNullOrWhiteSpace(CreatureName)
            || CreatureName.Length > SubnauticaCompatNetPayloads.MaximumCreatureNameLength) {
            throw new InvalidDataException("Invalid Subnautica creature kill request.");
        }
    }
}

public sealed class SubnauticaAchievementProgress {
    public string AchievementId = string.Empty;
    public int CurrentValue;
    public bool Unlocked;
    public double UnlockedGameTime;
    public bool RewardGranted;

    public void Serialize(PacketSerializer archive) {
        if (archive.IsWriting && AchievementId == null) {
            AchievementId = string.Empty;
        }
        archive.Value(ref AchievementId);
        archive.Value(ref CurrentValue);
        archive.Value(ref Unlocked);
        archive.Value(ref UnlockedGameTime);
        archive.Value(ref RewardGranted);
        if (string.IsNullOrWhiteSpace(AchievementId)
            || AchievementId.Length > SubnauticaCompatNetPayloads.MaximumAchievementIdLength
            || CurrentValue < 0
            || !double.IsFinite(UnlockedGameTime)) {
            throw new InvalidDataException("Invalid Subnautica achievement progress.");
        }
    }
}

public sealed class SubnauticaAchievementState {
    public int PlayerEntityId;
    public List<SubnauticaAchievementProgress> Progress = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref PlayerEntityId);
        if (PlayerEntityId <= 0) {
            throw new InvalidDataException("Invalid Subnautica achievement player.");
        }

        int count = archive.IsWriting ? Progress.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > SubnauticaCompatNetPayloads.MaximumAchievementsPerPlayer) {
            throw new InvalidDataException("Invalid Subnautica achievement count.");
        }

        if (archive.IsReading) {
            Progress.Clear();
            for (int i = 0; i < count; i++) {
                SubnauticaAchievementProgress progress = new();
                progress.Serialize(archive);
                Progress.Add(progress);
            }
        }
        else {
            foreach (SubnauticaAchievementProgress progress in Progress) {
                progress.Serialize(archive);
            }
        }
    }
}

public sealed class SubnauticaAchievementStateBatch {
    public int Sequence;
    public bool FullRefresh;
    public List<SubnauticaAchievementState> Records = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref FullRefresh);
        int count = archive.IsWriting ? Records.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > SubnauticaCompatNetPayloads.MaximumAchievementRecordsPerBatch) {
            throw new InvalidDataException("Invalid Subnautica achievement record count.");
        }

        if (archive.IsReading) {
            Records.Clear();
            for (int i = 0; i < count; i++) {
                SubnauticaAchievementState state = new();
                state.Serialize(archive);
                Records.Add(state);
            }
        }
        else {
            foreach (SubnauticaAchievementState state in Records) {
                state.Serialize(archive);
            }
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

public sealed class SubnauticaKrakenState {
    public List<string> TriggeredEncounterIds = [];
    public bool HasActiveEncounter;
    public string EncounterId = string.Empty;
    public Point3 TriggerPosition;
    public Vector3 CenterPosition;
    public Vector3 SurfacePosition;
    public Vector3 RiseStartPosition;
    public Vector3 RiseEndPosition;
    public int TriggerPlayerEntityId;
    public int KrakenEntityId;
    public int Phase;
    public float PhaseTimeRemaining;

    public void Serialize(PacketSerializer archive) {
        int triggeredCount = archive.IsWriting ? TriggeredEncounterIds.Count : 0;
        archive.Value(ref triggeredCount);
        if (triggeredCount < 0 || triggeredCount > SubnauticaCompatNetPayloads.MaximumKrakenTriggeredIds) {
            throw new InvalidDataException("Invalid Subnautica Kraken triggered id count.");
        }

        if (archive.IsReading) {
            TriggeredEncounterIds.Clear();
            for (int i = 0; i < triggeredCount; i++) {
                string id = string.Empty;
                archive.Value(ref id);
                if (string.IsNullOrWhiteSpace(id)
                    || id.Length > SubnauticaCompatNetPayloads.MaximumKrakenEncounterIdLength) {
                    throw new InvalidDataException("Invalid Subnautica Kraken encounter id.");
                }
                TriggeredEncounterIds.Add(id);
            }
        }
        else {
            foreach (string id in TriggeredEncounterIds) {
                string value = id ?? string.Empty;
                archive.Value(ref value);
                if (string.IsNullOrWhiteSpace(value)
                    || value.Length > SubnauticaCompatNetPayloads.MaximumKrakenEncounterIdLength) {
                    throw new InvalidDataException("Invalid Subnautica Kraken encounter id.");
                }
            }
        }

        archive.Value(ref HasActiveEncounter);
        if (!HasActiveEncounter) {
            return;
        }

        if (archive.IsWriting && EncounterId == null) {
            EncounterId = string.Empty;
        }
        archive.Value(ref EncounterId);
        archive.Value(ref TriggerPosition);
        archive.Value(ref CenterPosition);
        archive.Value(ref SurfacePosition);
        archive.Value(ref RiseStartPosition);
        archive.Value(ref RiseEndPosition);
        archive.Value(ref TriggerPlayerEntityId);
        archive.Value(ref KrakenEntityId);
        archive.Value(ref Phase);
        archive.Value(ref PhaseTimeRemaining);
        if (string.IsNullOrWhiteSpace(EncounterId)
            || EncounterId.Length > SubnauticaCompatNetPayloads.MaximumKrakenEncounterIdLength
            || TriggerPlayerEntityId < 0
            || KrakenEntityId < 0
            || Phase < 0
            || Phase > 16
            || !float.IsFinite(PhaseTimeRemaining)
            || PhaseTimeRemaining < 0f
            || !SubnauticaCompatNetPayloads.IsFinite(CenterPosition)
            || !SubnauticaCompatNetPayloads.IsFinite(SurfacePosition)
            || !SubnauticaCompatNetPayloads.IsFinite(RiseStartPosition)
            || !SubnauticaCompatNetPayloads.IsFinite(RiseEndPosition)) {
            throw new InvalidDataException("Invalid Subnautica Kraken state.");
        }
    }
}

public sealed class SubnauticaKrakenStateBatch {
    public int Sequence;
    public bool FullRefresh;
    public List<SubnauticaKrakenState> Records = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref FullRefresh);
        int count = archive.IsWriting ? Records.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > 1) {
            throw new InvalidDataException("Invalid Subnautica Kraken record count.");
        }

        if (archive.IsReading) {
            Records.Clear();
            for (int i = 0; i < count; i++) {
                SubnauticaKrakenState state = new();
                state.Serialize(archive);
                Records.Add(state);
            }
        }
        else {
            foreach (SubnauticaKrakenState state in Records) {
                state.Serialize(archive);
            }
        }
    }
}

public static class SubnauticaCompatNetPayloads {
    public const int MaximumRecordsPerBatch = 64;
    public const int MaximumCompletedQuestIds = 16;
    public const int MaximumQuestIdLength = 64;
    public const int MaximumCreatureNameLength = 96;
    public const int MaximumAchievementIdLength = 96;
    public const int MaximumAchievementsPerPlayer = 64;
    public const int MaximumAchievementRecordsPerBatch = 32;
    public const int MaximumRelationshipState = 16;
    public const int MaximumKrakenTriggeredIds = 256;
    public const int MaximumKrakenEncounterIdLength = 128;

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

    public static byte[] WriteGiftRequest(SubnauticaMermaidGiftRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SubnauticaMermaidGiftRequest ReadGiftRequest(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SubnauticaMermaidGiftRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteCreatureKillRequest(SubnauticaCreatureKillRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return CompatNetPayload.Write(request.Serialize);
    }

    public static SubnauticaCreatureKillRequest ReadCreatureKillRequest(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SubnauticaCreatureKillRequest request = new();
        CompatNetPayload.Read(payload, request.Serialize);
        return request;
    }

    public static byte[] WriteAchievementState(SubnauticaAchievementStateBatch batch) {
        ArgumentNullException.ThrowIfNull(batch);
        return CompatNetPayload.Write(batch.Serialize);
    }

    public static SubnauticaAchievementStateBatch ReadAchievementState(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SubnauticaAchievementStateBatch batch = new();
        CompatNetPayload.Read(payload, batch.Serialize);
        return batch;
    }

    public static bool IsFinite(Vector3 value) {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }

    public static byte[] WriteKrakenState(SubnauticaKrakenStateBatch batch) {
        ArgumentNullException.ThrowIfNull(batch);
        return CompatNetPayload.Write(batch.Serialize);
    }

    public static SubnauticaKrakenStateBatch ReadKrakenState(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        SubnauticaKrakenStateBatch batch = new();
        CompatNetPayload.Read(payload, batch.Serialize);
        return batch;
    }
}
