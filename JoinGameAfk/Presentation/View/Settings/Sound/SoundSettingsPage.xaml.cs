using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.Settings.Sound
{
    public partial class SoundSettingsPage : Page
    {
        private const double DragAutoScrollEdgeDistance = 56;
        private const double DragAutoScrollStep = 28;
        private const string SoundStudioPreviewChannelKey = "sound-studio-preview";
        private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(3);

        private readonly SoundSettings _settings;
        private readonly DispatcherTimer _savedMessageTimer;
        private readonly NotificationSoundPlayer _notificationSoundPlayer;
        private readonly List<NotificationSoundOption> _soundOptions = [];
        private readonly List<SoundChoiceOption> _soundLibraryChoices = [];
        private readonly List<SoundAlertGroupOption> _soundAlertGroups = [];
        private SoundChoiceOption? _lastPreviewedSoundChoice;
        private Point _soundDragStartPoint;
        private SoundChoiceOption? _pendingSoundDragChoice;
        private SoundAlertOption? _pendingSoundDragAlert;
        private SoundDragData? _activeSoundDragData;
        private UIElement? _soundDragCaptureElement;
        private SoundAlertOption? _soundDropTarget;
        private bool _isSoundDragActive;
        private bool _isApplyingSettingsToControls;

        public static readonly DependencyProperty IsSoundClearDropTargetProperty = DependencyProperty.Register(
            nameof(IsSoundClearDropTarget),
            typeof(bool),
            typeof(SoundSettingsPage),
            new PropertyMetadata(false));

        public bool IsSoundClearDropTarget
        {
            get => (bool)GetValue(IsSoundClearDropTargetProperty);
            private set => SetValue(IsSoundClearDropTargetProperty, value);
        }

        public SoundSettingsPage(SoundSettings settings)
        {
            _settings = settings;
            InitializeComponent();
            _notificationSoundPlayer = new NotificationSoundPlayer(ShowValidationMessage);
            _savedMessageTimer = new DispatcherTimer
            {
                Interval = SavedMessageDuration
            };
            _savedMessageTimer.Tick += (_, _) =>
            {
                _savedMessageTimer.Stop();
                FloatingSoundStatusBar.Visibility = Visibility.Collapsed;
            };

            _settings.Saved += Settings_Saved;
            Unloaded += SoundSettingsPage_Unloaded;
            LoadSoundAlertOptions();
            ApplySettingsToControls();
            AttachDirtyStateTracking();
            RefreshDirtyState();
        }

        private void AttachDirtyStateTracking()
        {
            SoundAlertVolumeSlider.ValueChanged += SoundAlertVolumeSlider_ValueChanged;
        }

        private void Settings_Saved()
        {
            Dispatcher.TryInvoke(() =>
            {
                if (DirtySoundBar.Visibility == Visibility.Visible)
                    return;

                ApplySettingsToControls();
                RefreshDirtyState();
            });
        }

        private void SoundSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isSoundDragActive)
                FinishSoundDrag(drop: false);

            _settings.Saved -= Settings_Saved;
            Unloaded -= SoundSettingsPage_Unloaded;
        }
    }
}
