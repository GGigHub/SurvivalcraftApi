namespace Game {
    public sealed class TetrisAudio {
        public bool Enabled = true;

        public void Play(TetrisSfx sfx) {
            if (!Enabled) {
                return;
            }
            switch (sfx) {
                case TetrisSfx.Move:
                    AudioManager.PlaySound("Audio/UI/ButtonClick", 0.25f, 0.2f, 0f);
                    break;
                case TetrisSfx.Rotate:
                    AudioManager.PlaySound("Audio/Click", 0.45f, 0.15f, 0f);
                    break;
                case TetrisSfx.Hold:
                    AudioManager.PlaySound("Audio/Click", 0.5f, -0.1f, 0f);
                    break;
                case TetrisSfx.Lock:
                    AudioManager.PlaySound("Audio/Stone", 0.4f, 0f, 0f);
                    break;
                case TetrisSfx.Clear:
                    AudioManager.PlaySound("Audio/Swoosh", 0.6f, 0.1f, 0f);
                    break;
                case TetrisSfx.Tetris:
                    AudioManager.PlaySound("Audio/Thunder", 0.5f, 0.2f, 0f);
                    break;
                case TetrisSfx.GameOver:
                    AudioManager.PlaySound("Audio/Death", 0.55f, -0.2f, 0f);
                    break;
            }
        }
    }
}
