using System.Text.Json;
using System.Text.Json.Serialization;

namespace JoinGameAfk.Services
{
    public static class JsonSettingsStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public static T Load<T>(string filePath, Func<T> createDefault, Action<T> normalize)
        {
            ArgumentNullException.ThrowIfNull(createDefault);
            ArgumentNullException.ThrowIfNull(normalize);

            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    var settings = JsonSerializer.Deserialize<T>(json, SerializerOptions);
                    if (settings is not null)
                    {
                        normalize(settings);
                        return settings;
                    }

                    QuarantineInvalidFile(filePath);
                }
                catch
                {
                    QuarantineInvalidFile(filePath);
                }
            }

            var defaults = createDefault();
            normalize(defaults);
            Save(filePath, defaults, normalize);
            return defaults;
        }

        public static void Save<T>(string filePath, T settings, Action<T> normalize)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(normalize);

            normalize(settings);
            string? directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                string json = JsonSerializer.Serialize(settings, SerializerOptions);
                File.WriteAllText(temporaryPath, json);

                if (File.Exists(filePath))
                    File.Replace(temporaryPath, filePath, null);
                else
                    File.Move(temporaryPath, filePath);
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }

        private static void QuarantineInvalidFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                string directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string extension = Path.GetExtension(filePath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

                string invalidPath = Path.Combine(directoryPath, $"{fileName}.invalid-{timestamp}{extension}");
                int suffix = 2;
                while (File.Exists(invalidPath))
                {
                    invalidPath = Path.Combine(directoryPath, $"{fileName}.invalid-{timestamp}-{suffix}{extension}");
                    suffix++;
                }

                File.Move(filePath, invalidPath);
            }
            catch
            {
            }
        }

        private static void TryDeleteTemporaryFile(string temporaryPath)
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }
}
