using Engine;
using Game.Network;

namespace Game;

public enum GhoulCompatNetOperation : ushort {
    StateSnapshot = 1
}

public sealed class GhoulPlayerState {
    public int PlayerIndex;
    public float SoulsValue;
    public float SpawnCd;
    public double HealProhibitTime;
    public float InAirTimeSum;
    public float EarthquakeDamage;
    public float ArmorProtection;
    public bool HasFullSet;
    public float RecoveryTo1Time;
    public bool IsAcArmorEquipped;
    public List<GhoulClothingSlotState> Clothing = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref PlayerIndex);
        archive.Value(ref SoulsValue);
        archive.Value(ref SpawnCd);
        archive.Value(ref HealProhibitTime);
        archive.Value(ref InAirTimeSum);
        archive.Value(ref EarthquakeDamage);
        archive.Value(ref ArmorProtection);
        archive.Value(ref HasFullSet);
        archive.Value(ref RecoveryTo1Time);
        archive.Value(ref IsAcArmorEquipped);

        if (PlayerIndex < 0
            || !float.IsFinite(SoulsValue)
            || !float.IsFinite(SpawnCd)
            || !double.IsFinite(HealProhibitTime)
            || !float.IsFinite(InAirTimeSum)
            || !float.IsFinite(EarthquakeDamage)
            || !float.IsFinite(ArmorProtection)
            || !float.IsFinite(RecoveryTo1Time)) {
            throw new InvalidDataException("Invalid Ghoul player state.");
        }

        int clothingCount = archive.IsWriting ? Clothing.Count : 0;
        archive.Value(ref clothingCount);
        if (clothingCount < 0 || clothingCount > GhoulCompatNetPayloads.MaximumClothingSlots) {
            throw new InvalidDataException("Invalid Ghoul clothing slot count.");
        }
        if (archive.IsReading) {
            Clothing.Clear();
            for (int i = 0; i < clothingCount; i++) {
                GhoulClothingSlotState slot = new();
                slot.Serialize(archive);
                Clothing.Add(slot);
            }
        }
        else {
            foreach (GhoulClothingSlotState slot in Clothing) {
                slot.Serialize(archive);
            }
        }
    }
}

public sealed class GhoulClothingSlotState {
    public int SlotIndex;
    public List<int> Values = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref SlotIndex);
        int count = archive.IsWriting ? Values.Count : 0;
        archive.Value(ref count);
        if (SlotIndex < 0
            || count < 0
            || count > GhoulCompatNetPayloads.MaximumClothingItemsPerSlot) {
            throw new InvalidDataException("Invalid Ghoul clothing slot.");
        }
        if (archive.IsReading) {
            Values.Clear();
            for (int i = 0; i < count; i++) {
                int value = 0;
                archive.Value(ref value);
                Values.Add(value);
            }
        }
        else {
            foreach (int value in Values) {
                int item = value;
                archive.Value(ref item);
            }
        }
    }
}

public sealed class GhoulCreatureState {
    public string EntityName = string.Empty;
    public Vector3 Position;
    public bool HasPosition;
    public bool IsBoss;
    public bool IsCommonFrank;
    public bool PersistentAttack;
    public bool IsTough;

    public void Serialize(PacketSerializer archive) {
        if (archive.IsWriting && EntityName == null) {
            EntityName = string.Empty;
        }
        archive.Value(ref EntityName);
        archive.Value(ref Position);
        archive.Value(ref HasPosition);
        archive.Value(ref IsBoss);
        archive.Value(ref IsCommonFrank);
        archive.Value(ref PersistentAttack);
        archive.Value(ref IsTough);

        if (string.IsNullOrWhiteSpace(EntityName)
            || EntityName.Length > GhoulCompatNetPayloads.MaximumEntityNameLength
            || !IsFinite(Position)) {
            throw new InvalidDataException("Invalid Ghoul creature state.");
        }
    }

    private static bool IsFinite(Vector3 value) {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}

public sealed class GhoulTartareosityState {
    public int WorldType;
    public bool IsBossDefeated;
    public Point3 ExistGranulation;
    public double ExistGranulationSpawnTime;
    public bool HasCorpsificeSpawnData;
    public double GameTimeToSpawn;
    public int CorpsificeWorldType;
    public int CorpsificeState;
    public Point3 CorpsificePosition;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref WorldType);
        archive.Value(ref IsBossDefeated);
        archive.Value(ref ExistGranulation);
        archive.Value(ref ExistGranulationSpawnTime);
        archive.Value(ref HasCorpsificeSpawnData);
        archive.Value(ref GameTimeToSpawn);
        archive.Value(ref CorpsificeWorldType);
        archive.Value(ref CorpsificeState);
        archive.Value(ref CorpsificePosition);

        if (!double.IsFinite(ExistGranulationSpawnTime)
            && !double.IsPositiveInfinity(ExistGranulationSpawnTime)
            || !double.IsFinite(GameTimeToSpawn)) {
            throw new InvalidDataException("Invalid Ghoul Tartareosity state.");
        }
    }
}

public sealed class GhoulStateSnapshot {
    public int Sequence;
    public bool SAGEnable;
    public int Difficulty;
    public int PeriodLengthMultiplier;
    public bool IsOptionDecided;
    public int NourishmentAcquiredCount;
    public int BossDataCount;
    public GhoulTartareosityState Tartareosity = new();
    public List<GhoulPlayerState> Players = [];
    public List<GhoulCreatureState> Creatures = [];

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
        archive.Value(ref SAGEnable);
        archive.Value(ref Difficulty);
        archive.Value(ref PeriodLengthMultiplier);
        archive.Value(ref IsOptionDecided);
        archive.Value(ref NourishmentAcquiredCount);
        archive.Value(ref BossDataCount);
        Tartareosity ??= new GhoulTartareosityState();
        Tartareosity.Serialize(archive);

        int playerCount = archive.IsWriting ? Players.Count : 0;
        archive.Value(ref playerCount);
        if (playerCount < 0 || playerCount > GhoulCompatNetPayloads.MaximumPlayers) {
            throw new InvalidDataException("Invalid Ghoul player count.");
        }
        if (archive.IsReading) {
            Players.Clear();
            for (int i = 0; i < playerCount; i++) {
                GhoulPlayerState player = new();
                player.Serialize(archive);
                Players.Add(player);
            }
        }
        else {
            foreach (GhoulPlayerState player in Players) {
                player.Serialize(archive);
            }
        }

        int creatureCount = archive.IsWriting ? Creatures.Count : 0;
        archive.Value(ref creatureCount);
        if (creatureCount < 0 || creatureCount > GhoulCompatNetPayloads.MaximumCreatures) {
            throw new InvalidDataException("Invalid Ghoul creature count.");
        }
        if (archive.IsReading) {
            Creatures.Clear();
            for (int i = 0; i < creatureCount; i++) {
                GhoulCreatureState creature = new();
                creature.Serialize(archive);
                Creatures.Add(creature);
            }
        }
        else {
            foreach (GhoulCreatureState creature in Creatures) {
                creature.Serialize(archive);
            }
        }
    }
}

public static class GhoulCompatNetPayloads {
    public const int MaximumPlayers = 32;
    public const int MaximumCreatures = 256;
    public const int MaximumEntityNameLength = 128;
    public const int MaximumClothingSlots = 32;
    public const int MaximumClothingItemsPerSlot = 32;

    public static byte[] Write(GhoulStateSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        return CompatNetPayload.Write(snapshot.Serialize);
    }

    public static GhoulStateSnapshot Read(byte[] payload) {
        ArgumentNullException.ThrowIfNull(payload);
        GhoulStateSnapshot snapshot = new();
        CompatNetPayload.Read(payload, snapshot.Serialize);
        return snapshot;
    }
}
