using System;
using System.Threading;

namespace Game
{
    public sealed class CommunityTransferProgress
    {
        private readonly Lock m_lock = new();
        private string m_message = string.Empty;
        private float m_fraction;
        private bool m_hasPercent = true;

        public void Set(string message, float value, bool hasPercent)
        {
            lock (m_lock)
            {
                m_message = message;
                m_fraction = float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);
                m_hasPercent = hasPercent;
            }
        }

        public (string Message, float Fraction, bool HasPercent) Snapshot()
        {
            lock (m_lock)
            {
                return (m_message, m_fraction, m_hasPercent);
            }
        }
    }
}
