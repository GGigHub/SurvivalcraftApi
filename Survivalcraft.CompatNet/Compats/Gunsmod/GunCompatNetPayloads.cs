using Engine;
using Game.Network;

namespace Game;

public enum GunCompatNetOperation : ushort {
    Projectile = 1,
    Missile = 2
}

public sealed class GunProjectileRequest {
    public int BulletValue;
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref BulletValue);
        archive.Value(ref Position);
        archive.Value(ref Velocity);
        archive.Value(ref AngularVelocity);
    }
}

public sealed class GunMissileRequest {
    public Vector3 Position;
    public Quaternion Rotation;
    public float RemainTime;
    public float DirectlyTime;
    public int GuideLevel;
    public bool AntiVehi;
    public int TargetEntityId;
    public Vector3 Velocity;
    public float StartEngineTime;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Position);
        archive.Value(ref Rotation);
        archive.Value(ref RemainTime);
        archive.Value(ref DirectlyTime);
        archive.Value(ref GuideLevel);
        archive.Value(ref AntiVehi);
        archive.Value(ref TargetEntityId);
        archive.Value(ref Velocity);
        archive.Value(ref StartEngineTime);
    }
}
