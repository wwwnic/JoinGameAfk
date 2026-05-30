namespace JoinGameAfk.Constant
{
    public static class AppStorage
    {
        public const int GeneralSettingsFileVersion = 1;
        public const int SoundSettingsFileVersion = 1;
        public const int RolePlanSettingsFileVersion = 1;
        public const int OverlaySettingsFileVersion = 1;
        public const int ChampionFileVersion = 1;
        public const int ChampionImageSelectionFileVersion = 1;

        public const string SettingsDirectoryName = "settings";
        public const string RolePlansDirectoryName = "role-plans";
        public const string DataDirectoryName = "data";
        public const string CacheDirectoryName = "cache";
        public const string GeneralSettingsFileName = "general.json";
        public const string SoundSettingsFileName = "sound.json";
        public const string RolePlanSettingsFileName = "plans.json";
        public const string OverlaySettingsFileName = "overlays.json";
        public const string ChampionFileName = "champions.json";
        public const string ChampionImageSelectionFileName = "champion-image-selections.json";
        public const string ChampionChipLabelBreaksFileName = "champion-chip-label-breaks.json";
        public const string ChampionTileDirectoryName = "ChampionTiles";
        public const string ChampionTileArchiveDirectoryName = "ChampionTileArchives";
        public const string ChampionTileCacheFileName = "champion-tile-cache.json";

        public static string DirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JoinGameAfk");

        public static string SettingsDirectoryPath => Path.Combine(DirectoryPath, SettingsDirectoryName);

        public static string RolePlansDirectoryPath => Path.Combine(DirectoryPath, RolePlansDirectoryName);

        public static string DataDirectoryPath => Path.Combine(DirectoryPath, DataDirectoryName);

        public static string CacheDirectoryPath => Path.Combine(DirectoryPath, CacheDirectoryName);

        public static string GeneralSettingsFilePath => Path.Combine(SettingsDirectoryPath, GeneralSettingsFileName);

        public static string SoundSettingsFilePath => Path.Combine(SettingsDirectoryPath, SoundSettingsFileName);

        public static string OverlaySettingsFilePath => Path.Combine(SettingsDirectoryPath, OverlaySettingsFileName);

        public static string RolePlanSettingsFilePath => Path.Combine(RolePlansDirectoryPath, RolePlanSettingsFileName);

        public static string ChampionImageSelectionFilePath => Path.Combine(RolePlansDirectoryPath, ChampionImageSelectionFileName);

        public static string ChampionChipLabelBreaksFilePath => Path.Combine(RolePlansDirectoryPath, ChampionChipLabelBreaksFileName);

        public static string ChampionFilePath => Path.Combine(DataDirectoryPath, ChampionFileName);

        public static string ChampionTileCacheFilePath => Path.Combine(DataDirectoryPath, ChampionTileCacheFileName);

        public static string ChampionTileDirectoryPath => Path.Combine(CacheDirectoryPath, ChampionTileDirectoryName);

        public static string ChampionTileArchiveDirectoryPath => Path.Combine(CacheDirectoryPath, ChampionTileArchiveDirectoryName);

        public static void EnsureDirectoryExists()
        {
            Directory.CreateDirectory(DirectoryPath);
        }

        public static void EnsureStorageLayoutExists()
        {
            EnsureSettingsDirectoryExists();
            EnsureRolePlansDirectoryExists();
            EnsureDataDirectoryExists();
            EnsureChampionTileDirectoryExists();
            EnsureChampionTileArchiveDirectoryExists();
        }

        public static void EnsureSettingsDirectoryExists()
        {
            EnsureDirectoryExists();
            Directory.CreateDirectory(SettingsDirectoryPath);
        }

        public static void EnsureRolePlansDirectoryExists()
        {
            EnsureDirectoryExists();
            Directory.CreateDirectory(RolePlansDirectoryPath);
        }

        public static void EnsureDataDirectoryExists()
        {
            EnsureDirectoryExists();
            Directory.CreateDirectory(DataDirectoryPath);
        }

        public static void EnsureCacheDirectoryExists()
        {
            EnsureDirectoryExists();
            Directory.CreateDirectory(CacheDirectoryPath);
        }

        public static void EnsureChampionTileDirectoryExists()
        {
            EnsureCacheDirectoryExists();
            Directory.CreateDirectory(ChampionTileDirectoryPath);
        }

        public static void EnsureChampionTileArchiveDirectoryExists()
        {
            EnsureCacheDirectoryExists();
            Directory.CreateDirectory(ChampionTileArchiveDirectoryPath);
        }
    }
}
