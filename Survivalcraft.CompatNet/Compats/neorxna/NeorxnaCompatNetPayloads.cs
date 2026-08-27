namespace Game;

/// <summary>
/// Input intents understood by the Neorxna CompatNet adapter.
/// The operation is the intent; the payload is deliberately empty.
/// </summary>
public enum NeorxnaCompatNetOperation : ushort {
    None = 0,
    StartDash = 1,
    StopDash = 2
}

/// <summary>
/// Payload helpers for the Neorxna input-intent protocol.
/// </summary>
public static class NeorxnaCompatNetPayloads {
    public static byte[] Empty() => [];
}
