using System.IO;
using System.Text;

namespace SurvivalcraftGodot;

public static class WorldSnapshot
{
    public const string Magic = "SCG1";
    public static bool HasPlayerPose { get; private set; }
    public static float SavedX { get; private set; }
    public static float SavedY { get; private set; }
    public static float SavedZ { get; private set; }
    public static float SavedHealth { get; private set; }
    public static float SavedHunger { get; private set; }
    public static float SavedLevel { get; private set; } = 1f;

    public static void ClearPose()
    {
        HasPlayerPose = false;
    }

    public static byte[] Write(VoxelWorld world, Inventory inventory, float time, float playerX = 0f, float playerY = 0f, float playerZ = 0f, float health = 20f, float hunger = 20f, float level = 1f)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(3);
        writer.Write(time);
        writer.Write(Inventory.SlotCount);
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            writer.Write(inventory.Ids[i]);
            writer.Write(inventory.Counts[i]);
        }

        writer.Write(playerX);
        writer.Write(playerY);
        writer.Write(playerZ);
        writer.Write(health);
        writer.Write(hunger);
        writer.Write(level);

        int count = 0;
        foreach (ChunkPos _ in world.Chunks)
        {
            count++;
        }

        writer.Write(count);
        foreach (ChunkPos pos in world.Chunks)
        {
            if (!world.TryGetChunk(pos, out Chunk? chunk))
            {
                continue;
            }

            writer.Write(pos.X);
            writer.Write(pos.Z);
            for (int i = 0; i < Chunk.Volume; i++)
            {
                writer.Write(chunk.Blocks[i]);
            }

            writer.Write(chunk.State);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static bool TryRead(byte[] data, VoxelWorld world, Inventory inventory, out float time, out string error)
    {
        time = 0;
        error = "";
        HasPlayerPose = false;
        if (data.Length >= 1 && data[0] == (byte)'<')
        {
            error = "这不是本游戏的世界";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            byte[] magic = reader.ReadBytes(4);
            if (Encoding.ASCII.GetString(magic) != Magic)
            {
                error = "这不是本游戏的世界";
                return false;
            }

            int version = reader.ReadInt32();
            if (version != 1 && version != 2 && version != 3)
            {
                error = "这不是本游戏的世界";
                return false;
            }

            time = reader.ReadSingle();
            int slots = reader.ReadInt32();
            if (slots != Inventory.SlotCount)
            {
                error = "这不是本游戏的世界";
                return false;
            }

            for (int i = 0; i < slots; i++)
            {
                inventory.SetSlot(i, reader.ReadUInt16(), reader.ReadInt32());
            }

            if (version >= 2)
            {
                SavedX = reader.ReadSingle();
                SavedY = reader.ReadSingle();
                SavedZ = reader.ReadSingle();
                SavedHealth = reader.ReadSingle();
                SavedHunger = reader.ReadSingle();
                HasPlayerPose = true;
            }

            if (version >= 3)
            {
                SavedLevel = reader.ReadSingle();
            }

            int chunks = reader.ReadInt32();
            for (int c = 0; c < chunks; c++)
            {
                var pos = new ChunkPos(reader.ReadInt32(), reader.ReadInt32());
                Chunk chunk = world.GetOrCreate(pos);
                for (int i = 0; i < Chunk.Volume; i++)
                {
                    chunk.Blocks[i] = reader.ReadUInt16();
                }

                byte[] state = reader.ReadBytes(Chunk.Volume);
                System.Array.Copy(state, chunk.State, Chunk.Volume);
                chunk.MeshDirty = true;
                chunk.MeshMinY = 0;
                chunk.MeshMaxY = Chunk.Height - 1;
            }

            return true;
        }
        catch (EndOfStreamException)
        {
            error = "这不是本游戏的世界";
            return false;
        }
    }
}
