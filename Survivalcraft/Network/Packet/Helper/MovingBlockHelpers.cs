using Engine;

namespace Game.Network;

public static class MovingBlockHelpers {
    public static void SerializeBlockList(PacketSerializer archive, List<MovingBlockAddPacket.MovingBlockData> blocks) {
        int count = archive.IsWriting ? blocks.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            blocks.Clear();
            if (count < 0) {
                throw new InvalidDataException($"MovingBlockPacket contains invalid block count: {count}");
            }
            for (int i = 0; i < count; i++) {
                MovingBlockAddPacket.MovingBlockData data = new();
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
                blocks.Add(data);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                MovingBlockAddPacket.MovingBlockData data = blocks[i];
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
            }
        }
    }
}

