using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game
{
    /// <summary>
    /// 社区资源文件类型。值与 API 协议一致；Image/Other/NetMod 仅出现在 API 返回的帖子类型中，
    /// 不参与筛选；Unknown 表示缺失/无效。
    /// </summary>
    public enum ScbbsPlusCommunityFileType
    {
        Unknown = 0,
        World = 1,
        Furniture = 2,
        Texture = 3,
        Skin = 4,
        Mod = 5,
        Image = 6,
        Other = 7,
        NetMod = 8,
        Modpack = 10
    }

    public enum CommunityOrderType
    {
        Latest = 1,
        Oldest = 2,
        Updated = 3,
        Hot = 4
    }

    internal static class CommunityFileTypes
    {
        internal const ScbbsPlusCommunityFileType ModpackFileType = ScbbsPlusCommunityFileType.Modpack;

        private static readonly ScbbsPlusCommunityFileType[] s_filterableValues =
        [
            ScbbsPlusCommunityFileType.World, ScbbsPlusCommunityFileType.Furniture, ScbbsPlusCommunityFileType.Texture,
            ScbbsPlusCommunityFileType.Skin, ScbbsPlusCommunityFileType.Mod, ScbbsPlusCommunityFileType.Modpack
        ];

        /// <summary>筛选可选集（原三处 {1,2,3,4,5,10} 数组）。</summary>
        public static ScbbsPlusCommunityFileType[] FilterableValues() => s_filterableValues;

        /// <summary>设置读取：非法/缺失归 Mod（原 NormalizeFileType 语义）。</summary>
        public static ScbbsPlusCommunityFileType FromSettingValue(int value) => value switch
        {
            >= 1 and <= 8 or 10 => (ScbbsPlusCommunityFileType)value,
            _ => ScbbsPlusCommunityFileType.Mod
        };

        /// <summary>API 读取：9/10 均表整合包（服务器协议）；未知正值归 Other；非正归 Unknown。</summary>
        public static ScbbsPlusCommunityFileType FromApiValue(int value) => value switch
        {
            9 => ScbbsPlusCommunityFileType.Modpack,
            >= 1 and <= 8 or 10 => (ScbbsPlusCommunityFileType)value,
            > 0 => ScbbsPlusCommunityFileType.Other,
            _ => ScbbsPlusCommunityFileType.Unknown
        };

        /// <summary>设置读取：非法/缺失归 Hot（原 NormalizeOrderType 语义）。</summary>
        public static CommunityOrderType OrderFromSettingValue(int value) => value is >= 1 and <= 4
            ? (CommunityOrderType)value
            : CommunityOrderType.Hot;

        /// <summary>模组类文件（含网络模组），原各处 file.Type==5||file.Type==8 判断。</summary>
        public static bool IsModLike(ScbbsPlusCommunityFileType value)
            => value is ScbbsPlusCommunityFileType.Mod or ScbbsPlusCommunityFileType.NetMod;
    }

    /// <summary>fileType 字段的 JSON 数字 ↔ 枚举转换：9→Modpack（协议），未知正值→Other，非正→Unknown。</summary>
    internal sealed class CommunityFileTypeJsonConverter : JsonConverter<ScbbsPlusCommunityFileType>
    {
        // 服务端可能发送 "fileType": null；不重写 HandleNull 时序列化器对值类型直接抛异常。
        public override bool HandleNull => true;

        public override ScbbsPlusCommunityFileType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value)
                ? CommunityFileTypes.FromApiValue(value)
                : ScbbsPlusCommunityFileType.Unknown;

        public override void Write(Utf8JsonWriter writer, ScbbsPlusCommunityFileType value, JsonSerializerOptions options)
            => writer.WriteNumberValue((int)value);
    }
}
