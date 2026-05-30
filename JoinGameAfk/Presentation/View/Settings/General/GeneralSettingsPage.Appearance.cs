using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinGameAfk.Theme;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private void RefreshThemeDrivenControls()
        {
            UpdateAutomationInputStates();
            RefreshChampionCatalogSyncStatus();
            RefreshChampionPictureCacheStatus();
            InvalidateVisual();
        }

        private void LoadThemeOptions()
        {
            _themeOptions.Clear();
            _themeOptions.AddRange(AppThemeManager.Themes.Select(CreateThemePickerOption));
            ThemePickerItemsControl.ItemsSource = _themeOptions;
            SelectTheme(AppThemeManager.CurrentThemeKey);
            UpdateThemePickerExpansionState();
        }

        private void ThemeOptionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ThemePickerOption option })
                return;

            SelectTheme(option.Key);
            RefreshDirtyState();
            PreviewTheme(option.Key);
        }

        private void PreviewTheme(string themeKey)
        {
            string normalizedThemeKey = AppThemeManager.NormalizeThemeKey(themeKey);
            if (_reloadUiForTheme is not null)
            {
                _reloadUiForTheme(_settings, _overlaySettings, normalizedThemeKey, _isThemePickerExpanded);
                return;
            }

            AppThemeManager.ApplyTheme(normalizedThemeKey);
            RefreshThemeDrivenControls();
        }

        private void ThemePickerExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _isThemePickerExpanded = !_isThemePickerExpanded;
            UpdateThemePickerExpansionState();
        }

        private void ThemePickerViewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateThemePickerExpansionState();
        }

        private void UpdateThemePickerExpansionState()
        {
            UpdatePickerExpansionState(
                ThemePickerViewport,
                ThemePickerExpandButton,
                _themeOptions.Count,
                ThemePickerTileOuterWidth,
                ThemePickerTileOuterHeight,
                _isThemePickerExpanded);
        }

        private static void UpdatePickerExpansionState(
            FrameworkElement viewport,
            Button expandButton,
            int itemCount,
            double itemOuterWidth,
            double itemOuterHeight,
            bool isExpanded)
        {
            double availableWidth = viewport.ActualWidth;
            int itemsPerRow = availableWidth > 0
                ? Math.Max(1, (int)Math.Floor(availableWidth / itemOuterWidth))
                : 1;

            bool needsExpansion = itemCount > itemsPerRow * CollapsedPickerRows;
            expandButton.Visibility = needsExpansion ? Visibility.Visible : Visibility.Collapsed;
            expandButton.Content = isExpanded ? "Show fewer" : "Show all";
            viewport.MaxHeight = needsExpansion && !isExpanded
                ? itemOuterHeight * CollapsedPickerRows
                : double.PositiveInfinity;
        }

        private static ThemePickerOption CreateThemePickerOption(AppThemeDefinition theme)
        {
            var dictionary = CreateThemePreviewDictionary(theme);

            return new ThemePickerOption(
                theme.Key,
                theme.DisplayName,
                GetThemePreviewBrush(dictionary, "AppBackgroundBrush", Brushes.Black),
                GetThemePreviewBrush(dictionary, "AppSurfaceBrush", Brushes.DimGray),
                GetThemePreviewBrush(dictionary, "AppBorderBrush", Brushes.SlateGray),
                GetThemePreviewBrush(dictionary, "AppInputBrush", Brushes.DarkSlateGray),
                GetThemePreviewBrush(dictionary, "AccentBlueActionBrush", Brushes.DodgerBlue),
                GetThemePreviewBrush(dictionary, "TextInverseBrush", Brushes.White),
                GetThemePreviewBrush(dictionary, "TextPrimaryBrush", Brushes.WhiteSmoke),
                GetThemePreviewBrush(dictionary, "TextMutedBrush", Brushes.SlateGray));
        }

        private static ResourceDictionary? CreateThemePreviewDictionary(AppThemeDefinition theme)
        {
            try
            {
                string assemblyName = typeof(AppThemeManager).Assembly.GetName().Name ?? "JoinGameAfk";
                return new ResourceDictionary
                {
                    Source = new Uri($"/{assemblyName};component/{theme.Source}", UriKind.Relative)
                };
            }
            catch
            {
                return null;
            }
        }

        private static Brush GetThemePreviewBrush(ResourceDictionary? dictionary, string key, Brush fallback)
        {
            Brush brush = dictionary?[key] as Brush ?? fallback;
            var clone = brush.CloneCurrentValue();
            if (clone.CanFreeze)
                clone.Freeze();

            return clone;
        }

        private void SelectTheme(string? themeKey)
        {
            string normalizedThemeKey = AppThemeManager.NormalizeThemeKey(themeKey);
            _selectedThemeKey = normalizedThemeKey;

            foreach (var option in _themeOptions)
                option.IsSelected = string.Equals(option.Key, normalizedThemeKey, StringComparison.OrdinalIgnoreCase);

            SelectedThemeTextBlock.Text = _themeOptions.FirstOrDefault(option => option.IsSelected)?.DisplayName
                ?? AppThemeManager.Themes[0].DisplayName;
        }

        private string GetSelectedThemeKey()
        {
            return AppThemeManager.NormalizeThemeKey(_selectedThemeKey);
        }

        private bool SelectedThemeRequiresReload()
        {
            return !string.Equals(GetSelectedThemeKey(), AppThemeManager.CurrentThemeKey, StringComparison.OrdinalIgnoreCase);
        }

        private bool SavedThemeRequiresReload()
        {
            return !string.Equals(
                AppThemeManager.NormalizeThemeKey(_settings.ThemeKey),
                AppThemeManager.CurrentThemeKey,
                StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ThemePickerOption : INotifyPropertyChanged
        {
            private bool _isSelected;

            public ThemePickerOption(
                string key,
                string displayName,
                Brush backgroundBrush,
                Brush surfaceBrush,
                Brush borderBrush,
                Brush inputBrush,
                Brush buttonBrush,
                Brush buttonTextBrush,
                Brush textBrush,
                Brush mutedTextBrush)
            {
                Key = key;
                DisplayName = displayName;
                BackgroundBrush = backgroundBrush;
                SurfaceBrush = surfaceBrush;
                BorderBrush = borderBrush;
                InputBrush = inputBrush;
                ButtonBrush = buttonBrush;
                ButtonTextBrush = buttonTextBrush;
                TextBrush = textBrush;
                MutedTextBrush = mutedTextBrush;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public string Key { get; }
            public string DisplayName { get; }
            public Brush BackgroundBrush { get; }
            public Brush SurfaceBrush { get; }
            public Brush BorderBrush { get; }
            public Brush InputBrush { get; }
            public Brush ButtonBrush { get; }
            public Brush ButtonTextBrush { get; }
            public Brush TextBrush { get; }
            public Brush MutedTextBrush { get; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                        return;

                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }
    }
}