using Engine;

namespace Game.NetWork;

public class ApplyImpulsePackage : IPackage {
    public override byte ID => (byte)PackageType.ApplyImpulse;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_entityId;
    public Vector3 m_impulse;

    public ApplyImpulsePackage() {
    }

    public ApplyImpulsePackage(ComponentBody body, Vector3 impulse) {
        m_entityId = body.Entity.Id;
        m_impulse = impulse;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_impulse);
    }

    public override void Handle(bool isServer) {
        GameManager.Project.FindEntity(m_entityId, e => {
            e.FindComponent<ComponentBody>()?.ApplyImpulse(m_impulse);
        });
    }
}