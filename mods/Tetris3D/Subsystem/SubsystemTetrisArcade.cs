using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class SubsystemTetrisArcade : SubsystemBlockBehavior, IDrawable, IUpdateable {
        public class Session {
            public Point3 Point;
            public TetrisBoard Board;
            public RenderTarget2D Target;
            public ComponentPlayer Occupant;
            public TetrisInput Input = new();
            public TetrisAudio Audio = new();
            public float AttractTime;
            public bool Dirty = true;
        }

        public const float InteractDistance = 2.6f;
        public const float LeaveDistance = 3.6f;
        public const int ScreenWidth = 256;
        public const int ScreenHeight = 512;

        public readonly Dictionary<Point3, Session> m_sessions = [];
        public readonly PrimitivesRenderer2D m_pr2 = new();
        public readonly PrimitivesRenderer3D m_pr3 = new();
        public readonly TetrisSave m_save = new();
        public static readonly int[] m_drawOrders = [50];
        public SubsystemPlayers m_subsystemPlayers;
        public SubsystemTime m_subsystemTime;
        public float m_discoverTimer;

        public override int[] HandledBlocks => TetrisArcadeBlock.Index > 0 ? [TetrisArcadeBlock.Index] : [];

        public int[] DrawOrders => m_drawOrders;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public float FloatUpdateOrder => 0.5f;

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemPlayers = Project.FindSubsystem<SubsystemPlayers>(true);
            m_subsystemTime = Project.FindSubsystem<SubsystemTime>(true);
            m_save.Read();
            Log.Information($"Tetris3D arcade ready, block index={TetrisArcadeBlock.Index}");
        }

        public override void Dispose() {
            foreach (Session session in m_sessions.Values) {
                session.Target?.Dispose();
            }
            m_sessions.Clear();
            base.Dispose();
        }

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z) {
            EnsureSession(new Point3(x, y, z));
        }

        public override void OnBlockGenerated(int value, int x, int y, int z, bool isLoaded) {
            EnsureSession(new Point3(x, y, z));
        }

        public override void OnBlockRemoved(int value, int newValue, int x, int y, int z) {
            Point3 point = new(x, y, z);
            if (m_sessions.Remove(point, out Session session)) {
                if (session.Occupant != null) {
                    Leave(session, session.Occupant);
                }
                session.Target?.Dispose();
            }
        }

        public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner) {
            ComponentPlayer player = componentMiner.ComponentPlayer;
            if (player == null) {
                return false;
            }
            Point3 point = raycastResult.CellFace.Point;
            if (!IsArcade(point)) {
                return false;
            }
            if (Vector3.Distance(player.ComponentBody.Position, CellCenter(point)) > InteractDistance) {
                player.ComponentGui.DisplaySmallMessage(Tetris3DScreen.T("TooFar"), Color.White, true, false);
                return true;
            }
            Session session = EnsureSession(point);
            if (session.Occupant == player) {
                Leave(session, player);
                return true;
            }
            if (session.Occupant != null && session.Occupant != player) {
                player.ComponentGui.DisplaySmallMessage(Tetris3DScreen.T("Occupied"), Color.White, true, false);
                return true;
            }
            Occupy(session, player);
            return true;
        }

        public void Update(float dt) {
            m_discoverTimer += dt;
            if (m_discoverTimer >= 0.4f) {
                m_discoverTimer = 0f;
                DiscoverNearby();
            }
            List<Point3> remove = null;
            foreach (Session session in m_sessions.Values) {
                if (!IsArcade(session.Point)) {
                    remove ??= [];
                    remove.Add(session.Point);
                    continue;
                }
                session.AttractTime += dt;
                if (session.Occupant != null) {
                    if (session.Occupant.ComponentHealth.Health <= 0f
                        || Vector3.Distance(session.Occupant.ComponentBody.Position, CellCenter(session.Point)) > LeaveDistance) {
                        Leave(session, session.Occupant);
                    }
                    else if (session.Board != null) {
                        FreezeOccupant(session.Occupant);
                        session.Input.PollKeyboard(false);
                        if (session.Input.PausePressed) {
                            session.Board.SetPaused(session.Board.State != TetrisState.Paused);
                        }
                        if (session.Board.State == TetrisState.GameOver
                            && (session.Input.HardDropPressed || session.Input.RotateCwPressed || session.Input.HoldPressed)) {
                            session.Board.Reset();
                        }
                        if (session.Board.State == TetrisState.Playing) {
                            session.Input.Apply(session.Board, dt);
                            session.Board.Tick(dt);
                        }
                        if (session.Board.Score > m_save.HighScore) {
                            m_save.HighScore = session.Board.Score;
                            m_save.Write();
                        }
                        session.Dirty = true;
                    }
                }
                else {
                    session.Dirty = true;
                }
                if (session.Dirty && IsNearAnyPlayer(session.Point)) {
                    try {
                        RefreshTarget(session);
                    }
                    catch (Exception e) {
                        Log.Error($"Tetris3D: refresh screen failed at {session.Point}: {e}");
                    }
                }
            }
            if (remove != null) {
                foreach (Point3 point in remove) {
                    if (m_sessions.Remove(point, out Session session)) {
                        session.Target?.Dispose();
                    }
                }
            }
        }

        public void Draw(Camera camera, int drawOrder) {
            try {
                foreach (Session session in m_sessions.Values) {
                    if (!IsArcade(session.Point)) {
                        continue;
                    }
                    if (Vector3.DistanceSquared(camera.ViewPosition, CellCenter(session.Point)) > 256f) {
                        continue;
                    }
                    if (session.Target == null) {
                        RefreshTarget(session);
                    }
                    if (session.Target == null) {
                        continue;
                    }
                    int face = CellFace.Vector3ToFace(camera.ViewPosition - CellCenter(session.Point), 3);
                    CellFace cellFace = new(session.Point, face);
                    Vector3[] bezel = cellFace.GetFourVertices(0.92f, 0.02f);
                    Vector3[] screen = cellFace.GetFourVertices(0.78f, 0.05f);
                    FlatBatch3D frame = m_pr3.FlatBatch(0, DepthStencilState.DepthRead, RasterizerState.CullNone, BlendState.Opaque);
                    frame.QueueQuad(bezel[0], bezel[1], bezel[2], bezel[3], new Color(20, 24, 32));
                    TexturedBatch3D batch = m_pr3.TexturedBatch(
                        session.Target,
                        false,
                        1,
                        DepthStencilState.DepthRead,
                        RasterizerState.CullNone,
                        BlendState.Opaque,
                        SamplerState.PointClamp
                    );
                    batch.QueueQuad(
                        screen[0],
                        screen[1],
                        screen[2],
                        screen[3],
                        new Vector2(0f, 1f),
                        new Vector2(1f, 1f),
                        new Vector2(1f, 0f),
                        new Vector2(0f, 0f),
                        Color.White
                    );
                }
                m_pr3.Flush(camera.ViewProjectionMatrix);
            }
            catch (Exception e) {
                Log.Error($"Tetris3D: draw screen failed: {e}");
            }
        }

        public bool IsPlayerOccupying(ComponentPlayer player) {
            foreach (Session session in m_sessions.Values) {
                if (session.Occupant == player) {
                    return true;
                }
            }
            return false;
        }

        void Occupy(Session session, ComponentPlayer player) {
            foreach (Session other in m_sessions.Values) {
                if (other.Occupant == player) {
                    Leave(other, player);
                }
            }
            session.Occupant = player;
            session.Board ??= new TetrisBoard();
            session.Board.Sfx = sfx => {
                session.Audio.Enabled = m_save.SoundEnabled;
                session.Audio.Play(sfx);
            };
            if (session.Board.State == TetrisState.GameOver) {
                session.Board.Reset();
            }
            else {
                session.Board.SetPaused(false);
            }
            session.Dirty = true;
            player.ComponentGui.DisplaySmallMessage(Tetris3DScreen.T("PlayingHint"), Color.White, true, false);
            AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
        }

        void Leave(Session session, ComponentPlayer player) {
            if (session.Board != null && session.Board.State == TetrisState.Playing) {
                session.Board.SetPaused(true);
            }
            session.Occupant = null;
            session.Dirty = true;
            if (player != null) {
                player.ComponentGui.DisplaySmallMessage(Tetris3DScreen.T("LeftMachine"), Color.White, true, false);
            }
        }

        static void FreezeOccupant(ComponentPlayer player) {
            ComponentLocomotion locomotion = player.ComponentLocomotion;
            locomotion.WalkOrder = null;
            locomotion.FlyOrder = null;
            locomotion.JumpOrder = 0f;
            locomotion.SwimOrder = null;
        }

        bool IsNearAnyPlayer(Point3 point) {
            Vector3 center = CellCenter(point);
            foreach (ComponentPlayer player in m_subsystemPlayers.ComponentPlayers) {
                if (Vector3.DistanceSquared(player.ComponentBody.Position, center) <= 256f) {
                    return true;
                }
            }
            return false;
        }

        void DiscoverNearby() {
            foreach (ComponentPlayer player in m_subsystemPlayers.ComponentPlayers) {
                Point3 p = Terrain.ToCell(player.ComponentBody.Position);
                for (int x = p.X - 8; x <= p.X + 8; x++) {
                    for (int y = p.Y - 3; y <= p.Y + 5; y++) {
                        for (int z = p.Z - 8; z <= p.Z + 8; z++) {
                            Point3 point = new(x, y, z);
                            if (IsArcade(point)) {
                                EnsureSession(point);
                            }
                        }
                    }
                }
            }
        }

        Session EnsureSession(Point3 point) {
            if (!m_sessions.TryGetValue(point, out Session session)) {
                session = new Session { Point = point };
                m_sessions[point] = session;
            }
            return session;
        }

        bool IsArcade(Point3 point) {
            int contents = Terrain.ExtractContents(SubsystemTerrain.Terrain.GetCellValue(point.X, point.Y, point.Z));
            return contents == TetrisArcadeBlock.Index && TetrisArcadeBlock.Index > 0;
        }

        void RefreshTarget(Session session) {
            session.Target ??= new RenderTarget2D(ScreenWidth, ScreenHeight, 1, ColorFormat.Rgba8888, DepthFormat.None);
            if (!session.Dirty) {
                return;
            }
            session.Dirty = false;
            RenderTarget2D previous = Display.RenderTarget;
            Display.RenderTarget = session.Target;
            try {
                Display.Clear(new Vector4(Color.Black));
                bool attract = session.Occupant == null || session.Board == null;
                TetrisScreenPainter.Paint(m_pr2, session.Board, new Vector2(ScreenWidth, ScreenHeight), attract, session.AttractTime);
                m_pr2.Flush();
            }
            finally {
                Display.RenderTarget = previous;
            }
        }

        static Vector3 CellCenter(Point3 point) => new(point.X + 0.5f, point.Y + 0.5f, point.Z + 0.5f);
    }
}
