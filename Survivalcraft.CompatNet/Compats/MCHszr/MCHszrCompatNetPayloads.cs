using Engine;
using Game.Network;

namespace Game;

/// <summary>
/// The only MCHszr operations currently accepted on the wire. The payload
/// carries intent and a slot identity; it never carries damage, velocity,
/// target IDs, or a client-computed result.
/// </summary>
public enum MCHszrCompatNetOperation : ushort {
    PoseToggle = 1,
    ChargeBegin = 2,
    ChargeRelease = 3,
    FoodConsume = 4,
    StateSnapshot = 5
}

public enum MCHszrCompatNetAction : byte {
    SpearDash = 1,
    HammerShockwave = 2
}

public sealed class MCHszrCompatNetRequest {
    public MCHszrCompatNetAction Action;
    public int EntityId;
    public int SlotIndex;
    public int ItemValue;
    public int Sequence;

    public void Serialize(PacketSerializer archive) {
        archive.Enum(ref Action);
        archive.Value(ref EntityId);
        archive.Value(ref SlotIndex);
        archive.Value(ref ItemValue);
        archive.Value(ref Sequence);
    }
}

public sealed class MCHszrCompatNetStateSnapshot {
    public int Sequence;
    public List<MCHszrCompatNetPlayerState> Players = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        int count = archive.IsWriting ? Players.Count : 0;
        archive.Value(ref count);
        if (count < 0 || count > 64) {
            throw new InvalidDataException("Invalid MCHszr player state count.");
        }

        if (archive.IsReading) {
            Players.Clear();
            for (int i = 0; i < count; i++) {
                MCHszrCompatNetPlayerState state = new();
                state.Serialize(archive);
                Players.Add(state);
            }
        }
        else {
            foreach (MCHszrCompatNetPlayerState state in Players) {
                state.Serialize(archive);
            }
        }

        int projectileCount = archive.IsWriting ? Projectiles.Count : 0;
        archive.Value(ref projectileCount);
        if (projectileCount < 0 || projectileCount > 512) {
            throw new InvalidDataException("Invalid MCHszr projectile state count.");
        }
        if (archive.IsReading) {
            Projectiles.Clear();
            for (int i = 0; i < projectileCount; i++) {
                MCHszrCompatNetProjectileState projectile = new();
                projectile.Serialize(archive);
                Projectiles.Add(projectile);
            }
        }
        else {
            foreach (MCHszrCompatNetProjectileState projectile in Projectiles) {
                projectile.Serialize(archive);
            }
        }
    }

    public List<MCHszrCompatNetProjectileState> Projectiles = [];
}

public sealed class MCHszrCompatNetPlayerState {
    public int PlayerIndex;
    public bool HasBody;
    public Vector3 Position;
    public Vector3 Velocity;
    public bool IsDashing;
    public Vector3 DashVelocity;
    public float WalkSpeed;
    public float JumpSpeed;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref PlayerIndex);
        archive.Value(ref HasBody);
        archive.Value(ref Position);
        archive.Value(ref Velocity);
        archive.Value(ref IsDashing);
        archive.Value(ref DashVelocity);
        archive.Value(ref WalkSpeed);
        archive.Value(ref JumpSpeed);
        if (PlayerIndex < 0
            || !IsFinite(Position)
            || !IsFinite(Velocity)
            || !IsFinite(DashVelocity)
            || !float.IsFinite(WalkSpeed)
            || !float.IsFinite(JumpSpeed)) {
            throw new InvalidDataException("Invalid MCHszr player state.");
        }
    }

    private static bool IsFinite(Vector3 value) {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }
}

public sealed class MCHszrCompatNetProjectileState {
    public int Value;
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Value);
        archive.Value(ref Position);
        archive.Value(ref Velocity);
        archive.Value(ref AngularVelocity);
        if (Value == 0
            || !IsFinite(Position)
            || !IsFinite(Velocity)
            || !IsFinite(AngularVelocity)) {
            throw new InvalidDataException("Invalid MCHszr projectile state.");
        }
    }

    private static bool IsFinite(Vector3 value) {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }
}
