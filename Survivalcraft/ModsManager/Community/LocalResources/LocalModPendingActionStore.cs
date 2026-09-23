using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// “重启后再执行”的本地模组待处理动作存储：单独落盘，避免页面实例销毁后丢失动作。
    /// 仅在 UI 线程访问，无需锁。
    /// </summary>
    internal static class LocalModPendingActionStore
    {
        private static readonly Dictionary<string, LocalModPendingActionEntry> s_pendingActions = new(StringComparer.OrdinalIgnoreCase);
        private static bool s_pendingActionsLoaded;

        public static Dictionary<string, LocalModPendingActionEntry> LocalModPendingActions
        {
            get
            {
                EnsurePendingActionsLoaded();
                return s_pendingActions;
            }
        }

        public static void EnsurePendingActionsLoaded()
        {
            if (s_pendingActionsLoaded)
            {
                return;
            }
            s_pendingActionsLoaded = true;
            LoadLocalModPendingActions();
        }

        public static void LoadLocalModPendingActions()
        {
            s_pendingActions.Clear();
            LocalModPendingActionsData data = PendingLocalModActions.Load();
            if (data?.Actions == null)
            {
                return;
            }

            foreach (KeyValuePair<string, LocalModPendingActionEntry> pair in data.Actions)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                {
                    s_pendingActions[pair.Key] = pair.Value;
                }
            }
        }

        public static void SaveLocalModPendingActions()
        {
            PendingLocalModActions.Save(LocalModPendingActions);
        }

        /// <summary>
        /// 清空全部待处理动作并覆写落盘：整合包激活/停用改用手工禁用状态直写，
        /// 旧的启用/禁用/删除意图若不清空，会在下次重启边界被重复兑现。
        /// 空表覆写时 PendingLocalModActions.Save 内部会删除清单文件。
        /// </summary>
        public static void ClearPendingActions()
        {
            s_pendingActions.Clear();
            SaveLocalModPendingActions();
        }
    }
}
