using JoinGameAfk.Constant;
using JoinGameAfk.Services;

namespace JoinGameAfk.Model
{
    public sealed class ChampionDataSettings
    {
        public int Version { get; set; } = AppStorage.ChampionDataSettingsFileVersion;

        public string PlatformId { get; set; } = RegionLocale.DefaultPlatformId;

        public string Locale { get; set; } = RegionLocale.DefaultLocale;

        public ChampionDataSourceMode SourceMode { get; set; } = ChampionDataSourceMode.LeagueClient;

        public bool DownloadNewChampionPicturesAfterCatalogUpdate { get; set; } = true;

        public bool DownloadRawChampionPictures { get; set; }

        public event Action? Saved;

        public void Save()
        {
            Save(AppStorage.ChampionDataSettingsFilePath);
        }

        public void Save(string filePath)
        {
            JsonSettingsStore.Save(filePath, this, NormalizeSettings);
            Saved?.Invoke();
        }

        public static ChampionDataSettings Load()
        {
            return Load(AppStorage.ChampionDataSettingsFilePath);
        }

        public static ChampionDataSettings Load(string filePath)
        {
            return JsonSettingsStore.Load(filePath, () => new ChampionDataSettings(), NormalizeSettings);
        }

        private static void NormalizeSettings(ChampionDataSettings settings)
        {
            settings.Version = AppStorage.ChampionDataSettingsFileVersion;
            settings.PlatformId = RegionLocale.NormalizePlatformId(settings.PlatformId);
            settings.Locale = RegionLocale.NormalizeLocale(settings.Locale);

            if (!Enum.IsDefined(settings.SourceMode))
            {
                settings.SourceMode = ChampionDataSourceMode.LeagueClient;
            }
        }
    }
}
