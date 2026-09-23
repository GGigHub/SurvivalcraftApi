namespace Game {
    public sealed class TetrisBoard {
        public const int Width = 10;
        public const int VisibleHeight = 20;
        public const int BufferHeight = 2;
        public const int TotalHeight = VisibleHeight + BufferHeight;
        public const float LockDelaySeconds = 0.5f;
        public const int MaxLockResets = 15;
        public const float SoftDropInterval = 0.05f;

        public readonly int[,] Cells = new int[Width, TotalHeight];
        public readonly Queue<PieceType> NextQueue = new();
        public readonly Int2[] m_cellScratch = new Int2[4];
        public TetrisPiece Active;
        public TetrisPiece Hold;
        public bool HoldUsedThisTurn;
        public int Score;
        public int Lines;
        public int Level = 1;
        public TetrisState State = TetrisState.Playing;
        public Action<TetrisSfx> Sfx;

        public SevenBagRandomizer m_bag;
        public float m_gravityTimer;
        public float m_lockTimer;
        public int m_lockResets;
        public bool m_wasGrounded;

        public TetrisBoard(int? seed = null) {
            m_bag = new SevenBagRandomizer(seed);
            Reset();
        }

        public void Reset() {
            Array.Clear(Cells, 0, Cells.Length);
            NextQueue.Clear();
            Hold = null;
            HoldUsedThisTurn = false;
            Score = 0;
            Lines = 0;
            Level = 1;
            State = TetrisState.Playing;
            m_gravityTimer = 0f;
            m_lockTimer = 0f;
            m_lockResets = 0;
            m_wasGrounded = false;
            FillNextQueue();
            SpawnNext();
        }

        public void SetPaused(bool paused) {
            if (State == TetrisState.GameOver) {
                return;
            }
            State = paused ? TetrisState.Paused : TetrisState.Playing;
        }

        public void Tick(float dt) {
            if (State != TetrisState.Playing || Active == null) {
                return;
            }
            bool grounded = IsGrounded();
            if (grounded) {
                m_lockTimer += dt;
                if (m_lockTimer >= LockDelaySeconds) {
                    LockActive();
                }
            }
            else {
                m_lockTimer = 0f;
                m_gravityTimer += dt;
                if (m_gravityTimer >= TetrisScoring.GravityInterval(Level)) {
                    m_gravityTimer = 0f;
                    TryMove(0, -1, false);
                }
            }
            m_wasGrounded = grounded;
        }

        public bool TryMove(int dx, int dy, bool fromPlayer = true) {
            if (State != TetrisState.Playing || Active == null) {
                return false;
            }
            Active.X += dx;
            Active.Y += dy;
            if (!CanPlace(Active)) {
                Active.X -= dx;
                Active.Y -= dy;
                return false;
            }
            OnPlayerShift(fromPlayer && (dx != 0 || dy > 0));
            if (fromPlayer && dx != 0) {
                RaiseSfx(TetrisSfx.Move);
            }
            return true;
        }

        public void SoftDropStep() {
            if (TryMove(0, -1, false)) {
                Score += TetrisScoring.SoftDropScore(1);
            }
        }

        public void HardDrop() {
            if (State != TetrisState.Playing || Active == null) {
                return;
            }
            int dropped = 0;
            while (TryMove(0, -1, false)) {
                dropped++;
            }
            Score += TetrisScoring.HardDropScore(dropped);
            LockActive();
        }

        public bool TryRotate(int dir) {
            if (State != TetrisState.Playing || Active == null || Active.Type == PieceType.Empty) {
                return false;
            }
            int from = Active.Rotation;
            int to = (from + dir + 4) % 4;
            int originX = Active.X;
            int originY = Active.Y;
            Active.Rotation = to;
            Int2[] kicks = TetrisPiece.GetKicks(Active.Type, from, to);
            for (int i = 0; i < kicks.Length; i++) {
                Active.X = originX + kicks[i].X;
                Active.Y = originY + kicks[i].Y;
                if (CanPlace(Active)) {
                    OnPlayerShift(true);
                    RaiseSfx(TetrisSfx.Rotate);
                    return true;
                }
            }
            Active.X = originX;
            Active.Y = originY;
            Active.Rotation = from;
            return false;
        }

        public bool TryHold() {
            if (State != TetrisState.Playing || Active == null || HoldUsedThisTurn) {
                return false;
            }
            HoldUsedThisTurn = true;
            PieceType current = Active.Type;
            if (Hold == null) {
                Hold = new TetrisPiece { Type = current };
                SpawnNext();
            }
            else {
                PieceType stored = Hold.Type;
                Hold.Type = current;
                Spawn(stored);
            }
            RaiseSfx(TetrisSfx.Hold);
            return true;
        }

        public void GetGhostCells(Int2[] dest) {
            if (Active == null) {
                for (int i = 0; i < 4; i++) {
                    dest[i] = new Int2(-1, -1);
                }
                return;
            }
            TetrisPiece ghost = Active.Clone();
            while (true) {
                ghost.Y--;
                if (!CanPlace(ghost)) {
                    ghost.Y++;
                    break;
                }
            }
            ghost.GetCells(dest);
        }

        public IReadOnlyList<PieceType> PeekNext(int count) {
            List<PieceType> list = new(count);
            foreach (PieceType type in NextQueue) {
                if (list.Count >= count) {
                    break;
                }
                list.Add(type);
            }
            return list;
        }

        void FillNextQueue() {
            while (NextQueue.Count < 5) {
                NextQueue.Enqueue(m_bag.Next());
            }
        }

        void SpawnNext() {
            FillNextQueue();
            Spawn(NextQueue.Dequeue());
            FillNextQueue();
        }

        void Spawn(PieceType type) {
            Active = new TetrisPiece {
                Type = type,
                Rotation = 0,
                X = type == PieceType.O ? 4 : 4,
                Y = type == PieceType.I ? 20 : 20
            };
            if (type == PieceType.I) {
                Active.X = 4;
            }
            m_gravityTimer = 0f;
            m_lockTimer = 0f;
            m_lockResets = 0;
            if (!CanPlace(Active)) {
                State = TetrisState.GameOver;
                Active = null;
                RaiseSfx(TetrisSfx.GameOver);
            }
        }

        void LockActive() {
            if (Active == null) {
                return;
            }
            Active.GetCells(m_cellScratch);
            for (int i = 0; i < 4; i++) {
                Int2 cell = m_cellScratch[i];
                if (cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < TotalHeight) {
                    Cells[cell.X, cell.Y] = (int)Active.Type;
                }
            }
            RaiseSfx(TetrisSfx.Lock);
            int cleared = ClearLines();
            if (cleared > 0) {
                Score += TetrisScoring.LineClearScore(cleared, Level);
                Lines += cleared;
                Level = 1 + Lines / 10;
                RaiseSfx(cleared >= 4 ? TetrisSfx.Tetris : TetrisSfx.Clear);
            }
            HoldUsedThisTurn = false;
            SpawnNext();
        }

        int ClearLines() {
            int cleared = 0;
            int writeY = 0;
            int[,] copy = new int[Width, TotalHeight];
            for (int y = 0; y < TotalHeight; y++) {
                bool full = true;
                for (int x = 0; x < Width; x++) {
                    if (Cells[x, y] == 0) {
                        full = false;
                        break;
                    }
                }
                if (full) {
                    cleared++;
                    continue;
                }
                for (int x = 0; x < Width; x++) {
                    copy[x, writeY] = Cells[x, y];
                }
                writeY++;
            }
            Array.Clear(Cells, 0, Cells.Length);
            for (int y = 0; y < writeY; y++) {
                for (int x = 0; x < Width; x++) {
                    Cells[x, y] = copy[x, y];
                }
            }
            return cleared;
        }

        bool CanPlace(TetrisPiece piece) {
            piece.GetCells(m_cellScratch);
            for (int i = 0; i < 4; i++) {
                Int2 cell = m_cellScratch[i];
                if (cell.X < 0 || cell.X >= Width || cell.Y < 0) {
                    return false;
                }
                if (cell.Y >= TotalHeight) {
                    continue;
                }
                if (Cells[cell.X, cell.Y] != 0) {
                    return false;
                }
            }
            return true;
        }

        bool IsGrounded() {
            if (Active == null) {
                return false;
            }
            Active.Y--;
            bool blocked = !CanPlace(Active);
            Active.Y++;
            return blocked;
        }

        void OnPlayerShift(bool resetLock) {
            if (!resetLock) {
                return;
            }
            if (IsGrounded() && m_lockResets < MaxLockResets) {
                m_lockTimer = 0f;
                m_lockResets++;
            }
        }

        void RaiseSfx(TetrisSfx sfx) {
            Sfx?.Invoke(sfx);
        }
    }
}
