using Engine;

namespace Game {
    public class NetworkStatusWidget : LabelWidget {
        private const float RightWidgetGap = 8f;

        public override void Update() {
            base.Update();

            bool showClientLatency = NetworkManager.IsClientRunning;
            bool showHostCode = NetworkManager.RelayHostCodeStatus != RelayHostCodeStatus.Inactive;
            IsVisible = showClientLatency || showHostCode;
            if (!IsVisible) {
                Text = string.Empty;
                return;
            }
            if (showClientLatency) {
                int latency = NetworkManager.ClientLatency;
                Text = latency >= 0
                    ? $"延迟：{latency}ms"
                    : "延迟：--ms";
                return;
            }
            Text = NetworkManager.RelayHostCodeStatus switch {
                RelayHostCodeStatus.Creating => "联机码：正在创建...",
                RelayHostCodeStatus.Ready => GetRelayHostCodeText(),
                RelayHostCodeStatus.Failed => "联机码：创建失败",
                _ => string.Empty
            };
        }

        public override void Arrange(Vector2 position, Vector2 parentActualSize) {
            float rightWidgetWidth = GetVisibleRightWidgetWidth();
            if (rightWidgetWidth > 0f) {
                position.X = MathUtils.Max(position.X - rightWidgetWidth - RightWidgetGap, 0f);
            }
            base.Arrange(position, parentActualSize);
        }

        private float GetVisibleRightWidgetWidth() {
            if (ParentWidget is not ContainerWidget parentWidget) return 0f;
            ContainerWidget rightControlsContainerWidget = parentWidget.Children.Find<ContainerWidget>("RightControlsContainer", false);
            if (rightControlsContainerWidget?.IsVisible != true) return 0f;

            float width = rightControlsContainerWidget.ActualSize.X;
            if (width <= 0f && !float.IsPositiveInfinity(rightControlsContainerWidget.ParentDesiredSize.X)) {
                width = rightControlsContainerWidget.ParentDesiredSize.X;
            }
            if (width <= 0f) return 0f;

            float hiddenWidth = MathUtils.Max(rightControlsContainerWidget.RenderTransform.M41, 0f);
            return MathUtils.Max(width - hiddenWidth, 0f);
        }

        private static string GetRelayHostCodeText() {
            string globalCode = NetworkManager.RelayGlobalCode;
            if (!string.IsNullOrWhiteSpace(globalCode)) {
                return $"联机码：{globalCode}";
            }
            string roomCode = NetworkManager.RelayRoomCode;
            if (!string.IsNullOrWhiteSpace(roomCode)) {
                return $"房间码：{roomCode}";
            }
            return "联机码：--";
        }
    }
}
