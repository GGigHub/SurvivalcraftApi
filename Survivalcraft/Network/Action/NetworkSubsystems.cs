using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkSubsystems {
        public static void OnExplosionCell(Dictionary<Point2, List<(Point3, float)>> explosionCells) {
            NetworkService.QueuePacket(new ExplosionCellPacket(explosionCells));
        }

        public static void OnExplosionSound(Vector3 position, float pressure, float delay) {
            NetworkService.QueuePacket(new ExplosionSoundPacket(position, pressure, delay));
        }

        public static void OnTimeUpdate(double totalElapsedGameTime, double timeOfDayOffset) {
            NetworkService.QueuePacket(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        }

        public static void OnMovingBlockAdd(IMovingBlockSet movingBlockSet) {
            NetworkService.QueuePacket(new MovingBlockAddPacket((SubsystemMovingBlocks.MovingBlockSet)movingBlockSet));
        }

        public static void OnMovingBlockRemove(IMovingBlockSet movingBlockSet, bool isCrash) {
            NetworkService.QueuePacket(new MovingBlockRemovePacket(movingBlockSet, isCrash));
        }

        public static void OnSkyLightning(Vector3 targetPosition) {
            NetworkService.QueuePacket(new SubsystemSkyPacket(targetPosition));
        }

        public static void OnPrecipitationUpdate(double startTime, double endTime, float intensity) {
            NetworkService.QueuePacket(new SubsystemWeatherPacket(startTime, endTime, intensity));
        }

        public static void OnFogUpdate(double startTime, float rampTime, double endTime) {
            NetworkService.QueuePacket(new SubsystemWeatherPacket(startTime, rampTime, endTime));
        }

        public static void OnFireBlockAdd(int x, int y, int z, float expandability) {
            NetworkService.QueuePacket(new FireBlockAddPacket(x, y, z, expandability));
        }

        public static void OnFireBlockRemove(int x, int y, int z) {
            NetworkService.QueuePacket(new FireBlockRemovePacket(x, y, z));
        }

        public static void OnProjectileFire(Projectile projectile) {
            NetworkService.QueuePacket(new ProjectilePacket(projectile));
        }
    }
}
