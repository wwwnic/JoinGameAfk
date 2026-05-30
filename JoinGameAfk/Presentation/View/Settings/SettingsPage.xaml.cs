using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinGameAfk.Model;
using JoinGameAfk.Presentation.View.Settings.General;
using JoinGameAfk.Presentation.View.Settings.Overlay;
using JoinGameAfk.Presentation.View.Settings.Sound;

namespace JoinGameAfk.Presentation.View.Settings
{
    public partial class SettingsPage : Page
    {
        private readonly Button[] _settingsSectionButtons;
        private readonly FrameworkElement[] _settingsSectionViews;

        public SettingsPage(
            GeneralSettings generalSettings,
            SoundSettings soundSettings,
            OverlaySettings overlaySettings,
            Action<GeneralSettings, OverlaySettings, string?, bool>? reloadUiForTheme = null,
            Action<string>? logMessage = null,
            Action<string>? logErrorMessage = null,
            string? selectedThemeKey = null,
            bool themePickerExpanded = false)
        {
            InitializeComponent();

            GeneralSettingsFrame.Content = new GeneralSettingsPage(
                generalSettings,
                overlaySettings,
                reloadUiForTheme,
                logMessage,
                logErrorMessage,
                selectedThemeKey,
                themePickerExpanded);
            SoundSettingsFrame.Content = new SoundSettingsPage(soundSettings);
            OverlaySettingsFrame.Content = new OverlaySettingsPage(overlaySettings);

            _settingsSectionButtons =
            [
                GeneralSettingsSectionButton,
                SoundSettingsSectionButton,
                OverlaySettingsSectionButton
            ];
            _settingsSectionViews =
            [
                GeneralSettingsFrame,
                SoundSettingsFrame,
                OverlaySettingsFrame
            ];
            ActivateSettingsSection(0);
        }

        private void GeneralSettingsSectionButton_Click(object sender, RoutedEventArgs e) => ActivateSettingsSection(0);

        private void SoundSettingsSectionButton_Click(object sender, RoutedEventArgs e) => ActivateSettingsSection(1);

        private void OverlaySettingsSectionButton_Click(object sender, RoutedEventArgs e) => ActivateSettingsSection(2);

        private void ActivateSettingsSection(int index)
        {
            if (index < 0 || index >= _settingsSectionButtons.Length || index >= _settingsSectionViews.Length)
                index = 0;

            Brush activeTabForeground = TryFindResource("TabActiveForegroundBrush") as Brush ?? Brushes.White;
            Brush inactiveTabForeground = TryFindResource("TabInactiveForegroundBrush") as Brush ?? Brushes.Gray;
            Brush activeTabBorder = TryFindResource("TabActiveBorderBrush") as Brush ?? Brushes.DodgerBlue;
            Brush inactiveTabBorder = TryFindResource("TabInactiveBorderBrush") as Brush ?? Brushes.Transparent;

            for (int i = 0; i < _settingsSectionButtons.Length; i++)
            {
                bool isActive = i == index;
                _settingsSectionButtons[i].Tag = isActive ? "Active" : null;
                _settingsSectionButtons[i].Background = Brushes.Transparent;
                _settingsSectionButtons[i].Foreground = isActive ? activeTabForeground : inactiveTabForeground;
                _settingsSectionButtons[i].BorderBrush = isActive ? activeTabBorder : inactiveTabBorder;
                _settingsSectionViews[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
