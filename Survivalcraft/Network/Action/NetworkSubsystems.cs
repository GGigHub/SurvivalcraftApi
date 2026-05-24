using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkSubsystems {
        public static void OnExplosionCell(SubsystemExplosions subsystem, Dictionary<Point2, List<(Point3, float)>> explosionCells) {
            NetworkManager.QueuePacket(new ExplosionCellPacket(explosionCells));
        }

        public static void OnExplosionSound(SubsystemExplosions subsystem, Vector3 position, float pressure, float delay) {
            NetworkManager.QueuePacket(new ExplosionSoundPacket(position, pressure, delay));
        }

        public static void OnTimeUpdate(SubsystemGameInfo subsystem, double totalElapsedGameTime, double timeOfDayOffset) {
            NetworkManager.QueuePacket(new SubsystemTimePacket(totalElapsedGameTime, timeOfDayOffset));
        }

        public static void OnMovingBlockAdd(SubsystemMovingBlocks subsystem, IMovingBlockSet movingBlockSet) {
            NetworkManager.QueuePacket(new MovingBlockAddPacket((SubsystemMovingBlocks.MovingBlockSet)movingBlockSet));
        }

        public static void OnMovingBlockRemove(SubsystemMovingBlocks subsystem, IMovingBlockSet movingBlockSet, bool isCrash) {
            NetworkManager.QueuePacket(new MovingBlockRemovePacket(movingBlockSet, isCrash));
        }

        public static void OnSkyLightning(SubsystemSky subsystem, Vector3 targetPosition) {
            NetworkManager.QueuePacket(new SubsystemSkyPacket(targetPosition));
        }

        public static void OnPrecipitationUpdate(SubsystemWeather subsystem, double startTime, double endTime, float intensity) {
            NetworkManager.QueuePacket(new SubsystemWeatherPacket(startTime, endTime, intensity));
        }

        public static void OnFogUpdate(SubsystemWeather subsystem, double startTime, float rampTime, double endTime) {
            NetworkManager.QueuePacket(new SubsystemWeatherPacket(startTime, rampTime, endTime));
        }

        public static void OnFireBlockAdd(SubsystemFireBlockBehavior subsystem, int x, int y, int z, float expandability) {
            NetworkManager.QueuePacket(new FireBlockAddPacket(x, y, z, expandability));
        }

        public static void OnFireBlockRemove(SubsystemFireBlockBehavior subsystem, int x, int y, int z) {
            NetworkManager.QueuePacket(new FireBlockRemovePacket(x, y, z));
        }

        public static void OnProjectileFire(SubsystemProjectiles subsystem, Projectile projectile) {
            NetworkManager.QueuePacket(new ProjectilePacket(projectile));
        }
    }
}
