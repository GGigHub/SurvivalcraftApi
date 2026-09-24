using System.Collections.Generic;

namespace SurvivalcraftGodot.Simulation;

public sealed class ElectricConnection
{
    public required ElectricNode Neighbor { get; init; }
    public required ElectricConnectorType ConnectorType { get; init; }
    public required ElectricConnectorType NeighborConnectorType { get; init; }
}

/// <summary>电路元件基类。电压与 Simulate 规则对齐原版 ElectricElement。</summary>
public abstract class ElectricNode
{
    public GridPos Pos { get; }
    public int BlockId { get; }
    public List<ElectricConnection> Connections { get; } = new();
    public CircuitNet? Net { get; set; }

    protected ElectricNode(GridPos pos, int blockId)
    {
        Pos = pos;
        BlockId = blockId;
    }

    public virtual float GetOutputVoltage() => 0f;

    /// <summary>返回 true 表示输出变了，需把下游入队。</summary>
    public abstract bool Simulate();

    public static bool IsSignalHigh(float voltage) => voltage >= 0.5f;

    protected int CountHighInputs()
    {
        int n = 0;
        foreach (ElectricConnection c in Connections)
        {
            if (c.ConnectorType != ElectricConnectorType.Output
                && c.NeighborConnectorType != ElectricConnectorType.Input
                && IsSignalHigh(c.Neighbor.GetOutputVoltage()))
            {
                n++;
            }
        }

        return n;
    }

    protected IEnumerable<float> InputVoltages()
    {
        foreach (ElectricConnection c in Connections)
        {
            if (c.ConnectorType != ElectricConnectorType.Output
                && c.NeighborConnectorType != ElectricConnectorType.Input)
            {
                yield return c.Neighbor.GetOutputVoltage();
            }
        }
    }
}

public sealed class WireNode : ElectricNode
{
    float _voltage;

    public WireNode(GridPos pos) : base(pos, ElectricIds.Wire) { }

    public override float GetOutputVoltage() => _voltage;

    public override bool Simulate()
    {
        float prev = _voltage;
        int bits = 0;
        foreach (float v in InputVoltages())
        {
            bits |= (int)System.MathF.Round(v * 15f);
        }

        _voltage = bits / 15f;
        return _voltage != prev;
    }
}

public sealed class ButtonNode : ElectricNode
{
    float _voltage;
    bool _wasPressed;
    readonly float _pressedVoltage;

    public ButtonNode(GridPos pos, float pressedVoltage = 1f) : base(pos, ElectricIds.Button)
    {
        _pressedVoltage = pressedVoltage;
    }

    public override float GetOutputVoltage() => _voltage;

    public void Press()
    {
        if (!_wasPressed && _voltage == 0f)
        {
            _wasPressed = true;
            Net?.Queue(this, Net.CircuitStep + 1);
        }
    }

    public override bool Simulate()
    {
        float prev = _voltage;
        if (_wasPressed)
        {
            _wasPressed = false;
            _voltage = _pressedVoltage;
            Net?.Queue(this, Net.CircuitStep + 10);
        }
        else
        {
            _voltage = 0f;
        }

        return _voltage != prev;
    }
}

public sealed class AndGateNode : ElectricNode
{
    float _voltage;

    public AndGateNode(GridPos pos) : base(pos, ElectricIds.AndGate) { }

    public override float GetOutputVoltage() => _voltage;

    public override bool Simulate()
    {
        float prev = _voltage;
        int count = 0;
        int bits = 15;
        foreach (float v in InputVoltages())
        {
            bits &= (int)System.MathF.Round(v * 15f);
            count++;
        }

        _voltage = count == 2 ? bits / 15f : 0f;
        return _voltage != prev;
    }
}

public sealed class OrGateNode : ElectricNode
{
    float _voltage;

    public OrGateNode(GridPos pos) : base(pos, ElectricIds.OrGate) { }

    public override float GetOutputVoltage() => _voltage;

    public override bool Simulate()
    {
        float prev = _voltage;
        int bits = 0;
        foreach (float v in InputVoltages())
        {
            bits |= (int)System.MathF.Round(v * 15f);
        }

        _voltage = bits / 15f;
        return _voltage != prev;
    }
}

public sealed class NotGateNode : ElectricNode
{
    float _voltage;

    public NotGateNode(GridPos pos) : base(pos, ElectricIds.NotGate) { }

    public override float GetOutputVoltage() => _voltage;

    public override bool Simulate()
    {
        float prev = _voltage;
        int bits = 0;
        foreach (float v in InputVoltages())
        {
            bits = (int)System.MathF.Round(v * 15f);
            break;
        }

        _voltage = (~bits & 0xF) / 15f;
        return _voltage != prev;
    }
}

public sealed class LightBulbNode : ElectricNode
{
    public int Intensity { get; private set; }

    public LightBulbNode(GridPos pos) : base(pos, ElectricIds.LightBulb) { }

    public bool IsOn => Intensity > 0;

    public override bool Simulate()
    {
        float max = 0f;
        foreach (float v in InputVoltages())
        {
            if (v > max)
            {
                max = v;
            }
        }

        Intensity = System.Math.Clamp((int)System.MathF.Round((max - 0.5f) * 30f), 0, 15);
        return false;
    }
}

public sealed class PistonNode : ElectricNode
{
    int _lastLength = -1;
    public int Face { get; }
    public int LastLength => _lastLength;

    public PistonNode(GridPos pos, int face = 1) : base(pos, ElectricIds.Piston)
    {
        Face = face;
    }

    public override bool Simulate()
    {
        float max = 0f;
        foreach (float v in InputVoltages())
        {
            if (v > max)
            {
                max = v;
            }
        }

        // 原版：Max((int)(voltage * 15.999f) - 7, 0)
        int length = System.Math.Max((int)(max * 15.999f) - 7, 0);
        if (length != _lastLength)
        {
            _lastLength = length;
            if (Net?.Grid != null)
            {
                PistonRules.Adjust(Net.Grid, Pos, Face, length);
            }
        }

        return false;
    }
}

public sealed class GunpowderKegNode : ElectricNode
{
    public bool Ignited { get; private set; }

    public GunpowderKegNode(GridPos pos, int blockId) : base(pos, blockId) { }

    public override bool Simulate()
    {
        if (!Ignited && CountHighInputs() > 0)
        {
            Ignited = true;
            if (Net?.Grid != null)
            {
                float pressure = ElectricIds.KegExplosionPressure(BlockId);
                ExplosionRules.Explode(Net.Grid, Pos, pressure);
            }
        }

        return false;
    }
}
