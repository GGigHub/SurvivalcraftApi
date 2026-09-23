using Engine;
using Engine.Graphics;
using Engine.Media;

namespace Game {
    public static class TetrisScreenPainter {
        public static readonly Int2[] Scratch = new Int2[4];

        public static void Paint(PrimitivesRenderer2D renderer, TetrisBoard board, Vector2 size, bool attract, float time) {
            FlatBatch2D batch = renderer.FlatBatch(0, DepthStencilState.None, null, BlendState.Opaque);
            batch.QueueQuad(Vector2.Zero, size, 0f, new Color(8, 10, 16));
            if (attract || board == null) {
                PaintAttract(renderer, size, time);
                return;
            }
            float cell = MathF.Min(size.X / (TetrisBoard.Width + 6), size.Y / (TetrisBoard.VisibleHeight + 2));
            Vector2 origin = new(cell * 0.6f, (size.Y - cell * TetrisBoard.VisibleHeight) * 0.5f);
            float boardW = cell * TetrisBoard.Width;
            float boardH = cell * TetrisBoard.VisibleHeight;
            batch.QueueQuad(origin, origin + new Vector2(boardW, boardH), 0f, new Color(12, 16, 24));
            Color grid = new(40, 50, 70, 90);
            for (int x = 0; x <= TetrisBoard.Width; x++) {
                batch.QueueLine(origin + new Vector2(x * cell, 0f), origin + new Vector2(x * cell, boardH), 0f, grid);
            }
            for (int y = 0; y <= TetrisBoard.VisibleHeight; y++) {
                batch.QueueLine(origin + new Vector2(0f, y * cell), origin + new Vector2(boardW, y * cell), 0f, grid);
            }
            for (int y = 0; y < TetrisBoard.VisibleHeight; y++) {
                for (int x = 0; x < TetrisBoard.Width; x++) {
                    int value = board.Cells[x, y];
                    if (value != 0) {
                        PaintCell(batch, origin, cell, x, y, TetrisColors.ByType[value], 1f, true);
                    }
                }
            }
            if (board.State != TetrisState.GameOver && board.Active != null) {
                board.GetGhostCells(Scratch);
                PaintCells(batch, origin, cell, Scratch, board.Active.Type, 0.35f, false);
                board.Active.GetCells(Scratch);
                PaintCells(batch, origin, cell, Scratch, board.Active.Type, 1f, true);
            }
            FontBatch2D font = renderer.FontBatch(LabelWidget.BitmapFont, 1, DepthStencilState.None, null, BlendState.AlphaBlend, SamplerState.LinearClamp);
            float infoX = origin.X + boardW + cell * 0.4f;
            float scale = MathF.Max(0.35f, cell / 28f);
            font.QueueText($"{board.Score}", new Vector2(infoX, origin.Y + cell), 0f, Color.White, TextAnchor.Default);
            font.QueueText($"Lv {board.Level}", new Vector2(infoX, origin.Y + cell * 3f), 0f, Color.LightGray, TextAnchor.Default);
            font.QueueText($"L {board.Lines}", new Vector2(infoX, origin.Y + cell * 5f), 0f, Color.LightGray, TextAnchor.Default);
            if (board.State == TetrisState.Paused) {
                font.QueueText("PAUSE", size * 0.5f, 0f, Color.Yellow, TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter);
            }
            else if (board.State == TetrisState.GameOver) {
                font.QueueText("GAME OVER", size * 0.5f, 0f, new Color(255, 160, 60), TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter);
            }
        }

        static void PaintAttract(PrimitivesRenderer2D renderer, Vector2 size, float time) {
            FlatBatch2D batch = renderer.FlatBatch(1, DepthStencilState.None, null, BlendState.AlphaBlend);
            for (int i = 0; i < 7; i++) {
                float y = size.Y * 0.25f + i * 18f + MathF.Sin(time * 2f + i) * 6f;
                batch.QueueQuad(new Vector2(size.X * 0.2f, y), new Vector2(size.X * 0.8f, y + 12f), 0f, TetrisColors.ByType[i + 1]);
            }
            FontBatch2D font = renderer.FontBatch(LabelWidget.BitmapFont, 2, DepthStencilState.None, null, BlendState.AlphaBlend, SamplerState.LinearClamp);
            font.QueueText("TETRIS", new Vector2(size.X * 0.5f, size.Y * 0.12f), 0f, Color.White, TextAnchor.HorizontalCenter);
            bool blink = (int)(time * 2f) % 2 == 0;
            if (blink) {
                font.QueueText("USE", new Vector2(size.X * 0.5f, size.Y * 0.82f), 0f, Color.Yellow, TextAnchor.HorizontalCenter);
            }
        }

        static void PaintCells(FlatBatch2D batch, Vector2 origin, float cell, Int2[] cells, PieceType type, float alpha, bool highlight) {
            Color color = TetrisColors.ByType[(int)type];
            for (int i = 0; i < 4; i++) {
                if (cells[i].Y < 0 || cells[i].Y >= TetrisBoard.VisibleHeight) {
                    continue;
                }
                PaintCell(batch, origin, cell, cells[i].X, cells[i].Y, color, alpha, highlight);
            }
        }

        static void PaintCell(FlatBatch2D batch, Vector2 origin, float cell, int x, int y, Color color, float alpha, bool highlight) {
            float pad = cell * 0.06f;
            float depth = cell * 0.18f;
            int drawY = TetrisBoard.VisibleHeight - 1 - y;
            Vector2 p0 = origin + new Vector2(x * cell + pad, drawY * cell + pad);
            Vector2 p1 = origin + new Vector2((x + 1) * cell - pad, (drawY + 1) * cell - pad);
            Vector2 offset = new(depth * 0.4f, -depth * 0.4f);
            Color face = Scale(color, 1f, alpha);
            Color top = Scale(color, highlight ? 1.25f : 1.05f, alpha);
            Color side = Scale(color, 0.55f, alpha);
            batch.QueueQuad(new Vector2(p1.X, p0.Y), new Vector2(p1.X, p0.Y) + offset, p1 + offset, p1, 0f, side);
            batch.QueueQuad(p0, new Vector2(p1.X, p0.Y), new Vector2(p1.X, p0.Y) + offset, p0 + offset, 0f, top);
            batch.QueueQuad(p0, new Vector2(p1.X, p0.Y), p1, new Vector2(p0.X, p1.Y), 0f, face);
        }

        static Color Scale(Color color, float mul, float alpha) {
            return new Color(
                (byte)Math.Clamp((int)(color.R * mul), 0, 255),
                (byte)Math.Clamp((int)(color.G * mul), 0, 255),
                (byte)Math.Clamp((int)(color.B * mul), 0, 255),
                (byte)Math.Clamp((int)(color.A * alpha), 0, 255)
            );
        }
    }
}
