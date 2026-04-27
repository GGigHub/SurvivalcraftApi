using Engine;

namespace Game.NetWork.Packages;

public class SubsystemBodyPackage : IPackage {
    public override byte ID => (byte)PackageType.Body;
    public override ClientStage MinNeedState => ClientStage.Playing;

    public enum EventType {
        BodyUpdate,
        ApplyImpulse,
        HandleAxisCollision
    }

    private struct BodyItem {
        public int CreatureId;
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
        public Vector2 LookAngles;
        public Vector3? FlyOrder;
        public ChangeFlag changeFlag;
    }

    public int CreatureId;
    public int TargetCreatureId;
    public Vector3 m_Impulse;
    public EventType m_eventType;
    public ComponentModel CreatureModel;

    public enum ChangeFlag : byte {
        None = 0,
        LookAnglesChange = 1,
        FlyOrderChange = 2,
        PositionChange = 4,
        RotationChange = 8,
        VelocityChange = 16
    }

    private List<BodyItem> m_bodyList = new List<BodyItem>();

    public SubsystemBodyPackage() {
    }

    public SubsystemBodyPackage(List<ComponentBody> bodies) {
        m_eventType = EventType.BodyUpdate;
        foreach (var b in bodies) {
            AddItem(b);
        }
    }

    public void AddItem(ComponentBody body) {
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

    public SubsystemBodyPackage(ComponentBody body, Vector3 vector) {
        CreatureId = body.Entity.Id;
        m_eventType = EventType.ApplyImpulse;
        m_Impulse = vector;
    }

    public SubsystemBodyPackage(ComponentBody from, ComponentBody target, Vector3 velocity) {
        m_eventType = EventType.HandleAxisCollision;
        CreatureId = from.Entity.Id;
        TargetCreatureId = target.Entity.Id;
        m_Impulse = velocity;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_eventType);
        switch (m_eventType) {
            case EventType.BodyUpdate:
                int cnt = archive.IsWriting ? m_bodyList.Count : 0;
                archive.Value(ref cnt);
                if (archive.IsReading) {
                    m_bodyList.Clear();
                    for (int i = 0; i < cnt; i++) {
                        BodyItem bodyItem = new BodyItem();
                        SerializeBodyItem(archive, ref bodyItem);
                        m_bodyList.Add(bodyItem);
                    }
                }
                else {
                    for (int i = 0; i < cnt; i++) {
                        BodyItem bodyItem = m_bodyList[i];
                        SerializeBodyItem(archive, ref bodyItem);
                    }
                }
                break;
            case EventType.HandleAxisCollision:
                archive.Value(ref CreatureId);
                archive.Value(ref TargetCreatureId);
                archive.Value(ref m_Impulse);
                break;
            case EventType.ApplyImpulse:
                archive.Value(ref CreatureId);
                archive.Value(ref m_Impulse);
                break;
        }
    }

    private void SerializeBodyItem(PackageSerializer archive, ref BodyItem bodyItem) {
        archive.Enum(ref bodyItem.changeFlag);
        archive.Value(ref bodyItem.CreatureId);
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.PositionChange)) archive.Value(ref bodyItem.Position);
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.RotationChange)) archive.Value(ref bodyItem.Rotation);
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.VelocityChange)) archive.Value(ref bodyItem.Velocity);
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) archive.Value(ref bodyItem.LookAngles);
        if (bodyItem.changeFlag.HasFlag(ChangeFlag.FlyOrderChange)) archive.Value(ref bodyItem.FlyOrder);
    }

    public override void Handle(bool isServer) {
        if (isServer && m_eventType == EventType.BodyUpdate) {
            return;
        }
        switch (m_eventType) {
            case EventType.BodyUpdate:
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
                    if (item2.Entity.FindComponent<ComponentPlayer>() != null || item2.ChildBodies.Count > 0) {
                        continue;
                    }
                    if (!m_bodyList.Exists(x => x.CreatureId == item2.Entity.Id)) {
                        rl.Add(item2);
                    }
                }
                if (ml.Count > 0) {
                    NetworkManager.QueuePackage(new EntityPackage(ml));
#if DEBUG
                    Log.Information($"瀹㈡埛绔悜鏈嶅姟鍣ㄨ姹傚悓姝ュ姩鐗╂暟:{ml.Count}");
#endif
                }
                if (rl.Count > 0) {
                    foreach (var b in rl) {
#if DEBUG
                        Log.Information($"瀹㈡埛绔Щ闄や笉鍚屾鐨勫疄浣?{b.Entity.Id}");
#endif
                        GameManager.Project.RemoveEntity(b.Entity, true);
                    }
                }
                break;
            case EventType.HandleAxisCollision:
                break;
            case EventType.ApplyImpulse:
                break;
        }
    }
}
