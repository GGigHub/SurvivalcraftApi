using Engine;
using TemplatesDatabase;

namespace Game.Network;

public abstract class PlayerPacketBase : Packet {
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public byte m_fromPlayerId;

    protected void SerializePlayerId(PacketSerializer archive, PlayerData playerData) {
        PlayerPacketHelpers.SerializeFromPlayerId(archive, ref m_fromPlayerId, playerData);
    }

    protected bool TryResolvePlayer(bool isServer, out PlayerData playerData) {
        return PlayerPacketHelpers.TryResolvePlayer(m_fromPlayerId, isServer, From, out playerData);
    }
}

