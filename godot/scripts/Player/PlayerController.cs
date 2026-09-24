using Godot;
using System.IO;

namespace SurvivalcraftGodot;

public partial class PlayerController : Node3D
{
    const float MoveSpeed = 5.2f;
    const float JumpSpeed = 7.4f;
    const float Gravity = 24f;
    const float Reach = 5f;

    Camera3D? _camera;
    WorldHost? _worldHost;
    Label? _hint;
    PanelContainer? _inventoryPanel;
    Label? _inventoryText;
    readonly Inventory _inventory = new();
    readonly Vitals _vitals = new();
    readonly ChestStore _chests = new();
    RecipeBook? _recipes;
    int _slot;
    float _sneakTime;
    float _noticeTime;
    string _notice = "";
    float _pitch;
    float _verticalVelocity;
    bool _grounded;

    public override void _Ready()
    {
        InputSetup.Ensure();
        _camera = GetNode<Camera3D>("Camera3D");
        _worldHost = GetParent().GetNode<WorldHost>("WorldHost");
        _hint = GetNode<Label>("../Hud/Hint");
        _inventory.GiveStart();
        string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        _recipes = RecipeBook.Load(
            Path.Combine(root, "Survivalcraft", "Content", "Assets", "CraftingRecipes.xml"),
            Path.Combine(root, "Survivalcraft", "Block"),
            Path.Combine(root, "Survivalcraft", "Content", "Assets", "BlocksData.txt"));
        BuildInventoryPanel();
        if (WorldSession.Inventory != null)
        {
            for (int i = 0; i < Inventory.SlotCount; i++)
            {
                _inventory.SetSlot(i, WorldSession.Inventory.Ids[i], WorldSession.Inventory.Counts[i]);
                _inventory.Wear[i] = WorldSession.Inventory.Wear[i];
            }
        }
        else
        {
            _inventory.GiveStart();
        }

        ShowHotbar();
        if (WorldSnapshot.HasPlayerPose)
        {
            GlobalPosition = new Vector3(WorldSnapshot.SavedX, WorldSnapshot.SavedY, WorldSnapshot.SavedZ);
            _vitals.Health = WorldSnapshot.SavedHealth;
            _vitals.Hunger = WorldSnapshot.SavedHunger;
            _vitals.Level = WorldSnapshot.SavedLevel;
        }
        else
        {
            int surface = TerrainGenerator.SurfaceY(0, 0);
            GlobalPosition = new Vector3(0.5f, surface + 1.01f, 0.5f);
        }

        Input.MouseMode = Input.MouseModeEnum.Captured;
        AddToGroup("player");
    }

    void SaveProgress()
    {
        if (_worldHost == null)
        {
            return;
        }

        byte[] data = WorldSnapshot.Write(
            _worldHost.World,
            _inventory,
            _worldHost.Clock,
            GlobalPosition.X,
            GlobalPosition.Y,
            GlobalPosition.Z,
            _vitals.Health,
            _vitals.Hunger,
            _vitals.Level);
        WorldSave.WriteBytes(data);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("open_inventory"))
        {
            ToggleInventory();
            return;
        }

        if (@event.IsActionPressed("pause"))
        {
            SaveProgress();
            Notice("已保存");
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
            && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            return;
        }

        if (Input.MouseMode != Input.MouseModeEnum.Captured || _camera == null)
        {
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            RotateY(-motion.Relative.X * 0.0022f);
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.0022f, -1.45f, 1.45f);
            _camera.Rotation = new Vector3(_pitch, 0, 0);
        }

        if (@event.IsActionPressed("hotbar_1"))
        {
            Select(0);
        }
        else if (@event.IsActionPressed("hotbar_2"))
        {
            Select(1);
        }
        else if (@event.IsActionPressed("hotbar_3"))
        {
            Select(2);
        }
        else if (@event.IsActionPressed("hotbar_4"))
        {
            Select(3);
        }
        else if (@event.IsActionPressed("hotbar_5"))
        {
            Select(4);
        }
        else if (@event.IsActionPressed("hotbar_6"))
        {
            Select(5);
        }
        else if (@event.IsActionPressed("hotbar_7"))
        {
            Select(6);
        }
        else if (@event.IsActionPressed("hotbar_8"))
        {
            Select(7);
        }
        else if (@event.IsActionPressed("hotbar_9"))
        {
            Select(8);
        }
        else if (@event.IsActionPressed("hotbar_0"))
        {
            Select(9);
        }
        else if (@event.IsActionPressed("attack"))
        {
            Dig();
        }
        else if (@event.IsActionPressed("use"))
        {
            UseOrPlace();
        }
    }

    void Select(int slot)
    {
        _slot = slot;
        ShowHotbar();
    }

    void ShowHotbar()
    {
        if (_hint == null)
        {
            return;
        }

        ushort held = _inventory.Counts[_slot] > 0 ? _inventory.Ids[_slot] : BlockIds.Air;
        string name = held == BlockIds.Air ? "空" : BlockCatalog.NameOf(held);
        string wear = "";
        if (held != BlockIds.Air && OriginalBlockData.TryGet(held, out OriginalBlockRow row) && row.Durability > 0)
        {
            wear = $"  耐久{row.Durability - _inventory.Wear[_slot]}";
        }

        string line = $"生命{(int)_vitals.Health} 饥饿{(int)_vitals.Hunger}  {name} x{_inventory.Counts[_slot]}{wear}    I背包  蹲住睡觉";
        if (_vitals.Air < 14.5f)
        {
            line = $"氧气{(int)_vitals.Air}  " + line;
        }

        if (_worldHost != null)
        {
            line = _worldHost.WeatherLabel + $"  等级{_vitals.LevelRank}  " + line;
        }
        _hint.Text = _noticeTime > 0f ? _notice + "\n" + line : line;
    }

    void Notice(string text)
    {
        _notice = text;
        _noticeTime = 4f;
        ShowHotbar();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_worldHost == null)
        {
            return;
        }

        float dt = Mathf.Min((float)delta, 0.05f);
        bool inWater = FeetIn(BlockIds.Water);
        bool inMagma = FeetIn(BlockIds.Magma);
        Vector3 wish = Vector3.Zero;
        if (Input.IsActionPressed("move_forward"))
        {
            wish -= GlobalTransform.Basis.Z;
        }

        if (Input.IsActionPressed("move_back"))
        {
            wish += GlobalTransform.Basis.Z;
        }

        if (Input.IsActionPressed("move_left"))
        {
            wish -= GlobalTransform.Basis.X;
        }

        if (Input.IsActionPressed("move_right"))
        {
            wish += GlobalTransform.Basis.X;
        }

        wish.Y = 0;
        if (wish.LengthSquared() > 0.001f)
        {
            wish = wish.Normalized() * (inWater ? MoveSpeed * 0.55f : MoveSpeed);
        }

        if (_grounded && Input.IsActionPressed("jump"))
        {
            _verticalVelocity = JumpSpeed;
            _grounded = false;
        }

        _verticalVelocity -= Gravity * dt * (inWater ? 0.25f : 1f);
        if (inWater)
        {
            _verticalVelocity *= 0.9f;
        }
        Vector3 motion = new(wish.X * dt, _verticalVelocity * dt, wish.Z * dt);
        Vector3 feet = GlobalPosition;
        VoxelMotion.Move(_worldHost.World, ref feet, motion, out bool grounded);
        SeparateFromCreatures(ref feet);
        if (grounded && motion.Y < 0)
        {
            _verticalVelocity = 0;
        }

        _grounded = grounded;
        GlobalPosition = feet;
        if (GlobalPosition.Y < -8f || _vitals.Dead)
        {
            string reason = GlobalPosition.Y < -8f ? "掉出世界" : _vitals.DeathCause;
            Respawn();
            _notice = string.IsNullOrEmpty(reason) ? "你回到了出生点" : $"你因为{reason}回到了出生点";
            _noticeTime = 6f;
        }

        float sun = _worldHost.Sun;
        bool headUnder = HeadIn(BlockIds.Water);
        bool raining = _worldHost.Weather is WeatherKind.Rain or WeatherKind.Thunder;
        _vitals.Tick(dt, inWater, inMagma, sun, headUnder, raining, _worldHost.Season);
        if (Input.IsActionPressed("sneak") && _grounded && !inWater)
        {
            _sneakTime += dt;
            if (_sneakTime > 2.5f)
            {
                _worldHost.AdvanceToMorning();
                _vitals.Wake();
                _sneakTime = 0f;
            }
        }
        else
        {
            _sneakTime = 0f;
        }

        if (_noticeTime > 0f)
        {
            _noticeTime -= dt;
        }

        ShowHotbar();
    }

    public void Hurt(float amount, string cause)
    {
        if (_vitals.Dead || amount <= 0f)
        {
            return;
        }

        _vitals.Health -= amount;
        _vitals.DeathCause = cause;
        if (_vitals.Health <= 0f)
        {
            _vitals.Health = 0f;
            _vitals.Dead = true;
        }
    }

    void SeparateFromCreatures(ref Vector3 feet)
    {
        foreach (Node node in GetTree().GetNodesInGroup("creature"))
        {
            if (node is not Grazer grazer || grazer.IsDead)
            {
                continue;
            }

            feet = CreatureMind.PushOut(feet, grazer.GlobalPosition, grazer.Radius + 0.35f);
        }
    }

    void Respawn()
    {
        int surface = TerrainGenerator.SurfaceY(0, 0);
        GlobalPosition = new Vector3(0.5f, surface + 1.01f, 0.5f);
        _verticalVelocity = 0;
        _vitals.Health = 20f;
        _vitals.Hunger = 12f;
        _vitals.Air = 15f;
        _vitals.Dead = false;
    }

    bool FeetIn(ushort id)
    {
        if (_worldHost == null)
        {
            return false;
        }

        int x = Mathf.FloorToInt(GlobalPosition.X);
        int y = Mathf.FloorToInt(GlobalPosition.Y + 0.2f);
        int z = Mathf.FloorToInt(GlobalPosition.Z);
        return _worldHost.World.GetBlock(x, y, z) == id;
    }

    bool HeadIn(ushort id)
    {
        if (_worldHost == null)
        {
            return false;
        }

        int x = Mathf.FloorToInt(GlobalPosition.X);
        int y = Mathf.FloorToInt(GlobalPosition.Y + 1.5f);
        int z = Mathf.FloorToInt(GlobalPosition.Z);
        return _worldHost.World.GetBlock(x, y, z) == id;
    }

    void UseOrPlace()
    {
        if (_worldHost == null)
        {
            return;
        }

        ushort held = _inventory.Ids[_slot];
        if (held == BlockIds.Meat && _inventory.TryConsume(_slot))
        {
            _vitals.Eat();
            ShowHotbar();
            return;
        }

        if (!TryHit(out VoxelHit hit, placing: true))
        {
            Notice("没有看准方块");
            return;
        }

        if (BucketActions.TryUse(_inventory, _slot, _worldHost.World, hit))
        {
            ShowHotbar();
            RefreshInventory();
            return;
        }

        ushort looking = _worldHost.World.GetBlock(hit.Block.X, hit.Block.Y, hit.Block.Z);
        if (looking == (ushort)SurvivalcraftGodot.Simulation.ElectricIds.Button)
        {
            _worldHost.PressCircuitButton(hit.Block.X, hit.Block.Y, hit.Block.Z);
            _worldHost.Dispensers.EjectNeighbors(_worldHost.World, hit.Block.X, hit.Block.Y, hit.Block.Z, out string dispensed);
            if (dispensed.Length > 0)
            {
                _notice = dispensed;
                _noticeTime = 4f;
                ShowHotbar();
            }

            return;
        }

        if (looking == BlockIds.Dispenser)
        {
            _worldHost.Dispensers.TryInsert(_inventory, _slot, hit, out string loaded);
            if (loaded.Length > 0)
            {
                _notice = loaded;
                _noticeTime = 4f;
            }

            ShowHotbar();
            RefreshInventory();
            return;
        }

        if (looking == BlockIds.Furnace)
        {
            _worldHost.Furnaces.TryUse(_inventory, _slot, hit, out string message);
            if (message.Length > 0)
            {
                _notice = message;
                _noticeTime = 4f;
            }

            ShowHotbar();
            RefreshInventory();
            return;
        }

        if (looking == BlockIds.Door)
        {
            byte state = _worldHost.World.GetState(hit.Block.X, hit.Block.Y, hit.Block.Z);
            _worldHost.World.SetBlock(hit.Block.X, hit.Block.Y, hit.Block.Z, BlockIds.Door, BlockShapes.ToggleDoor(state));
            return;
        }

        if (_worldHost.World.GetBlock(hit.Block.X, hit.Block.Y, hit.Block.Z) == BlockIds.Chest)
        {
            ushort taken = _chests.TakeOne(hit.Block.X, hit.Block.Y, hit.Block.Z);
            if (taken == BlockIds.Air)
            {
                ushort give = _inventory.Ids[_slot];
                if (give != BlockIds.Air && give != BlockIds.Chest && _inventory.TryConsume(_slot))
                {
                    _chests.Deposit(hit.Block.X, hit.Block.Y, hit.Block.Z, give, 1);
                }
            }
            else
            {
                _inventory.Add(taken, 1);
            }

            ShowHotbar();
            RefreshInventory();
            return;
        }

        Place();
    }

    public string UseHeldItem()
    {
        UseOrPlace();
        return _hint?.Text ?? "";
    }

    void Dig()
    {
        if (_worldHost == null)
        {
            return;
        }

        if (TryHurtCreature())
        {
            ShowHotbar();
            return;
        }

        if (!TryHit(out VoxelHit hit, placing: false))
        {
            return;
        }

        ushort had = _worldHost.World.GetBlock(hit.Block.X, hit.Block.Y, hit.Block.Z);
        if (!_worldHost.World.SetBlock(hit.Block.X, hit.Block.Y, hit.Block.Z, BlockIds.Air))
        {
            return;
        }

        _inventory.Add(BlockCatalog.DropOf(had), 1);
        _vitals.AddExperience(1);
        if (_inventory.ApplyWear(_slot, out string broken))
        {
            Notice(broken);
        }

        ShowHotbar();
        RefreshInventory();
    }

    void Place()
    {
        if (!TryHit(out VoxelHit hit, placing: true) || _worldHost == null)
        {
            Notice("没有看准方块");
            return;
        }

        Vector3I cell = hit.Previous;
        ushort id = _inventory.Ids[_slot];
        if (id == BlockIds.Air)
        {
            return;
        }
        if (cell == hit.Block || _worldHost.World.GetBlock(cell.X, cell.Y, cell.Z) != BlockIds.Air)
        {
            Notice("这里放不下");
            return;
        }

        byte facing = FacingFromLook(-GlobalTransform.Basis.Z);
        if (VoxelMotion.OverlapsCell(id, facing, cell.X, cell.Y, cell.Z, GlobalPosition))
        {
            Notice("离得太近");
            return;
        }

        if (_inventory.Ids[_slot] != id || !_inventory.TryConsume(_slot))
        {
            return;
        }

        if (!_worldHost.World.SetBlock(cell.X, cell.Y, cell.Z, id, facing))
        {
            _inventory.Add(id, 1);
        }
        else if (SurvivalcraftGodot.Simulation.ElectricIds.IsElectric(id))
        {
            _worldHost.NotifyCircuit(cell.X, cell.Y, cell.Z);
        }

        ShowHotbar();
        RefreshInventory();
    }

    bool TryHit(out VoxelHit hit, bool placing)
    {
        hit = default;
        if (_camera == null || _worldHost == null)
        {
            return false;
        }

        hit = VoxelMotion.Raycast(
            _worldHost.World,
            _camera.GlobalPosition,
            -_camera.GlobalTransform.Basis.Z,
            Reach,
            placing);
        return hit.Hit && hit.Block.Y is >= 0 and < Chunk.Height
            && _worldHost.World.GetBlock(hit.Block.X, hit.Block.Y, hit.Block.Z) != BlockIds.Boundary;
    }

    void BuildInventoryPanel()
    {
        var hud = GetNode<CanvasLayer>("../Hud");
        _inventoryPanel = new PanelContainer();
        _inventoryPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _inventoryPanel.Visible = false;
        _inventoryText = new Label();
        var craft = new Button { Text = "用背包里的材料合成" };
        craft.Pressed += () =>
        {
            _recipes?.TryCraftFromInventory(_inventory, _inventory.Ids[_slot]);
            ShowHotbar();
            RefreshInventory();
        };
        var box = new VBoxContainer();
        box.AddChild(_inventoryText);
        box.AddChild(craft);
        _inventoryPanel.AddChild(box);
        hud.AddChild(_inventoryPanel);
        RefreshInventory();
    }

    void ToggleInventory()
    {
        if (_inventoryPanel == null)
        {
            return;
        }

        _inventoryPanel.Visible = !_inventoryPanel.Visible;
        Input.MouseMode = _inventoryPanel.Visible
            ? Input.MouseModeEnum.Visible
            : Input.MouseModeEnum.Captured;
        RefreshInventory();
    }

    void RefreshInventory()
    {
        if (_inventoryText == null)
        {
            return;
        }

        string text = "背包\n";
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            if (_inventory.Counts[i] <= 0)
            {
                continue;
            }

            text += $"{BlockCatalog.NameOf(_inventory.Ids[i])} x{_inventory.Counts[i]}\n";
        }

        _inventoryText.Text = text;
    }

    bool TryHurtCreature()
    {
        if (_camera == null)
        {
            return false;
        }

        Vector3 origin = _camera.GlobalPosition;
        Vector3 direction = -_camera.GlobalTransform.Basis.Z;
        foreach (Node node in GetTree().GetNodesInGroup("creature"))
        {
            if (node is not Grazer grazer)
            {
                continue;
            }

            Vector3 to = grazer.GlobalPosition - origin;
            if (to.Length() > Reach || to.Normalized().Dot(direction) < 0.75f)
            {
                continue;
            }

            if (grazer.Hurt(2f))
            {
                _inventory.Add(grazer.DropId, grazer.DropCount);
                _vitals.AddExperience(3);
            }

            return true;
        }

        return false;
    }

    static byte FacingFromLook(Vector3 forward)
    {
        if (Mathf.Abs(forward.X) > Mathf.Abs(forward.Z))
        {
            return (byte)(forward.X > 0f ? 0 : 2);
        }

        return (byte)(forward.Z > 0f ? 1 : 3);
    }
}
