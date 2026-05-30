using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.Settings.Overlay
{
    public partial class OverlaySettingsPage : Page
    {
        private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(3);

        private readonly OverlaySettings _settings;
        private readonly DispatcherTimer _savedMessageTimer;
        private bool _isApplyingSettingsToControls;

        public OverlaySettingsPage(OverlaySettings settings)
        {
            _settings = settings;
            InitializeComponent();

            _savedMessageTimer = new DispatcherTimer
            {
                Interval = SavedMessageDuration
            };
            _savedMessageTimer.Tick += (_, _) =>
            {
                _savedMessageTimer.Stop();
                FloatingOverlayStatusBar.Visibility = Visibility.Collapsed;
            };

            _settings.Saved += Settings_Saved;
            Unloaded += OverlaySettingsPage_Unloaded;
            ApplySettingsToControls();
            AttachDirtyStateTracking();
            RefreshDirtyState();
        }

        private void Settings_Saved()
        {
            Dispatcher.TryInvoke(() =>
            {
                if (DirtyOverlayBar.Visibility == Visibility.Visible)
                    return;

                ApplySettingsToControls();
                RefreshDirtyState();
            });
        }

        private void OverlaySettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _settings.Saved -= Settings_Saved;
            Unloaded -= OverlaySettingsPage_Unloaded;
        }

        private void ApplySettingsToControls()
        {
            _isApplyingSettingsToControls = true;
            try
            {
                ApplyQueueOverlaySettingsToControls();
                ApplyPickBanOverlaySettingsToControls();
                RefreshSliderValueText();
            }
            finally
            {
                _isApplyingSettingsToControls = false;
            }
        }
    }
}
