using Engine;

using GameEntitySystem;

namespace Game.Network;

public struct PickableLifecycleStamp {
    public int PickableId;
    public long Revision;
}

public static class PickablePacketHelpers {
    public static void SerializeFullPickable(PacketSerializer archive, Pickable pickable) {
        int id = pickable.Id;
        int count = archive.IsWriting ? pickable.Count : 0;
        int value = archive.IsWriting ? pickable.Value : 0;
        Vector3 position = archive.IsWriting ? pickable.Position : default;
        Vector3 velocity = archive.IsWriting ? pickable.Velocity : default;
        Matrix? matrix = archive.IsWriting ? pickable.StuckMatrix : null;
        Vector3? flyToPosition = archive.IsWriting ? pickable.FlyToPosition : null;
        archive.Value(ref id);
        archive.Value(ref count);
        archive.Value(ref value);
        archive.Value(ref position);
        archive.Value(ref velocity);
        archive.Value(ref matrix);
        archive.Value(ref flyToPosition);
        if (archive.IsReading) {
            pickable.Id = id;
            pickable.Count = count;
            pickable.Value = value;
            pickable.Position = position;
            pickable.Velocity = velocity;
            pickable.StuckMatrix = matrix;
            pickable.FlyToPosition = flyToPosition;
        }
    }

    public static void SerializeUpdatePickable(PacketSerializer archive, Pickable pickable) {
        int id = pickable.Id;
        Vector3 position = archive.IsWriting ? pickable.Position : default;
        Vector3? flyToPosition = archive.IsWriting ? pickable.FlyToPosition : null;
        archive.Value(ref id);
        archive.Value(ref position);
        archive.Value(ref flyToPosition);
        if (archive.IsReading) {
            pickable.Id = id;
            pickable.Position = position;
            pickable.FlyToPosition = flyToPosition;
        }
    }

    public static void SerializeDeletePickable(PacketSerializer archive, Pickable pickable) {
        int id = pickable.Id;
        archive.Value(ref id);
        if (archive.IsReading) {
            pickable.Id = id;
        }
    }

    public static void WriteList(PacketSerializer archive, IEnumerable<Pickable> pickables, Action<PacketSerializer, Pickable> serializer) {
        List<Pickable> list = [.. pickables];
        int count = list.Count;
        archive.Value(ref count);
        foreach (Pickable pickable in list) {
            serializer(archive, pickable);
        }
    }

    public static List<Pickable> ReadList(PacketSerializer archive, Action<PacketSerializer, Pickable> serializer) {
        int count = 0;
        archive.Value(ref count);
        List<Pickable> pickables = [];
        for (int i = 0; i < count; i++) {
            Pickable pickable = new();
            serializer(archive, pickable);
            pickables.Add(pickable);
        }
        return pickables;
    }

    public static List<PickableLifecycleStamp> CreateLifecycleStamps(
        IEnumerable<Pickable> pickables) {
        Project project = GameManager.Project
            ?? throw new InvalidOperationException("Cannot stamp pickables without a loaded project.");
        return pickables
            .Where(pickable => pickable.Id > 0)
            .Select(pickable => new PickableLifecycleStamp {
                PickableId = pickable.Id,
                Revision = PickableLifecycleTracker.GetAuthoritativeRevision(project, pickable.Id)
            })
            .ToList();
    }

    public static void SerializeLifecycleStamps(
        PacketSerializer archive,
        ref List<PickableLifecycleStamp> stamps) {
        int count = archive.IsWriting ? stamps.Count : 0;
        archive.Value(ref count);
        if (count is < 0 or > 65_536) {
            throw new InvalidDataException($"Invalid pickable lifecycle stamp count {count}.");
        }
        if (archive.IsReading) {
            stamps = new List<PickableLifecycleStamp>(count);
            for (int index = 0; index < count; index++) {
                PickableLifecycleStamp stamp = new();
                archive.Value(ref stamp.PickableId);
                archive.Value(ref stamp.Revision);
                stamps.Add(stamp);
            }
        }
        else {
            foreach (PickableLifecycleStamp source in stamps) {
                PickableLifecycleStamp stamp = source;
                archive.Value(ref stamp.PickableId);
                archive.Value(ref stamp.Revision);
            }
        }
    }

    public static IReadOnlyDictionary<int, long> BuildLifecycleMap(
        IEnumerable<PickableLifecycleStamp> stamps) {
        Dictionary<int, long> result = [];
        foreach (PickableLifecycleStamp stamp in stamps) {
            if (stamp.PickableId > 0 && stamp.Revision >= 0) {
                result.TryAdd(stamp.PickableId, stamp.Revision);
            }
        }
        return result;
    }

    public static bool TryApplyCreate(
        SubsystemPickables subsystemPickables,
        Pickable source,
        long revision) {
        if (source.Id <= 0 || revision < 0) {
            return false;
        }
        Project project = GameManager.Project;
        bool exists = subsystemPickables.Pickables.Any(pickable => pickable.Id == source.Id);
        PickableLifecycleDecision decision = PickableLifecycleTracker.ReceiveCreate(
            project,
            source.Id,
            revision,
            forceRefresh: !exists);
        if (!decision.Apply) {
            return false;
        }
        UpsertPickable(subsystemPickables, source);
        return true;
    }

    public static bool TryApplyUpdate(
        SubsystemPickables subsystemPickables,
        Pickable source,
        long revision) {
        if (source.Id <= 0 || revision < 0) {
            return false;
        }
        bool found = subsystemPickables.PickableAction(source.Id, current => {
            PickableLifecycleDecision decision = PickableLifecycleTracker.ReceiveUpdate(
                GameManager.Project,
                source.Id,
                revision);
            if (decision.Apply) {
                current.Position = source.Position;
                current.FlyToPosition = source.FlyToPosition;
            }
        }, requestSync: false);
        if (!found && NetworkManager.IsClientRunning) {
            PickableLifecycleTracker.RecordMissingUpdateRequest(GameManager.Project);
            NetworkManager.Queue(new PickableRequestSyncPacket(source.Id));
        }
        return found;
    }

    public static bool TryApplyFlyToPosition(
        SubsystemPickables subsystemPickables,
        int pickableId,
        Vector3? flyToPosition,
        long revision) {
        if (pickableId <= 0 || revision < 0) {
            return false;
        }
        bool found = subsystemPickables.PickableAction(pickableId, current => {
            PickableLifecycleDecision decision = PickableLifecycleTracker.ReceiveUpdate(
                GameManager.Project,
                pickableId,
                revision);
            if (decision.Apply) {
                current.FlyToPosition = flyToPosition;
            }
        }, requestSync: false);
        if (!found && NetworkManager.IsClientRunning) {
            PickableLifecycleTracker.RecordMissingUpdateRequest(GameManager.Project);
            NetworkManager.Queue(new PickableRequestSyncPacket(pickableId));
        }
        return found;
    }

    public static bool TryApplyRemoval(
        SubsystemPickables subsystemPickables,
        int pickableId,
        long revision,
        bool playSound) {
        if (pickableId <= 0 || revision < 0) {
            return false;
        }
        Project project = GameManager.Project;
        bool exists = subsystemPickables.Pickables.Any(pickable => pickable.Id == pickableId);
        PickableLifecycleDecision decision = PickableLifecycleTracker.ReceiveRemoval(
            project,
            pickableId,
            revision,
            forceRefresh: exists && !PickableLifecycleTracker.IsPresent(project, pickableId));
        if (!decision.Apply) {
            return false;
        }
        RemovePickable(subsystemPickables, pickableId, playSound);
        return true;
    }

    public static void UpsertPickable(SubsystemPickables subsystemPickables, Pickable source) {
        Pickable existing = subsystemPickables.Pickables.Find(p => p.Id == source.Id);
        if (existing != null) {
            existing.Value = source.Value;
            existing.Count = source.Count;
            existing.Position = source.Position;
            existing.Velocity = source.Velocity;
            existing.StuckMatrix = source.StuckMatrix;
            existing.FlyToPosition = source.FlyToPosition;
        }
        else {
            Pickable created = subsystemPickables.CreatePickable(source.Id, source.Value, source.Count, source.Position, source.Velocity, source.StuckMatrix, null);
            if (created == null) {
                return;
            }
            created.FlyToPosition = source.FlyToPosition;
            subsystemPickables.AddPickable(created);
        }
    }

    public static void RemovePickable(SubsystemPickables subsystemPickables, int id, bool playSound) {
        _ = subsystemPickables.PickableAction(id, pick => {
            if (playSound) {
                subsystemPickables.PlayPickableCollectedSound(pick);
            }
            subsystemPickables.m_pickables.Remove(pick);
            subsystemPickables.useMaps.Remove(pick.Id);
            subsystemPickables.PickableRemoved?.Invoke(pick);
        }, false);
    }
}

