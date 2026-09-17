using System.Collections;
using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Server-authoritative state bridge for Ghoul. The optional assembly is only
/// accessed through reflection so CompatNet remains loadable without the mod.
/// </summary>
public static class GhoulCompatNetRuntime {
    public const string TargetPackageName = "saghoulsmod";
    public const string TargetAssemblyName = "sc-saghoulmod.dll";

    private const string SAGSubsystemName = "SAGhoul.Subsystems.SubsystemSAG";
    private const string TartareositySubsystemName = "SAGhoul.Tartareosity.SubsystemTartareosity";
    private const string SAGPlayerComponentName = "SAGhoul.Components.ComponentSAGPlayer";
    private const string ClothingMassComponentName = "SAGhoul.Components.ComponentClothingMass";
    private const string GhoulComponentName = "SAGhoul.Components.ComponentSAGhouls";
    private const double PublishInterval = 0.01;

    private static double s_nextPublishTime;
    private static int s_sequence;
    private static int s_lastAppliedSequence;

    public static void Reset() {
        s_nextPublishTime = 0;
        s_sequence = 0;
        s_lastAppliedSequence = 0;
    }

    public static bool IsAvailable() {
        Project project = GameManager.Project;
        return project != null
            && (FindSubsystem(project, SAGSubsystemName) != null
                || project.Entities.Any(entity =>
                    FindComponent(entity, SAGPlayerComponentName) != null
                    || FindComponent(entity, GhoulComponentName) != null));
    }

    public static void Publish() {
        if (!NetworkManager.IsServerRunning
            || GameManager.Project == null
            || Time.RealTime < s_nextPublishTime) {
            return;
        }

        s_nextPublishTime = Time.RealTime + PublishInterval;
        GhoulStateSnapshot snapshot = Capture();
        snapshot.Sequence = NextSequence(s_sequence);
        s_sequence = snapshot.Sequence;
        CompatNetCore.QueueBroadcast(
            GhoulCompatNetAdapter.StableAdapterId,
            (ushort)GhoulCompatNetOperation.StateSnapshot,
            GhoulCompatNetPayloads.Write(snapshot));
    }

    public static void Apply(byte[] payload) {
        GhoulStateSnapshot snapshot;
        try {
            snapshot = GhoulCompatNetPayloads.Read(payload);
        }
        catch {
            return;
        }

        if (snapshot.Sequence <= 0
            || (s_lastAppliedSequence != 0
                && !IsSequenceAtLeast(snapshot.Sequence, s_lastAppliedSequence))) {
            return;
        }
        s_lastAppliedSequence = snapshot.Sequence;

        Project project = GameManager.Project;
        if (project == null) {
            return;
        }

        ApplySubsystemState(project, snapshot);
        ApplyPlayers(project, snapshot);
        ApplyTartareosity(project, snapshot.Tartareosity);
        ApplyCreatures(project, snapshot.Creatures);
    }

    private static GhoulStateSnapshot Capture() {
        GhoulStateSnapshot snapshot = new();
        Project project = GameManager.Project;
        if (project == null) {
            return snapshot;
        }

        object sag = FindSubsystem(project, SAGSubsystemName);
        if (sag != null) {
            snapshot.SAGEnable = GetBool(sag, "SAGEnable");
            snapshot.Difficulty = GetInt(sag, "Difficulty");
            snapshot.PeriodLengthMultiplier = GetInt(sag, "PeriodLengthMultiplier");
            snapshot.IsOptionDecided = GetBool(sag, "IsOptionDecided");
            snapshot.NourishmentAcquiredCount = GetInt(sag, "NourishmentAcquiredCount");
            snapshot.BossDataCount = GetCollectionCount(GetMember(sag, "BossData"));
        }

        foreach (Entity entity in project.Entities) {
            ComponentPlayer player = entity.FindComponent<ComponentPlayer>();
            if (player?.PlayerData != null) {
                CapturePlayer(snapshot, player);
            }

            Component ghoul = FindComponent(entity, GhoulComponentName);
            if (ghoul != null && player == null && TryCaptureCreature(ghoul, out GhoulCreatureState creature)) {
                if (snapshot.Creatures.Count < GhoulCompatNetPayloads.MaximumCreatures) {
                    snapshot.Creatures.Add(creature);
                }
            }
        }

        object tartareosity = FindSubsystem(project, TartareositySubsystemName);
        if (tartareosity != null) {
            CaptureTartareosity(snapshot.Tartareosity, tartareosity);
        }
        return snapshot;
    }

    private static void CapturePlayer(GhoulStateSnapshot snapshot, ComponentPlayer player) {
        Component sagPlayer = FindComponent(player.Entity, SAGPlayerComponentName);
        Component clothing = FindComponent(player.Entity, ClothingMassComponentName);
        if (sagPlayer == null && clothing == null) {
            return;
        }

        GhoulPlayerState state = new() {
            PlayerIndex = player.PlayerData.PlayerIndex
        };
        if (sagPlayer != null) {
            state.SoulsValue = GetFloat(sagPlayer, "SoulsValue");
            state.SpawnCd = GetFloat(sagPlayer, "SpawnCd");
            state.HealProhibitTime = GetDouble(sagPlayer, "HealProhibitTime");
            state.InAirTimeSum = GetFloat(sagPlayer, "InAirTimeSum");
            state.EarthquakeDamage = GetFloat(sagPlayer, "EarthquakeDamage");
        }
        if (clothing != null) {
            state.ArmorProtection = GetFloat(clothing, "ArmorProtection");
            state.HasFullSet = GetBool(clothing, "HasFullSet");
            state.RecoveryTo1Time = GetFloat(clothing, "RecoveryTo1Time");
            state.IsAcArmorEquipped = InvokeBool(clothing, "IsAcArmorEquipped");
        }
        CaptureClothing(player.ComponentClothing, state);
        snapshot.Players.Add(state);
    }

    private static void CaptureClothing(
        ComponentClothing clothing,
        GhoulPlayerState state) {
        if (clothing == null) {
            return;
        }
        foreach (ClothingSlot slot in ClothingSlot.ClothingSlots.Values) {
            state.Clothing.Add(new GhoulClothingSlotState {
                SlotIndex = (int)slot,
                Values = [.. clothing.GetClothes(slot)]
            });
        }
    }

    private static void CaptureTartareosity(
        GhoulTartareosityState state,
        object tartareosity) {
        state.WorldType = GetInt(tartareosity, "WorldType");
        state.IsBossDefeated = GetBool(tartareosity, "IsBossDefeated");
        state.ExistGranulation = GetPoint3(tartareosity, "ExistGranulation");
        state.ExistGranulationSpawnTime = GetDouble(tartareosity, "ExistGranulationSpawnTime");

        object spawnData = GetMember(tartareosity, "CorpsificeSpawnData");
        if (spawnData == null) {
            state.ExistGranulationSpawnTime = double.IsFinite(state.ExistGranulationSpawnTime)
                ? state.ExistGranulationSpawnTime
                : double.MaxValue;
            return;
        }

        state.HasCorpsificeSpawnData = true;
        state.GameTimeToSpawn = GetDouble(spawnData, "GameTimeToSpawn");
        state.CorpsificeWorldType = GetInt(spawnData, "WorldType");
        state.CorpsificeState = GetInt(spawnData, "State");
        state.CorpsificePosition = GetPoint3(spawnData, "Position");
    }

    private static bool TryCaptureCreature(
        Component ghoul,
        out GhoulCreatureState state) {
        state = new GhoulCreatureState {
            EntityName = GetString(ghoul, "EntityName")
        };
        if (string.IsNullOrWhiteSpace(state.EntityName)) {
            return false;
        }

        ComponentBody body = ghoul.Entity?.FindComponent<ComponentBody>();
        if (body != null) {
            state.HasPosition = true;
            state.Position = body.Position;
        }
        state.IsBoss = GetBool(ghoul, "IsBoss");
        state.IsCommonFrank = GetBool(ghoul, "IsCommonFrank");
        state.PersistentAttack = GetBool(ghoul, "PersistentAttack");
        state.IsTough = GetBool(ghoul, "IsTough");
        return true;
    }

    private static void ApplySubsystemState(Project project, GhoulStateSnapshot snapshot) {
        object sag = FindSubsystem(project, SAGSubsystemName);
        if (sag == null) {
            return;
        }
        SetMember(sag, "SAGEnable", snapshot.SAGEnable);
        SetMember(sag, "Difficulty", snapshot.Difficulty);
        SetMember(sag, "PeriodLengthMultiplier", snapshot.PeriodLengthMultiplier);
        SetMember(sag, "IsOptionDecided", snapshot.IsOptionDecided);
        SetMember(sag, "NourishmentAcquiredCount", snapshot.NourishmentAcquiredCount);
    }

    private static void ApplyPlayers(Project project, GhoulStateSnapshot snapshot) {
        foreach (GhoulPlayerState state in snapshot.Players) {
            ComponentPlayer player = project.Entities
                .Select(entity => entity.FindComponent<ComponentPlayer>())
                .FirstOrDefault(candidate => candidate?.PlayerData?.PlayerIndex == state.PlayerIndex);
            if (player == null) {
                continue;
            }

            Component sagPlayer = FindComponent(player.Entity, SAGPlayerComponentName);
            Component clothing = FindComponent(player.Entity, ClothingMassComponentName);
            if (sagPlayer != null) {
                SetMember(sagPlayer, "SoulsValue", state.SoulsValue);
                SetMember(sagPlayer, "SpawnCd", state.SpawnCd);
                SetMember(sagPlayer, "HealProhibitTime", state.HealProhibitTime);
                SetMember(sagPlayer, "InAirTimeSum", state.InAirTimeSum);
                SetMember(sagPlayer, "EarthquakeDamage", state.EarthquakeDamage);
            }
            if (clothing != null) {
                SetMember(clothing, "ArmorProtection", state.ArmorProtection);
                SetMember(clothing, "HasFullSet", state.HasFullSet);
                SetMember(clothing, "RecoveryTo1Time", state.RecoveryTo1Time);
                ApplyClothing(player.ComponentClothing, state.Clothing);
            }
        }
    }

    private static void ApplyClothing(
        ComponentClothing clothing,
        List<GhoulClothingSlotState> states) {
        foreach (GhoulClothingSlotState state in states ?? []) {
            if (!ClothingSlot.ClothingSlotsByInt.TryGetValue(
                    state.SlotIndex,
                    out ClothingSlot slot)) {
                continue;
            }
            try {
                clothing.SetClothes(slot, state.Values);
            }
            catch {
            }
        }
    }

    private static void ApplyTartareosity(Project project, GhoulTartareosityState state) {
        object tartareosity = FindSubsystem(project, TartareositySubsystemName);
        if (tartareosity == null || state == null) {
            return;
        }

        SetMember(tartareosity, "WorldType", state.WorldType);
        SetMember(tartareosity, "IsBossDefeated", state.IsBossDefeated);
        SetMember(tartareosity, "ExistGranulation", state.ExistGranulation);
        SetMember(tartareosity, "ExistGranulationSpawnTime", state.ExistGranulationSpawnTime);

        if (!state.HasCorpsificeSpawnData) {
            SetMember(tartareosity, "CorpsificeSpawnData", null);
            return;
        }

        Type spawnType = tartareosity.GetType().Assembly.GetType(
            "SAGhoul.Tartareosity.CorpsificeSpawnData",
            throwOnError: false);
        if (spawnType == null) {
            return;
        }

        Type worldType = spawnType.Assembly.GetType("SAGhoul.Tartareosity.WorldType", false);
        object world = ConvertToTargetType(state.CorpsificeWorldType, worldType);
        ConstructorInfo constructor = spawnType.GetConstructor([typeof(double), worldType, typeof(Point3)]);
        if (constructor == null) {
            return;
        }

        object spawnData = constructor.Invoke([state.GameTimeToSpawn, world, state.CorpsificePosition]);
        SetMember(spawnData, "State", state.CorpsificeState);
        SetMember(tartareosity, "CorpsificeSpawnData", spawnData);
    }

    private static void ApplyCreatures(Project project, List<GhoulCreatureState> states) {
        List<GhoulCreatureState> remaining = states ?? [];
        foreach (Entity entity in project.Entities.ToArray()) {
            if (entity.FindComponent<ComponentPlayer>() != null) {
                continue;
            }
            Component ghoul = FindComponent(entity, GhoulComponentName);
            if (ghoul == null) {
                continue;
            }

            int matchIndex = FindCreatureMatch(entity, ghoul, remaining);
            if (matchIndex < 0) {
                project.RemoveEntity(entity, true);
                continue;
            }

            GhoulCreatureState state = remaining[matchIndex];
            remaining.RemoveAt(matchIndex);
            SetMember(ghoul, "IsBoss", state.IsBoss);
            SetMember(ghoul, "IsCommonFrank", state.IsCommonFrank);
            SetMember(ghoul, "PersistentAttack", state.PersistentAttack);
            SetMember(ghoul, "IsTough", state.IsTough);
        }
    }

    private static int FindCreatureMatch(
        Entity entity,
        Component ghoul,
        List<GhoulCreatureState> states) {
        string name = GetString(ghoul, "EntityName");
        ComponentBody body = entity.FindComponent<ComponentBody>();
        int bestIndex = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < states.Count; i++) {
            GhoulCreatureState state = states[i];
            if (!string.Equals(state.EntityName, name, StringComparison.Ordinal)) {
                continue;
            }
            float distance = body != null && state.HasPosition
                ? Vector3.DistanceSquared(body.Position, state.Position)
                : 0f;
            if (body != null && state.HasPosition && distance > 16f) {
                continue;
            }
            if (distance < bestDistance) {
                bestDistance = distance;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    private static object FindSubsystem(Project project, string fullName) {
        return project.Subsystems.FirstOrDefault(subsystem =>
            string.Equals(subsystem.GetType().FullName, fullName, StringComparison.Ordinal));
    }

    private static Component FindComponent(Entity entity, string fullName) {
        return entity?.Components.FirstOrDefault(component =>
            string.Equals(component.GetType().FullName, fullName, StringComparison.Ordinal));
    }

    private static object GetMember(object instance, string name) {
        if (instance == null) {
            return null;
        }
        for (Type type = instance.GetType(); type != null; type = type.BaseType) {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field.GetValue(instance);
            }
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property?.GetMethod != null) {
                return property.GetValue(instance);
            }
        }
        return null;
    }

    private static void SetMember(object instance, string name, object value) {
        if (instance == null) {
            return;
        }
        for (Type type = instance.GetType(); type != null; type = type.BaseType) {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null && !field.IsInitOnly) {
                try {
                    field.SetValue(instance, ConvertToTargetType(value, field.FieldType));
                }
                catch {
                }
                return;
            }
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            MethodInfo setter = property?.SetMethod;
            if (setter != null) {
                try {
                    setter.Invoke(instance, [ConvertToTargetType(value, property.PropertyType)]);
                }
                catch {
                }
                return;
            }
        }
    }

    private static object ConvertToTargetType(object value, Type targetType) {
        if (targetType == null || value == null) {
            return value;
        }
        Type nullableType = Nullable.GetUnderlyingType(targetType);
        Type actualType = nullableType ?? targetType;
        if (actualType.IsInstanceOfType(value)) {
            return value;
        }
        if (actualType.IsEnum) {
            return Enum.ToObject(actualType, value);
        }
        return Convert.ChangeType(value, actualType, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetString(object instance, string name) {
        return GetMember(instance, name) as string ?? string.Empty;
    }

    private static bool GetBool(object instance, string name) {
        return GetMember(instance, name) is bool value && value;
    }

    private static bool InvokeBool(object instance, string name) {
        MethodInfo method = instance?.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        try {
            return method?.Invoke(instance, null) is bool result && result;
        }
        catch {
            return false;
        }
    }

    private static int GetInt(object instance, string name) {
        object value = GetMember(instance, name);
        try {
            return value == null ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch {
            return 0;
        }
    }

    private static float GetFloat(object instance, string name) {
        object value = GetMember(instance, name);
        try {
            return value == null ? 0f : Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch {
            return 0f;
        }
    }

    private static double GetDouble(object instance, string name) {
        object value = GetMember(instance, name);
        try {
            return value == null ? 0d : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch {
            return 0d;
        }
    }

    private static Point3 GetPoint3(object instance, string name) {
        return GetMember(instance, name) is Point3 value ? value : Point3.Zero;
    }

    private static int GetCollectionCount(object value) {
        return value is ICollection collection ? collection.Count : 0;
    }

    private static int NextSequence(int value) {
        value++;
        return value <= 0 ? 1 : value;
    }

    private static bool IsSequenceAtLeast(int value, int previous) {
        return value == previous || (value > previous && value - previous < int.MaxValue / 2)
            || (value < previous && previous - value > int.MaxValue / 2);
    }
}
