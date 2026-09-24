using System.Collections.Generic;

namespace SurvivalcraftGodot.Simulation;

/// <summary>
/// 按 tick 模拟电路。连接语义对齐 SubsystemElectricity：
/// 本端非 Output 且对端非纯 Input 才读入电压；输出变化则把下游入队下一步。
/// </summary>
public sealed class CircuitNet
{
    readonly Dictionary<GridPos, ElectricNode> _byPos = new();
    readonly Dictionary<int, HashSet<ElectricNode>> _future = new();
    HashSet<ElectricNode>? _nextStep;

    public SimGrid? Grid { get; private set; }
    public int CircuitStep { get; private set; }
    public IReadOnlyDictionary<GridPos, ElectricNode> Nodes => _byPos;

    public ElectricNode? Get(GridPos pos) => _byPos.TryGetValue(pos, out ElectricNode? n) ? n : null;

    public ElectricNode? Get(int x, int y, int z) => Get(new GridPos(x, y, z));

    public void Queue(ElectricNode node, int step)
    {
        if (step == CircuitStep + 1)
        {
            _nextStep ??= GetOrCreateFuture(CircuitStep + 1);
            _nextStep.Add(node);
        }
        else if (step > CircuitStep + 1)
        {
            GetOrCreateFuture(step).Add(node);
        }
    }

    HashSet<ElectricNode> GetOrCreateFuture(int step)
    {
        if (!_future.TryGetValue(step, out HashSet<ElectricNode>? set))
        {
            set = new HashSet<ElectricNode>();
            _future[step] = set;
        }

        return set;
    }

    public void Add(ElectricNode node)
    {
        node.Net = this;
        _byPos[node.Pos] = node;
    }

    public void Link(ElectricNode a, ElectricConnectorType aType, ElectricNode b, ElectricConnectorType bType)
    {
        // 原版：双方有一端能驱动、一端能接收才连上
        bool ok = (aType != ElectricConnectorType.Input && bType != ElectricConnectorType.Output)
            || (aType != ElectricConnectorType.Output && bType != ElectricConnectorType.Input);
        if (!ok)
        {
            return;
        }

        a.Connections.Add(new ElectricConnection
        {
            Neighbor = b,
            ConnectorType = aType,
            NeighborConnectorType = bType
        });
        b.Connections.Add(new ElectricConnection
        {
            Neighbor = a,
            ConnectorType = bType,
            NeighborConnectorType = aType
        });
    }

    public static CircuitNet BuildFromGrid(SimGrid grid, int gateFacing = 1)
    {
        var net = new CircuitNet { Grid = grid };
        for (int x = 0; x < grid.SizeX; x++)
        {
            for (int y = 0; y < grid.SizeY; y++)
            {
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    int id = grid.Get(x, y, z);
                    if (!ElectricIds.IsElectric(id))
                    {
                        continue;
                    }

                    var pos = new GridPos(x, y, z);
                    ElectricNode node = id switch
                    {
                        ElectricIds.Wire => new WireNode(pos),
                        ElectricIds.Button => new ButtonNode(pos),
                        ElectricIds.AndGate => new AndGateNode(pos),
                        ElectricIds.OrGate => new OrGateNode(pos),
                        ElectricIds.NotGate => new NotGateNode(pos),
                        ElectricIds.LightBulb => new LightBulbNode(pos),
                        ElectricIds.Piston => new PistonNode(pos, gateFacing),
                        _ when ElectricIds.IsGunpowderKeg(id) => new GunpowderKegNode(pos, id),
                        _ => throw new System.InvalidOperationException("unknown electric id " + id)
                    };
                    net.Add(node);
                }
            }
        }

        foreach (ElectricNode node in net._byPos.Values)
        {
            for (int face = 0; face < 6; face++)
            {
                GridPos nPos = node.Pos.Offset(face);
                if (!net._byPos.TryGetValue(nPos, out ElectricNode? neighbor))
                {
                    continue;
                }

                // 只从较小坐标方向连一次，避免重复
                if (ComparePos(node.Pos, nPos) >= 0)
                {
                    continue;
                }

                ElectricConnectorType aType = ConnectorToward(node, face, gateFacing);
                ElectricConnectorType bType = ConnectorToward(neighbor, CellFaces.Opposite(face), gateFacing);
                net.Link(node, aType, neighbor, bType);
            }
        }

        foreach (ElectricNode node in net._byPos.Values)
        {
            net.Queue(node, 1);
        }

        return net;
    }

    static int ComparePos(GridPos a, GridPos b)
    {
        int c = a.X.CompareTo(b.X);
        if (c != 0)
        {
            return c;
        }

        c = a.Y.CompareTo(b.Y);
        return c != 0 ? c : a.Z.CompareTo(b.Z);
    }

    /// <summary>
    /// 简化的引脚朝向：门默认朝 gateFacing 出电；与/或左右进；非门背后进。
    /// </summary>
    static ElectricConnectorType ConnectorToward(ElectricNode node, int towardFace, int gateFacing)
    {
        switch (node.BlockId)
        {
            case ElectricIds.Button:
                return ElectricConnectorType.Output;
            case ElectricIds.LightBulb:
            case ElectricIds.Piston:
            case ElectricIds.SmallGunpowderKeg:
            case ElectricIds.MediumGunpowderKeg:
            case ElectricIds.LargeGunpowderKeg:
                return ElectricConnectorType.Input;
            case ElectricIds.Wire:
                return ElectricConnectorType.InputOutput;
            case ElectricIds.AndGate:
            case ElectricIds.OrGate:
            {
                int left = RotateY(gateFacing, 1);
                int right = RotateY(gateFacing, 3);
                if (towardFace == left || towardFace == right)
                {
                    return ElectricConnectorType.Input;
                }

                if (towardFace == gateFacing || towardFace == CellFaces.Opposite(gateFacing))
                {
                    // In / Top 侧当输出；对侧也允许输出以便直线串灯
                    return towardFace == gateFacing
                        ? ElectricConnectorType.Output
                        : ElectricConnectorType.Input;
                }

                return ElectricConnectorType.Output;
            }
            case ElectricIds.NotGate:
                return towardFace == CellFaces.Opposite(gateFacing)
                    ? ElectricConnectorType.Input
                    : ElectricConnectorType.Output;
            default:
                return ElectricConnectorType.InputOutput;
        }
    }

    /// <summary>水平面绕 +Y 旋转 face（仅 0..3）。</summary>
    static int RotateY(int face, int steps)
    {
        if (face >= 4)
        {
            return face;
        }

        return (face + steps) % 4;
    }

    public void Step(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            CircuitStep++;
            _nextStep = null;
            if (!_future.Remove(CircuitStep, out HashSet<ElectricNode>? list))
            {
                continue;
            }

            foreach (ElectricNode node in list)
            {
                if (!_byPos.ContainsKey(node.Pos))
                {
                    continue;
                }

                if (node.Simulate())
                {
                    QueueDownstream(node, CircuitStep + 1);
                }
            }
        }
    }

    void QueueDownstream(ElectricNode node, int step)
    {
        foreach (ElectricConnection c in node.Connections)
        {
            // 原版 QueueElectricElementConnectionsForSimulation：
            // ConnectorType != Input(0) 且 NeighborConnectorType != Output
            if (c.ConnectorType != ElectricConnectorType.Input
                && c.NeighborConnectorType != ElectricConnectorType.Output)
            {
                Queue(c.Neighbor, step);
            }
        }
    }

    public bool PressButton(int x, int y, int z)
    {
        if (Get(x, y, z) is ButtonNode button)
        {
            button.Press();
            return true;
        }

        return false;
    }
}
