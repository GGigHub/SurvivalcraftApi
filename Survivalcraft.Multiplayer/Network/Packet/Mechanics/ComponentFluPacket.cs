

namespace Game.Network;

public class ComponentFluPacket : Packet {
    public const float MaximumDuration = 86_400f;
    public const float MaximumImpulseLengthSquared = 64f;

    public override byte ID => (byte)PacketType.Flu;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public enum EventType {
        SyncStat,
        FluEffect,
        StartFlu,
        Sneeze,
        Impulse
    }

    public float m_fluDuration;
    public float m_coughDuration;
    public float m_sneezeDuration;
    public float m_fluOnset;
    public Engine.Vector3 m_Impulse;

    public EventType m_eventType;

    public int entityId;
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public ComponentFluPacket() {
    }

    public ComponentFluPacket(
        ComponentFlu flu,
        EventType eventType,
        long revision = -1) {
        entityId = flu.Entity.Id;
        m_eventType = eventType;
        m_fluOnset = flu.m_fluOnset;
        m_fluDuration = flu.m_fluDuration;
        m_coughDuration = flu.m_coughDuration;
        m_sneezeDuration = flu.m_sneezeDuration;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(flu);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            flu,
            NetworkStateRevisionDomain.Flu,
            revision);
    }

    public ComponentFluPacket(
        ComponentFlu flu,
        Engine.Vector3 impulse,
        long revision = -1)
        : this(flu, EventType.Impulse, revision) {
        m_Impulse = impulse;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref entityId);
        archive.Enum(ref m_eventType);
        archive.Value(ref m_fluOnset);
        archive.Value(ref m_fluDuration);
        archive.Value(ref m_coughDuration);
        archive.Value(ref m_sneezeDuration);
        archive.Value(ref m_Impulse);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (!Enum.IsDefined(m_eventType)
            || !IsValidDuration(m_fluOnset)
            || !IsValidDuration(m_fluDuration)
            || !IsValidDuration(m_coughDuration)
            || !IsValidDuration(m_sneezeDuration)
            || !IsFinite(m_Impulse)
            || m_Impulse.LengthSquared() > MaximumImpulseLengthSquared
            || !NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.Flu,
                out ComponentFlu flu,
                forceRefresh: false)) {
            return;
        }
        flu.m_fluOnset = m_fluOnset;
        flu.m_sneezeDuration = m_sneezeDuration;
        flu.m_coughDuration = m_coughDuration;
        flu.m_fluDuration = m_fluDuration;
        switch (m_eventType) {
            case EventType.SyncStat:
                break;
            case EventType.FluEffect:
                flu.FluEffect(fromNetwork: true);
                break;
            case EventType.StartFlu:
                break;
            case EventType.Sneeze:
                flu.Sneeze(fromNetwork: true);
                break;
            case EventType.Impulse:
                if (m_Impulse.LengthSquared() > 0f) {
                    flu.m_componentPlayer.ComponentBody.ApplyImpulse(m_Impulse);
                }
                break;
        }
    }

    private static bool IsValidDuration(float value) =>
        float.IsFinite(value) && value is >= 0f and <= MaximumDuration;

    private static bool IsFinite(Engine.Vector3 value) =>
        float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && float.IsFinite(value.Z);
}

