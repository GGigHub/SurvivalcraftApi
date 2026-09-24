using Godot;

namespace SurvivalcraftGodot;

public partial class Grazer : Node3D
{
    VoxelWorld? _world;
    Vector3 _target;
    float _health = 6f;
    bool _dead;
    bool _hostile;
    ushort _drop = BlockIds.Meat;
    int _dropCount = 1;

    public float Radius { get; private set; } = 0.55f;
    public bool IsDead => _dead;
    public ushort DropId => _drop;
    public int DropCount => _dropCount;

    public void Setup(VoxelWorld world, Vector3 position) =>
        Setup(world, position, new Color(0.72f, 0.55f, 0.32f), new Vector3(0.7f, 0.8f, 1.1f), 6f, BlockIds.Meat, 1, false, 0.55f);

    public void Setup(
        VoxelWorld world,
        Vector3 position,
        Color color,
        Vector3 size,
        float health,
        ushort drop,
        int dropCount,
        bool hostile,
        float radius)
    {
        _world = world;
        _health = health;
        _drop = drop;
        _dropCount = dropCount;
        _hostile = hostile;
        Radius = radius;
        if (IsInsideTree())
        {
            GlobalPosition = position;
        }
        else
        {
            Position = position;
        }

        _target = position + new Vector3(3f, 0f, 2f);
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        mesh.SetSurfaceOverrideMaterial(0, material);
        mesh.Position = new Vector3(0f, size.Y * 0.5f, 0f);
        AddChild(mesh);
        AddToGroup("creature");
    }

    public override void _Process(double delta)
    {
        if (_dead || _world == null)
        {
            return;
        }

        if (_hostile)
        {
            Node3D? player = GetTree().GetFirstNodeInGroup("player") as Node3D;
            if (player != null && GlobalPosition.DistanceTo(player.GlobalPosition) < 14f)
            {
                _target = player.GlobalPosition;
            }
        }

        GlobalPosition = CreatureMind.StepToward(_world, GlobalPosition, _target, 1.6f * (float)delta);
        if (_hostile)
        {
            Node3D? player = GetTree().GetFirstNodeInGroup("player") as Node3D;
            if (player != null && GlobalPosition.DistanceTo(player.GlobalPosition) < Radius + 0.4f && player is PlayerController controller)
            {
                controller.Hurt(5f * (float)delta, "被咬");
            }
        }
        else if (GlobalPosition.DistanceTo(_target) < 0.4f)
        {
            _target += new Vector3(2.5f, 0f, -1.5f);
        }
    }

    public bool Hurt(float amount)
    {
        if (_dead)
        {
            return false;
        }

        _health -= amount;
        if (_health > 0f)
        {
            return false;
        }

        _dead = true;
        QueueFree();
        return true;
    }
}
