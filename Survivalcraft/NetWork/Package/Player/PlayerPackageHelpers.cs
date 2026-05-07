using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public static class PlayerPackageHelpers {
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
            .FindPlayerData(playerData => playerData.ClientId.HasValue && playerData.ClientId.Value == clientId);
    }

    public static bool ValidateServerSender(PlayerData playerData, Client from) {
        return playerData != null && from != null && playerData.ClientId == from.ID;
    }

    public static void SerializeFromPlayerId(PackageSerializer archive, ref byte fromPlayerId, PlayerData playerData) {
        if (archive.IsWriting && playerData != null) {
            fromPlayerId = playerData?.ClientId ?? 0;
        }
        archive.Value(ref fromPlayerId);
    }

    public static bool ThrottleHit(PlayerData playerData) {
        if (playerData == null) {
            return false;
        }
        double time = GameManager.Project.FindSubsystem<SubsystemTime>().GameTime;
        Guid guid = playerData.PlayerGUID;
        if (s_lastHitTimes.TryGetValue(guid, out double last) && time - last < ServerHitCooldown) {
            return true;
        }
        s_lastHitTimes[guid] = time;
        return false;
    }

    public static void QueuePlayerPackage(IPackage package, bool isServer) {
        if (isServer) {
            package.Except = package.From;
            NetworkManager.QueuePackage(package);
        }
    }

    public static bool TryResolvePlayer(byte fromPlayerId, bool isServer, Client from, out PlayerData playerData) {
        playerData = ResolvePlayer(fromPlayerId);
        if (isServer && !ValidateServerSender(playerData, from)) {
            return false;
        }
        return playerData != null;
    }
}