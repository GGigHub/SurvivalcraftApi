using System;
using System.IO;
using System.IO.Compression;
using Engine;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Globalization;

namespace Game
{
    internal static class PostIconDecoder
    {
        public static Image<Rgba32> Decode(byte[] data, bool isModArchive)
        {
            return isModArchive ? DecodeArchiveIcon(data) : DecodeImage(data);
        }

        public static Image<Rgba32> DecodeStandalone(byte[] data)
        {
            return DecodeImage(data);
        }

        private static Image<Rgba32> DecodeImage(byte[] data)
        {
            Image<Rgba32> image = Image.Load<Rgba32>(data);
            try
            {
                if (image.Width > 256 || image.Height > 256)
                {
                    bool isWide = image.Width > image.Height;
                    image.Mutate(context => context.Resize(
                        isWide ? 256 : 0,
                        isWide ? 0 : 256,
                        KnownResamplers.Bicubic));
                }
                return image;
            }
            catch
            {
                // Resize 中途异常（损坏像素数据等）时释放已分配的位图，避免泄漏。
                image.Dispose();
                throw;
            }
        }

        private static Image<Rgba32> DecodeArchiveIcon(byte[] data)
        {
            try
            {
                // 与引擎 ModEntity.InitResources 的图标约定一致：icon.webp 优先、icon.png 兜底。
                // 引擎 ModFiles 字典大小写敏感，历史代码查的 "Icon.png" 永远不在其读取范围内。
                // 同一归档视图上做两次探测，解密只发生一次。
                using System.IO.Compression.ZipArchive archive = ScmodArchiveReader.OpenArchive(new MemoryStream(data, writable: false));
                if (!ScmodArchiveReader.TryReadRootEntry(archive, "icon.webp", out byte[] iconData)
                    && !ScmodArchiveReader.TryReadRootEntry(archive, "icon.png", out iconData))
                {
                    return null;
                }

                return DecodeImage(iconData);
            }
            catch (Exception ex)
            {
                // 归档结构无效（G2 类型化）统一友好文案；其余异常保留原文便于诊断。
                string message = ex is InvalidScmodArchiveException
                    ? ScmodArchiveReader.InvalidArchiveFriendlyMessage
                    : ex.Message;
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostIconDecoder", "DecodePackageIconFailed"), message));
                return null;
            }
        }
    }
}
