using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerStatSyncPacket : PlayerPacketBase {
    public const int MaximumStatsBytes = 256 * 1024;

    public override byte ID => (byte)PacketType.PlayerStatSync;
    public ValuesDictionary m_stat;
    public int m_entityId;
    public long m_lifecycleRevision = -1;
    public long m_revision;
    public PlayerStatSyncPacket() { }
    public PlayerStatSyncPacket(ComponentPlayer player, long revision = -1) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_stat = new ValuesDictionary();
        player.PlayerStats.Save(m_stat);
        m_entityId = player.Entity.Id;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(player);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            player,
            NetworkStateRevisionDomain.PlayerStats,
            revision);
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, null);
        byte[] messagePack = archive.IsWriting ? m_stat.ToMessagePack() : null;
        archive.Buffer(ref messagePack, MaximumStatsBytes);
        if (archive.IsReading) {
            m_stat = new ValuesDictionary();
            m_stat.ApplyOverridesUseMessagePack(messagePack);
        }
        archive.Value(ref m_entityId);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }
    public override void Handle(bool isServer) {
        if (m_stat == null
            || !TryResolvePlayer(false, out PlayerData playerData)
            || !NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.PlayerStats,
                out ComponentPlayer player,
                forceRefresh: false)
            || playerData.ComponentPlayer?.Entity.Id != m_entityId
            || !ReferenceEquals(playerData.ComponentPlayer, player)) return;
        player.PlayerStats.Load(m_stat);
    }
}


