using Engine;

namespace Game {
    public class Tetris3DScreen : Screen {
        public TetrisBoard m_board;
        public TetrisInput m_input = new();
        public TetrisSave m_save = new();
        public TetrisAudio m_audio = new();

        public BevelledButtonWidget m_backButton;
        public BevelledButtonWidget m_pauseButton;
        public BevelledButtonWidget m_soundButton;
        public BevelledButtonWidget m_restartButton;
        public BevelledButtonWidget m_overlayRestart;
        public BevelledButtonWidget m_overlayBack;
        public LabelWidget m_scoreLabel;
        public LabelWidget m_levelLabel;
        public LabelWidget m_linesLabel;
        public LabelWidget m_highScoreLabel;
        public LabelWidget m_pauseTitle;
        public LabelWidget m_gameOverTitle;
        public LabelWidget m_newRecordLabel;
        public TetrisPlayfieldWidget m_playfield;
        public TetrisMiniPieceWidget m_holdPiece;
        public TetrisMiniPieceWidget[] m_nextPieces = new TetrisMiniPieceWidget[3];
        public CanvasWidget m_overlay;
        public BevelledButtonWidget m_touchLeft;
        public BevelledButtonWidget m_touchRight;
        public BevelledButtonWidget m_touchSoft;
        public BevelledButtonWidget m_touchHard;
        public BevelledButtonWidget m_touchCw;
        public BevelledButtonWidget m_touchCcw;
        public BevelledButtonWidget m_touchHold;
        public bool m_newRecordThisGame;

        public Tetris3DScreen() {
            Children.Add(
                new RectangleWidget {
                    Size = new Vector2(float.PositiveInfinity),
                    FillColor = new Color(8, 10, 16),
                    OutlineColor = Color.Transparent,
                    OutlineThickness = 0f
                }
            );

            StackPanelWidget root = new() {
                Direction = LayoutDirection.Vertical,
                HorizontalAlignment = WidgetAlignment.Stretch,
                VerticalAlignment = WidgetAlignment.Stretch
            };
            Children.Add(root);

            root.Children.Add(BuildTopBar());
            root.Children.Add(BuildMainRow());
            root.Children.Add(BuildTouchBar());

            m_overlay = BuildOverlay();
            Children.Add(m_overlay);
        }

        public static string T(string key) {
            string text = LanguageControl.Get("Tetris3D", key);
            return string.IsNullOrEmpty(text) || text == "Tetris3D:" + key ? Fallback(key) : text;
        }

        static string Fallback(string key) => key switch {
            "MenuButton" => "俄罗斯方块",
            "Back" => "返回",
            "Pause" => "暂停",
            "Resume" => "继续",
            "Restart" => "重新开始",
            "Score" => "分数",
            "Level" => "等级",
            "Lines" => "行数",
            "HighScore" => "最高分",
            "Hold" => "保留",
            "Next" => "下一个",
            "GameOver" => "游戏结束",
            "Sound" => "音效",
            "NewRecord" => "新纪录！",
            "TooFar" => "再靠近一点才能操作游戏机",
            "Occupied" => "这台游戏机正在被使用",
            "PlayingHint" => "方向键/AD 移动，空格硬降，P 暂停，再交互离开",
            "LeftMachine" => "已离开游戏机",
            _ => key
        };

        StackPanelWidget BuildTopBar() {
            StackPanelWidget bar = new() {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Stretch,
                Margin = new Vector2(8f, 6f)
            };
            m_backButton = SmallButton(T("Back"));
            m_pauseButton = SmallButton(T("Pause"));
            m_soundButton = SmallButton(T("Sound"));
            m_restartButton = SmallButton(T("Restart"));
            LabelWidget title = new() {
                Text = T("MenuButton"),
                FontScale = 1.1f,
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Center,
                Color = Color.White
            };
            bar.Children.Add(m_backButton);
            bar.Children.Add(new CanvasWidget { Size = new Vector2(float.PositiveInfinity, 1f) });
            bar.Children.Add(title);
            bar.Children.Add(new CanvasWidget { Size = new Vector2(float.PositiveInfinity, 1f) });
            bar.Children.Add(m_soundButton);
            bar.Children.Add(m_pauseButton);
            bar.Children.Add(m_restartButton);
            return bar;
        }

        StackPanelWidget BuildMainRow() {
            StackPanelWidget row = new() {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Center
            };

            StackPanelWidget left = new() {
                Direction = LayoutDirection.Vertical,
                VerticalAlignment = WidgetAlignment.Near,
                Margin = new Vector2(8f, 0f)
            };
            left.Children.Add(SideLabel(T("Hold")));
            m_holdPiece = new TetrisMiniPieceWidget();
            left.Children.Add(m_holdPiece);
            m_scoreLabel = SideLabel("");
            m_levelLabel = SideLabel("");
            m_linesLabel = SideLabel("");
            m_highScoreLabel = SideLabel("");
            left.Children.Add(m_scoreLabel);
            left.Children.Add(m_levelLabel);
            left.Children.Add(m_linesLabel);
            left.Children.Add(m_highScoreLabel);

            m_playfield = new TetrisPlayfieldWidget { Size = new Vector2(300f, 600f) };

            StackPanelWidget right = new() {
                Direction = LayoutDirection.Vertical,
                VerticalAlignment = WidgetAlignment.Near,
                Margin = new Vector2(8f, 0f)
            };
            right.Children.Add(SideLabel(T("Next")));
            for (int i = 0; i < 3; i++) {
                m_nextPieces[i] = new TetrisMiniPieceWidget();
                right.Children.Add(m_nextPieces[i]);
            }

            row.Children.Add(left);
            row.Children.Add(m_playfield);
            row.Children.Add(right);
            return row;
        }

        StackPanelWidget BuildTouchBar() {
            StackPanelWidget bar = new() {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Center,
                Margin = new Vector2(4f, 8f)
            };
            m_touchLeft = SmallButton("◀", 72f);
            m_touchRight = SmallButton("▶", 72f);
            m_touchSoft = SmallButton("▼", 72f);
            m_touchHard = SmallButton("⇩", 72f);
            m_touchCcw = SmallButton("↺", 72f);
            m_touchCw = SmallButton("↻", 72f);
            m_touchHold = SmallButton("H", 72f);
            bar.Children.Add(m_touchLeft);
            bar.Children.Add(m_touchRight);
            bar.Children.Add(m_touchSoft);
            bar.Children.Add(m_touchHard);
            bar.Children.Add(m_touchCcw);
            bar.Children.Add(m_touchCw);
            bar.Children.Add(m_touchHold);
            return bar;
        }

        CanvasWidget BuildOverlay() {
            CanvasWidget overlay = new() {
                Size = new Vector2(float.PositiveInfinity),
                IsVisible = false,
                IsHitTestVisible = true
            };
            overlay.Children.Add(
                new RectangleWidget {
                    Size = new Vector2(float.PositiveInfinity),
                    FillColor = new Color(0, 0, 0, 160),
                    OutlineThickness = 0f
                }
            );
            StackPanelWidget panel = new() {
                Direction = LayoutDirection.Vertical,
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Center
            };
            m_pauseTitle = new LabelWidget {
                Text = T("Pause"),
                FontScale = 1.4f,
                HorizontalAlignment = WidgetAlignment.Center,
                Color = Color.White,
                Margin = new Vector2(0f, 8f)
            };
            m_gameOverTitle = new LabelWidget {
                Text = T("GameOver"),
                FontScale = 1.4f,
                HorizontalAlignment = WidgetAlignment.Center,
                Color = new Color(255, 180, 80),
                Margin = new Vector2(0f, 8f)
            };
            m_newRecordLabel = new LabelWidget {
                Text = T("NewRecord"),
                HorizontalAlignment = WidgetAlignment.Center,
                Color = new Color(255, 220, 80),
                IsVisible = false,
                Margin = new Vector2(0f, 4f)
            };
            m_overlayRestart = SmallButton(T("Restart"), 220f);
            m_overlayBack = SmallButton(T("Back"), 220f);
            panel.Children.Add(m_pauseTitle);
            panel.Children.Add(m_gameOverTitle);
            panel.Children.Add(m_newRecordLabel);
            panel.Children.Add(m_overlayRestart);
            panel.Children.Add(m_overlayBack);
            overlay.Children.Add(panel);
            return overlay;
        }

        static BevelledButtonWidget SmallButton(string text, float width = 110f) {
            return new BevelledButtonWidget {
                Text = text,
                Size = new Vector2(width, 48f),
                Margin = new Vector2(4f, 2f),
                HorizontalAlignment = WidgetAlignment.Center
            };
        }

        static LabelWidget SideLabel(string text) {
            return new LabelWidget {
                Text = text,
                Color = Color.White,
                Margin = new Vector2(0f, 4f)
            };
        }

        public override void Enter(object[] parameters) {
            MusicManager.CurrentMix = MusicManager.Mix.Menu;
            m_save.Read();
            m_audio.Enabled = m_save.SoundEnabled;
            m_board = new TetrisBoard();
            m_board.Sfx = sfx => m_audio.Play(sfx);
            m_playfield.Board = m_board;
            m_newRecordThisGame = false;
            m_input = new TetrisInput();
            RefreshUiText();
        }

        public override void Leave() {
            PersistHighScore();
            m_save.Write();
        }

        public override void Update() {
            if (m_board == null) {
                return;
            }
            if (m_backButton.IsClicked || m_overlayBack.IsClicked || Input.Back) {
                PersistHighScore();
                ScreensManager.SwitchScreen("MainMenu");
                return;
            }
            if (m_soundButton.IsClicked) {
                m_save.SoundEnabled = !m_save.SoundEnabled;
                m_audio.Enabled = m_save.SoundEnabled;
                m_save.Write();
                RefreshUiText();
            }
            if (m_restartButton.IsClicked || m_overlayRestart.IsClicked) {
                Restart();
            }

            m_input.PollKeyboard();
            MergeTouch();
            if (m_input.PausePressed || m_pauseButton.IsClicked) {
                if (m_board.State == TetrisState.Playing) {
                    m_board.SetPaused(true);
                }
                else if (m_board.State == TetrisState.Paused) {
                    m_board.SetPaused(false);
                }
            }

            if (m_board.State == TetrisState.Playing) {
                m_input.Apply(m_board, Time.FrameDuration);
                m_board.Tick(Time.FrameDuration);
            }

            PersistHighScore();
            RefreshHud();
        }

        void MergeTouch() {
            if (m_touchLeft.m_clickableWidget.IsPressed) {
                m_input.LeftHeld = true;
            }
            if (m_touchRight.m_clickableWidget.IsPressed) {
                m_input.RightHeld = true;
            }
            if (m_touchSoft.m_clickableWidget.IsPressed) {
                m_input.SoftDropHeld = true;
            }
            if (m_touchHard.IsClicked) {
                m_input.HardDropPressed = true;
            }
            if (m_touchCw.IsClicked) {
                m_input.RotateCwPressed = true;
            }
            if (m_touchCcw.IsClicked) {
                m_input.RotateCcwPressed = true;
            }
            if (m_touchHold.IsClicked) {
                m_input.HoldPressed = true;
            }
        }

        void Restart() {
            m_board.Reset();
            m_newRecordThisGame = false;
            m_input = new TetrisInput();
        }

        void PersistHighScore() {
            if (m_board != null && m_board.Score > m_save.HighScore) {
                m_save.HighScore = m_board.Score;
                m_newRecordThisGame = true;
                m_save.Write();
            }
        }

        void RefreshHud() {
            m_scoreLabel.Text = $"{T("Score")}: {m_board.Score}";
            m_levelLabel.Text = $"{T("Level")}: {m_board.Level}";
            m_linesLabel.Text = $"{T("Lines")}: {m_board.Lines}";
            m_highScoreLabel.Text = $"{T("HighScore")}: {m_save.HighScore}";
            m_holdPiece.Type = m_board.Hold?.Type ?? PieceType.Empty;
            IReadOnlyList<PieceType> next = m_board.PeekNext(3);
            for (int i = 0; i < 3; i++) {
                m_nextPieces[i].Type = i < next.Count ? next[i] : PieceType.Empty;
            }
            bool overlay = m_board.State != TetrisState.Playing;
            m_overlay.IsVisible = overlay;
            bool gameOver = m_board.State == TetrisState.GameOver;
            m_gameOverTitle.IsVisible = gameOver;
            m_pauseTitle.IsVisible = m_board.State == TetrisState.Paused;
            m_newRecordLabel.IsVisible = gameOver && m_newRecordThisGame;
            m_pauseButton.Text = m_board.State == TetrisState.Paused ? T("Resume") : T("Pause");
        }

        void RefreshUiText() {
            m_backButton.Text = T("Back");
            m_restartButton.Text = T("Restart");
            m_overlayRestart.Text = T("Restart");
            m_overlayBack.Text = T("Back");
            m_soundButton.Text = T("Sound") + (m_save.SoundEnabled ? " ON" : " OFF");
            m_pauseButton.Text = T("Pause");
            m_pauseTitle.Text = T("Pause");
            m_gameOverTitle.Text = T("GameOver");
            m_newRecordLabel.Text = T("NewRecord");
        }
    }
}
