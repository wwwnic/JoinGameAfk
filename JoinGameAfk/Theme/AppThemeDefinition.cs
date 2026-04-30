namespace JoinGameAfk.Theme
{
    public sealed class AppThemeDefinition
    {
        public AppThemeDefinition(string key, string displayName, string source)
        {
            Key = key;
            DisplayName = displayName;
            Source = source;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string Source { get; }
    }
}
