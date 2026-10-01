namespace Game {
    /// <summary>
    /// “重启后再执行”的本地模组待处理动作存储：单独落盘，避免页面实例销毁后丢失动作。
    /// 仅在 UI 线程访问，无需锁。
    /// </summary>
    static class LocalModPendingActionStore {
        static readonly Dictionary<string, LocalModPendingActionEntry> s_pendingActions = new(StringComparer.OrdinalIgnoreCase);
        static bool s_pendingActionsLoaded;

        public static Dictionary<string, LocalModPendingActionEntry> LocalModPendingActions {
            get {
                EnsurePendingActionsLoaded();
                return s_pendingActions;
            }
        }

        public static void EnsurePendingActionsLoaded() {
            if (s_pendingActionsLoaded) {
                return;
            }
            s_pendingActionsLoaded = true;
            LoadLocalModPendingActions();
        }

        public static void LoadLocalModPendingActions() {
            s_pendingActions.Clear();
            LocalModPendingActionsData data = PendingLocalModActions.Load();
            if (data?.Actions == null) {
                return;
            }
            foreach (KeyValuePair<string, LocalModPendingActionEntry> pair in data.Actions) {
                if (!string.IsNullOrWhiteSpace(pair.Key)
                    && pair.Value != null) {
                    s_pendingActions[pair.Key] = pair.Value;
                }
            }
        }

        public static void SaveLocalModPendingActions() {
            PendingLocalModActions.Save(LocalModPendingActions);
        }

        /// <summary>
        /// 清空启用/禁用类待处理动作并覆写落盘：整合包激活/停用改用手工禁用状态直写，
        /// 旧的启用/禁用意图若不清空，会在下次重启边界被重复兑现；删除意图保留到重启边界兑现。
        /// 先确保延迟加载（否则进程内首调会在 Clear 后经属性 getter 把盘上数据重载回来）。
        /// 无可清条目时不覆写；空表覆写时 PendingLocalModActions.Save 内部会删除清单文件。
        /// </summary>
        public static void ClearPendingActions() {
            EnsurePendingActionsLoaded();
            List<string> keysToRemove = null;
            foreach (KeyValuePair<string, LocalModPendingActionEntry> pair in s_pendingActions) {
                if (pair.Value?.ActionType is LocalModPendingActionType.Enable or LocalModPendingActionType.Disable) {
                    (keysToRemove ??= []).Add(pair.Key);
                }
            }
            if (keysToRemove == null) {
                return;
            }
            foreach (string key in keysToRemove) {
                s_pendingActions.Remove(key);
            }
            SaveLocalModPendingActions();
        }
    }
}