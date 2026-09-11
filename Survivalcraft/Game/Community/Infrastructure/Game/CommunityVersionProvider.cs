using System;
using Engine;

namespace Game
{
    internal static class CommunityVersionProvider {
        static string m_version;
        public static string GetCurrentVersion()
        {
            if (m_version != null)
            {
                return m_version;
            }
            // ModListAll 在主线程列表重建（Clear+AddRange）期间不可并发枚举：
            // 调用方含后台线程（AutoUpdateController 的 Task.Run），枚举封送主线程同步执行。
            Dispatcher.Dispatch(() =>
            {
                try
                {
                    foreach (ModEntity entity in ModsManager.ModListAll)
                    {
                        if (entity?.modInfo != null
                            && string.Equals(entity.modInfo.PackageName, ModIdentity.PackageName, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(entity.modInfo.Version))
                        {
                            m_version = entity.modInfo.Version;
                            return;
                        }
                    }
                }
                catch
                {
                    // 版本不可知：返回 null，由调用方显式处理，不再伪造 "0.0.1" 误导更新判断。
                }
            }, waitUntilCompleted: true);
            return m_version;
        }
    }
}
