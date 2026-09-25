using System.Reflection;

namespace Game {
    /// <summary>
    ///     模组程序集在进入 CLR 之前的内存镜像。
    ///     预加载模组可以替换 Bytes，从而在不写回原始 .scmod 的情况下改写程序集。
    /// </summary>
    public sealed class ModAssemblyImage {
        public ModAssemblyImage(ModEntity owner, string fileName, byte[] originalBytes)
            : this(owner, fileName, originalBytes, originalBytes) { }

        internal ModAssemblyImage(ModEntity owner, string fileName, byte[] originalBytes, byte[] bytes) {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            OriginalBytes = originalBytes?.ToArray() ?? throw new ArgumentNullException(nameof(originalBytes));
            Bytes = bytes?.ToArray() ?? throw new ArgumentNullException(nameof(bytes));
        }

        public ModEntity Owner { get; }
        public string FileName { get; }
        public byte[] OriginalBytes { get; }
        public byte[] Bytes { get; set; }
        public Assembly LoadedAssembly { get; internal set; }
        public bool Skip { get; set; }
        public bool IsModified => !Bytes.AsSpan().SequenceEqual(OriginalBytes);
    }
}
