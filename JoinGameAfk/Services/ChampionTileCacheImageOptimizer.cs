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

        public static void SaveImageBytesAsJpeg(
            byte[] imageBytes,
            string filePath,
            bool resizeForLocalCache,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(imageBytes);
            if (imageBytes.Length == 0)
                throw new ArgumentException("Image bytes are required.", nameof(imageBytes));

            cancellationToken.ThrowIfCancellationRequested();
            using var input = new MemoryStream(imageBytes, writable: false);
            BitmapSource source = LoadBitmap(
                input,
                resizeForLocalCache ? ResizeWidth : null);
            cancellationToken.ThrowIfCancellationRequested();
            SaveJpeg(source, filePath, JpegQuality);
        }

        private static BitmapSource LoadBitmap(string filePath, int resizeWidth)
        {
            using var input = File.OpenRead(filePath);
            return LoadBitmap(input, resizeWidth);
        }

        private static BitmapSource LoadBitmap(Stream input, int? resizeWidth)
        {
            var decoder = BitmapDecoder.Create(
                input,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            BitmapSource source = decoder.Frames[0];
            if (resizeWidth is null || source.PixelWidth <= resizeWidth.Value)
            {
                source.Freeze();
                return source;
            }

            double scale = resizeWidth.Value / (double)source.PixelWidth;
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
