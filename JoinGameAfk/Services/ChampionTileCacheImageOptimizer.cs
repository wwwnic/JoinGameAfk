using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JoinGameAfk.Services
{
    internal static class ChampionTileCacheImageOptimizer
    {
        public const int ResizeWidth = 96;
        public const int JpegQuality = 100;

        public static bool TryOptimizeJpegInPlace(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            return TryOptimizeJpegInPlace(filePath, ResizeWidth, JpegQuality, cancellationToken);
        }

        public static bool TryOptimizeJpegInPlace(
            string filePath,
            int resizeWidth,
            int jpegQuality,
            CancellationToken cancellationToken = default)
        {
            if (resizeWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(resizeWidth), "Resize width must be greater than zero.");

            cancellationToken.ThrowIfCancellationRequested();

            string temporaryFilePath = $"{filePath}.{Guid.NewGuid():N}.optimized";

            try
            {
                long originalLength = new FileInfo(filePath).Length;
                BitmapSource source = LoadBitmap(filePath, resizeWidth);
                cancellationToken.ThrowIfCancellationRequested();

                SaveJpeg(source, temporaryFilePath, Math.Clamp(jpegQuality, 1, 100));
                cancellationToken.ThrowIfCancellationRequested();

                long optimizedLength = new FileInfo(temporaryFilePath).Length;
                if (optimizedLength >= originalLength)
                    return false;

                File.Move(temporaryFilePath, filePath, overwrite: true);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
            finally
            {
                TryDeleteFile(temporaryFilePath);
            }
        }

        private static BitmapSource LoadBitmap(string filePath, int resizeWidth)
        {
            using var input = File.OpenRead(filePath);
            var decoder = BitmapDecoder.Create(
                input,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            BitmapSource source = decoder.Frames[0];
            if (source.PixelWidth <= resizeWidth)
            {
                source.Freeze();
                return source;
            }

            double scale = resizeWidth / (double)source.PixelWidth;
            var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            resized.Freeze();
            return resized;
        }

        private static void SaveJpeg(BitmapSource source, string filePath, int jpegQuality)
        {
            var encoder = new JpegBitmapEncoder
            {
                QualityLevel = jpegQuality
            };

            encoder.Frames.Add(BitmapFrame.Create(source));

            using var output = File.Create(filePath);
            encoder.Save(output);
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
            }
        }
    }
}
