using System.Windows;

namespace JoinGameAfk.Theme
{
    public static class AppThemeManager
    {
        public const string DefaultThemeKey = "draft-desk";

        public static string CurrentThemeKey { get; private set; } = DefaultThemeKey;

        public static IReadOnlyList<AppThemeDefinition> Themes { get; } =
        [
            new AppThemeDefinition(DefaultThemeKey, "Draft Desk", "Styles/Themes/DraftDesk.xaml"),
            new AppThemeDefinition("color-safe-light", "Color Safe Light", "Styles/Themes/ColorSafeLight.xaml"),
            new AppThemeDefinition("color-safe-dark", "Color Safe Dark", "Styles/Themes/ColorSafeDark.xaml"),
            new AppThemeDefinition("blood-forge", "Blood Forge", "Styles/Themes/BloodForge.xaml"),
            new AppThemeDefinition("celestial-candy", "Celestial Candy", "Styles/Themes/CelestialCandy.xaml"),
            new AppThemeDefinition("cloud-light", "Cloud Light", "Styles/Themes/CloudLight.xaml"),
            new AppThemeDefinition("command-deck", "Command Deck", "Styles/Themes/CommandDeck.xaml"),
            new AppThemeDefinition("harbor-fog", "Harbor Fog", "Styles/Themes/HarborFog.xaml"),
            new AppThemeDefinition("iron-ember", "Iron Ember", "Styles/Themes/IronEmber.xaml"),
            new AppThemeDefinition("ivory-standard", "Ivory Standard", "Styles/Themes/IvoryStandard.xaml"),
            new AppThemeDefinition("lavender-mint", "Lavender Mint", "Styles/Themes/LavenderMint.xaml"),
            new AppThemeDefinition("midnight-current", "Midnight Current", "Styles/Themes/MidnightCurrent.xaml"),
            new AppThemeDefinition("neon-circuit", "Neon Circuit", "Styles/Themes/NeonCircuit.xaml"),
            new AppThemeDefinition("peach-sorbet", "Peach Sorbet", "Styles/Themes/PeachSorbet.xaml"),
            new AppThemeDefinition("rose-noir", "Rose Noir", "Styles/Themes/RoseNoir.xaml"),
            new AppThemeDefinition("sakura-bloom", "Sakura Bloom", "Styles/Themes/SakuraBloom.xaml")
        ];

        public static event Action? ThemeChanged;

        public static string NormalizeThemeKey(string? themeKey)
        {
            return Themes.FirstOrDefault(theme => string.Equals(theme.Key, themeKey, StringComparison.OrdinalIgnoreCase))?.Key
                ?? DefaultThemeKey;
        }

        public static void ApplyTheme(string? themeKey)
        {
            var theme = GetTheme(themeKey);
            var appResources = Application.Current?.Resources;
            if (appResources is null)
                return;

            ApplyTheme(appResources, theme);
            CurrentThemeKey = theme.Key;
            ThemeChanged?.Invoke();
        }

        private static AppThemeDefinition GetTheme(string? themeKey)
        {
            return Themes.FirstOrDefault(theme => string.Equals(theme.Key, themeKey, StringComparison.OrdinalIgnoreCase))
                ?? Themes[0];
        }

        private static void ApplyTheme(ResourceDictionary appResources, AppThemeDefinition theme)
        {
            if (TryReplaceThemeDictionary(appResources, theme))
                return;

            appResources.MergedDictionaries.Add(CreateThemeDictionary(theme));
        }

        private static bool TryReplaceThemeDictionary(ResourceDictionary dictionary, AppThemeDefinition theme)
        {
            for (int i = 0; i < dictionary.MergedDictionaries.Count; i++)
            {
                var mergedDictionary = dictionary.MergedDictionaries[i];
                if (IsThemeDictionary(mergedDictionary))
                {
                    dictionary.MergedDictionaries[i] = CreateThemeDictionary(theme);
                    return true;
                }

                if (TryReplaceThemeDictionary(mergedDictionary, theme))
                    return true;
            }

            return false;
        }

        private static bool IsThemeDictionary(ResourceDictionary dictionary)
        {
            string source = NormalizeSource(dictionary.Source?.OriginalString);
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return Themes.Any(theme =>
            {
                string themeSource = NormalizeSource(theme.Source);
                string themeFileName = GetFileName(themeSource);

                return source.EndsWith(themeSource, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(GetFileName(source), themeFileName, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string NormalizeSource(string? source)
        {
            return source?.Replace('\\', '/') ?? "";
        }

        private static string GetFileName(string source)
        {
            int index = source.LastIndexOf('/');
            return index >= 0 ? source[(index + 1)..] : source;
        }

        private static ResourceDictionary CreateThemeDictionary(AppThemeDefinition theme)
        {
            string assemblyName = typeof(AppThemeManager).Assembly.GetName().Name ?? "JoinGameAfk";

            return new ResourceDictionary
            {
                Source = new Uri($"/{assemblyName};component/{theme.Source}", UriKind.Relative)
            };
        }
    }
}
