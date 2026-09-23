namespace Game {
    public enum PieceType {
        Empty = 0,
        I = 1,
        O = 2,
        T = 3,
        L = 4,
        J = 5,
        S = 6,
        Z = 7
    }

    public enum TetrisState {
        Playing,
        Paused,
        GameOver
    }

    public enum TetrisSfx {
        Move,
        Rotate,
        Lock,
        Clear,
        Tetris,
        GameOver,
        Hold
    }

    public sealed class TetrisPiece {
        public PieceType Type;
        public int Rotation;
        public int X;
        public int Y;

        public TetrisPiece Clone() => new() {
            Type = Type,
            Rotation = Rotation,
            X = X,
            Y = Y
        };

        public void GetCells(Int2[] dest) {
            Int2[] local = Shapes[(int)Type - 1, Rotation];
            for (int i = 0; i < 4; i++) {
                dest[i] = new Int2(X + local[i].X, Y + local[i].Y);
            }
        }

        public static readonly Int2[,][] Shapes = CreateShapes();

        static Int2[,][] CreateShapes() {
            Int2[,][] shapes = new Int2[7, 4][];
            shapes[0, 0] = [new(-1, 0), new(0, 0), new(1, 0), new(2, 0)];
            shapes[0, 1] = [new(1, 1), new(1, 0), new(1, -1), new(1, -2)];
            shapes[0, 2] = [new(-1, -1), new(0, -1), new(1, -1), new(2, -1)];
            shapes[0, 3] = [new(0, 1), new(0, 0), new(0, -1), new(0, -2)];

            shapes[1, 0] = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
            shapes[1, 1] = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
            shapes[1, 2] = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
            shapes[1, 3] = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];

            shapes[2, 0] = [new(-1, 0), new(0, 0), new(1, 0), new(0, 1)];
            shapes[2, 1] = [new(0, 1), new(0, 0), new(0, -1), new(1, 0)];
            shapes[2, 2] = [new(-1, 0), new(0, 0), new(1, 0), new(0, -1)];
            shapes[2, 3] = [new(0, 1), new(0, 0), new(0, -1), new(-1, 0)];

            shapes[3, 0] = [new(-1, 0), new(0, 0), new(1, 0), new(1, 1)];
            shapes[3, 1] = [new(0, 1), new(0, 0), new(0, -1), new(1, -1)];
            shapes[3, 2] = [new(-1, -1), new(-1, 0), new(0, 0), new(1, 0)];
            shapes[3, 3] = [new(-1, 1), new(0, 1), new(0, 0), new(0, -1)];

            shapes[4, 0] = [new(-1, 1), new(-1, 0), new(0, 0), new(1, 0)];
            shapes[4, 1] = [new(1, 1), new(0, 1), new(0, 0), new(0, -1)];
            shapes[4, 2] = [new(-1, 0), new(0, 0), new(1, 0), new(1, -1)];
            shapes[4, 3] = [new(0, 1), new(0, 0), new(0, -1), new(-1, -1)];

            shapes[5, 0] = [new(-1, 0), new(0, 0), new(0, 1), new(1, 1)];
            shapes[5, 1] = [new(0, 1), new(0, 0), new(1, 0), new(1, -1)];
            shapes[5, 2] = [new(-1, -1), new(0, -1), new(0, 0), new(1, 0)];
            shapes[5, 3] = [new(-1, 1), new(-1, 0), new(0, 0), new(0, -1)];

            shapes[6, 0] = [new(-1, 1), new(0, 1), new(0, 0), new(1, 0)];
            shapes[6, 1] = [new(1, 1), new(1, 0), new(0, 0), new(0, -1)];
            shapes[6, 2] = [new(-1, 0), new(0, 0), new(0, -1), new(1, -1)];
            shapes[6, 3] = [new(0, 1), new(0, 0), new(-1, 0), new(-1, -1)];
            return shapes;
        }

        public static Int2[] GetKicks(PieceType type, int fromRotation, int toRotation) {
            if (type == PieceType.O) {
                return s_oKicks;
            }
            int index = KickIndex(fromRotation, toRotation);
            return type == PieceType.I ? s_iKicks[index] : s_jlstzKicks[index];
        }

        static int KickIndex(int from, int to) {
            if (from == 0 && to == 1) {
                return 0;
            }
            if (from == 1 && to == 0) {
                return 1;
            }
            if (from == 1 && to == 2) {
                return 2;
            }
            if (from == 2 && to == 1) {
                return 3;
            }
            if (from == 2 && to == 3) {
                return 4;
            }
            if (from == 3 && to == 2) {
                return 5;
            }
            if (from == 3 && to == 0) {
                return 6;
            }
            return 7;
        }

        static readonly Int2[] s_oKicks = [new(0, 0)];

        static readonly Int2[][] s_jlstzKicks = [
            [new(0, 0), new(-1, 0), new(-1, 1), new(0, -2), new(-1, -2)],
            [new(0, 0), new(1, 0), new(1, -1), new(0, 2), new(1, 2)],
            [new(0, 0), new(1, 0), new(1, -1), new(0, 2), new(1, 2)],
            [new(0, 0), new(-1, 0), new(-1, 1), new(0, -2), new(-1, -2)],
            [new(0, 0), new(1, 0), new(1, 1), new(0, -2), new(1, -2)],
            [new(0, 0), new(-1, 0), new(-1, -1), new(0, 2), new(-1, 2)],
            [new(0, 0), new(-1, 0), new(-1, -1), new(0, 2), new(-1, 2)],
            [new(0, 0), new(1, 0), new(1, 1), new(0, -2), new(1, -2)]
        ];

        static readonly Int2[][] s_iKicks = [
            [new(0, 0), new(-2, 0), new(1, 0), new(-2, -1), new(1, 2)],
            [new(0, 0), new(2, 0), new(-1, 0), new(2, 1), new(-1, -2)],
            [new(0, 0), new(-1, 0), new(2, 0), new(-1, 2), new(2, -1)],
            [new(0, 0), new(1, 0), new(-2, 0), new(1, -2), new(-2, 1)],
            [new(0, 0), new(2, 0), new(-1, 0), new(2, 1), new(-1, -2)],
            [new(0, 0), new(-2, 0), new(1, 0), new(-2, -1), new(1, 2)],
            [new(0, 0), new(1, 0), new(-2, 0), new(1, -2), new(-2, 1)],
            [new(0, 0), new(-1, 0), new(2, 0), new(-1, 2), new(2, -1)]
        ];
    }
}
