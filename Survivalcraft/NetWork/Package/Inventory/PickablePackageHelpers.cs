using Engine;

namespace Game.NetWork;

public static class PickablePackageHelpers {
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