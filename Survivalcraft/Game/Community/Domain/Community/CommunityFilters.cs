using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game {
    /// <summary>
    /// 服务器文件类型协议枚举：值与 API 协议一致，仅限线协议层使用
    /// （帖子/文件 API 模型、fileTypes 查询串、上传 TypeId/Type 字段）。
    /// 域内一律用 <see cref="ExternalContentType" />，跨界经 CommunityFileTypes.ToExternal/FromExternal。
    /// Image/Other/NetMod 仅出现在 API 返回的帖子类型中；Unknown 表示缺失/无效。
    /// </summary>
    public enum CommunityFileType {
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

    public enum CommunityOrderType {
        Latest = 1,
        Oldest = 2,
        Updated = 3,
        Hot = 4
    }

    static class CommunityFileTypes {
        internal const CommunityFileType ModpackFileType = CommunityFileType.Modpack;

        static readonly ExternalContentType[] s_filterableValues = [
            ExternalContentType.World,
            ExternalContentType.FurniturePack,
            ExternalContentType.BlocksTexture,
            ExternalContentType.CharacterSkin,
            ExternalContentType.Mod,
            ExternalContentType.Modpack
        ];

        /// <summary>筛选可选集（域内类型，原三处 {1,2,3,4,5,10} 数组）。</summary>
        public static ExternalContentType[] FilterableValues() => s_filterableValues;

        /// <summary>协议值 → 域内类型（API 边界专用，域内禁止直接持有协议枚举）。</summary>
        public static ExternalContentType ToExternal(CommunityFileType value) => value switch {
            CommunityFileType.World => ExternalContentType.World,
            CommunityFileType.Furniture => ExternalContentType.FurniturePack,
            CommunityFileType.Texture => ExternalContentType.BlocksTexture,
            CommunityFileType.Skin => ExternalContentType.CharacterSkin,
            CommunityFileType.Mod => ExternalContentType.Mod,
            CommunityFileType.NetMod => ExternalContentType.NetMod,
            CommunityFileType.Image => ExternalContentType.Image,
            CommunityFileType.Other => ExternalContentType.Other,
            CommunityFileType.Modpack => ExternalContentType.Modpack,
            _ => ExternalContentType.Unknown
        };

        /// <summary>域内类型 → 协议值（API 边界专用）。Directory/ModList 非文件类型，归 Unknown。</summary>
        public static CommunityFileType FromExternal(ExternalContentType value) => value switch {
            ExternalContentType.World => CommunityFileType.World,
            ExternalContentType.FurniturePack => CommunityFileType.Furniture,
            ExternalContentType.BlocksTexture => CommunityFileType.Texture,
            ExternalContentType.CharacterSkin => CommunityFileType.Skin,
            ExternalContentType.Mod => CommunityFileType.Mod,
            ExternalContentType.NetMod => CommunityFileType.NetMod,
            ExternalContentType.Image => CommunityFileType.Image,
            ExternalContentType.Other => CommunityFileType.Other,
            ExternalContentType.Modpack => CommunityFileType.Modpack,
            _ => CommunityFileType.Unknown
        };

        /// <summary>API 读取：9/10 均表整合包（服务器协议）；未知正值归 Other；非正归 Unknown。</summary>
        public static CommunityFileType FromApiValue(int value) => value switch {
            9 => CommunityFileType.Modpack,
            >= 1 and <= 8 or 10 => (CommunityFileType)value,
            > 0 => CommunityFileType.Other,
            _ => CommunityFileType.Unknown
        };

        /// <summary>域内类型 → 协议值（上传边界）：仅接受五个可上传类型，其余抛错（原 MapUploadFileType 语义）。</summary>
        public static CommunityFileType FromUploadableExternal(ExternalContentType value) => value switch {
            ExternalContentType.World => CommunityFileType.World,
            ExternalContentType.FurniturePack => CommunityFileType.Furniture,
            ExternalContentType.BlocksTexture => CommunityFileType.Texture,
            ExternalContentType.CharacterSkin => CommunityFileType.Skin,
            ExternalContentType.Mod => CommunityFileType.Mod,
            _ => throw new InvalidOperationException($"Unsupported upload type: {value}")
        };

        /// <summary>设置读取：域内原生 int，非法归 Mod（原 NormalizeFileType 语义）。</summary>
        public static ExternalContentType FromSettingValue(int value) => Array.IndexOf(s_filterableValues, (ExternalContentType)value) >= 0
            ? (ExternalContentType)value
            : ExternalContentType.Mod;

        /// <summary>设置读取：非法/缺失归 Hot（原 NormalizeOrderType 语义）。</summary>
        public static CommunityOrderType OrderFromSettingValue(int value) => value is >= 1 and <= 4
            ? (CommunityOrderType)value
            : CommunityOrderType.Hot;

        /// <summary>模组类文件（含网络模组），原各处 file.Type==5||file.Type==8 判断。</summary>
        public static bool IsModLike(ExternalContentType value) => value is ExternalContentType.Mod or ExternalContentType.NetMod;

        /// <summary>线协议模型（PostFileItem/PostVersionItem 等）专用重载。</summary>
        public static bool IsModLike(CommunityFileType value) => IsModLike(ToExternal(value));
    }

    /// <summary>fileType 字段的 JSON 数字 ↔ 枚举转换：9→Modpack（协议），未知正值→Other，非正→Unknown。</summary>
    sealed class CommunityFileTypeJsonConverter : JsonConverter<CommunityFileType> {
        // 服务端可能发送 "fileType": null；不重写 HandleNull 时序列化器对值类型直接抛异常。
        public override bool HandleNull => true;

        public override CommunityFileType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value)
                ? CommunityFileTypes.FromApiValue(value)
                : CommunityFileType.Unknown;

        public override void Write(Utf8JsonWriter writer, CommunityFileType value, JsonSerializerOptions options) =>
            writer.WriteNumberValue((int)value);
    }
}