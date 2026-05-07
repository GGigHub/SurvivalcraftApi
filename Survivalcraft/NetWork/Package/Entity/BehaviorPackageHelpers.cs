using Engine;
using GameEntitySystem;

namespace Game.NetWork;

public static class BehaviorPackageHelpers {
    public static void WithEntity(int entityId, Action<Entity> action) {
        GameManager.Project.FindEntity(entityId, action);
    }

    public static void BroadcastFromServer(IPackage package, bool isServer) {
        if (isServer) {
            package.Except = package.From;
            NetworkManager.QueuePackage(package);
        }
    }
}