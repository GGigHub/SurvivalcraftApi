using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network;

public abstract class PlayerPacketBase : Packet {
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_fromPlayerIndex;

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

    public static PlayerData ResolvePlayer(int playerIndex) {
        return GameManager.Project.FindSubsystem<SubsystemPlayers>()
            .FindPlayerData(playerData => playerData.PlayerIndex == playerIndex);
    }

    public static bool ValidateServerSender(PlayerData playerData, ClientSession from) {
        // Here `from` is the network connection. We check if the connection's ID matches the PlayerIndex.
        // Once we fully migrate Client to NetSession, this will check connection.PlayerIndex.
        return playerData != null && from != null && from.PlayerIndex == playerData.PlayerIndex;
    }

    public static void QueuePlayerPacket(Packet packet, bool isServer) {
        if (isServer) {
            packet.Except = packet.From;
            NetworkManager.Queue(packet);
        }
    }

    protected void SerializePlayerId(PacketSerializer archive, PlayerData playerData) {
        if (archive.IsWriting && playerData != null) {
            m_fromPlayerIndex = playerData.PlayerIndex;
        }
        archive.Value(ref m_fromPlayerIndex);
    }

    protected bool TryResolvePlayer(bool isServer, out PlayerData playerData) {
        playerData = ResolvePlayer(m_fromPlayerIndex);
        if (isServer && !ValidateServerSender(playerData, From)) {
            return false;
        }
        return playerData != null;
    }
}


