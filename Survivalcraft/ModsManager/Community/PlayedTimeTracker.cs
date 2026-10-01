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
        // 线程模型：StartProject/StopProject 由引擎在主线程调用，入口先 EnsureStoredAuthLoaded
        // （恢复登录可能触发 LoginStateChanged/UI 刷新），再拍下上报身份快照；OnProjectXmlSaved 由
        // GameManager.SaveProject 的 Task.Run 在线程池触发——这正是 s_lock 存在的原因。
        // 保存回调线程只消费主线程拍下的快照做结算与上报排队，绝不实时读认证状态；
        // 快照陈旧的后果限于单条上报归属旧账号，对非关键路径的上报可接受。

        // 引擎 GameScreen 每 300s 自动保存一次，正常结算间隔不会明显超过它。
        // 挂起/后台冻结期间墙钟（Time.RealTime 不感知暂停）照走，间隔会膨胀到任意长，
        // 全额计入会大量虚增游玩时长——按 2×自动保存周期封顶，超出部分视为不可玩时间丢弃。
        internal static readonly double MaxSettlementIntervalSeconds = 2.0 * 300.0;

        // 待上报队列的硬上限：防 token 长期失效/服务端持续不可达时无限堆积。
        // 正常每自动保存周期至多每模组 1 条，2000 条已是数周不间断游玩的量级；超限丢最旧。
        internal const int MaxPendingReports = 2000;

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
        // 待上报队列（复用 s_lock）：上报前先落盘，发送成功整批出队；开机补报、新记录入队时触发。
        // 上报循环单飞：一次只有一个 ReportPendingAsync 在跑，失败不自动循环重试，
        // 由下次启动或新记录入队再触发——与上游模组 e56f312/374182e 的队列语义一致。
        private static List<ModPlayRecordItem> s_pendingReports;
        private static bool s_pendingReportsLoaded;
        private static bool s_reportLoopRunning;

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
            }

            QueueReports(carryOverReports);
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
                return string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "SecondsFormat"), value);
            }
            if (seconds < 3600.0)
            {
                int value = (int)(seconds / 60.0);
                return string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "MinutesFormat"), value);
            }

            int hours = (int)(seconds / 3600.0);
            int minutes = (int)((seconds % 3600.0) / 60.0);
            return minutes > 0
                ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "HoursMinutesFormat"), hours, minutes)
                : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "HoursFormat"), hours);
        }

        public static string FormatDurationCompact(double seconds)
        {
            return seconds < 60.0
                ? LanguageControl.Get("PlayedTimeTracker", "LessThanOneMinute")
                : FormatDuration(seconds);
        }

        private static IEnumerable<ModEntity> EnumerateTrackableMods()
        {
            return ModsManager.ModList
                .Where(entity => entity != null && entity.modInfo != null)
                .Where(entity => entity is not SurvivalCraftModEntity && entity is not FastDebugModEntity);
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
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "ReadModPlayedTimeFailed"), ex.Message));
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
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "SaveModPlayedTimeFailed"), ex.Message));
            }
        }

        private static List<ModPlayRecordItem> CreateReportsLocked(IEnumerable<ActivePlaySession> sessions)
        {
            // 只读主线程入口拍下的快照，结算线程不实时读认证状态（见文件头线程模型）。
            return CreateReports(s_store.Mods, sessions, s_reportUserId, VersionsManager.PlatformString.ToLowerInvariant());
        }

        /// <summary>
        /// 生成会话的上报记录，取走累计中的整数秒（尾数留在会话里随后续增量上报）。
        /// 不足 1 秒的会话整段跳过；记录生成后先进待上报队列落盘，发送失败保留重试
        /// （开机补报/新记录入队触发）。internal 供测试注入台账与身份。
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
                Log.Information(LanguageControl.Get("PlayedTimeTracker", "PlayedTimeReportingDisabledSkipping"));
                return;
            }

            lock (s_lock)
            {
                EnsurePendingReportsLoadedLocked();
                s_pendingReports.AddRange(reports);
                if (s_pendingReports.Count > MaxPendingReports)
                {
                    int excess = s_pendingReports.Count - MaxPendingReports;
                    s_pendingReports.RemoveRange(0, excess);
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "PendingReportsTrimmed"), excess));
                }
                SavePendingReportsLocked();
            }

            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "PendingReportsEnqueued"), reports.Count));
            StartReportLoop();
        }

        /// <summary>开机补报：装载完成时调用，把上次会话遗留的待上报记录重试发送。</summary>
        public static void RetryPendingReports()
        {
            // 开机时身份快照尚未建立（s_reportToken 为 null），先幂等恢复本地凭据并拍照，
            // 否则补报恒以匿名发送、对已登录用户形同虚设。与 StartProject 同款两行。
            AuthSessionManager.Instance.EnsureStoredAuthLoaded();
            RefreshReportIdentitySnapshot();
            if (!SettingsManager.CommunityPlayedTimeReportingEnabled)
            {
                return;
            }

            int pendingCount;
            lock (s_lock)
            {
                EnsurePendingReportsLoadedLocked();
                pendingCount = s_pendingReports.Count;
            }

            if (pendingCount <= 0)
            {
                return;
            }

            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "PendingReportsRetryStart"), pendingCount));
            StartReportLoop();
        }

        private static void StartReportLoop()
        {
            lock (s_lock)
            {
                if (s_reportLoopRunning)
                {
                    return;
                }

                s_reportLoopRunning = true;
            }

            _ = ReportPendingAsync();
        }

        private static async Task ReportPendingAsync()
        {
            List<ModPlayRecordItem> reports;
            try
            {
                lock (s_lock)
                {
                    EnsurePendingReportsLoadedLocked();
                    reports = [.. s_pendingReports];
                }

                // 发送前再查开关与网络：排队与真正发送之间用户可能刚关掉开关，或本就无网络。
                if (reports.Count == 0 || !SettingsManager.CommunityPlayedTimeReportingEnabled)
                {
                    return;
                }

                if (!WebManager.IsInternetConnectionAvailable())
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "PendingReportsOffline"), reports.Count));
                    return;
                }

                bool fullyAccepted = await s_playedTimeClient.ReportAsync(AuthSessionManager.Instance.ApiBaseUrl, s_reportToken, reports);
                if (!fullyAccepted)
                {
                    // 整批成功才出队：部分被拒/服务端异常时全批保留，等下次触发重发。
                    Log.Information(LanguageControl.Get("PlayedTimeTracker", "PendingReportsKept"));
                    return;
                }

                lock (s_lock)
                {
                    EnsurePendingReportsLoadedLocked();
                    int removeCount = Math.Min(reports.Count, s_pendingReports.Count);
                    s_pendingReports.RemoveRange(0, removeCount);
                    SavePendingReportsLocked();
                }

                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "PendingReportsDequeued"), reports.Count));
            }
            catch (Exception ex)
            {
                // 防御兜底：上报链各层已各自兜异常，这里是最后一道，避免 unobserved task exception 静默。
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "PendingReportsLoopFaulted"), ex));
            }
            finally
            {
                int remaining;
                lock (s_lock)
                {
                    s_reportLoopRunning = false;
                    EnsurePendingReportsLoadedLocked();
                    remaining = s_pendingReports.Count;
                }

                // 失败/离线不自动循环重试：留给下次启动补报或新记录入队时触发。
                if (remaining > 0 && SettingsManager.CommunityPlayedTimeReportingEnabled)
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "PendingReportsRemaining"), remaining));
                }
            }
        }

        private static void EnsurePendingReportsLoadedLocked()
        {
            if (s_pendingReportsLoaded)
            {
                return;
            }

            s_pendingReportsLoaded = true;
            // JsonFileStore 读失败自带日志并回退 null（空队列），不吞数据之外的风险。
            s_pendingReports = JsonFileStore.Read<List<ModPlayRecordItem>>(CommunityStoragePaths.PlayedTimePendingReportsFile, CommunityPlayedTimeClient.SerializationOptions) ?? [];
        }

        private static void SavePendingReportsLocked()
        {
            try
            {
                JsonFileStore.Write(CommunityStoragePaths.PlayedTimePendingReportsFile, s_pendingReports, CommunityPlayedTimeClient.SerializationOptions);
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PlayedTimeTracker", "PendingReportsSaveFailed"), ex.Message));
            }
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
