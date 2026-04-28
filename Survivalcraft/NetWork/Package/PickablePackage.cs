using Engine;

namespace Game.NetWork.Packages;

public class PickablePackage : IPackage {
    public override byte ID => (byte)PackageType.Pickable;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public enum PickType {
        Create,
        Update,
        Delete,
        RequestSync,
        SetFlyToPosition,
        CreateList,
        DeleteList,
        SyncList,
    }

    public PickType m_type;
    public int m_id;
    public int m_value;
    public byte m_count;
    public Vector3 m_position;
    public Vector3 m_velocity;
    public Vector3? m_flyToPosition;
    public byte? m_getPlayer;
    public Vector3 m_positionFix;
    public float m_distance;
    public Matrix? stuckMatrix;
    public bool PlaySound;
    public List<Pickable> m_pickables = new List<Pickable>();

    public PickablePackage() {
    }

    public PickablePackage(List<Pickable> pickables, PickType type = PickType.Update) {
        m_type = type;
        m_pickables.AddRange(pickables);
    }

    public PickablePackage(Pickable pickable, PickType pickType) {
        m_id = pickable.Id;
        m_value = pickable.Value;
        m_count = (byte)pickable.Count;
        m_position = pickable.Position;
        m_velocity = pickable.Velocity;
        m_getPlayer = pickable.GetPickPlayer;
        stuckMatrix = pickable.StuckMatrix;
        m_flyToPosition = pickable.FlyToPosition;
        m_type = pickType;
        PlaySound = pickable.PlaySound;
    }

    public PickablePackage(int pickId) {
        m_id = pickId;
        m_type = PickType.Delete;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_type);
        switch (m_type) {
            case PickType.Create:
                archive.Value(ref m_id);
                archive.Value(ref m_count);
                archive.Value(ref m_value);
                archive.Value(ref m_position);
                archive.Value(ref m_velocity);
                archive.Value(ref stuckMatrix);
                break;
            case PickType.Update:
                SerializePickableList(archive, PickType.Update);
                break;
            case PickType.SetFlyToPosition:
                archive.Value(ref m_id);
                Vector3 flyTo = m_flyToPosition ?? default;
                archive.Value(ref flyTo);
                if (archive.IsReading) {
                    m_flyToPosition = flyTo;
                }
                break;
            case PickType.Delete:
                archive.Value(ref m_id);
                archive.Value(ref PlaySound);
                break;
            case PickType.CreateList:
            case PickType.SyncList:
                SerializePickableList(archive, m_type);
                break;
            case PickType.DeleteList:
                SerializePickableList(archive, PickType.DeleteList);
                break;
            case PickType.RequestSync:
                archive.Value(ref m_id);
                break;
        }
    }

    private void SerializePickableList(PackageSerializer archive, PickType type) {
        int count;
        if (archive.IsWriting && type == PickType.SyncList) {
            count = GameManager.Project.FindSubsystem<SubsystemPickables>().Pickables.Count;
        }
        else {
            count = archive.IsWriting ? m_pickables.Count : 0;
        }
        archive.Value(ref count);
        if (archive.IsReading) {
            m_pickables.Clear();
            for (int i = 0; i < count; i++) {
                Pickable pickable = new Pickable();
                SerializePickable(archive, pickable, type);
                m_pickables.Add(pickable);
            }
        }
        else {
            IEnumerable<Pickable> source = type == PickType.SyncList
                ? GameManager.Project.FindSubsystem<SubsystemPickables>().Pickables
                : m_pickables;
            foreach (Pickable pickable in source) {
                SerializePickable(archive, pickable, type);
            }
        }
    }

    private void SerializePickable(PackageSerializer archive, Pickable pickable, PickType type) {
        int id = pickable.Id;
        archive.Value(ref id);
        if (archive.IsReading) {
            pickable.Id = id;
        }

        switch (type) {
            case PickType.Update:
                Vector3 position = archive.IsWriting ? pickable.Position : default;
                archive.Value(ref position);
                if (archive.IsReading) pickable.Position = position;
                break;
            case PickType.DeleteList:
                break;
            default:
                byte count = archive.IsWriting ? (byte)pickable.Count : (byte)0;
                int value = archive.IsWriting ? pickable.Value : 0;
                Vector3 position2 = archive.IsWriting ? pickable.Position : default;
                Vector3 velocity = archive.IsWriting ? pickable.Velocity : default;
                Matrix? matrix = archive.IsWriting ? pickable.StuckMatrix : null;
                archive.Value(ref count);
                archive.Value(ref value);
                archive.Value(ref position2);
                archive.Value(ref velocity);
                archive.Value(ref matrix);
                if (archive.IsReading) {
                    pickable.Count = count;
                    pickable.Value = value;
                    pickable.Position = position2;
                    pickable.Velocity = velocity;
                    pickable.StuckMatrix = matrix;
                }
                break;
        }
    }

    public override void Handle(bool isServer) {
        if (isServer && m_type != PickType.RequestSync) {
            return;
        }
        var subsystemPickable = GameManager.Project.FindSubsystem<SubsystemPickables>();
        bool flag = false;
        switch (m_type) {
            case PickType.Create:
                Pickable tmp = subsystemPickable.Pickables.Find(p => p.Id == m_id);
                if (tmp != null) {
                    tmp.Value = m_value;
                    tmp.Count = m_count;
                    tmp.Velocity = m_velocity;
                    tmp.StuckMatrix = stuckMatrix;
                }
                else {
                    Pickable created = subsystemPickable.CreatePickable(m_id, m_value, (int)m_count, m_position, (Vector3?)m_velocity, stuckMatrix, null);
                    subsystemPickable.AddPickable(created);
                }
                break;
            case PickType.Update:
                foreach (var c in m_pickables) {
                    subsystemPickable.PickableAction(c.Id, pick => { pick.Position = c.Position; });
                }
                foreach (var c in subsystemPickable.Pickables) {
                    if (m_pickables.Find(x => x.Id == c.Id) == null) {
                        subsystemPickable.m_pickablesToRemove.Add(c);
                    }
                }
                break;
            case PickType.Delete:
                _ = subsystemPickable.PickableAction(m_id, pick => {
                    if (PlaySound) subsystemPickable.PlayPickableCollectedSound(pick);
                    subsystemPickable.m_pickables.Remove(pick);
                    subsystemPickable.useMaps.Remove(pick.Id);
                    subsystemPickable.PickableRemoved?.Invoke(pick);
                }, false);
                break;
            case PickType.RequestSync:
                flag = subsystemPickable.PickableAction(m_id, pick => {
                    NetworkManager.QueuePackage(new PickablePackage(pick, PickType.Create) { To = From });
                });
                if (!flag) {
                    NetworkManager.QueuePackage(new PickablePackage(m_id) { To = From });
                }
                break;
            case PickType.SetFlyToPosition:
                subsystemPickable.PickableAction(m_id, pick => {
                    pick.FlyToPosition = m_flyToPosition;
                });
                break;
            case PickType.SyncList:
            case PickType.CreateList:
                if (!isServer) {
                    foreach (Pickable pickable in m_pickables) {
                        Pickable created = subsystemPickable.CreatePickable(pickable.Id, pickable.Value, (int)pickable.Count, pickable.Position, (Vector3?)pickable.Velocity, pickable.StuckMatrix, null);
                        subsystemPickable.AddPickable(created);
                    }
                }
                break;
            case PickType.DeleteList:
                if (!isServer) {
                    foreach (Pickable pickable in m_pickables) {
                        _ = subsystemPickable.PickableAction(pickable.Id, pick => {
                            subsystemPickable.m_pickables.Remove(pick);
                            subsystemPickable.useMaps.Remove(pick.Id);
                            subsystemPickable.PickableRemoved?.Invoke(pick);
                        }, false);
                    }
                }
                break;
        }
    }
}
