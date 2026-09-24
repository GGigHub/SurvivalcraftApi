using Godot;
using System.Collections.Generic;
using System.IO;

namespace SurvivalcraftGodot;

public partial class WorldHost : Node3D
{
    public VoxelWorld World { get; } = new();

    readonly WorldElectric _electric = new();
    public FurnaceStore Furnaces { get; } = new();
    public DispenserStore Dispensers { get; } = new();
    RecipeBook? _recipes;

    public void NotifyCircuit(int x, int y, int z) => _electric.Rebuild(World, x, y, z);

    public bool PressCircuitButton(int x, int y, int z)
    {
        if (!_electric.Active)
        {
            _electric.Rebuild(World, x, y, z);
        }

        return _electric.PressButton(x, y, z);
    }

    readonly Dictionary<ChunkPos, ChunkDraw> _draws = new();
    ShaderMaterial? _material;
    WorldEnvironment? _sky;
    GpuParticles3D? _precip;
    PlayerController? _player;
    bool _spawnReady;
    float _clock = 1.2f;
    float _bolt;
    public float Sun { get; private set; } = 1f;
    public float Clock => _clock;
    public WeatherKind Weather => WorldClimate.WeatherAt(_clock);
    public int Season => WorldClimate.SeasonIndex(_clock);
    public string WeatherLabel => WorldClimate.SeasonName(Season) + WorldClimate.WeatherName(Weather);

    public void AdvanceToMorning()
    {
        _clock = 1.2f;
    }

    public void SetClock(float clock) => _clock = clock;

    public override void _Ready()
    {
        if (Acceptance.Requested())
        {
            int code = Acceptance.Run();
            GetTree().Quit(code);
            return;
        }

        string root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        OriginalBlockData.Load(root);
        _recipes = RecipeBook.Load(
            System.IO.Path.Combine(root, "Survivalcraft", "Content", "Assets", "CraftingRecipes.xml"),
            System.IO.Path.Combine(root, "Survivalcraft", "Block"),
            System.IO.Path.Combine(root, "Survivalcraft", "Content", "Assets", "BlocksData.txt"));
        Shader shader = GD.Load<Shader>("res://assets/shaders/terrain.gdshader");
        _material = new ShaderMaterial { Shader = shader };
        _material.SetShaderParameter("sun", 1f);
        _material.SetShaderParameter("atlas", GD.Load<Texture2D>("res://assets/textures/Blocks.webp"));
        _sky = GetParent().GetNode<WorldEnvironment>("WorldEnvironment");
        _precip = CreatePrecipitation();
        AddChild(_precip);
        _player = GetParent().GetNode<PlayerController>("Player");
        BuildSpawn();
    }

    public override void _Process(double delta)
    {
        if (!_spawnReady || _player == null || _material == null)
        {
            return;
        }

        _clock += (float)delta * 0.035f;
        float wave = Mathf.Sin(_clock);
        Sun = Mathf.Lerp(0.1f, 1f, Mathf.Clamp((wave + 0.2f) / 1.2f, 0f, 1f));
        _material.SetShaderParameter("sun", Sun);
        ApplySkyAndWeather();
        if (Weather == WeatherKind.Thunder && _player != null && WorldClimate.AdvanceBolt(ref _bolt, (float)delta))
        {
            int x = Mathf.FloorToInt(_player.GlobalPosition.X);
            int y = Mathf.FloorToInt(_player.GlobalPosition.Y + 2f);
            int z = Mathf.FloorToInt(_player.GlobalPosition.Z);
            if (World.SampleSky(x, y, z) >= 12)
            {
                _player.Hurt(5f, "雷击");
            }
        }
        else if (Weather != WeatherKind.Thunder)
        {
            _bolt = 0f;
        }
        if (_precip != null && _player != null)
        {
            _precip.GlobalPosition = _player.GlobalPosition + new Vector3(0f, 8f, 0f);
        }

        World.TickFlow(8);
        World.TickLight(2048);
        Furnaces.Tick((float)delta, _recipes, World);
        _electric.Step(World);
        StreamAround(_player.GlobalPosition);
        UploadDirty(6);
    }

    public override void _ExitTree()
    {
        foreach (ChunkDraw draw in _draws.Values)
        {
            draw.Free();
        }

        _draws.Clear();
    }

    void BuildSpawn()
    {
        if (WorldSession.Continue && WorldSave.TryLoad(World, out Inventory loaded, out float time))
        {
            WorldSession.Inventory = loaded;
            _clock = time;
            foreach (ChunkPos pos in World.Chunks)
            {
                Upload(pos);
            }

            _spawnReady = true;
            return;
        }

        const int radius = 2;
        var region = new HashSet<ChunkPos>();
        for (int z = -radius; z <= radius; z++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                ChunkPos pos = new(x, z);
                World.Generate(pos);
                region.Add(pos);
            }
        }

        foreach (ChunkPos pos in region)
        {
            Upload(pos);
        }

        int ground = TerrainGenerator.SurfaceY(6, 6);
        for (int i = 0; i < 2; i++)
        {
            var grazer = new Grazer();
            AddChild(grazer);
            grazer.Setup(World, new Vector3(6f + (i * 3f), ground + 1.01f, 8f));
        }

        var bird = new Grazer();
        AddChild(bird);
        bird.Setup(World, new Vector3(10f, ground + 1.01f, 12f), new Color(0.35f, 0.45f, 0.7f), new Vector3(0.35f, 0.35f, 0.45f), 3f, BlockIds.Meat, 1, false, 0.3f);
        var wolf = new Grazer();
        AddChild(wolf);
        wolf.Setup(World, new Vector3(18f, ground + 1.01f, 4f), new Color(0.45f, 0.45f, 0.48f), new Vector3(0.7f, 0.7f, 1.2f), 10f, BlockIds.Meat, 2, true, 0.6f);

        _spawnReady = true;
    }

    void ApplySkyAndWeather()
    {
        WeatherKind weather = Weather;
        bool falling = weather is WeatherKind.Rain or WeatherKind.Snow or WeatherKind.Thunder;
        if (_precip != null)
        {
            _precip.Emitting = falling;
            if (_precip.DrawPass1 is QuadMesh quad)
            {
                quad.Size = weather == WeatherKind.Snow ? new Vector2(0.08f, 0.08f) : new Vector2(0.05f, 0.45f);
            }
        }

        if (_sky?.Environment == null)
        {
            return;
        }

        Color night = new(0.02f, 0.03f, 0.08f);
        Color day = weather switch
        {
            WeatherKind.Rain => new Color(0.35f, 0.4f, 0.45f),
            WeatherKind.Thunder => new Color(0.22f, 0.24f, 0.3f),
            WeatherKind.Snow => new Color(0.75f, 0.78f, 0.82f),
            _ => new Color(0.53f, 0.74f, 0.92f),
        };
        _sky.Environment.BackgroundColor = night.Lerp(day, Sun);
    }

    static GpuParticles3D CreatePrecipitation()
    {
        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(12f, 0.5f, 12f),
            Direction = new Vector3(0f, -1f, 0f),
            Spread = 6f,
            InitialVelocityMin = 10f,
            InitialVelocityMax = 14f,
            Gravity = new Vector3(0f, -6f, 0f),
        };
        return new GpuParticles3D
        {
            Amount = 600,
            Lifetime = 1.1f,
            VisibilityAabb = new Aabb(new Vector3(-16f, -12f, -16f), new Vector3(32f, 20f, 32f)),
            ProcessMaterial = material,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.05f, 0.45f) },
            Emitting = false,
        };
    }

    void StreamAround(Vector3 feet)
    {
        ChunkPos center = VoxelWorld.CoordOf(Mathf.FloorToInt(feet.X), Mathf.FloorToInt(feet.Z));
        const int loadRadius = 4;
        const int unloadRadius = 6;
        ChunkPos? generated = null;
        for (int z = -loadRadius; z <= loadRadius && generated == null; z++)
        {
            for (int x = -loadRadius; x <= loadRadius; x++)
            {
                ChunkPos pos = new(center.X + x, center.Z + z);
                if (World.TryGetChunk(pos, out _))
                {
                    continue;
                }

                World.Generate(pos);
                generated = pos;
                break;
            }
        }

        var remove = new List<ChunkPos>();
        foreach (ChunkPos pos in World.Chunks)
        {
            int dx = pos.X - center.X;
            int dz = pos.Z - center.Z;
            if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) > unloadRadius)
            {
                remove.Add(pos);
            }
        }

        foreach (ChunkPos pos in remove)
        {
            World.Remove(pos);
            if (_draws.Remove(pos, out ChunkDraw? draw))
            {
                draw.Free();
            }
        }
    }

    void UploadDirty(int budget)
    {
        foreach (ChunkPos pos in World.Chunks)
        {
            if (budget <= 0)
            {
                return;
            }

            if (!World.TryGetChunk(pos, out Chunk? chunk) || !chunk.MeshDirty)
            {
                continue;
            }

            Upload(pos);
            budget--;
        }
    }

    void Upload(ChunkPos pos)
    {
        if (!World.TryGetChunk(pos, out Chunk? chunk))
        {
            return;
        }

        chunk.MeshDirty = false;
        ChunkMeshData meshData = ChunkMesher.Build(World, pos);
        if (!_draws.TryGetValue(pos, out ChunkDraw? draw))
        {
            draw = ChunkDraw.Create(GetWorld3D().Scenario, pos);
            _draws.Add(pos, draw);
        }

        draw.Upload(meshData, _material!);
    }

    sealed class ChunkDraw
    {
        public Rid MeshRid { get; private set; }
        public Rid InstanceRid { get; private set; }

        public static ChunkDraw Create(Rid scenario, ChunkPos pos)
        {
            var draw = new ChunkDraw
            {
                MeshRid = RenderingServer.MeshCreate(),
                InstanceRid = RenderingServer.InstanceCreate(),
            };
            RenderingServer.InstanceSetBase(draw.InstanceRid, draw.MeshRid);
            RenderingServer.InstanceSetScenario(draw.InstanceRid, scenario);
            Vector3 origin = new(pos.X * Chunk.Size, 0, pos.Z * Chunk.Size);
            RenderingServer.InstanceSetTransform(draw.InstanceRid, new Transform3D(Basis.Identity, origin));
            return draw;
        }

        public void Upload(ChunkMeshData meshData, Material material)
        {
            RenderingServer.MeshClear(MeshRid);
            if (meshData.Vertices.Length == 0)
            {
                RenderingServer.InstanceSetVisible(InstanceRid, false);
                return;
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = meshData.Vertices;
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = meshData.Uvs;
            arrays[(int)Godot.Mesh.ArrayType.TexUV2] = meshData.Uv2;
            arrays[(int)Godot.Mesh.ArrayType.Color] = meshData.Colors;
            arrays[(int)Godot.Mesh.ArrayType.Index] = meshData.Indices;
            RenderingServer.MeshAddSurfaceFromArrays(MeshRid, RenderingServer.PrimitiveType.Triangles, arrays);
            RenderingServer.MeshSurfaceSetMaterial(MeshRid, 0, material.GetRid());
            RenderingServer.InstanceSetVisible(InstanceRid, true);
        }

        public void Free()
        {
            if (InstanceRid.IsValid)
            {
                RenderingServer.FreeRid(InstanceRid);
            }

            if (MeshRid.IsValid)
            {
                RenderingServer.FreeRid(MeshRid);
            }
        }
    }
}
