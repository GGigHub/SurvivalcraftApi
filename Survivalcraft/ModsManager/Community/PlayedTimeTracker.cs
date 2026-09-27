using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Engine;
using GameEntitySystem;

namespace Game
{
    internal static class PlayedTimeTracker
    {
        // 项目模组时间按自动保存回调结算，而不是每帧累计；因此精度约为项目自动保存周期。
        // 线程模型：StartProject/StopSelfPage/StopProject 由引擎在主线程调用，入口先 EnsureStoredAuthLoaded
        // （恢复登录可能触发 LoginStateChanged/UI 刷新），再拍下上报身份快照；OnProjectXmlSaved 由
        // GameManager.SaveProject 的 Task.Run 在线程池触发——这正是 s_lock 存在的原因。
        // 保存回调线程只消费主线程拍下的快照做结算与上报排队，绝不实时读认证状态；
        // 快照陈旧的后果限于单条上报归属旧账号，对非关键路径的上报可接受。

        // 引擎 GameScreen 每 300s 自动保存一次，正常结算间隔不会明显超过它。
        // 挂起/后台冻结期间墙钟（Time.RealTime 不感知暂停）照走，间隔会膨胀到任意长，
        // 全额计入会大量虚增游玩时长——按 2×自动保存周期封顶，超出部分视为不可玩时间丢弃。
        internal static readonly double MaxSettlementIntervalSeconds = 2.0 * 300.0;

        private static readonly Lock s_lock = new();
        // 上报客户端仅本类使用，自持单例（不再经组合根转发）。
        private static readonly CommunityPlayedTimeClient s_playedTimeClient = new(HttpClientProvider.Community);
        private static int? s_reportUserId;
        private static string s_reportToken;
        private static CommunityPlayedTimeStore s_store;
        private static bool s_loaded;
        // 读取失败置位：后续保存全部跳过，防止用空台账覆盖磁盘上的完好历史。
        private static bool s_loadFailed;
        private static string s_worldKey;
        private static double s_lastProjectSettlementRealTime;
        private static readonly Dictionary<string, ActivePlaySession> s_projectSessions = new(StringComparer.OrdinalIgnoreCase);
        private static ActivePlaySession s_selfPageSession;

        /// <summary>
        /// 项目销毁（退出世界）时结算剩余时长并上报，随后清空会话与结算基准。
        /// 没有这一步，主菜单的驻留墙钟会在下次 StartProject 被当作上个项目的游玩时间收割。
        /// </summary>
        public static void StopProject()
        {
            AuthSessionManager.Instance.EnsureStoredAuthLoaded();
            RefreshReportIdentitySnapshot();
            List<ModPlayRecordItem> reports;
            lock (s_lock)
            {
                if (s_projectSessions.Count == 0)
                {
                    s_lastProjectSettlementRealTime = 0.0;
                    return;
                }

                SettleProjectLocked(Time.RealTime);
                reports = CreateReportsLocked(s_projectSessions.Values);
                s_projectSessions.Clear();
                s_worldKey = null;
                s_lastProjectSettlementRealTime = 0.0;
                SaveLocked();
            }

            QueueReports(reports);
        }

        public static void StartProject(Project project)
        {
            // 结算入口在拍身份快照前幂等恢复本地凭据，让匿名游玩也能归属到账号。
            AuthSessionManager.Instance.EnsureStoredAuthLoaded();
            RefreshReportIdentitySnapshot();
            List<ModPlayRecordItem> carryOverReports = null;
            lock (s_lock)
            {
                EnsureLoadedLocked();
                // 正常退出已由 StopProject 清空会话，此处残留仅覆盖异常路径（如崩溃前未触发
                // OnProjectDisposed 而下次会话直接进新项目）。
                if (s_projectSessions.Count > 0)
                {
                    SettleProjectLocked(Time.RealTime);
                    carryOverReports = CreateReportsLocked(s_projectSessions.Values);
                    SaveLocked();
                }

                s_projectSessions.Clear();
                s_worldKey = GetWorldKey(project);
                s_lastProjectSettlementRealTime = Time.RealTime;

                foreach (ModEntity entity in EnumerateTrackableMods())
                {
                    string key = MakeModKey(entity);
                    ModPlayedTimeData data = GetOrCreateModDataLocked(entity);
                    DateTime now = DateTime.UtcNow;
                    data.LastStartedAtUtc = now;
                    s_projectSessions[key] = new ActivePlaySession
                    {
                        Key = key
                    };
                }

                if (s_projectSessions.Count > 0)
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "StartedTrackingModPlayedTime"), s_projectSessions.Count));
                }
            }

            QueueReports(carryOverReports);
        }

        public static void StartSelfPage()
        {
            lock (s_lock)
            {
                EnsureLoadedLocked();
                ModEntity entity = FindSelfMod();
                if (entity?.modInfo == null)
                {
                    Log.Information(LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "ScbbsPlusModEntityNotFound"));
                    return;
                }

                string key = MakeModKey(entity);
                ModPlayedTimeData data = GetOrCreateModDataLocked(entity);
                if (s_selfPageSession == null)
                {
                    DateTime now = DateTime.UtcNow;
                    data.LastStartedAtUtc = now;
                    s_selfPageSession = new ActivePlaySession
                    {
                        Key = key,
                        LastSettlementRealTime = Time.RealTime
                    };
                    Log.Information(LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "StartedTrackingScreenPlayedTime"));
                }
            }
        }

        /// <summary>项目 XML 保存完成时结算项目内模组时间，落盘并把自上次上报以来的增量排队上报。</summary>
        /// <remarks>引擎经 Task.Run 在线程池调用本方法；认证恢复由主线程的 StartProject 保证先行完成。</remarks>
        public static void OnProjectXmlSaved()
        {
            List<ModPlayRecordItem> reports;
            lock (s_lock)
            {
                if (s_projectSessions.Count == 0)
                {
                    return;
                }
                SettleProjectLocked(Time.RealTime);
                reports = CreateReportsLocked(s_projectSessions.Values);
                SaveLocked();
            }

            QueueReports(reports);
        }

        public static void StopSelfPage()
        {
            AuthSessionManager.Instance.EnsureStoredAuthLoaded();
            RefreshReportIdentitySnapshot();
            List<ModPlayRecordItem> reports = null;
            lock (s_lock)
            {
                if (s_selfPageSession != null)
                {
                    SettleSelfPageLocked(Time.RealTime);
                    reports = CreateReportsLocked([s_selfPageSession]);
                    s_selfPageSession = null;
                    SaveLocked();
                }
            }

            QueueReports(reports);
        }

        public static double GetTotalSeconds(ModEntity entity)
        {
            if (entity?.modInfo == null)
            {
                return 0.0;
            }

            lock (s_lock)
            {
                EnsureLoadedLocked();
                return s_store.Mods.TryGetValue(MakeModKey(entity), out ModPlayedTimeData data)
                    ? data.TotalSeconds
                    : 0.0;
            }
        }

        // 语言选择交给 LanguageControl.Get 按当前语言表读取（缺键自动回退英文表），
        // 不按 CurrentLanguageName 手动分叉语言键；各语言的措辞差异全部收在语言文件里。
        public static string FormatDuration(double seconds)
        {
            if (seconds < 60.0)
            {
                int value = Math.Max(0, (int)Math.Round(seconds));
                return string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "SecondsFormat"), value);
            }
            if (seconds < 3600.0)
            {
                int value = (int)(seconds / 60.0);
                return string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "MinutesFormat"), value);
            }

            int hours = (int)(seconds / 3600.0);
            int minutes = (int)((seconds % 3600.0) / 60.0);
            return minutes > 0
                ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "HoursMinutesFormat"), hours, minutes)
                : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "HoursFormat"), hours);
        }

        public static string FormatDurationCompact(double seconds)
        {
            return seconds < 60.0
                ? LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "LessThanOneMinute")
                : FormatDuration(seconds);
        }

        private static IEnumerable<ModEntity> EnumerateTrackableMods()
        {
            return ModsManager.ModList
                .Where(entity => entity != null && entity.modInfo != null)
                .Where(entity => entity is not SurvivalCraftModEntity && entity is not FastDebugModEntity)
                .Where(entity => !string.Equals(entity.modInfo.PackageName, ModIdentity.PackageName, StringComparison.OrdinalIgnoreCase));
        }

        private static ModEntity FindSelfMod()
        {
            return ModsManager.ModList.FirstOrDefault(entity =>
                entity?.modInfo != null
                && string.Equals(entity.modInfo.PackageName, ModIdentity.PackageName, StringComparison.OrdinalIgnoreCase));
        }

        private static ModPlayedTimeData GetOrCreateModDataLocked(ModEntity entity)
        {
            string key = MakeModKey(entity);
            if (!s_store.Mods.TryGetValue(key, out ModPlayedTimeData data))
            {
                data = new ModPlayedTimeData();
                s_store.Mods[key] = data;
            }

            data.PackageName = entity.modInfo.PackageName;
            data.Version = entity.modInfo.Version;
            data.DisplayName = entity.modInfo.Name;
            data.FileName = string.IsNullOrEmpty(entity.ModFilePath) ? null : Storage.GetFileName(entity.ModFilePath);
            data.FilePath = entity.ModFilePath;
            return data;
        }

        private static string MakeModKey(ModEntity entity)
        {
            // 仅用 PackageName+Version 会让同包同版本的多个模组实体（如并存正式版/测试版文件）计时被合并。
            // 追加文件名区分不同实体，接受历史时长因键变更而不连续。
            string fileName = string.IsNullOrEmpty(entity.ModFilePath) ? null : Storage.GetFileName(entity.ModFilePath);
            return $"{entity.modInfo.PackageName}\n{entity.modInfo.Version}\n{fileName}";
        }

        private static string GetWorldKey(Project project)
        {
            try
            {
                string directoryName = project?.FindSubsystem<SubsystemGameInfo>(false)?.DirectoryName;
                return string.IsNullOrEmpty(directoryName)
                    ? null
                    : Storage.GetFileName(directoryName);
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureLoadedLocked()
        {
            if (s_loaded)
            {
                return;
            }

            s_loaded = true;
            try
            {
                s_store = CommunityPlayedTimeStore.Load();
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "ReadModPlayedTimeFailed"), ex.Message));
                s_store = CommunityPlayedTimeStore.CreateEmpty();
                s_loadFailed = true;
            }
        }

        private static void SettleProjectLocked(double realTime)
        {
            double elapsed = ClampSettlementElapsed(realTime - s_lastProjectSettlementRealTime);
            if (elapsed <= 0.0)
            {
                return;
            }

            EnsureLoadedLocked();
            DateTime now = DateTime.UtcNow;
            foreach (ActivePlaySession session in s_projectSessions.Values)
            {
                AddElapsed(s_store.Mods, session, elapsed, now, s_worldKey);
            }
            s_lastProjectSettlementRealTime = realTime;
        }

        /// <summary>结算间隔封顶：负值归零（时钟回退/重复结算），超长截断（挂起/冻结虚增）。internal 供测试。</summary>
        internal static double ClampSettlementElapsed(double rawElapsed)
        {
            return Math.Min(MaxSettlementIntervalSeconds, Math.Max(0.0, rawElapsed));
        }

        private static void SettleSelfPageLocked(double realTime)
        {
            if (s_selfPageSession == null)
            {
                return;
            }

            double elapsed = ClampSettlementElapsed(realTime - s_selfPageSession.LastSettlementRealTime);
            if (elapsed <= 0.0)
            {
                return;
            }

            EnsureLoadedLocked();
            AddElapsed(s_store.Mods, s_selfPageSession, elapsed, DateTime.UtcNow, null);
            s_selfPageSession.LastSettlementRealTime = realTime;
        }

        /// <summary>把一段时长计入台账与会话；internal 供测试直接驱动结算核心。</summary>
        internal static void AddElapsed(
            IDictionary<string, ModPlayedTimeData> mods,
            ActivePlaySession session,
            double elapsed,
            DateTime now,
            string worldKey)
        {
            if (!mods.TryGetValue(session.Key, out ModPlayedTimeData data))
            {
                return;
            }

            data.TotalSeconds += elapsed;
            session.Seconds += elapsed;
            data.LastPlayedAtUtc = now;
            if (!string.IsNullOrEmpty(worldKey))
            {
                data.WorldSeconds.TryGetValue(worldKey, out double worldSeconds);
                data.WorldSeconds[worldKey] = worldSeconds + elapsed;
            }
        }

        private static void SaveLocked()
        {
            EnsureLoadedLocked();
            if (s_loadFailed)
            {
                // 首次读取失败（AV/锁等瞬态原因）：磁盘文件可能完好，保存空台账会清掉历史，跳过。
                return;
            }
            try
            {
                s_store.Save();
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "SaveModPlayedTimeFailed"), ex.Message));
            }
        }

        private static List<ModPlayRecordItem> CreateReportsLocked(IEnumerable<ActivePlaySession> sessions)
        {
            // 只读主线程入口拍下的快照，结算线程不实时读认证状态（见文件头线程模型）。
            return CreateReports(s_store.Mods, sessions, s_reportUserId, VersionsManager.PlatformString.ToLowerInvariant());
        }

        /// <summary>
        /// 生成会话的上报记录，取走累计中的整数秒（尾数留在会话里随后续增量上报）。
        /// 不足 1 秒的会话整段跳过；上报本身非事务，失败不重试（与本地时长台账可能
        /// 出现单个自动保存周期的服务端缺口，属接受的权衡）。internal 供测试注入台账与身份。
        /// </summary>
        internal static List<ModPlayRecordItem> CreateReports(
            IDictionary<string, ModPlayedTimeData> mods,
            IEnumerable<ActivePlaySession> sessions,
            int? userId,
            string device)
        {
            DateTime playedAtUtc = DateTime.UtcNow;
            List<ModPlayRecordItem> reports = [];

            foreach (ActivePlaySession session in sessions)
            {
                if (session.Seconds < 1.0
                    || !mods.TryGetValue(session.Key, out ModPlayedTimeData data)
                    || string.IsNullOrWhiteSpace(data.PackageName))
                {
                    continue;
                }

                // 向下取整：保证扣减后尾数非负（AwayFromZero 会把 10.5 扣成 -0.5，预支下一轮时长）。
                int reportedSeconds = (int)Math.Floor(session.Seconds);
                reports.Add(new ModPlayRecordItem
                {
                    PackageName = data.PackageName.Trim(),
                    PackageVersion = string.IsNullOrWhiteSpace(data.Version) ? null : data.Version.Trim(),
                    PlayedTime = reportedSeconds,
                    UserId = userId,
                    Device = device,
                    PlayedAt = playedAtUtc.ToString("O")
                });
                // 只取走已上报的整数秒，不足 1 秒的尾数留在会话里随后续增量一起上报。
                session.Seconds -= reportedSeconds;
            }

            return reports;
        }

        private static void QueueReports(List<ModPlayRecordItem> reports)
        {
            if (reports == null || reports.Count == 0)
            {
                return;
            }

            // 设置读取与上报任务启动编组回游戏线程（已在主线程时 Dispatcher 走同线程快速路径），
            // 与 RefreshReportIdentitySnapshot 的主线程写侧互斥。
            Dispatcher.Dispatch(() => QueueReportsOnMainThread(reports));
        }

        private static void QueueReportsOnMainThread(List<ModPlayRecordItem> reports)
        {
            if (!SettingsManager.CommunityPlayedTimeReportingEnabled)
            {
                Log.Information(LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "PlayedTimeReportingDisabledSkipping"));
                return;
            }

            string token = s_reportToken;
            _ = ReportAsync(reports, token);
        }

        private static async Task ReportAsync(List<ModPlayRecordItem> reports, string token)
        {
            // 发送前再查一次开关：排队与真正发送之间用户可能刚把它关掉。
            if (!SettingsManager.CommunityPlayedTimeReportingEnabled)
            {
                Log.Information(LanguageControl.Get("ScbbsPlusPlayedTimeTracker", "PlayedTimeReportingDisabledCancelingPending"));
                return;
            }

            await s_playedTimeClient.ReportAsync(AuthSessionManager.Instance.ApiBaseUrl, token, reports);
        }

        /// <summary>主线程入口调用：把当前登录态拍成上报身份快照，供任意结算线程消费。</summary>
        private static void RefreshReportIdentitySnapshot()
        {
            AuthSessionManager manager = AuthSessionManager.Instance;
            s_reportUserId = manager.CurrentUser?.Id > 0 ? manager.CurrentUser.Id : null;
            s_reportToken = manager.CurrentToken;
        }

        // internal 供测试直接构造会话驱动结算核心。
        internal sealed class ActivePlaySession
        {
            public string Key { get; set; }
            public double Seconds { get; set; }
            public double LastSettlementRealTime { get; set; }
        }

    }
}
