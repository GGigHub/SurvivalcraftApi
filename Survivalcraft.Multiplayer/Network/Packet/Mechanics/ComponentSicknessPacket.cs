

namespace Game.Network;

public class ComponentSicknessPacket : Packet {
    public const float MaximumDuration = 86_400f;

    public override byte ID => (byte)PacketType.Sickness;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public enum EventType {
        SyncStat,
        NauseaEffect
    }

    public float m_sicknessDuration;
    public bool m_isNausea;
    public EventType m_eventType;
    public int entityId;
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public ComponentSicknessPacket() {
    }

    public ComponentSicknessPacket(
        ComponentSickness sickness,
        EventType eventType = EventType.SyncStat,
        long revision = -1) {
        entityId = sickness.Entity.Id;
        m_eventType = eventType;
        m_sicknessDuration = sickness.m_sicknessDuration;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(sickness);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            sickness,
            NetworkStateRevisionDomain.Sickness,
            revision);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref entityId);
        archive.Enum(ref m_eventType);
        archive.Value(ref m_sicknessDuration);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (!Enum.IsDefined(m_eventType)
            || !float.IsFinite(m_sicknessDuration)
            || m_sicknessDuration is < 0f or > MaximumDuration
            || !NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.Sickness,
                out ComponentSickness sickness,
                forceRefresh: false)) {
            return;
        }
        sickness.m_sicknessDuration = m_sicknessDuration;
        if (m_eventType == EventType.NauseaEffect) {
            sickness.NauseaEffect(true);
        }
    }
}

