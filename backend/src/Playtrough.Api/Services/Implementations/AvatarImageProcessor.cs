using SkiaSharp;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Implementations;

/// <summary>Bounded decoding; only the generated WebP is retained, never the original or metadata.</summary>
public sealed class AvatarImageProcessor
{
    private readonly SemaphoreSlim gate = new(2);

    public async Task<byte[]> ProcessAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0) throw new ProfileException("invalid_avatar");
        if (file.Length > AvatarSettings.MaxFileBytes) throw new ProfileException("avatar_too_large");
        await gate.WaitAsync(ct);
        try
        {
            await using var input = file.OpenReadStream();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > AvatarSettings.MaxFileBytes) throw new ProfileException("avatar_too_large");
                buffer.Write(chunk, 0, read);
            }
            using var data = SKData.CreateCopy(buffer.ToArray());
            using var codec = SKCodec.Create(data);
            if (codec is null) throw new ProfileException("invalid_avatar");
            var expected = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Jpeg => "image/jpeg",
                SKEncodedImageFormat.Png => "image/png",
                SKEncodedImageFormat.Webp => "image/webp",
                _ => throw new ProfileException("invalid_avatar"),
            };
            if (!string.Equals(file.ContentType, expected, StringComparison.OrdinalIgnoreCase) || codec.FrameCount > 1)
                throw new ProfileException("invalid_avatar");
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!(expected == "image/jpeg" && extension is ".jpg" or ".jpeg" ||
                expected == "image/png" && extension == ".png" || expected == "image/webp" && extension == ".webp"))
                throw new ProfileException("invalid_avatar");
            var info = codec.Info;
            if (info.Width < 64 || info.Height < 64 || info.Width > 4096 || info.Height > 4096 ||
                (long)info.Width * info.Height > 16_000_000)
                throw new ProfileException("invalid_avatar_dimensions");
            using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new ProfileException("invalid_avatar");
            ct.ThrowIfCancellationRequested();
            var w = bitmap.Width;
            var h = bitmap.Height;
            var origin = (int)codec.EncodedOrigin;
            var swapped = origin >= 5;
            using var oriented = new SKBitmap(swapped ? h : w, swapped ? w : h, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(oriented))
            {
                var matrix = origin switch
                {
                    2 => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
                    3 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
                    4 => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
                    5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                    6 => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
                    7 => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
                    8 => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
                    _ => SKMatrix.Identity,
                };
                canvas.SetMatrix(matrix);
                using var raw = SKImage.FromBitmap(bitmap);
                canvas.DrawImage(raw, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
            }
            using var result = new SKBitmap(AvatarSettings.Size, AvatarSettings.Size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(result))
            using (var source = SKImage.FromBitmap(oriented))
            {
                canvas.Clear(SKColors.Transparent);
                var side = Math.Min(oriented.Width, oriented.Height);
                var left = (oriented.Width - side) / 2f;
                var top = (oriented.Height - side) / 2f;
                canvas.DrawImage(source, new SKRect(left, top, left + side, top + side),
                    new SKRect(0, 0, AvatarSettings.Size, AvatarSettings.Size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }
            using var image = SKImage.FromBitmap(result);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 85);
            if (encoded is null) throw new ProfileException("invalid_avatar");
            return encoded.ToArray();
        }
        finally { gate.Release(); }
    }
}
