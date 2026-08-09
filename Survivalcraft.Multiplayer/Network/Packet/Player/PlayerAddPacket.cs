using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerAddPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerAdd;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public override bool RequiresProject => true;
    public ValuesDictionary m_vd;

    public PlayerAddPacket() { }

    public PlayerAddPacket(PlayerData playerData) {
        m_vd = new ValuesDictionary();
        playerData.Save(m_vd);
    }

    public override void Serialize(PacketSerializer archive) {
        byte[] messagePack = archive.IsWriting ? m_vd.ToMessagePack() : null;
        archive.Buffer(ref messagePack);
        if (archive.IsReading) {
            m_vd = new ValuesDictionary();
            m_vd.ApplyOverridesUseMessagePack(messagePack);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        Project project = GameManager.Project;
        if (project == null) return;
        var subsystemPlayers = project.FindSubsystem<SubsystemPlayers>();
        var playerData = new PlayerData(project);
        playerData.Load(m_vd);

        PlayerData[] sameIndex = subsystemPlayers.PlayersData
            .Where(existing => existing.PlayerIndex == playerData.PlayerIndex)
            .ToArray();
        PlayerData retainedPlayer = sameIndex.FirstOrDefault(existing => existing.IsMainPlayer)
            ?? sameIndex.FirstOrDefault(existing => existing.ComponentPlayer != null);
        foreach (PlayerData stale in sameIndex.Where(existing => existing != retainedPlayer)) {
            Log.Warning(
                $"Replacing stale remote player data. PlayerIndex={stale.PlayerIndex}, Name={stale.Name}");
            subsystemPlayers.RemovePlayerData(stale, true);
        }
        if (retainedPlayer != null) {
            NetworkManager.Queue(new PlayerKnownPacket(retainedPlayer.PlayerIndex));
            playerData.Dispose();
            return;
        }

        subsystemPlayers.AddPlayerData(playerData);
        NetworkManager.Queue(new PlayerKnownPacket(playerData.PlayerIndex));
    }
}

