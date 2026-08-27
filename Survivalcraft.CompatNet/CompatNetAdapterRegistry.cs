namespace Game;

/// <summary>
/// Process-local registry for compatibility adapters.
/// Adapter IDs are part of the wire protocol and must remain stable.
/// </summary>
public static class CompatNetAdapterRegistry {
    private static readonly Dictionary<ushort, ICompatNetAdapter> s_adapters = [];

    public static IReadOnlyCollection<ICompatNetAdapter> Adapters => s_adapters.Values;

    public static void Reset() {
        s_adapters.Clear();
    }

    public static void Register(ICompatNetAdapter adapter) {
        ArgumentNullException.ThrowIfNull(adapter);
        if (adapter.AdapterId == 0) {
            throw new ArgumentOutOfRangeException(nameof(adapter), "Adapter ID 0 is reserved.");
        }

        if (s_adapters.TryGetValue(adapter.AdapterId, out ICompatNetAdapter existing)) {
            if (existing.GetType() == adapter.GetType()) {
                return;
            }

            throw new InvalidOperationException(
                $"Duplicate CompatNet adapter ID {adapter.AdapterId}: "
                + $"{existing.AdapterName} and {adapter.AdapterName}.");
        }

        s_adapters.Add(adapter.AdapterId, adapter);
    }

    public static bool TryGet(ushort adapterId, out ICompatNetAdapter adapter) {
        return s_adapters.TryGetValue(adapterId, out adapter);
    }
}
