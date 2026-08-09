using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerAddExperiencePacket : PlayerPacketBase {
    public const int MaximumExperienceCount = 100_000;
    public const float MaximumLevel = 10_000f;

    public override byte ID => (byte)PacketType.PlayerAddExperience;
    public int m_count;
    public bool m_playSound;
    public float m_level;
    public int m_entityId;
    public long m_lifecycleRevision = -1;
    public long m_revision;
    public PlayerAddExperiencePacket() { }
    public PlayerAddExperiencePacket(
        PlayerData playerData,
        int count,
        bool playSound,
        float level,
        long revision = -1) {
        m_fromPlayerIndex = playerData.PlayerIndex;
        m_count = count;
        m_playSound = playSound;
        m_level = level;
        ComponentLevel componentLevel = playerData.ComponentPlayer.ComponentLevel;
        m_entityId = componentLevel.Entity.Id;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(componentLevel);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            componentLevel,
            NetworkStateRevisionDomain.PlayerProgression,
            revision);
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, null);
        archive.Value(ref m_count);
        archive.Value(ref m_playSound);
        archive.Value(ref m_level);
        archive.Value(ref m_entityId);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }
    public override void Handle(bool isServer) {
        if (m_count is < 0 or > MaximumExperienceCount
            || !float.IsFinite(m_level)
            || m_level is < 1f or > MaximumLevel
            || !TryResolvePlayer(false, out PlayerData playerData)
            || !NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.PlayerProgression,
                out ComponentLevel level,
                forceRefresh: false)
            || playerData.ComponentPlayer?.Entity.Id != m_entityId
            || !ReferenceEquals(playerData.ComponentPlayer.ComponentLevel, level)) return;
        level.AddExperience(m_count, m_playSound, fromNetwork: true);
        playerData.Level = m_level;
    }
}


