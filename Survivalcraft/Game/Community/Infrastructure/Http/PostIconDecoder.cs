using System;
using Engine;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Game
{
    internal static class PostIconDecoder
    {
        public static Image<Rgba32> Decode(byte[] data)
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
    }
}
