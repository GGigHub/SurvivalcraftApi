using GameEntitySystem;

namespace Game.Network;

internal static class NetworkEntityStatePacketHelpers {
    public static long LifecycleRevision(Component component) =>
        EntityLifecycleTracker.GetAuthoritativeRevision(
            component.Entity.Project,
            component.Entity.Id);

    public static long StateRevision(
        Component component,
        NetworkStateRevisionDomain domain,
        long revision = -1) =>
        revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                component.Entity.Project,
                domain,
                component.Entity.Id);

    public static bool TryResolveComponent<TComponent>(
        bool isServer,
        int entityId,
        long lifecycleRevision,
        long revision,
        NetworkStateRevisionDomain domain,
        out TComponent component,
        bool forceRefresh = true)
        where TComponent : Component {
        component = null;
        if (isServer || entityId <= 0 || revision < 0) {
            return false;
        }

        Project project = GameManager.Project;
        Entity entity = project?.FindEntity(entityId);
        if (project == null
            || !EntityLifecycleTracker.CanApplyState(
                project,
                entityId,
                lifecycleRevision)) {
            if (project != null
                && EntityLifecycleTracker.ShouldRequestLoad(
                    project,
                    entityId,
                    lifecycleRevision,
                    entity != null)) {
                NetworkManager.Queue(new EntitySyncRequestPacket([entityId]));
            }
            return false;
        }

        component = entity?.FindComponent<TComponent>();
        if (component == null) {
            return false;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            domain,
            entityId,
            revision,
            forceRefresh);
        if (!decision.Apply) {
            component = null;
            return false;
        }
        return true;
    }
}
