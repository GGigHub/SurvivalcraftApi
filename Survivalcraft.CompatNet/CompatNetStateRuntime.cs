using System.Reflection;
using Engine;
using GameEntitySystem;
using Game.Network;

namespace Game;

/// <summary>
/// Reflection-only state bridge for optional mods. It intentionally exposes a
/// small whitelist of scalar fields instead of serializing arbitrary object
/// graphs or calling a mod's Save/Load methods during gameplay.
/// </summary>
public static class CompatNetStateRuntime {
    private const int MaximumFieldsPerComponent = 24;
    private const int MaximumRecordsPerPublish = CompatNetStatePayloads.MaximumRecordsPerBatch;
    private const double PublishInterval = 0.2;
    private const double FullRefreshInterval = 5.0;

    private static readonly string[] s_allowedTokens = [
        "active", "affection", "air", "amount", "broken", "channel", "count",
        "cooldown", "current", "damage", "direction", "drilling", "duration",
        "energy", "enabled", "fuel", "health", "index", "level", "loaded",
        "mode", "oxygen", "power", "preparing", "pressure", "process",
        "progress", "quest", "rotation", "speed", "start", "stamina", "state", "target",
        "tank", "temperature", "timer", "turn", "velocity", "voltage", "work"
    ];

    private static readonly string[] s_blockedTokens = [
        "array", "audio", "cache", "component", "dictionary", "event", "gui",
        "last", "list", "model", "particle", "project", "random", "subsystem",
        "task", "texture", "widget"
    ];

    private static int s_sequence;

    public static void Publish(
        ushort adapterId,
        string assemblyName,
        ScopedUpdateGuardScope scope,
        bool targetLoaded,
        ref double nextPublishTime,
        ref double nextFullRefreshTime,
        Dictionary<string, byte[]> lastPayloads) {
        if (!targetLoaded
            || !NetworkManager.IsServerRunning
            || GameManager.Project == null) {
            return;
        }

        double now = Time.RealTime;
        if (now < nextPublishTime) {
            return;
        }
        nextPublishTime = now + PublishInterval;
        bool fullRefresh = now >= nextFullRefreshTime;
        if (fullRefresh) {
            nextFullRefreshTime = now + FullRefreshInterval;
        }

        List<CompatNetStateRecord> changed = [];
        foreach (Entity entity in GameManager.Project.Entities) {
            if (entity == null || !MatchesScope(entity, scope, false)) {
                continue;
            }

            foreach (Component component in entity.Components) {
                if (!IsTargetComponent(component, assemblyName)
                    || !TryCapture(component, assemblyName, out CompatNetStateRecord record)) {
                    continue;
                }

                byte[] recordPayload = CompatNetStatePayloads.Write(new CompatNetStateBatch {
                    // The sequence belongs to the outgoing batch, not to the
                    // change-detection key. Keeping it out here prevents every
                    // record from looking changed after an unrelated update.
                    Sequence = 0,
                    Records = [record]
                });
                string key = $"{record.EntityId}:{record.ComponentTypeName}";
                if (fullRefresh
                    || !lastPayloads.TryGetValue(key, out byte[] previous)
                    || !previous.AsSpan().SequenceEqual(recordPayload)) {
                    lastPayloads[key] = recordPayload;
                    changed.Add(record);
                    if (changed.Count >= MaximumRecordsPerPublish) {
                        break;
                    }
                }
            }

            if (changed.Count >= MaximumRecordsPerPublish) {
                break;
            }
        }

        if (changed.Count == 0) {
            return;
        }

        s_sequence = NextSequence(s_sequence);
        QueueBatches(adapterId, changed, s_sequence);
    }

    public static void Apply(
        byte[] payload,
        string assemblyName,
        ScopedUpdateGuardScope scope,
        ref int lastAppliedSequence) {
        CompatNetStateBatch batch = CompatNetStatePayloads.Read(payload);
        if (batch.Sequence <= 0
            || (lastAppliedSequence != 0
                && !IsSequenceAtLeast(batch.Sequence, lastAppliedSequence))
            || batch.Records.Count > CompatNetStatePayloads.MaximumRecordsPerBatch) {
            return;
        }
        if (IsSequenceNewer(batch.Sequence, lastAppliedSequence)) {
            lastAppliedSequence = batch.Sequence;
        }

        foreach (CompatNetStateRecord record in batch.Records) {
            if (record.EntityId <= 0
                || string.IsNullOrWhiteSpace(record.ComponentTypeName)) {
                continue;
            }

            Entity entity = GameManager.Project?.FindEntity(record.EntityId);
            if (!HasTargetComponent(entity, assemblyName, record.ComponentTypeName)
                || !MatchesScope(entity, scope, true)) {
                entity = FindEntityByIdentity(record, assemblyName, scope);
            }
            if (entity == null || !MatchesScope(entity, scope, true)) {
                continue;
            }

            Component component = entity.Components.FirstOrDefault(candidate =>
                IsTargetComponent(candidate, assemblyName)
                && string.Equals(
                    candidate.GetType().FullName,
                    record.ComponentTypeName,
                    StringComparison.Ordinal));
            if (component == null) {
                continue;
            }

            foreach (CompatNetStateValue value in record.Values) {
                ApplyValue(component, assemblyName, value);
            }
            ApplyBodyState(entity, record);
        }
    }

    private static void QueueBatches(
        ushort adapterId,
        List<CompatNetStateRecord> records,
        int sequence) {
        List<CompatNetStateRecord> current = [];
        foreach (CompatNetStateRecord record in records) {
            current.Add(record);
            byte[] payload = CompatNetStatePayloads.Write(new CompatNetStateBatch {
                Sequence = sequence,
                Records = current
            });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                continue;
            }

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0) {
                CompatNetCore.QueueBroadcast(
                    adapterId,
                    (ushort)CompatNetStateOperation.StateSnapshot,
                    CompatNetStatePayloads.Write(new CompatNetStateBatch {
                        Sequence = sequence,
                        Records = current
                    }));
            }
            current = [record];
        }

        if (current.Count > 0) {
            byte[] payload = CompatNetStatePayloads.Write(new CompatNetStateBatch {
                Sequence = sequence,
                Records = current
            });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                CompatNetCore.QueueBroadcast(
                    adapterId,
                    (ushort)CompatNetStateOperation.StateSnapshot,
                    payload);
            }
        }
    }

    private static bool TryCapture(
        Component component,
        string assemblyName,
        out CompatNetStateRecord record) {
        record = new CompatNetStateRecord {
            EntityId = component.Entity.Id,
            ComponentTypeName = component.GetType().FullName ?? string.Empty
        };

        ComponentBody body = component.Entity.FindComponent<ComponentBody>();
        ComponentFrame frame = component.Entity.FindComponent<ComponentFrame>();
        if (body != null) {
            record.HasEntityPosition = true;
            record.EntityPosition = body.Position;
            record.HasBodyState = true;
            record.BodyPosition = body.Position;
            record.BodyVelocity = body.Velocity;
            record.BodyRotation = body.Rotation;
        }
        else if (frame != null) {
            record.HasEntityPosition = true;
            record.EntityPosition = frame.Position;
        }

        for (Type current = component.GetType();
             current != null && current != typeof(object);
             current = current.BaseType) {
            foreach (FieldInfo field in current.GetFields(
                         BindingFlags.Instance
                         | BindingFlags.Public
                         | BindingFlags.NonPublic
                         | BindingFlags.DeclaredOnly)) {
                if (record.Values.Count >= MaximumFieldsPerComponent
                    || !IsAssemblyMatch(field.DeclaringType?.Assembly, assemblyName)
                    || !IsAllowedField(field)
                    || !TryReadValue(field, component, out CompatNetStateValue value)) {
                    continue;
                }

                record.Values.Add(value);
            }
        }

        return record.EntityId > 0
            && !string.IsNullOrEmpty(record.ComponentTypeName)
            && record.Values.Count > 0;
    }

    private static bool HasTargetComponent(
        Entity entity,
        string assemblyName,
        string componentTypeName) {
        return entity?.Components.Any(component =>
            IsTargetComponent(component, assemblyName)
            && string.Equals(
                component.GetType().FullName,
                componentTypeName,
                StringComparison.Ordinal)) == true;
    }

    private static Entity FindEntityByIdentity(
        CompatNetStateRecord record,
        string assemblyName,
        ScopedUpdateGuardScope scope) {
        if (!record.HasEntityPosition || GameManager.Project == null) {
            return null;
        }

        Entity best = null;
        float bestDistance = 4f;
        foreach (Entity candidate in GameManager.Project.Entities) {
            if (!HasTargetComponent(candidate, assemblyName, record.ComponentTypeName)
                || !MatchesScope(candidate, scope, true)) {
                continue;
            }

            ComponentBody body = candidate.FindComponent<ComponentBody>();
            ComponentFrame frame = candidate.FindComponent<ComponentFrame>();
            Vector3 position = body?.Position
                ?? frame?.Position
                ?? new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            float distance = Vector3.DistanceSquared(position, record.EntityPosition);
            if (distance <= bestDistance) {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static void ApplyBodyState(Entity entity, CompatNetStateRecord record) {
        if (!record.HasBodyState) {
            return;
        }

        ComponentBody body = entity?.FindComponent<ComponentBody>();
        if (body == null
            || !IsFinite(record.BodyPosition)
            || !IsFinite(record.BodyVelocity)) {
            return;
        }

        try {
            body.Position = record.BodyPosition;
            body.Velocity = record.BodyVelocity;
            body.Rotation = record.BodyRotation;
        }
        catch {
            // A version-skewed body implementation must not break other state.
        }
    }

    private static bool IsFinite(Vector3 value) {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }

    private static bool IsTargetComponent(Component component, string assemblyName) {
        if (component == null
            || component.Entity == null
            || component is not IUpdateable
            || !IsAssemblyMatch(component.GetType().Assembly, assemblyName)) {
            return false;
        }

        string name = component.GetType().Name;
        return !name.Contains("Gui", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("Model", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("Overlay", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesScope(Entity entity, ScopedUpdateGuardScope scope, bool isClient) {
        ComponentPlayer player = entity.FindComponent<ComponentPlayer>();
        return scope switch {
            ScopedUpdateGuardScope.RemotePlayerOnly =>
                player != null && (!isClient || player.PlayerData?.IsMainPlayer != true),
            ScopedUpdateGuardScope.RemoteEntityBody =>
                entity.FindComponent<ComponentBody>() != null
                && (!isClient || player?.PlayerData?.IsMainPlayer != true),
            ScopedUpdateGuardScope.AllNonPlayerEntities => player == null,
            _ => false
        };
    }

    private static bool IsAssemblyMatch(Assembly assembly, string assemblyName) {
        if (assembly == null) {
            return false;
        }

        string simpleName = assembly.GetName().Name;
        string moduleName = assembly.ManifestModule?.Name;
        return NamesEqual(simpleName, assemblyName) || NamesEqual(moduleName, assemblyName);
    }

    private static bool NamesEqual(string left, string right) {
        return string.Equals(
            NormalizeName(left),
            NormalizeName(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        string result = value.Replace('\\', '/');
        int separator = result.LastIndexOf('/');
        if (separator >= 0) {
            result = result[(separator + 1)..];
        }
        return result.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? result[..^4]
            : result;
    }

    private static bool IsAllowedField(FieldInfo field) {
        if (field.IsStatic
            || field.IsInitOnly
            || field.FieldType.IsPointer
            || field.FieldType.IsByRef) {
            return false;
        }

        string name = field.Name.ToLowerInvariant();
        if (s_blockedTokens.Any(name.Contains)) {
            return false;
        }
        return s_allowedTokens.Any(name.Contains);
    }

    private static bool TryReadValue(
        FieldInfo field,
        object instance,
        out CompatNetStateValue value) {
        value = new CompatNetStateValue { Name = field.Name };
        try {
            object raw = field.GetValue(instance);
            switch (raw) {
                case bool boolean:
                    value.Kind = CompatNetStateValueKind.Boolean;
                    value.BooleanValue = boolean;
                    return true;
                case byte @byte:
                    value.Kind = CompatNetStateValueKind.Byte;
                    value.ByteValue = @byte;
                    return true;
                case int integer:
                    value.Kind = CompatNetStateValueKind.Integer;
                    value.IntegerValue = integer;
                    return true;
                case float single when float.IsFinite(single):
                    value.Kind = CompatNetStateValueKind.Single;
                    value.SingleValue = single;
                    return true;
                case double @double when double.IsFinite(@double):
                    value.Kind = CompatNetStateValueKind.Double;
                    value.DoubleValue = @double;
                    return true;
                case Point3 point:
                    value.Kind = CompatNetStateValueKind.Point3;
                    value.Point3Value = point;
                    return true;
                case Vector3 vector:
                    value.Kind = CompatNetStateValueKind.Vector3;
                    value.Vector3Value = vector;
                    return true;
                case Quaternion quaternion:
                    value.Kind = CompatNetStateValueKind.Quaternion;
                    value.QuaternionValue = quaternion;
                    return true;
                default:
                    return false;
            }
        }
        catch {
            return false;
        }
    }

    private static void ApplyValue(
        Component component,
        string assemblyName,
        CompatNetStateValue value) {
        if (value == null || !IsSafeValueName(value.Name)) {
            return;
        }

        FieldInfo field = null;
        for (Type current = component.GetType();
             current != null
             && current != typeof(object)
             && IsAssemblyMatch(current.Assembly, assemblyName);
             current = current.BaseType) {
            field = current.GetField(
                value.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                break;
            }
        }

        if (field == null
            || !IsAssemblyMatch(field.DeclaringType?.Assembly, assemblyName)
            || !IsAllowedField(field)) {
            return;
        }

        try {
            object raw = value.Kind switch {
                CompatNetStateValueKind.Boolean => value.BooleanValue,
                CompatNetStateValueKind.Byte => value.ByteValue,
                CompatNetStateValueKind.Integer => value.IntegerValue,
                CompatNetStateValueKind.Single => value.SingleValue,
                CompatNetStateValueKind.Double => value.DoubleValue,
                CompatNetStateValueKind.Point3 => value.Point3Value,
                CompatNetStateValueKind.Vector3 => value.Vector3Value,
                CompatNetStateValueKind.Quaternion => value.QuaternionValue,
                _ => null
            };
            if (raw != null && field.FieldType == raw.GetType()) {
                field.SetValue(component, raw);
            }
        }
        catch {
            // A version-skewed field is intentionally ignored. Other state in
            // the same record remains useful.
        }
    }

    private static bool IsSafeValueName(string name) {
        if (string.IsNullOrEmpty(name)) {
            return false;
        }
        string lowered = name.ToLowerInvariant();
        return !s_blockedTokens.Any(lowered.Contains) && s_allowedTokens.Any(lowered.Contains);
    }

    private static int NextSequence(int sequence) {
        return sequence == int.MaxValue ? 1 : sequence + 1;
    }

    private static bool IsSequenceNewer(int sequence, int previous) {
        if (previous == 0) {
            return sequence > 0;
        }
        if (sequence == previous) {
            return false;
        }
        if (previous == int.MaxValue) {
            return sequence == 1;
        }
        if (previous < int.MaxValue / 2 && sequence > int.MaxValue / 2) {
            return false;
        }
        return sequence > previous;
    }

    private static bool IsSequenceAtLeast(int sequence, int previous) {
        return sequence == previous || IsSequenceNewer(sequence, previous);
    }
}
