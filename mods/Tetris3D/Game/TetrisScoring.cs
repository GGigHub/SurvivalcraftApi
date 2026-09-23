namespace Game {
    public static class TetrisScoring {
        public static int LineClearScore(int linesCleared, int level) => linesCleared switch {
            1 => 100 * level,
            2 => 300 * level,
            3 => 500 * level,
            4 => 800 * level,
            _ => 0
        };

        public static int SoftDropScore(int cells) => cells;

        public static int HardDropScore(int cells) => cells * 2;

        public static float GravityInterval(int level) {
            if (level <= 1) {
                return 0.80f;
            }
            if (level == 2) {
                return 0.72f;
            }
            if (level == 3) {
                return 0.63f;
            }
            if (level == 4) {
                return 0.55f;
            }
            if (level == 5) {
                return 0.47f;
            }
            if (level == 6) {
                return 0.38f;
            }
            if (level == 7) {
                return 0.30f;
            }
            if (level == 8) {
                return 0.22f;
            }
            if (level == 9) {
                return 0.13f;
            }
            return MathF.Max(0.05f, 0.10f - 0.01f * (level - 10));
        }
    }
}
