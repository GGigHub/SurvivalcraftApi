using Engine;
using Engine.Graphics;

namespace Game {
    public static class TetrisColors {
        public static readonly Color[] ByType = [
            Color.Transparent,
            new Color(0, 220, 220),
            new Color(230, 210, 40),
            new Color(170, 70, 200),
            new Color(230, 140, 30),
            new Color(50, 90, 220),
            new Color(70, 200, 70),
            new Color(220, 50, 50)
        ];
    }

    public sealed class TetrisPlayfieldWidget : Widget {
        public TetrisBoard Board;
        public Vector2 Size = new(280f, 560f);
        public readonly Int2[] m_scratch = new Int2[4];

        public TetrisPlayfieldWidget() {
            IsDrawRequired = true;
            IsHitTestVisible = false;
        }

        public override void Draw(DrawContext dc) {
            if (Board == null) {
                return;
            }
            FlatBatch2D batch = dc.PrimitivesRenderer2D.FlatBatch(0, DepthStencilState.None, null, BlendState.AlphaBlend);
            Vector2 origin = Vector2.Zero;
            float cell = MathF.Min(ActualSize.X / TetrisBoard.Width, ActualSize.Y / TetrisBoard.VisibleHeight);
            float boardW = cell * TetrisBoard.Width;
            float boardH = cell * TetrisBoard.VisibleHeight;
            origin.X = (ActualSize.X - boardW) * 0.5f;
            origin.Y = (ActualSize.Y - boardH) * 0.5f;
            Matrix transform = GlobalTransform;

            batch.QueueQuad(
                Transform(origin, transform),
                Transform(origin + new Vector2(boardW, boardH), transform),
                0f,
                new Color(12, 16, 24, 220)
            );
            Color grid = new(40, 50, 70, 80);
            for (int x = 0; x <= TetrisBoard.Width; x++) {
                Vector2 a = origin + new Vector2(x * cell, 0f);
                Vector2 b = origin + new Vector2(x * cell, boardH);
                batch.QueueLine(Transform(a, transform), Transform(b, transform), 0f, grid);
            }
            for (int y = 0; y <= TetrisBoard.VisibleHeight; y++) {
                Vector2 a = origin + new Vector2(0f, y * cell);
                Vector2 b = origin + new Vector2(boardW, y * cell);
                batch.QueueLine(Transform(a, transform), Transform(b, transform), 0f, grid);
            }

            for (int y = 0; y < TetrisBoard.VisibleHeight; y++) {
                for (int x = 0; x < TetrisBoard.Width; x++) {
                    int value = Board.Cells[x, y];
                    if (value != 0) {
                        DrawCell(batch, origin, cell, x, y, TetrisColors.ByType[value], 1f, transform);
                    }
                }
            }

            if (Board.State != TetrisState.GameOver && Board.Active != null) {
                Board.GetGhostCells(m_scratch);
                DrawPieceCells(batch, origin, cell, m_scratch, Board.Active.Type, 0.35f, transform, false);
                Board.Active.GetCells(m_scratch);
                DrawPieceCells(batch, origin, cell, m_scratch, Board.Active.Type, 1f, transform, true);
            }
        }

        void DrawPieceCells(FlatBatch2D batch, Vector2 origin, float cell, Int2[] cells, PieceType type, float alpha, Matrix transform, bool highlight) {
            Color color = TetrisColors.ByType[(int)type];
            color.A = (byte)Math.Clamp((int)(color.A * alpha), 0, 255);
            for (int i = 0; i < 4; i++) {
                if (cells[i].Y < 0 || cells[i].Y >= TetrisBoard.VisibleHeight) {
                    continue;
                }
                DrawCell(batch, origin, cell, cells[i].X, cells[i].Y, color, alpha, transform, highlight);
            }
        }

        static void DrawCell(FlatBatch2D batch, Vector2 origin, float cell, int x, int y, Color color, float alpha, Matrix transform, bool highlight = true) {
            float pad = cell * 0.06f;
            float depth = cell * 0.20f;
            int drawY = TetrisBoard.VisibleHeight - 1 - y;
            Vector2 p0 = origin + new Vector2(x * cell + pad, drawY * cell + pad);
            Vector2 p1 = origin + new Vector2((x + 1) * cell - pad, (drawY + 1) * cell - pad);
            Vector2 offset = new(depth * 0.45f, -depth * 0.45f);
            Color top = ScaleColor(color, highlight ? 1.25f : 1.05f, alpha);
            Color side = ScaleColor(color, 0.55f, alpha);
            Color face = ScaleColor(color, 1f, alpha);

            Vector2 a = Transform(p0, transform);
            Vector2 b = Transform(new Vector2(p1.X, p0.Y), transform);
            Vector2 c = Transform(p1, transform);
            Vector2 d = Transform(new Vector2(p0.X, p1.Y), transform);
            Vector2 a2 = Transform(p0 + offset, transform);
            Vector2 b2 = Transform(new Vector2(p1.X, p0.Y) + offset, transform);
            Vector2 c2 = Transform(p1 + offset, transform);

            batch.QueueQuad(b, b2, c2, c, 0f, side);
            batch.QueueQuad(a, b, b2, a2, 0f, top);
            batch.QueueQuad(a, b, c, d, 0f, face);
        }

        static Color ScaleColor(Color color, float mul, float alpha) {
            return new Color(
                (byte)Math.Clamp((int)(color.R * mul), 0, 255),
                (byte)Math.Clamp((int)(color.G * mul), 0, 255),
                (byte)Math.Clamp((int)(color.B * mul), 0, 255),
                (byte)Math.Clamp((int)(color.A * alpha), 0, 255)
            );
        }

        static Vector2 Transform(Vector2 local, Matrix m) {
            Vector2.Transform(ref local, ref m, out Vector2 result);
            return result;
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            IsDrawRequired = true;
            DesiredSize = Size;
        }
    }

    public sealed class TetrisMiniPieceWidget : Widget {
        public PieceType Type;
        public Vector2 Size = new(88f, 72f);
        public readonly Int2[] m_scratch = new Int2[4];

        public TetrisMiniPieceWidget() {
            IsDrawRequired = true;
            IsHitTestVisible = false;
        }

        public override void Draw(DrawContext dc) {
            FlatBatch2D batch = dc.PrimitivesRenderer2D.FlatBatch(0, DepthStencilState.None, null, BlendState.AlphaBlend);
            Matrix transform = GlobalTransform;
            batch.QueueQuad(
                Transform(Vector2.Zero, transform),
                Transform(ActualSize, transform),
                0f,
                new Color(10, 12, 18, 160)
            );
            if (Type == PieceType.Empty) {
                return;
            }
            TetrisPiece piece = new() { Type = Type, Rotation = 0, X = 0, Y = 0 };
            piece.GetCells(m_scratch);
            int minX = 99, minY = 99, maxX = -99, maxY = -99;
            for (int i = 0; i < 4; i++) {
                minX = Math.Min(minX, m_scratch[i].X);
                minY = Math.Min(minY, m_scratch[i].Y);
                maxX = Math.Max(maxX, m_scratch[i].X);
                maxY = Math.Max(maxY, m_scratch[i].Y);
            }
            float cell = MathF.Min(ActualSize.X / 5f, ActualSize.Y / 4f);
            float w = (maxX - minX + 1) * cell;
            float h = (maxY - minY + 1) * cell;
            Vector2 origin = new((ActualSize.X - w) * 0.5f, (ActualSize.Y - h) * 0.5f);
            Color color = TetrisColors.ByType[(int)Type];
            for (int i = 0; i < 4; i++) {
                float x = origin.X + (m_scratch[i].X - minX) * cell;
                float y = origin.Y + (maxY - m_scratch[i].Y) * cell;
                Vector2 p0 = new(x + 1f, y + 1f);
                Vector2 p1 = new(x + cell - 1f, y + cell - 1f);
                batch.QueueQuad(Transform(p0, transform), Transform(p1, transform), 0f, color);
            }
        }

        static Vector2 Transform(Vector2 local, Matrix m) {
            Vector2.Transform(ref local, ref m, out Vector2 result);
            return result;
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            IsDrawRequired = true;
            DesiredSize = Size;
        }
    }
}
