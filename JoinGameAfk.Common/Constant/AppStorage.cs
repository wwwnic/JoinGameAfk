namespace JoinGameAfk.Constant
{
    public static class AppStorage
    {
        public const int SettingsFileVersion = 1;
        public const int ChampionFileVersion = 2;

        public const string SettingsFileName = "configuration.json";
        public const string ChampionFileName = "champions.json";

        public static string DirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JoinGameAfk");

        public static string SettingsFilePath => Path.Combine(DirectoryPath, SettingsFileName);

        public static string ChampionFilePath => Path.Combine(DirectoryPath, ChampionFileName);

        public static void EnsureDirectoryExists()
        {
            Directory.CreateDirectory(DirectoryPath);
        }
    }
}
