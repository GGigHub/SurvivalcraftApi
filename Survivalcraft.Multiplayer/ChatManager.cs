using Game.Network;

namespace Survivalcraft.Multiplayer {
    /// <summary>
    ///     客户端聊天消息缓冲区。网络层收到 <see cref="ChatMessagePacket" /> 后写入，
    ///     <see cref="ChatWidget" /> 每帧读取渲染。
    /// </summary>
    public static class ChatManager {
        public sealed class ChatMessage {
            public ChatMessageType Type;
            public int PlayerIndex;
            public string Name;
            public string Text;
        }

        public const int MaxMessages = 200;

        static readonly List<ChatMessage> m_messages = [];

        public static IReadOnlyList<ChatMessage> Messages => m_messages;

        public static event Action<ChatMessage> MessageAdded;

        public static void Add(ChatMessageType type, int playerIndex, string name, string text) {
            ChatMessage message = new() {
                Type = type,
                PlayerIndex = playerIndex,
                Name = name ?? string.Empty,
                Text = text ?? string.Empty
            };
            m_messages.Add(message);
            if (m_messages.Count > MaxMessages) {
                m_messages.RemoveRange(0, m_messages.Count - MaxMessages);
            }
            MessageAdded?.Invoke(message);
        }

        public static string Format(ChatMessage message) {
            if (message == null) {
                return string.Empty;
            }
            return message.Type switch {
                ChatMessageType.Join => $"[系统] {message.Name} 加入游戏",
                ChatMessageType.Leave => $"[系统] {message.Name} 离开游戏",
                _ => $"[{message.Name}] {message.Text}"
            };
        }

        public static void Clear() => m_messages.Clear();
    }
}
