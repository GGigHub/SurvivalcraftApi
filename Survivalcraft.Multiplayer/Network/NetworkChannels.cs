namespace Game.Network;

/// <summary>
/// LiteNetLib QoS channels used by the multiplayer transport.
/// </summary>
public static class NetworkChannels {
    public const byte Default = 0;
    public const byte Terrain = 1;
    public const byte Count = 2;
}
