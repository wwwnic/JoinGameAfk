namespace JoinGameAfk.Constant
{
    public static class AppStorage
    {
        public static string DirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JoinGameAfk");

        public static string SettingsFilePath => Path.Combine(DirectoryPath, "champselectsettings.json");

        public static string ChampionFilePath => Path.Combine(DirectoryPath, "champions.json");

        public static void EnsureDirectoryExists()
        {
            Directory.CreateDirectory(DirectoryPath);
        }
    }
}
