using Godot;
using System.IO;

namespace SurvivalcraftGodot;

public static class WorldSession
{
    public static bool Continue { get; set; }
    public static Inventory? Inventory { get; set; }
}

public static class WorldSave
{
    public static string FilePath => ProjectSettings.GlobalizePath("user://saves/world.scg");

    public static bool Exists => File.Exists(FilePath);

    public static void WriteBytes(byte[] data)
    {
        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(FilePath, data);
    }

    public static bool TryLoad(VoxelWorld world, out Inventory inventory, out float time)
    {
        inventory = new Inventory();
        time = 0f;
        if (!Exists)
        {
            return false;
        }

        return WorldSnapshot.TryRead(File.ReadAllBytes(FilePath), world, inventory, out time, out _);
    }

    public static void Delete()
    {
        if (Exists)
        {
            File.Delete(FilePath);
        }
    }
}
