using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network;

public abstract class PlayerPacketBase : Packet {
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public byte m_fromPlayerId;

    public enum ChangeFlag : byte {
        None = 0,
        ParentBodyChange = 1,
        LookAnglesChange = 2,
        LadderChange = 4,
        PositionChange = 8,
        RotationChange = 16,
        VelocityChange = 32,
        ChildLookAnglesChange = 64,
        SneakChange = 128
    }

    private static readonly Dictionary<Guid, double> s_lastHitTimes = [];
    private const double ServerHitCooldown = 0.2;

    public static PlayerData ResolvePlayer(byte clientId) {
        return GameManager.Project.FindSubsystem<SubsystemPlayers>()
            .FindPlayerData(playerData => NetworkService.GetClientByGUID(playerData.NetworkGuid).ID == clientId);
    }

    public static bool ValidateServerSender(PlayerData playerData, Client from) {
        return playerData != null && from != null && NetworkService.GetClientByGUID(playerData.NetworkGuid).ID == from.ID;
    }

    public static bool ThrottleHit(PlayerData playerData) {
        if (playerData == null) {
            return false;
        }
        double time = GameManager.Project.FindSubsystem<SubsystemTime>().GameTime;
        Guid guid = playerData.NetworkGuid;
        if (s_lastHitTimes.TryGetValue(guid, out double last) && time - last < ServerHitCooldown) {
            return true;
        }
        s_lastHitTimes[guid] = time;
        return false;
    }

    public static void QueuePlayerPacket(Packet packet, bool isServer) {
        if (isServer) {
            packet.Except = packet.From;
            NetworkService.QueuePacket(packet);
        }
    }

    protected void SerializePlayerId(PacketSerializer archive, PlayerData playerData) {
        if (archive.IsWriting && playerData != null) {
            m_fromPlayerId = NetworkService.GetClientByGUID(playerData.NetworkGuid).ID;
        }
        archive.Value(ref m_fromPlayerId);
    }

    protected bool TryResolvePlayer(bool isServer, out PlayerData playerData) {
        playerData = ResolvePlayer(m_fromPlayerId);
        if (isServer && !ValidateServerSender(playerData, From)) {
            return false;
        }
        return playerData != null;
    }
}


