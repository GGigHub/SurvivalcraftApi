using Engine;

namespace Game.NetWork.Packages;

internal static class PickablePackageHelpers {
    public static void SerializeFullPickable(PackageSerializer archive, Pickable pickable) {
        int id = pickable.Id;
        byte count = archive.IsWriting ? (byte)pickable.Count : (byte)0;
        int value = archive.IsWriting ? pickable.Value : 0;
        Vector3 position = archive.IsWriting ? pickable.Position : default;
        Vector3 velocity = archive.IsWriting ? pickable.Velocity : default;
        Matrix? matrix = archive.IsWriting ? pickable.StuckMatrix : null;
        archive.Value(ref id);
        archive.Value(ref count);
        archive.Value(ref value);
        archive.Value(ref position);
        archive.Value(ref velocity);
        archive.Value(ref matrix);
        if (archive.IsReading) {
            pickable.Id = id;
            pickable.Count = count;
            pickable.Value = value;
            pickable.Position = position;
            pickable.Velocity = velocity;
            pickable.StuckMatrix = matrix;
        }
    }

    public static void SerializeUpdatePickable(PackageSerializer archive, Pickable pickable) {
        int id = pickable.Id;
        Vector3 position = archive.IsWriting ? pickable.Position : default;
        archive.Value(ref id);
        archive.Value(ref position);
        if (archive.IsReading) {
            pickable.Id = id;
            pickable.Position = position;
        }
    }

    public static void SerializeDeletePickable(PackageSerializer archive, Pickable pickable) {
        int id = pickable.Id;
        archive.Value(ref id);
        if (archive.IsReading) {
            pickable.Id = id;
        }
    }

    public static void WriteList(PackageSerializer archive, IEnumerable<Pickable> pickables, Action<PackageSerializer, Pickable> serializer) {
        List<Pickable> list = [.. pickables];
        int count = list.Count;
        archive.Value(ref count);
        foreach (Pickable pickable in list) {
            serializer(archive, pickable);
        }
    }

    public static List<Pickable> ReadList(PackageSerializer archive, Action<PackageSerializer, Pickable> serializer) {
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

    public static void UpsertPickable(SubsystemPickables subsystemPickables, Pickable source) {
        Pickable existing = subsystemPickables.Pickables.Find(p => p.Id == source.Id);
        if (existing != null) {
            existing.Value = source.Value;
            existing.Count = source.Count;
            existing.Position = source.Position;
            existing.Velocity = source.Velocity;
            existing.StuckMatrix = source.StuckMatrix;
        }
        else {
            Pickable created = subsystemPickables.CreatePickable(source.Id, source.Value, source.Count, source.Position, source.Velocity, source.StuckMatrix, null);
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

public class PickableCreatePackage : IPackage {
    public override byte ID => (byte)PackageType.PickableCreate;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public Pickable m_pickable = new();

    public PickableCreatePackage() { }

    public PickableCreatePackage(Pickable pickable) {
        m_pickable.Id = pickable.Id;
        m_pickable.Value = pickable.Value;
        m_pickable.Count = pickable.Count;
        m_pickable.Position = pickable.Position;
        m_pickable.Velocity = pickable.Velocity;
        m_pickable.StuckMatrix = pickable.StuckMatrix;
    }

    public override void Serialize(PackageSerializer archive) {
        PickablePackageHelpers.SerializeFullPickable(archive, m_pickable);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        PickablePackageHelpers.UpsertPickable(GameManager.Project.FindSubsystem<SubsystemPickables>(), m_pickable);
    }
}

public class PickableUpdatePackage : IPackage {
    public override byte ID => (byte)PackageType.PickableUpdate;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableUpdatePackage() { }

    public PickableUpdatePackage(List<Pickable> pickables) {
        m_pickables.AddRange(pickables);
    }

    public override void Serialize(PackageSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePackageHelpers.ReadList(archive, PickablePackageHelpers.SerializeUpdatePickable);
        }
        else {
            PickablePackageHelpers.WriteList(archive, m_pickables, PickablePackageHelpers.SerializeUpdatePickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            subsystemPickables.PickableAction(pickable.Id, current => current.Position = pickable.Position);
        }
        foreach (Pickable existing in subsystemPickables.Pickables) {
            if (m_pickables.Find(x => x.Id == existing.Id) == null) {
                subsystemPickables.m_pickablesToRemove.Add(existing);
            }
        }
    }
}

public class PickableDeletePackage : IPackage {
    public override byte ID => (byte)PackageType.PickableDelete;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_id;
    public bool PlaySound;

    public PickableDeletePackage() { }

    public PickableDeletePackage(Pickable pickable) {
        m_id = pickable.Id;
        PlaySound = pickable.PlaySound;
    }

    public PickableDeletePackage(int pickId) {
        m_id = pickId;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_id);
        archive.Value(ref PlaySound);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        PickablePackageHelpers.RemovePickable(GameManager.Project.FindSubsystem<SubsystemPickables>(), m_id, PlaySound);
    }
}

public class PickableRequestSyncPackage : IPackage {
    public override byte ID => (byte)PackageType.PickableRequestSync;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_id;

    public PickableRequestSyncPackage() { }

    public PickableRequestSyncPackage(int pickId) {
        m_id = pickId;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_id);
    }

    public override void Handle(bool isServer) {
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        bool found = subsystemPickables.PickableAction(m_id, pick => {
            NetworkManager.QueuePackage(new PickableCreatePackage(pick) { To = From });
        });
        if (!found) {
            NetworkManager.QueuePackage(new PickableDeletePackage(m_id) { To = From });
        }
    }
}

public class PickableSetFlyToPositionPackage : IPackage {
    public override byte ID => (byte)PackageType.PickableSetFlyToPosition;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_id;
    public Vector3? m_flyToPosition;

    public PickableSetFlyToPositionPackage() { }

    public PickableSetFlyToPositionPackage(Pickable pickable) {
        m_id = pickable.Id;
        m_flyToPosition = pickable.FlyToPosition;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_id);
        Vector3 flyTo = m_flyToPosition ?? default;
        archive.Value(ref flyTo);
        if (archive.IsReading) {
            m_flyToPosition = flyTo;
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindSubsystem<SubsystemPickables>().PickableAction(m_id, pick => pick.FlyToPosition = m_flyToPosition);
    }
}

public class PickableCreateListPackage : IPackage {
    public override byte ID => (byte)PackageType.PickableCreateList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableCreateListPackage() { }

    public PickableCreateListPackage(IEnumerable<Pickable> pickables) {
        m_pickables = [.. pickables];
    }

    public override void Serialize(PackageSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePackageHelpers.ReadList(archive, PickablePackageHelpers.SerializeFullPickable);
        }
        else {
            PickablePackageHelpers.WriteList(archive, m_pickables, PickablePackageHelpers.SerializeFullPickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            PickablePackageHelpers.UpsertPickable(subsystemPickables, pickable);
        }
    }
}

public class PickableDeleteListPackage : IPackage {
    public override byte ID => (byte)PackageType.PickableDeleteList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableDeleteListPackage() { }

    public PickableDeleteListPackage(IEnumerable<Pickable> pickables) {
        m_pickables = [.. pickables];
    }

    public override void Serialize(PackageSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePackageHelpers.ReadList(archive, PickablePackageHelpers.SerializeDeletePickable);
        }
        else {
            PickablePackageHelpers.WriteList(archive, m_pickables, PickablePackageHelpers.SerializeDeletePickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            PickablePackageHelpers.RemovePickable(subsystemPickables, pickable.Id, false);
        }
    }
}

public class PickableSyncListPackage : IPackage {
    public override byte ID => (byte)PackageType.PickableSyncList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Pickable> m_pickables = [];

    public PickableSyncListPackage() { }

    public override void Serialize(PackageSerializer archive) {
        if (archive.IsReading) {
            m_pickables = PickablePackageHelpers.ReadList(archive, PickablePackageHelpers.SerializeFullPickable);
        }
        else {
            PickablePackageHelpers.WriteList(archive, GameManager.Project.FindSubsystem<SubsystemPickables>().Pickables, PickablePackageHelpers.SerializeFullPickable);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        foreach (Pickable pickable in m_pickables) {
            PickablePackageHelpers.UpsertPickable(subsystemPickables, pickable);
        }
    }
}
