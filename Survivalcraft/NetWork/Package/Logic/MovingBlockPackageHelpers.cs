using Engine;

namespace Game.NetWork;

public static class MovingBlockPackageHelpers {
    public static void SerializeBlockList(PackageSerializer archive, List<MovingBlockAddPackage.MovingBlockData> blocks) {
        int count = archive.IsWriting ? blocks.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            blocks.Clear();
            if (count < 0) {
                throw new InvalidDataException($"MovingBlockPackage contains invalid block count: {count}");
            }
            for (int i = 0; i < count; i++) {
                MovingBlockAddPackage.MovingBlockData data = new();
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
                blocks.Add(data);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                MovingBlockAddPackage.MovingBlockData data = blocks[i];
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
            }
        }
    }
}