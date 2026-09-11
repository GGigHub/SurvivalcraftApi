using System;

namespace Game
{
    /// <summary>枚举显示名统一语言键：ScbbsPlusCommon.Enums.{枚举名}.{成员名}。</summary>
    public static class EnumDisplayName
    {
        public static string GetEnumDisplayName<T>(T enumValue, string fallback = null) where T : Enum
        {
            return LanguageControl.TryGet(out string result, "ScbbsPlusCommon", "Enums", typeof(T).Name, enumValue.ToString()) ? result : fallback ?? enumValue.ToString();
        }
    }
}
