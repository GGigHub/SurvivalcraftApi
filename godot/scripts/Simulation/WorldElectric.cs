using SurvivalcraftGodot.Simulation;

namespace SurvivalcraftGodot;

/// <summary>
/// 把玩家附近的电路方块抄进 CircuitNet，每帧走一步，再把格子变化写回世界。
/// </summary>
public sealed class WorldElectric
{
    const int Radius = 10;

    SimGrid? _grid;
    CircuitNet? _net;
    int _originX;
    int _originY;
    int _originZ;

    public bool Active => _net != null;

    public void Rebuild(VoxelWorld world, int centerX, int centerY, int centerZ)
    {
        _originX = centerX - Radius;
        _originY = System.Math.Max(0, centerY - Radius);
        _originZ = centerZ - Radius;
        int size = Radius * 2 + 1;
        int height = System.Math.Min(size, Chunk.Height - _originY);
        _grid = new SimGrid(size, height, size);
        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    _grid.Set(x, y, z, world.GetBlock(_originX + x, _originY + y, _originZ + z));
                }
            }
        }

        _net = CircuitNet.BuildFromGrid(_grid, gateFacing: 1);
    }

    public bool PressButton(int x, int y, int z)
    {
        if (_net == null)
        {
            return false;
        }

        return _net.PressButton(x - _originX, y - _originY, z - _originZ);
    }

    public void Step(VoxelWorld world)
    {
        if (_net == null || _grid == null)
        {
            return;
        }

        _net.Step(1);
        int height = _grid.SizeY;
        for (int z = 0; z < _grid.SizeZ; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < _grid.SizeX; x++)
                {
                    int wx = _originX + x;
                    int wy = _originY + y;
                    int wz = _originZ + z;
                    ushort current = world.GetBlock(wx, wy, wz);
                    if (current == BlockIds.Boundary)
                    {
                        continue;
                    }

                    int simulated = _grid.Get(x, y, z);
                    if (current != simulated)
                    {
                        world.SetBlock(wx, wy, wz, (ushort)simulated);
                    }
                }
            }
        }

        foreach (ElectricNode node in _net.Nodes.Values)
        {
            if (node is not LightBulbNode bulb)
            {
                continue;
            }

            int wx = _originX + bulb.Pos.X;
            int wy = _originY + bulb.Pos.Y;
            int wz = _originZ + bulb.Pos.Z;
            byte state = (byte)(bulb.IsOn ? 1 : 0);
            if (world.GetBlock(wx, wy, wz) == ElectricIds.LightBulb && world.GetState(wx, wy, wz) != state)
            {
                world.SetBlock(wx, wy, wz, (ushort)ElectricIds.LightBulb, state);
            }
        }
    }
}
