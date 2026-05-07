using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerPositionSetPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerPositionSet;
    public Vector3 Position;
    public Vector3 Velocity;
    public PlayerPositionSetPackage() { }
    public PlayerPositionSetPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        Position = player.ComponentBody.Position;
        Velocity = player.ComponentBody.Velocity;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref Position);
        archive.Value(ref Velocity);
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        playerData.ComponentPlayer.ComponentBody.Position = Position;
        playerData.ComponentPlayer.ComponentBody.Velocity = Velocity;
    }
}