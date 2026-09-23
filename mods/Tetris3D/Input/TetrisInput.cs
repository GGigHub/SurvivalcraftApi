using Engine.Input;

namespace Game {
    public sealed class TetrisInput {
        public const float DasDelay = 0.17f;
        public const float ArrInterval = 0.05f;

        public bool LeftHeld;
        public bool RightHeld;
        public bool SoftDropHeld;
        public bool HardDropPressed;
        public bool RotateCwPressed;
        public bool RotateCcwPressed;
        public bool HoldPressed;
        public bool PausePressed;

        public float m_leftHoldTime = -1f;
        public float m_rightHoldTime = -1f;
        public float m_softDropTime;

        public void PollKeyboard(bool includeEscape = true) {
            LeftHeld = Keyboard.IsKeyDown(Key.LeftArrow) || Keyboard.IsKeyDown(Key.A);
            RightHeld = Keyboard.IsKeyDown(Key.RightArrow) || Keyboard.IsKeyDown(Key.D);
            SoftDropHeld = Keyboard.IsKeyDown(Key.DownArrow) || Keyboard.IsKeyDown(Key.S);
            HardDropPressed = Keyboard.IsKeyDownOnce(Key.Space);
            RotateCwPressed = Keyboard.IsKeyDownOnce(Key.UpArrow) || Keyboard.IsKeyDownOnce(Key.X);
            RotateCcwPressed = Keyboard.IsKeyDownOnce(Key.Z);
            HoldPressed = Keyboard.IsKeyDownOnce(Key.C) || Keyboard.IsKeyDownOnce(Key.Shift);
            PausePressed = Keyboard.IsKeyDownOnce(Key.P) || (includeEscape && Keyboard.IsKeyDownOnce(Key.Escape));
        }

        public void ClearEdges() {
            HardDropPressed = false;
            RotateCwPressed = false;
            RotateCcwPressed = false;
            HoldPressed = false;
            PausePressed = false;
        }

        public void Apply(TetrisBoard board, float dt) {
            if (board.State != TetrisState.Playing) {
                m_leftHoldTime = -1f;
                m_rightHoldTime = -1f;
                m_softDropTime = 0f;
                return;
            }
            ApplyHorizontal(board, dt, LeftHeld, RightHeld);
            if (SoftDropHeld) {
                m_softDropTime += dt;
                while (m_softDropTime >= TetrisBoard.SoftDropInterval) {
                    m_softDropTime -= TetrisBoard.SoftDropInterval;
                    board.SoftDropStep();
                }
            }
            else {
                m_softDropTime = TetrisBoard.SoftDropInterval;
            }
            if (HardDropPressed) {
                board.HardDrop();
            }
            if (RotateCwPressed) {
                board.TryRotate(1);
            }
            if (RotateCcwPressed) {
                board.TryRotate(-1);
            }
            if (HoldPressed) {
                board.TryHold();
            }
        }

        void ApplyHorizontal(TetrisBoard board, float dt, bool left, bool right) {
            if (left == right) {
                m_leftHoldTime = -1f;
                m_rightHoldTime = -1f;
                return;
            }
            if (left) {
                m_rightHoldTime = -1f;
                StepAxis(board, dt, ref m_leftHoldTime, -1);
            }
            else {
                m_leftHoldTime = -1f;
                StepAxis(board, dt, ref m_rightHoldTime, 1);
            }
        }

        static void StepAxis(TetrisBoard board, float dt, ref float holdTime, int dx) {
            if (holdTime < 0f) {
                board.TryMove(dx, 0);
                holdTime = 0f;
                return;
            }
            holdTime += dt;
            if (holdTime < DasDelay) {
                return;
            }
            float extra = holdTime - DasDelay;
            int steps = 1 + (int)(extra / ArrInterval);
            holdTime = DasDelay + extra - (steps - 1) * ArrInterval;
            for (int i = 0; i < steps; i++) {
                if (!board.TryMove(dx, 0)) {
                    break;
                }
            }
        }
    }
}
