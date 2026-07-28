using System.Diagnostics;

namespace Engine {
    public interface IEngineTimeSource {
        long Timestamp { get; }
        long Frequency { get; }
    }

    public sealed class ManualEngineTimeSource : IEngineTimeSource {
        private long m_timestamp;

        public ManualEngineTimeSource(double initialSeconds = 0.0) {
            if (double.IsNaN(initialSeconds) || double.IsInfinity(initialSeconds) || initialSeconds < 0.0) {
                throw new ArgumentOutOfRangeException(nameof(initialSeconds));
            }
            m_timestamp = checked((long)Math.Round(initialSeconds * Frequency));
        }

        public long Timestamp => Interlocked.Read(ref m_timestamp);

        public long Frequency => TimeSpan.TicksPerSecond;

        public double Seconds => Timestamp / (double)Frequency;

        public void Advance(double seconds) {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0) {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
            Interlocked.Add(ref m_timestamp, checked((long)Math.Round(seconds * Frequency)));
        }
    }

    public static class Time {
        struct DelayedExecutionRequest {
            public double Time;

            // ReSharper disable MemberHidesStaticFromOuterClass
            public int FrameIndex;
            // ReSharper restore MemberHidesStaticFromOuterClass

            public Action Action;
        }

        static long? m_startTicks;

        static IEngineTimeSource m_timeSource = new StopwatchEngineTimeSource();

        static List<DelayedExecutionRequest> m_delayedExecutionsRequests = [];

        static int m_fpsStartFrameIndex;

        static double m_fpsStartTime;

        static float m_fpsCpuTime;

        static float m_remainingDuration;
        public static int FrameIndex { get; private set; }

        public static long Ticks {
            get {
                long timestamp = m_timeSource.Timestamp;
                m_startTicks ??= timestamp;
                return timestamp - m_startTicks.Value;
            }
        }

        public static long TicksPerSecond => m_timeSource.Frequency;

        public static double RealTime => Ticks / (double)TicksPerSecond;

        public static bool IsManualTime => m_timeSource is ManualEngineTimeSource;

        public static void EnableManualTime(double initialSeconds = 0.0) {
            SetTimeSource(new ManualEngineTimeSource(initialSeconds));
        }

        public static void DisableManualTime() {
            SetTimeSource(new StopwatchEngineTimeSource());
        }

        public static void AdvanceManualTime(double seconds) {
            if (m_timeSource is not ManualEngineTimeSource manualTimeSource) {
                throw new InvalidOperationException("Manual time is not enabled.");
            }
            manualTimeSource.Advance(seconds);
        }

        public static double PreviousFrameStartTime { get; private set; }

        public static double FrameStartTime { get; private set; }

        public static float PreviousFrameDuration { get; private set; }

        public static float FrameDuration { get; private set; }

        public static float CpuFrameDuration { get; private set; }

        public static float AverageFrameDuration { get; private set; }

        public static float AverageCpuFrameDuration { get; private set; }

        public static float FrameDurationLimit { get; set; }

        public static bool SingleEvent(double time) {
            if (FrameStartTime >= time) {
                return PreviousFrameStartTime < time;
            }
            return false;
        }

        public static bool PeriodicEvent(double period, double offset) {
            double num = FrameStartTime - offset;
            double num2 = Math.Floor(num / period) * period;
            return num >= num2 && num - FrameDuration < num2;
        }

        public static void QueueTimeDelayedExecution(double time, Action action) {
            m_delayedExecutionsRequests.Add(new DelayedExecutionRequest { Time = time, FrameIndex = -1, Action = action });
        }

        public static void QueueFrameIndexDelayedExecution(int frameIndex, Action action) {
            m_delayedExecutionsRequests.Add(new DelayedExecutionRequest { Time = -1.0, FrameIndex = frameIndex, Action = action });
        }

        internal static void BeforeFrame() {
            double realTime = RealTime;
            PreviousFrameDuration = FrameDuration;
            FrameDuration = (float)(realTime - FrameStartTime);
            PreviousFrameStartTime = FrameStartTime;
            FrameStartTime = realTime;
            if (FrameStartTime >= m_fpsStartTime + 1.0) {
                int num = FrameIndex - m_fpsStartFrameIndex;
                AverageFrameDuration = (float)(FrameStartTime - m_fpsStartTime) / num;
                AverageCpuFrameDuration = m_fpsCpuTime / num;
                m_fpsStartTime = FrameStartTime;
                m_fpsCpuTime = 0f;
                m_fpsStartFrameIndex = FrameIndex;
            }
            int num2 = 0;
            while (num2 < m_delayedExecutionsRequests.Count) {
                DelayedExecutionRequest delayedExecutionRequest = m_delayedExecutionsRequests[num2];
                if ((delayedExecutionRequest.Time >= 0.0 && FrameStartTime >= delayedExecutionRequest.Time)
                    || (delayedExecutionRequest.FrameIndex >= 0 && FrameIndex >= delayedExecutionRequest.FrameIndex)) {
                    m_delayedExecutionsRequests.RemoveAt(num2);
                    delayedExecutionRequest.Action();
                }
                else {
                    num2++;
                }
            }
        }

        internal static void AfterFrame() {
            CpuFrameDuration = (float)(RealTime - FrameStartTime);
            m_fpsCpuTime += CpuFrameDuration;
            FrameIndex++;
            if (FrameDurationLimit > 0f) {
                float num = Math.Clamp(FrameDurationLimit, 0f, 1f);
                m_remainingDuration = Math.Clamp(m_remainingDuration + num - FrameDuration, 0f, 2f * num);
                float num2 = (float)(RealTime - FrameStartTime);
                Sleep(m_remainingDuration - num2);
            }
        }

        static void Sleep(double duration) {
            Task.Delay((int)Math.Clamp(Math.Round(duration * 1000.0), 0.0, 2147483647.0)).Wait();
        }

        static void SetTimeSource(IEngineTimeSource timeSource) {
            m_timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
            m_startTicks = timeSource is ManualEngineTimeSource ? 0 : null;
            PreviousFrameStartTime = 0.0;
            FrameStartTime = 0.0;
            PreviousFrameDuration = 0f;
            FrameDuration = 0f;
            CpuFrameDuration = 0f;
            AverageFrameDuration = 0f;
            AverageCpuFrameDuration = 0f;
            m_fpsStartTime = 0.0;
            m_fpsStartFrameIndex = FrameIndex;
            m_fpsCpuTime = 0f;
            m_remainingDuration = 0f;
        }

        sealed class StopwatchEngineTimeSource : IEngineTimeSource {
            public long Timestamp => Stopwatch.GetTimestamp();
            public long Frequency => Stopwatch.Frequency;
        }
    }
}
