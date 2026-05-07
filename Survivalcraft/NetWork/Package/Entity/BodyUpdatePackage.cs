using Engine;

namespace Game.NetWork;

public class BodyUpdatePackage : IPackage {
    public override byte ID => (byte)PackageType.BodyUpdate;
    public override ClientStage MinNeedState => ClientStage.Playing;

    [Flags]
    public enum ChangeFlag : byte {
        None = 0,
        LookAnglesChange = 1,
        FlyOrderChange = 2,
        PositionChange = 4,
        RotationChange = 8,
        VelocityChange = 16
    }

    public struct BodyItem {
        public int CreatureId;
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
        public Vector2 LookAngles;
        public Vector3? FlyOrder;
        public ChangeFlag changeFlag;
    }

    public List<BodyItem> m_bodyList = new List<BodyItem>();

    public BodyUpdatePackage() {
    }

    public BodyUpdatePackage(List<ComponentBody> bodies) {
        foreach (var body in bodies) {
            BodyItem bodyItem = new BodyItem();
            bodyItem.CreatureId = body.Entity.Id;
            if (body.SendPosition.HasValue) {
                bodyItem.changeFlag |= ChangeFlag.PositionChange;
                bodyItem.Position = body.SendPosition.Value;
            }
            if (body.SendRotation.HasValue) {
                bodyItem.changeFlag |= ChangeFlag.RotationChange;
                bodyItem.Rotation = body.SendRotation.Value;
            }
            if (body.SendVelocity.HasValue) {
                bodyItem.changeFlag |= ChangeFlag.VelocityChange;
                bodyItem.Velocity = body.SendVelocity.Value;
            }
            if (body.Locomotion != null) {
                if (body.Locomotion.SendLookAngles.HasValue) {
                    bodyItem.changeFlag |= ChangeFlag.LookAnglesChange;
                    bodyItem.LookAngles = body.Locomotion.SendLookAngles.Value;
                }
                if (body.Locomotion.FlyOrderChange) {
                    bodyItem.changeFlag |= ChangeFlag.FlyOrderChange;
                    bodyItem.FlyOrder = body.Locomotion.LastFlyOrder;
                }
            }
            m_bodyList.Add(bodyItem);
        }
    }

    public override void Serialize(PackageSerializer archive) {
        int cnt = archive.IsWriting ? m_bodyList.Count : 0;
        archive.Value(ref cnt);
        if (archive.IsReading) {
            m_bodyList.Clear();
            for (int i = 0; i < cnt; i++) {
                BodyItem item = new BodyItem();
                SerializeItem(archive, ref item);
                m_bodyList.Add(item);
            }
        }
        else {
            for (int i = 0; i < cnt; i++) {
                BodyItem item = m_bodyList[i];
                SerializeItem(archive, ref item);
            }
        }
    }

    private void SerializeItem(PackageSerializer archive, ref BodyItem item) {
        archive.Enum(ref item.changeFlag);
        archive.Value(ref item.CreatureId);
        if (item.changeFlag.HasFlag(ChangeFlag.PositionChange)) archive.Value(ref item.Position);
        if (item.changeFlag.HasFlag(ChangeFlag.RotationChange)) archive.Value(ref item.Rotation);
        if (item.changeFlag.HasFlag(ChangeFlag.VelocityChange)) archive.Value(ref item.Velocity);
        if (item.changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) archive.Value(ref item.LookAngles);
        if (item.changeFlag.HasFlag(ChangeFlag.FlyOrderChange)) archive.Value(ref item.FlyOrder);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        var bodies = GameManager.Project.FindSubsystem<SubsystemBodies>();
        List<int> ml = new List<int>();
        List<ComponentBody> rl = new List<ComponentBody>();
        foreach (var item in m_bodyList) {
            bodies.FindBodyByCreatureID(item.CreatureId, body => {
                if (item.changeFlag.HasFlag(ChangeFlag.PositionChange)) body.netPosition.SetNext(item.Position);
                if (item.changeFlag.HasFlag(ChangeFlag.RotationChange)) body.netRotation.SetNext(item.Rotation);
                if (item.changeFlag.HasFlag(ChangeFlag.VelocityChange)) body.netVelocity.SetNext(item.Velocity);
                if (body.Locomotion != null) {
                    if (item.changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) body.Locomotion.NetLookAngles.SetNext(item.LookAngles);
                    if (item.changeFlag.HasFlag(ChangeFlag.FlyOrderChange)) body.Locomotion.LastFlyOrder = item.FlyOrder;
                }
            }, () => {
                ml.Add(item.CreatureId);
            });
        }
        foreach (var item2 in bodies.Bodies) {
            if (item2.Entity.FindComponent<ComponentPlayer>() != null || item2.ChildBodies.Count > 0) continue;
            if (!m_bodyList.Exists(x => x.CreatureId == item2.Entity.Id)) {
                rl.Add(item2);
            }
        }
        if (ml.Count > 0) {
            NetworkManager.QueuePackage(new EntitySyncRequestPackage(ml));
        }
        if (rl.Count > 0) {
            foreach (var b in rl) {
                GameManager.Project.RemoveEntity(b.Entity, true);
            }
        }
    }
}