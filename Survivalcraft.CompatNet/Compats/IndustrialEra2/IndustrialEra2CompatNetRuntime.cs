using System.Collections;
using System.Globalization;
using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Synchronizes Industrial Era 2's ITank state without referencing the optional
/// assembly at compile time. The mod keeps fluid contents in component-owned
/// objects, so the normal scalar snapshot cannot carry this state.
/// </summary>
public static class IndustrialEra2CompatNetRuntime {
    public const string TargetAssemblyName = "SCIENEW.dll";

    private const string VoltSubsystemTypeName = "SCIENEW.VoltNet.SubsystemVoltNet";
    private const double PublishInterval = 0.25;
    private const double FullRefreshInterval = 5.0;
    private const string TankInterfaceName = "SCIENEW.ITank";
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void PublishVolt(
        ref double nextPublishTime,
        ref double nextFullRefreshTime,
        ref int sequence,
        Dictionary<string, byte[]> lastPayloads) {
        if (!NetworkManager.IsServerRunning || GameManager.Project == null) {
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

        IDictionary elements = GetVoltElements();
        if (elements == null) {
            return;
        }

        List<IndustrialEra2VoltElementState> changed = [];
        foreach (DictionaryEntry entry in elements) {
            if (entry.Key is not Point3 position
                || !TryCaptureVoltElement(position, entry.Value, out IndustrialEra2VoltElementState state)) {
                continue;
            }

            byte[] statePayload = IndustrialEra2VoltPayloads.Write(
                new IndustrialEra2VoltStateBatch {
                    Sequence = 0,
                    FullRefresh = false,
                    Records = [state]
                });
            string key = $"{position.X}:{position.Y}:{position.Z}";
            if (fullRefresh
                || !lastPayloads.TryGetValue(key, out byte[] previous)
                || !previous.AsSpan().SequenceEqual(statePayload)) {
                lastPayloads[key] = statePayload;
                changed.Add(state);
                if (changed.Count >= IndustrialEra2VoltPayloads.MaximumRecordsPerBatch) {
                    break;
                }
            }
        }

        if (changed.Count == 0) {
            return;
        }

        sequence = NextSequence(sequence);
        QueueVoltBatches(changed, sequence, fullRefresh);
    }

    public static void ApplyVolt(byte[] payload, ref int lastAppliedSequence) {
        IndustrialEra2VoltStateBatch batch;
        try {
            batch = IndustrialEra2VoltPayloads.Read(payload);
        }
        catch {
            return;
        }

        if (batch.Sequence <= 0
            || (lastAppliedSequence != 0
                && !IsSequenceAtLeast(batch.Sequence, lastAppliedSequence))
            || batch.Records.Count > IndustrialEra2VoltPayloads.MaximumRecordsPerBatch) {
            return;
        }

        if (IsSequenceNewer(batch.Sequence, lastAppliedSequence)) {
            lastAppliedSequence = batch.Sequence;
        }

        IDictionary elements = GetVoltElements();
        if (elements == null) {
            return;
        }

        foreach (IndustrialEra2VoltElementState state in batch.Records) {
            if (state == null
                || string.IsNullOrWhiteSpace(state.ElementTypeName)
                || state.ElementTypeName.Length > IndustrialEra2VoltPayloads.MaximumElementTypeNameLength
                || !elements.Contains(state.Position)) {
                continue;
            }

            object element = elements[state.Position];
            if (element == null
                || !string.Equals(
                    element.GetType().FullName,
                    state.ElementTypeName,
                    StringComparison.Ordinal)) {
                continue;
            }

            foreach (CompatNetStateValue value in state.Values) {
                ApplyVoltValue(element, value);
            }
        }
    }

    public static void Publish(
        ref double nextPublishTime,
        ref double nextFullRefreshTime,
        ref int sequence,
        Dictionary<string, byte[]> lastPayloads) {
        if (!NetworkManager.IsServerRunning || GameManager.Project == null) {
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

        List<IndustrialEra2TankState> changed = [];
        foreach (Entity entity in GameManager.Project.Entities) {
            if (entity == null || entity.FindComponent<ComponentPlayer>() != null) {
                continue;
            }

            foreach (Component component in entity.Components) {
                if (!IsTargetComponent(component)
                    || !TryCapture(component, out IndustrialEra2TankState state)) {
                    continue;
                }

                byte[] statePayload = IndustrialEra2CompatNetPayloads.Write(
                    new IndustrialEra2TankStateBatch {
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
                    if (changed.Count >= IndustrialEra2CompatNetPayloads.MaximumRecordsPerBatch) {
                        break;
                    }
                }
            }

            if (changed.Count >= IndustrialEra2CompatNetPayloads.MaximumRecordsPerBatch) {
                break;
            }
        }

        if (changed.Count == 0) {
            return;
        }

        sequence = NextSequence(sequence);
        QueueBatches(changed, sequence, fullRefresh);
    }

    public static void Apply(byte[] payload, ref int lastAppliedSequence) {
        IndustrialEra2TankStateBatch batch;
        try {
            batch = IndustrialEra2CompatNetPayloads.Read(payload);
        }
        catch {
            return;
        }

        if (batch.Sequence <= 0
            || (lastAppliedSequence != 0
                && !IsSequenceAtLeast(batch.Sequence, lastAppliedSequence))
            || batch.Records.Count > IndustrialEra2CompatNetPayloads.MaximumRecordsPerBatch) {
            return;
        }

        if (IsSequenceNewer(batch.Sequence, lastAppliedSequence)) {
            lastAppliedSequence = batch.Sequence;
        }

        foreach (IndustrialEra2TankState state in batch.Records) {
            if (state == null
                || state.EntityId <= 0
                || string.IsNullOrWhiteSpace(state.ComponentTypeName)
                || state.ComponentTypeName.Length > IndustrialEra2CompatNetPayloads.MaximumComponentTypeNameLength) {
                continue;
            }

            Entity entity = GameManager.Project?.FindEntity(state.EntityId);
            if (entity == null || entity.FindComponent<ComponentPlayer>() != null) {
                continue;
            }

            Component component = entity.Components.FirstOrDefault(candidate =>
                IsTargetComponent(candidate)
                && string.Equals(
                    candidate.GetType().FullName,
                    state.ComponentTypeName,
                    StringComparison.Ordinal));
            if (component != null) {
                ApplyState(component, state);
            }
        }
    }

    private static IDictionary GetVoltElements() {
        object subsystem = GameManager.Project?.Subsystems?.FirstOrDefault(candidate =>
            candidate != null
            && string.Equals(
                candidate.GetType().FullName,
                VoltSubsystemTypeName,
                StringComparison.Ordinal));
        return GetMemberValue(subsystem, "m_elements") as IDictionary;
    }

    private static bool TryCaptureVoltElement(
        Point3 position,
        object element,
        out IndustrialEra2VoltElementState state) {
        state = new IndustrialEra2VoltElementState {
            Position = position,
            ElementTypeName = element?.GetType().FullName ?? string.Empty
        };
        if (element == null
            || state.ElementTypeName.Length == 0
            || state.ElementTypeName.Length > IndustrialEra2VoltPayloads.MaximumElementTypeNameLength) {
            return false;
        }

        HashSet<string> capturedNames = [];
        for (Type current = element.GetType();
             current != null && current != typeof(object);
             current = current.BaseType) {
            foreach (FieldInfo field in current.GetFields(
                         BindingFlags.Instance
                         | BindingFlags.Public
                         | BindingFlags.NonPublic
                         | BindingFlags.DeclaredOnly)) {
                if (state.Values.Count >= IndustrialEra2VoltPayloads.MaximumValuesPerElement
                    || capturedNames.Contains(field.Name)
                    || !IsTargetAssembly(field.DeclaringType?.Assembly)
                    || !IsVoltStateMemberName(field.Name)
                    || field.IsStatic
                    || field.FieldType.IsPointer
                    || field.FieldType.IsByRef
                    || !TryReadVoltField(field, element, out CompatNetStateValue value)) {
                    continue;
                }

                state.Values.Add(value);
                capturedNames.Add(value.Name);
            }

            foreach (PropertyInfo property in current.GetProperties(
                         BindingFlags.Instance
                         | BindingFlags.Public
                         | BindingFlags.NonPublic
                         | BindingFlags.DeclaredOnly)) {
                if (state.Values.Count >= IndustrialEra2VoltPayloads.MaximumValuesPerElement
                    || capturedNames.Contains(property.Name)
                    || property.GetIndexParameters().Length != 0
                    || !IsTargetAssembly(property.DeclaringType?.Assembly)
                    || !IsVoltStateMemberName(property.Name)
                    || property.GetMethod == null
                    || !TryReadVoltProperty(property, element, out CompatNetStateValue value)) {
                    continue;
                }

                state.Values.Add(value);
                capturedNames.Add(value.Name);
            }
        }

        return state.Values.Count > 0;
    }

    private static bool IsVoltStateMemberName(string name) {
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains("BackingField", StringComparison.Ordinal)
            || string.Equals(name, "Position", StringComparison.Ordinal)
            || string.Equals(name, "BlockValue", StringComparison.Ordinal)
            || string.Equals(name, "SubsystemVoltNet", StringComparison.Ordinal)
            || string.Equals(name, "SubsystemTerrain", StringComparison.Ordinal)
            || string.Equals(name, "m_standardVoltage", StringComparison.Ordinal)
            || string.Equals(name, "m_standardPower", StringComparison.Ordinal)
            || string.Equals(name, "m_belongingConnections", StringComparison.Ordinal)) {
            return false;
        }
        return true;
    }

    private static bool TryReadVoltValue(
        string name,
        object raw,
        out CompatNetStateValue value) {
        value = new CompatNetStateValue { Name = name };
        switch (raw) {
            case bool boolean:
                value.Kind = CompatNetStateValueKind.Boolean;
                value.BooleanValue = boolean;
                return true;
            case byte @byte:
                value.Kind = CompatNetStateValueKind.Byte;
                value.ByteValue = @byte;
                return true;
            case sbyte or short or ushort or uint or int:
                try {
                    value.Kind = CompatNetStateValueKind.Integer;
                    value.IntegerValue = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    return true;
                }
                catch {
                    return false;
                }
            case Enum enumValue:
                try {
                    value.Kind = CompatNetStateValueKind.Integer;
                    value.IntegerValue = Convert.ToInt32(enumValue, CultureInfo.InvariantCulture);
                    return true;
                }
                catch {
                    return false;
                }
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

    private static bool TryReadVoltField(
        FieldInfo field,
        object instance,
        out CompatNetStateValue value) {
        try {
            return TryReadVoltValue(field.Name, field.GetValue(instance), out value);
        }
        catch {
            value = null;
            return false;
        }
    }

    private static bool TryReadVoltProperty(
        PropertyInfo property,
        object instance,
        out CompatNetStateValue value) {
        try {
            return TryReadVoltValue(property.Name, property.GetValue(instance), out value);
        }
        catch {
            value = null;
            return false;
        }
    }

    private static void ApplyVoltValue(object element, CompatNetStateValue value) {
        if (element == null
            || value == null
            || string.IsNullOrWhiteSpace(value.Name)
            || value.Name.Length > IndustrialEra2VoltPayloads.MaximumValueNameLength) {
            return;
        }

        Type type = element.GetType();
        FieldInfo field = FindField(type, value.Name);
        if (field != null
            && !field.IsStatic
            && !field.IsInitOnly
            && TryConvertVoltValue(value, field.FieldType, out object fieldValue)) {
            try {
                field.SetValue(element, fieldValue);
            }
            catch {
            }
            return;
        }

        PropertyInfo property = FindProperty(type, value.Name);
        if (property?.CanWrite != true
            || !TryConvertVoltValue(value, property.PropertyType, out object propertyValue)) {
            return;
        }

        try {
            property.SetValue(element, propertyValue);
        }
        catch {
        }
    }

    private static bool TryConvertVoltValue(
        CompatNetStateValue value,
        Type targetType,
        out object converted) {
        converted = null;
        try {
            if (value.Kind == CompatNetStateValueKind.Boolean && targetType == typeof(bool)) {
                converted = value.BooleanValue;
                return true;
            }
            if (value.Kind == CompatNetStateValueKind.Byte && targetType == typeof(byte)) {
                converted = value.ByteValue;
                return true;
            }
            if (value.Kind == CompatNetStateValueKind.Integer) {
                if (targetType.IsEnum) {
                    converted = Enum.ToObject(targetType, value.IntegerValue);
                    return true;
                }
                if (targetType == typeof(sbyte)
                    || targetType == typeof(short)
                    || targetType == typeof(ushort)
                    || targetType == typeof(int)
                    || targetType == typeof(uint)) {
                    converted = Convert.ChangeType(value.IntegerValue, targetType, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            if (value.Kind == CompatNetStateValueKind.Single && targetType == typeof(float)) {
                converted = value.SingleValue;
                return true;
            }
            if (value.Kind == CompatNetStateValueKind.Double && targetType == typeof(double)) {
                converted = value.DoubleValue;
                return true;
            }
            if (value.Kind == CompatNetStateValueKind.Point3 && targetType == typeof(Point3)) {
                converted = value.Point3Value;
                return true;
            }
            if (value.Kind == CompatNetStateValueKind.Vector3 && targetType == typeof(Vector3)) {
                converted = value.Vector3Value;
                return true;
            }
            if (value.Kind == CompatNetStateValueKind.Quaternion && targetType == typeof(Quaternion)) {
                converted = value.QuaternionValue;
                return true;
            }
        }
        catch {
        }
        return false;
    }

    private static void QueueVoltBatches(
        List<IndustrialEra2VoltElementState> records,
        int sequence,
        bool fullRefresh) {
        List<IndustrialEra2VoltElementState> current = [];
        foreach (IndustrialEra2VoltElementState record in records) {
            current.Add(record);
            byte[] payload = IndustrialEra2VoltPayloads.Write(
                new IndustrialEra2VoltStateBatch {
                    Sequence = sequence,
                    FullRefresh = fullRefresh,
                    Records = current
                });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                continue;
            }

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0) {
                QueueVoltBatch(current, sequence, fullRefresh);
            }
            current = [record];
        }

        if (current.Count > 0) {
            QueueVoltBatch(current, sequence, fullRefresh);
        }
    }

    private static void QueueVoltBatch(
        List<IndustrialEra2VoltElementState> records,
        int sequence,
        bool fullRefresh) {
        byte[] payload = IndustrialEra2VoltPayloads.Write(
            new IndustrialEra2VoltStateBatch {
                Sequence = sequence,
                FullRefresh = fullRefresh,
                Records = records
            });
        if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
            CompatNetCore.QueueBroadcast(
                ScopedUpdateGuardCompatNetAdapter.IndustrialEra2AdapterId,
                (ushort)IndustrialEra2CompatNetOperation.VoltStateSnapshot,
                payload);
        }
    }

    private static void QueueBatches(
        List<IndustrialEra2TankState> records,
        int sequence,
        bool fullRefresh) {
        List<IndustrialEra2TankState> current = [];
        foreach (IndustrialEra2TankState record in records) {
            current.Add(record);
            byte[] payload = IndustrialEra2CompatNetPayloads.Write(
                new IndustrialEra2TankStateBatch {
                    Sequence = sequence,
                    FullRefresh = fullRefresh,
                    Records = current
                });
            if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
                continue;
            }

            current.RemoveAt(current.Count - 1);
            if (current.Count > 0) {
                QueueBatch(current, sequence, fullRefresh);
            }
            current = [record];
        }

        if (current.Count > 0) {
            QueueBatch(current, sequence, fullRefresh);
        }
    }

    private static void QueueBatch(
        List<IndustrialEra2TankState> records,
        int sequence,
        bool fullRefresh) {
        byte[] payload = IndustrialEra2CompatNetPayloads.Write(
            new IndustrialEra2TankStateBatch {
                Sequence = sequence,
                FullRefresh = fullRefresh,
                Records = records
            });
        if (payload.Length <= CompatNetPacket.MaximumPayloadBytes) {
            CompatNetCore.QueueBroadcast(
                ScopedUpdateGuardCompatNetAdapter.IndustrialEra2AdapterId,
                (ushort)IndustrialEra2CompatNetOperation.TankStateSnapshot,
                payload);
        }
    }

    private static bool IsTargetComponent(Component component) {
        if (component?.Entity == null || !IsTargetAssembly(component.GetType().Assembly)) {
            return false;
        }

        Type type = component.GetType();
        bool implementsTank = type.GetInterfaces().Any(candidate =>
            string.Equals(candidate.FullName, TankInterfaceName, StringComparison.Ordinal));
        return implementsTank
            && FindProperty(type, "TanksCount") != null
            && FindMethod(type, "GetTankValue", 1) != null
            && FindMethod(type, "GetTankVolume", 1) != null
            && FindMethod(type, "AddTankFluid", 4) != null
            && FindMethod(type, "RemoveTankFluid", 2) != null;
    }

    private static bool TryCapture(
        Component component,
        out IndustrialEra2TankState state) {
        state = new IndustrialEra2TankState {
            EntityId = component.Entity.Id,
            ComponentTypeName = component.GetType().FullName ?? string.Empty
        };
        if (state.EntityId <= 0
            || state.ComponentTypeName.Length > IndustrialEra2CompatNetPayloads.MaximumComponentTypeNameLength
            || !TryGetTankCount(component, out int tankCount)
            || tankCount <= 0
            || tankCount > IndustrialEra2CompatNetPayloads.MaximumTanksPerComponent) {
            return false;
        }

        MethodInfo getValue = FindMethod(component.GetType(), "GetTankValue", 1);
        MethodInfo getVolume = FindMethod(component.GetType(), "GetTankVolume", 1);
        for (int i = 0; i < tankCount; i++) {
            if (!TryInvokeInt(getValue, component, [i], out int value)
                || !TryInvokeFloat(getVolume, component, [i], out float volume)
                || !float.IsFinite(volume)) {
                return false;
            }

            if (volume <= 0f || value <= 0) {
                value = 0;
                volume = 0f;
            }
            state.Tanks.Add(new IndustrialEra2TankSlotState {
                Value = value,
                Volume = volume
            });
        }

        return state.Tanks.Count > 0;
    }

    private static void ApplyState(
        Component component,
        IndustrialEra2TankState state) {
        if (!TryGetTankCount(component, out int tankCount)
            || tankCount != state.Tanks.Count
            || tankCount > IndustrialEra2CompatNetPayloads.MaximumTanksPerComponent) {
            return;
        }

        MethodInfo getVolume = FindMethod(component.GetType(), "GetTankVolume", 1);
        MethodInfo remove = FindMethod(component.GetType(), "RemoveTankFluid", 2);
        MethodInfo add = FindMethod(component.GetType(), "AddTankFluid", 4);
        if (getVolume == null || remove == null || add == null) {
            return;
        }

        for (int i = 0; i < tankCount; i++) {
            IndustrialEra2TankSlotState desired = state.Tanks[i];
            if (desired == null || !float.IsFinite(desired.Volume)) {
                continue;
            }

            float targetVolume = Math.Max(0f, desired.Volume);
            if (!TryInvokeFloat(getVolume, component, [i], out float currentVolume)
                || !float.IsFinite(currentVolume)) {
                continue;
            }

            if (currentVolume > 0f) {
                TryInvoke(remove, component, [i, currentVolume], out _);
            }
            if (targetVolume > 0f && desired.Value > 0) {
                object[] arguments = [i, desired.Value, targetVolume, 0f];
                TryInvoke(add, component, arguments, out _);
            }
        }

        // ComponentFluidTank uses these hooks to update its material and draw
        // request. They are optional, so other ITank implementations remain
        // compatible with this bridge.
        TryInvoke(FindMethod(component.GetType(), "OnSwitchFluid", 0), component, [], out _);
        SetField(component, "m_drawRequire", true);
        TryInvoke(FindMethod(component.GetType(), "Draw", 0), component, [], out _);
    }

    private static bool TryGetTankCount(Component component, out int count) {
        count = 0;
        PropertyInfo property = FindProperty(component.GetType(), "TanksCount");
        if (property == null) {
            return false;
        }

        try {
            object raw = property.GetValue(component);
            if (raw is int value) {
                count = value;
                return true;
            }
        }
        catch {
        }
        return false;
    }

    private static bool TryInvokeInt(
        MethodInfo method,
        object instance,
        object[] arguments,
        out int value) {
        value = 0;
        if (!TryInvoke(method, instance, arguments, out object result)
            || result is not int intValue) {
            return false;
        }
        value = intValue;
        return true;
    }

    private static bool TryInvokeFloat(
        MethodInfo method,
        object instance,
        object[] arguments,
        out float value) {
        value = 0f;
        if (!TryInvoke(method, instance, arguments, out object result)
            || result is not float floatValue) {
            return false;
        }
        value = floatValue;
        return true;
    }

    private static bool TryInvoke(
        MethodInfo method,
        object instance,
        object[] arguments,
        out object result) {
        result = null;
        if (method == null) {
            return false;
        }
        try {
            result = method.Invoke(instance, arguments);
            return true;
        }
        catch {
            return false;
        }
    }

    private static void SetField(object instance, string name, object value) {
        for (Type type = instance?.GetType(); type != null && type != typeof(object); type = type.BaseType) {
            FieldInfo field = type.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field == null || field.IsInitOnly || !field.FieldType.IsInstanceOfType(value)) {
                continue;
            }
            try {
                field.SetValue(instance, value);
            }
            catch {
            }
            return;
        }
    }

    private static object GetMemberValue(object instance, string name) {
        if (instance == null) {
            return null;
        }

        FieldInfo field = FindField(instance.GetType(), name);
        try {
            if (field != null) {
                return field.GetValue(instance);
            }
            return FindProperty(instance.GetType(), name)?.GetValue(instance);
        }
        catch {
            return null;
        }
    }

    private static FieldInfo FindField(Type type, string name) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            FieldInfo field = current.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        return null;
    }

    private static PropertyInfo FindProperty(Type type, string name) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            PropertyInfo property = current.GetProperty(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (property != null) {
                return property;
            }
        }
        return null;
    }

    private static MethodInfo FindMethod(Type type, string name, int parameterCount) {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType) {
            foreach (MethodInfo method in current.GetMethods(InstanceFlags | BindingFlags.DeclaredOnly)) {
                if ((method.Name == name || method.Name.EndsWith("." + name, StringComparison.Ordinal))
                    && method.GetParameters().Length == parameterCount) {
                    return method;
                }
            }
        }
        return null;
    }

    private static bool IsTargetAssembly(Assembly assembly) {
        if (assembly == null) {
            return false;
        }
        return NamesEqual(assembly.GetName().Name, TargetAssemblyName)
            || NamesEqual(assembly.ManifestModule?.Name, TargetAssemblyName);
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
