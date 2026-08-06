using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

namespace Survivalcraft.Multiplayer {
    public class PlayerNameArrowWidget : CanvasWidget {
        const float FontScale = 1f;
        const float MaxNameWidth = 300f;
        const float ArrowGap = 4f;
        const float LeftWidgetGap = 4f;

        readonly List<PlayerRow> m_rows = [];
        readonly float m_arrowWidth;
        readonly float m_rowHeight;
        GameWidget m_gameWidget;
        bool m_hasMultiplePlayers;
        float m_contentWidth;

        sealed class PlayerRow {
            public int PlayerIndex;
            public string SourceName;
            public string DisplayName;
            public float NameWidth;
            public float Angle;
            public bool ArrowVisible;
            public Color Color;
        }

        public PlayerNameArrowWidget() {
            HorizontalAlignment = WidgetAlignment.Near;
            VerticalAlignment = WidgetAlignment.Near;
            IsHitTestVisible = false;
            ClampToBounds = true;
            BitmapFont font = LabelWidget.BitmapFont;
            Vector2 scale = new(FontScale);
            m_arrowWidth = font.MeasureText("↑", scale, Vector2.Zero).X;
            m_rowHeight = MathF.Ceiling(font.MeasureText("↑", scale, Vector2.Zero).Y);
            IsDrawRequired = true;
        }

        public void SetGameWidget(GameWidget gameWidget) {
            m_gameWidget = gameWidget;
        }

        public override void Update() {
            base.Update();

            SubsystemPlayers subsystemPlayers = m_gameWidget?.SubsystemGameWidgets.Project.FindSubsystem<SubsystemPlayers>(true);
            if (subsystemPlayers == null) {
                m_hasMultiplePlayers = false;
                IsDrawRequired = false;
                return;
            }

            ReadOnlyList<PlayerData> players = subsystemPlayers.PlayersData;
            m_hasMultiplePlayers = players.Count > 1;
            IsDrawRequired = m_hasMultiplePlayers;
            if (!m_hasMultiplePlayers) {
                return;
            }
            if (PlayersChanged(players)) {
                RebuildRows(players);
            }

            m_contentWidth = 0f;
            Camera camera = m_gameWidget.ActiveCamera;
            for (int i = 0; i < m_rows.Count; i++) {
                PlayerData playerData = players[i];
                PlayerRow row = m_rows[i];
                string name = string.IsNullOrEmpty(playerData.Name) ? $"Player {playerData.PlayerIndex}" : playerData.Name;
                UpdateName(row, name);
                m_contentWidth = MathUtils.Max(m_contentWidth, row.NameWidth + m_arrowWidth + ArrowGap) + 4;

                bool isMainPlayer = playerData.IsMainPlayer;
                row.Color = isMainPlayer ? Color.Yellow : Color.White;
                ComponentBody body = playerData.ComponentPlayer?.ComponentMiner?.ComponentCreature?.ComponentBody;
                if (isMainPlayer || body == null || camera == null) {
                    row.ArrowVisible = false;
                    row.Angle = 0f;
                    continue;
                }

                row.ArrowVisible = true;
                Vector3 viewPosition = Vector3.Transform(body.Position, camera.ViewMatrix);
                float targetAngle = MathUtils.Atan2(viewPosition.X, -viewPosition.Z);
                float angleDelta = MathUtils.NormalizeAngle(targetAngle - row.Angle);
                row.Angle += angleDelta * MathUtils.Saturate(12f * MathUtils.Min(Time.FrameDuration, 0.1f));
            }
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            DesiredSize = m_hasMultiplePlayers
                ? new Vector2(m_contentWidth, m_rows.Count * m_rowHeight)
                : Vector2.Zero;
        }

        public override void Arrange(Vector2 position, Vector2 parentActualSize) {
            float leftWidgetWidth = GetVisibleLeftWidgetWidth();
            if (leftWidgetWidth > 0f) {
                position.X += leftWidgetWidth + LeftWidgetGap;
            }
            base.Arrange(position, parentActualSize);
        }

        float GetVisibleLeftWidgetWidth() {
            if (ParentWidget is not ContainerWidget parentWidget) {
                return 0f;
            }
            ContainerWidget leftControlsContainerWidget = parentWidget.Children.Find<ContainerWidget>("LeftControlsContainer", false);
            if (leftControlsContainerWidget?.IsVisible != true) {
                return 0f;
            }

            float width = leftControlsContainerWidget.ActualSize.X;
            if (width <= 0f && !float.IsPositiveInfinity(leftControlsContainerWidget.ParentDesiredSize.X)) {
                width = leftControlsContainerWidget.ParentDesiredSize.X;
            }
            if (width <= 0f) {
                return 0f;
            }

            float hiddenWidth = MathUtils.Max(-leftControlsContainerWidget.RenderTransform.M41, 0f);
            return MathUtils.Max(width - hiddenWidth, 0f);
        }

        public override void Draw(DrawContext dc) {
            if (!m_hasMultiplePlayers || m_rows.Count == 0) {
                return;
            }

            BitmapFont font = LabelWidget.BitmapFont;
            FontBatch2D fontBatch = dc.PrimitivesRenderer2D.FontBatch(
                font,
                1,
                DepthStencilState.None,
                RasterizerState.CullNoneScissor,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp
            );
            int start = fontBatch.TriangleVertices.Count;
            Vector2 scale = new(FontScale);
            Vector2 position = Vector2.Zero;
            for (int i = 0; i < m_rows.Count; i++) {
                PlayerRow row = m_rows[i];
                Color color = row.Color * GlobalColorTransform;
                fontBatch.QueueText(
                    row.DisplayName,
                    position + scale,
                    0f,
                    new Color((byte)0, (byte)0, (byte)0, color.A),
                    TextAnchor.Default,
                    scale
                );
                fontBatch.QueueText(row.DisplayName, position, 0f, color, TextAnchor.Default, scale);
                if (row.ArrowVisible) {
                    Vector2 arrowCenter = new(
                        position.X + row.NameWidth + ArrowGap + 0.5f * m_arrowWidth,
                        position.Y + 0.5f * m_rowHeight
                    );
                    fontBatch.QueueText(
                        "↑",
                        arrowCenter + scale,
                        0f,
                        new Color((byte)0, (byte)0, (byte)0, color.A),
                        TextAnchor.Center | TextAnchor.DisableSnapToPixels,
                        scale,
                        Vector2.Zero,
                        row.Angle
                    );
                    fontBatch.QueueText(
                        "↑",
                        arrowCenter,
                        0f,
                        color,
                        TextAnchor.Center | TextAnchor.DisableSnapToPixels,
                        scale,
                        Vector2.Zero,
                        row.Angle
                    );
                }
                position.Y += m_rowHeight;
            }
            fontBatch.TransformTriangles(GlobalTransform, start);
        }

        bool PlayersChanged(ReadOnlyList<PlayerData> players) {
            if (m_rows.Count != players.Count) {
                return true;
            }
            for (int i = 0; i < players.Count; i++) {
                if (m_rows[i].PlayerIndex != players[i].PlayerIndex) {
                    return true;
                }
            }
            return false;
        }

        void RebuildRows(ReadOnlyList<PlayerData> players) {
            m_rows.Clear();
            for (int i = 0; i < players.Count; i++) {
                m_rows.Add(new PlayerRow {
                    PlayerIndex = players[i].PlayerIndex,
                    Color = Color.White
                });
            }
        }

        void UpdateName(PlayerRow row, string name) {
            if (row.SourceName == name) {
                return;
            }
            row.SourceName = name;
            row.DisplayName = FitName(name);
            row.NameWidth = MathF.Ceiling(LabelWidget.BitmapFont.MeasureText(row.DisplayName, new Vector2(FontScale), Vector2.Zero).X);
        }

        string FitName(string name) {
            BitmapFont font = LabelWidget.BitmapFont;
            Vector2 scale = new(FontScale);
            if (font.MeasureText(name, scale, Vector2.Zero).X <= MaxNameWidth) {
                return name;
            }

            const string ellipsis = "...";
            float ellipsisWidth = font.MeasureText(ellipsis, scale, Vector2.Zero).X;
            int fitLength = font.FitText(MathUtils.Max(MaxNameWidth - ellipsisWidth, 0f), name, FontScale, 0f);
            return name.Substring(0, MathUtils.Clamp(fitLength, 0, name.Length)).TrimEnd() + ellipsis;
        }
    }
}
