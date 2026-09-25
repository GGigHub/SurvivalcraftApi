using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Replicates custom IInventory implementations that are not covered by the
/// vanilla InventorySyncPacket. The protocol is deliberately a complete,
/// bounded slot image and applies only to an explicitly configured assembly.
/// </summary>
public static class CompatNetInventoryRuntime {
    private const double PublishInterval = 0.35;
    private const double FullRefreshInterval = 5.0;
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Publish(
        ushort adapterId,
        string assemblyName,
        bool enabled,
        ref double nextPublishTime,
        ref double nextFullRefreshTime,
        ref int sequence,
        Dictionary<string, byte[]> lastPayloads) {
        if (!enabled || !NetworkManager.IsServerRunning || GameManager.Project == null) {
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

        List<CompatNetInventoryState> changed = [];
        foreach (Entity entity in GameManager.Project.Entities) {
            if (entity == null) {
                continue;
            }
            foreach (Component component in entity.Components) {
                if (!IsTargetComponent(component, assemblyName)
                    || !TryCapture(component, out CompatNetInventoryState state)) {
                    continue;
                }

                byte[] statePayload = CompatNetInventoryPayloads.Write(
                    new CompatNetInventoryStateBatch {
                        Sequence = 0,
                        FullRefresh = false,
                        Records = [state]
                    });
                string key = $"{state.EntityId}:{state.ComponentTypeName}";
                if (fullRefresh
                    || !lastPayloads.TryGetValue(key, out byte[] previous)
                    || !previous.AsSpan().SequenceEqual(statePayload)) {
                    lastPayloads[key] = statePayload;
                    changed.Add(state);
                    if (changed.Count >= CompatNetInventoryPayloads.MaximumRecordsPerBatch) {
                        break;
                    }
                }
            }
            if (changed.Count >= CompatNetInventoryPayloads.MaximumRecordsPerBatch) {
                break;
            }
        }

        if (changed.Count < CompatNetInventoryPayloads.MaximumRecordsPerBatch) {
            foreach (Subsystem subsystem in GameManager.Project.Subsystems) {
                if (!IsTargetSubsystem(subsystem, assemblyName)
                    || subsystem is not IInventory inventory
                    || !TryCapture(subsystem, inventory, out CompatNetInventoryState state)) {
                    continue;
                }

                byte[] statePayload = CompatNetInventoryPayloads.Write(
                    new CompatNetInventoryStateBatch {
                        Sequence = 0,
                        FullRefresh = false,
                        Records = [state]
                    });
                string key = $"subsystem:{state.ComponentTypeName}";
                if (fullRefresh
                    || !lastPayloads.TryGetValue(key, out byte[] previous)
                    || !previous.AsSpan().SequenceEqual(statePayload)) {
                    lastPayloads[key] = statePayload;
                    changed.Add(state);
                    if (changed.Count >= CompatNetInventoryPayloads.MaximumRecordsPerBatch) {
                        break;
                    }
                }
            }
        }

        if (changed.Count == 0) {
            return;
        }

        sequence = sequence == int.MaxValue ? 1 : sequence + 1;
        QueueBatches(adapterId, changed, sequence, fullRefresh);
    }

    public static void Apply(
        byte[] payload,
        string assemblyName,
        ref int lastAppliedSequence) {
        CompatNetInventoryStateBatch batch;
        try {
            batch = CompatNetInventoryPayloads.Read(payload);
        }
        catch {
            return;
        }

        if (batch.Sequence <= 0
            || (lastAppliedSequence != 0
                && !IsSequenceAtLeast(batch.Sequence, lastAppliedSequence))) {
            return;
        }
        if (IsSequenceNewer(batch.Sequence, lastAppliedSequence)) {
            lastAppliedSequence = batch.Sequence;
        }

        foreach (CompatNetInventoryState state in batch.Records) {
            if (state == null) {
                continue;
            }
            if (state.IsSubsystem) {
                Subsystem subsystem = GameManager.Project?.Subsystems?.FirstOrDefault(candidate =>
                    candidate is IInventory
                    && IsTargetSubsystem(candidate, assemblyName)
                    && string.Equals(
                        candidate.GetType().FullName,
                        state.ComponentTypeName,
                        StringComparison.Ordinal));
                if (subsystem is IInventory subsystemInventory) {
                    ApplyState(subsystem, subsystemInventory, state);
                }
                continue;
            }

            if (state.EntityId <= 0) {
                continue;
            }
            Entity entity = GameManager.Project?.FindEntity(state.EntityId);
            Component component = entity?.Components.FirstOrDefault(candidate =>
                IsTargetComponent(candidate, assemblyName)
                && string.Equals(
                    candidate.GetType().FullName,
                    state.ComponentTypeName,
                    StringComparison.Ordinal));
            if (component is IInventory inventory) {
                ApplyState(component, inventory, state);
            }
        }
    }

    private static bool IsTargetComponent(Component component, string assemblyName) {
        return component?.Entity != null
            && component is IInventory
            && IsTargetAssembly(component.GetType().Assembly, assemblyName);
    }

    private static bool IsTargetSubsystem(Subsystem subsystem, string assemblyName) {
        return subsystem != null
            && IsTargetAssembly(subsystem.GetType().Assembly, assemblyName);
    }

    private static bool TryCapture(
        Component component,
        out CompatNetInventoryState state) {
        state = new CompatNetInventoryState {
            EntityId = component.Entity.Id,
            ComponentTypeName = component.GetType().FullName ?? string.Empty
        };
        return component is IInventory inventory
            && TryCapture(component, inventory, out state);
    }

    private static bool TryCapture(
        object owner,
        IInventory inventory,
        out CompatNetInventoryState state) {
        state = new CompatNetInventoryState {
            EntityId = owner is Component component ? component.Entity.Id : 0,
            IsSubsystem = owner is Subsystem,
            ComponentTypeName = owner?.GetType().FullName ?? string.Empty
        };
        if ((!state.IsSubsystem && state.EntityId <= 0)
            || (state.IsSubsystem && state.EntityId != 0)
            || string.IsNullOrWhiteSpace(state.ComponentTypeName)) {
            return false;
        }

        try {
            int slots = inventory.SlotsCount;
            if (slots <= 0 || slots > CompatNetInventoryPayloads.MaximumSlotsPerInventory) {
                return false;
            }
            for (int i = 0; i < slots; i++) {
                int value = inventory.GetSlotValue(i);
                int count = inventory.GetSlotCount(i);
                if (value < 0 || count < 0) {
                    return false;
                }
                state.Slots.Add(new CompatNetInventorySlotState {
                    Value = value,
                    Count = count
                });
            }
            return state.Slots.Count == slots;
        }
        catch {
            return false;
        }
    }

    private static void ApplyState(
        object component,
        IInventory inventory,
        CompatNetInventoryState state) {
        try {
            if (state.Slots.Count != inventory.SlotsCount) {
                return;
            }
            for (int i = 0; i < state.Slots.Count; i++) {
                CompatNetInventorySlotState desired = state.Slots[i];
                if (desired == null) {
                    continue;
                }
                int currentValue = inventory.GetSlotValue(i);
                int currentCount = inventory.GetSlotCount(i);
                if (currentValue == desired.Value && currentCount == desired.Count) {
                    continue;
                }

                if (component is ComponentInventoryBase baseInventory) {
                    baseInventory.SetSlotValue(i, new ComponentInventoryBase.Slot {
                        Value = desired.Value,
                        Count = desired.Count
                    });
                    continue;
                }

                if (TryInvokeCustomSetSlotValue(component, i, desired)) {
                    continue;
                }

                if (currentCount > 0) {
                    inventory.RemoveSlotItems(i, currentCount);
                }
                if (desired.Count > 0 && desired.Value > 0) {
                    inventory.AddSlotItems(i, desired.Value, desired.Count);
                }
            }
        }
        catch {
        }
    }

    private static bool TryInvokeCustomSetSlotValue(
        object component,
        int slotIndex,
        CompatNetInventorySlotState desired) {
        MethodInfo method = component?.GetType().GetMethods(InstanceFlags)
            .FirstOrDefault(candidate =>
                candidate.Name == "SetSlotValue"
                && candidate.GetParameters().Length == 2
                && candidate.GetParameters()[0].ParameterType == typeof(int));
        if (method == null) {
            return false;
        }

        ParameterInfo parameter = method.GetParameters()[1];
        if (!TryCreateCustomSlotValue(component, parameter.ParameterType, desired, out object value)) {
            return false;
        }
        try {
            method.Invoke(component, [slotIndex, value]);
            return true;
        }
        catch {
            return false;
        }
    }

    private static bool TryCreateCustomSlotValue(
        object owner,
        Type parameterType,
        CompatNetInventorySlotState desired,
        out object value) {
        value = desired.Value;
        if (parameterType != typeof(object)) {
            return parameterType.IsInstanceOfType(value);
        }

        Type slotType = owner?.GetType().GetNestedType(
            "Slot",
            BindingFlags.Public | BindingFlags.NonPublic);
        if (slotType == null
            || slotType.IsEnum
            || slotType.IsAbstract
            || slotType.GetConstructor(Type.EmptyTypes) == null) {
            return true;
        }

        try {
            object slot = Activator.CreateInstance(slotType);
            FieldInfo valueField = slotType.GetField("Value", InstanceFlags);
            FieldInfo countField = slotType.GetField("Count", InstanceFlags);
            PropertyInfo valueProperty = slotType.GetProperty("Value", InstanceFlags);
            PropertyInfo countProperty = slotType.GetProperty("Count", InstanceFlags);
            if (valueField?.FieldType == typeof(int)
                && countField?.FieldType == typeof(int)) {
                valueField.SetValue(slot, desired.Value);
                countField.SetValue(slot, desired.Count);
                value = slot;
                return true;
            }
            if (valueProperty?.PropertyType == typeof(int)
                && valueProperty.SetMethod != null
                && countProperty?.PropertyType == typeof(int)
                && countProperty.SetMethod != null) {
                valueProperty.SetValue(slot, desired.Value);
                countProperty.SetValue(slot, desired.Count);
                value = slot;
                return true;
            }
        }
        catch {
        }
        return true;
    }

    private static void QueueBatches(
        ushort adapterId,
        List<CompatNetInventoryState> records,
        int sequence,
        bool fullRefresh) {
        List<CompatNetInventoryState> current = [];
        foreach (CompatNetInventoryState record in records) {
            current.Add(record);
            byte[] payload = CompatNetInventoryPayloads.Write(
                new CompatNetInventoryStateBatch {
                    Sequence = sequence,
                    FullRefresh = fullRefresh,
                    Records = current
                });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                continue;
            }

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0) {
                QueueBatch(adapterId, current, sequence, fullRefresh);
            }
            current = [record];
        }
        if (current.Count > 0) {
            QueueBatch(adapterId, current, sequence, fullRefresh);
        }
    }

    private static void QueueBatch(
        ushort adapterId,
        List<CompatNetInventoryState> records,
        int sequence,
        bool fullRefresh) {
        byte[] payload = CompatNetInventoryPayloads.Write(
            new CompatNetInventoryStateBatch {
                Sequence = sequence,
                FullRefresh = fullRefresh,
                Records = records
            });
        if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
            CompatNetCore.QueueBroadcast(
                adapterId,
                (ushort)CompatNetInventoryOperation.StateSnapshot,
                payload);
        }
    }

    private static bool IsTargetAssembly(Assembly assembly, string assemblyName) {
        if (assembly == null || string.IsNullOrWhiteSpace(assemblyName)) {
            return false;
        }
        return NamesEqual(assembly.GetName().Name, assemblyName)
            || NamesEqual(assembly.ManifestModule?.Name, assemblyName);
    }

    private static bool NamesEqual(string left, string right) {
        return string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);
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
