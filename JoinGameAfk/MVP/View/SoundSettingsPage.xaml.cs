using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Model;
using JoinGameAfk.Services;
using JoinGameAfk.Validation;

namespace JoinGameAfk.View
{
    public partial class SoundSettingsPage : Page
    {
        private const double DragAutoScrollEdgeDistance = 56;
        private const double DragAutoScrollStep = 28;
        private const string SoundStudioPreviewChannelKey = "sound-studio-preview";
        private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(3);

        private readonly ChampSelectSettings _settings;
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

        public SoundSettingsPage(ChampSelectSettings settings)
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

        private void LoadSoundAlertOptions()
        {
            _soundOptions.Clear();
            _soundOptions.AddRange(NotificationSoundPlayer.SoundOptions);

            _soundLibraryChoices.Clear();
            _soundLibraryChoices.AddRange(_soundOptions.Select(option => new SoundChoiceOption(option.Key, option.DisplayName, option.IsLoopable)));

            _soundAlertGroups.Clear();
            _soundAlertGroups.AddRange(SoundAlertDefaults.Definitions
                .GroupBy(definition => definition.GroupName)
                .Select(group => new SoundAlertGroupOption(
                    group.Key,
                    group.Select(definition => new SoundAlertOption(definition, _soundOptions)).ToList())));

            SoundAlertGroupItemsControl.ItemsSource = _soundAlertGroups;
        }

        private void ApplySettingsToControls()
        {
            _isApplyingSettingsToControls = true;
            try
            {
                SoundAlertsEnabledCheckBox.IsChecked = _settings.SoundAlertProfile != SoundAlertProfile.Off;
                SoundAlertVolumeSlider.Value = ChampSelectSettings.NormalizeSoundAlertVolumePercent(_settings.SoundAlertVolumePercent);
                ApplySoundAlertSettingsToRows();
                RefreshSoundAlertVolumeValueText();
                RefreshSoundPickerChoices();
                UpdateSoundAlertInputStates();
            }
            finally
            {
                _isApplyingSettingsToControls = false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryValidateSoundAlertSettings())
                return;

            _settings.SoundAlertProfile = AreSoundAlertsEnabled()
                ? SoundAlertProfile.Custom
                : SoundAlertProfile.Off;
            _settings.SoundAlertVolumePercent = GetSoundAlertVolumePercent();
            _settings.SoundAlerts = CaptureSoundAlertSettings();
            _settings.Save();

            RefreshDirtyState();
            ShowSavedMessage();
        }

        private void ResetSoundDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Restore default sound alert mode, volume, sounds, and warning timing?",
                "Reset Sounds",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            if (result != MessageBoxResult.OK)
                return;

            _settings.ResetSoundAlertOptionsToDefaults();
            ApplySettingsToControls();
            _settings.Save();

            RefreshDirtyState();
            ShowDefaultsRestoredMessage();
        }

        private void CancelSoundChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmCancelSoundChanges())
                return;

            if (_isSoundDragActive)
                FinishSoundDrag(drop: false);

            ClearPendingSoundDrag();
            ClearSoundDropTarget();
            IsSoundClearDropTarget = false;
            _notificationSoundPlayer.StopChannel(SoundStudioPreviewChannelKey);
            ClearLastPreviewedSoundChoice();

            ApplySettingsToControls();
            NotificationSoundPlayer.SetActivePlayerVolume(GetSoundAlertVolumePercent());
            RefreshDirtyState();
            ShowChangesCanceledMessage();
        }

        private bool ConfirmCancelSoundChanges()
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Discard unsaved sound changes and return to the last saved sound settings?",
                "Cancel Sound Changes",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private void SoundAlertVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RefreshSoundAlertVolumeValueText();
            NotificationSoundPlayer.SetActivePlayerVolume(GetSoundAlertVolumePercent());
            RefreshDirtyState();
        }

        private void SoundAlertsEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSoundAlertInputStates();
            RefreshDirtyState();
        }

        private void SoundAlertRow_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetSoundDragData(out var sound)
                || sound is null
                || !CanAssignSoundToAlert(sound)
                || sender is not FrameworkElement { DataContext: SoundAlertOption option })
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            ShowSoundDropTarget(option);
            UpdateSoundDragFeedback(e.GetPosition(this));
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void SoundAlertRow_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SoundAlertOption option }
                && ReferenceEquals(_soundDropTarget, option))
            {
                ClearSoundDropTarget();
            }
        }

        private void SoundAlertRow_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSoundDragData(out var sound)
                && sound is not null
                && sender is FrameworkElement { DataContext: SoundAlertOption option })
            {
                ApplyDroppedSoundToAlert(option, sound);
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void SoundAlertSoundCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInsideButton(e.OriginalSource as DependencyObject)
                || sender is not FrameworkElement { DataContext: SoundAlertOption option }
                || !option.IsEnabled)
            {
                return;
            }

            _pendingSoundDragAlert = option;
            _soundDragStartPoint = e.GetPosition(this);
        }

        private void SoundAlertSoundCell_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isSoundDragActive)
                return;

            if (e.LeftButton != MouseButtonState.Pressed || _pendingSoundDragAlert is null)
                return;

            Point currentPosition = e.GetPosition(this);
            if (!IsPastDragThreshold(_soundDragStartPoint, currentPosition))
                return;

            StartSoundDrag(
                (DependencyObject)sender,
                SoundDragData.FromAlert(_pendingSoundDragAlert),
                currentPosition);

            e.Handled = true;
        }

        private void SoundAlertSoundCell_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSoundDragActive
                || IsInsideButton(e.OriginalSource as DependencyObject)
                || sender is not FrameworkElement { DataContext: SoundAlertOption option } sourceElement
                || !option.IsEnabled
                || !ReferenceEquals(_pendingSoundDragAlert, option)
                || IsPastDragThreshold(_soundDragStartPoint, e.GetPosition(this))
                || !IsPointInsideElement(sourceElement, e.GetPosition(sourceElement)))
            {
                return;
            }

            PreviewSoundAlertOption(option);
            ClearPendingSoundDrag();
            e.Handled = true;
        }

        private void SoundPickerChoiceButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SoundChoiceOption soundChoice })
                return;

            _pendingSoundDragChoice = soundChoice;
            _soundDragStartPoint = e.GetPosition(this);
        }

        private void SoundPickerChoiceButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isSoundDragActive)
                return;

            if (e.LeftButton != MouseButtonState.Pressed || _pendingSoundDragChoice is null)
                return;

            Point currentPosition = e.GetPosition(this);
            if (!IsPastDragThreshold(_soundDragStartPoint, currentPosition))
                return;

            StartSoundDrag(
                (DependencyObject)sender,
                SoundDragData.FromChoice(_pendingSoundDragChoice),
                currentPosition);

            e.Handled = true;
        }

        private void SoundPickerChoiceButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSoundDragActive
                || sender is not FrameworkElement { DataContext: SoundChoiceOption soundChoice } sourceElement
                || !ReferenceEquals(_pendingSoundDragChoice, soundChoice)
                || IsPastDragThreshold(_soundDragStartPoint, e.GetPosition(this))
                || !IsPointInsideElement(sourceElement, e.GetPosition(sourceElement)))
            {
                return;
            }

            PreviewSoundChoice(soundChoice);
            ClearPendingSoundDrag();
        }

        private void ClearSoundAlertButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SoundAlertOption option })
                return;

            ClearSoundAlertOption(option);
            e.Handled = true;
        }

        private void SoundAlertInfiniteToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSettingsToControls)
                return;

            RefreshLockCountdownDescriptions();
            RefreshDirtyState();
            e.Handled = true;
        }

        private static bool IsInsideButton(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is ButtonBase)
                    return true;

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void SoundPickerSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshSoundPickerChoices();
        }

        private void SoundPickerSearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (SoundPickerSearchBox.Text.Length > 0)
                SoundPickerSearchBox.Clear();

            e.Handled = true;
        }

        private void Page_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSoundDragActive)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishSoundDrag(drop: false);
                e.Handled = true;
                return;
            }

            UpdateManualSoundDrag(e.GetPosition(this));
            e.Handled = true;
        }

        private void Page_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSoundDragActive)
            {
                FinishSoundDrag(drop: true, e.GetPosition(this));
                e.Handled = true;
                return;
            }

            ClearPendingSoundDragLater();
        }

        private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_isSoundDragActive)
                ClearPendingSoundDrag();

            Point pagePosition = e.GetPosition(this);
            if (TryScrollViewerWithMouseWheel(SoundAlertListScrollViewer, pagePosition, e.Delta)
                || TryScrollViewerWithMouseWheel(SoundPickerScrollViewer, pagePosition, e.Delta))
            {
                e.Handled = true;
            }
        }

        private void StartSoundDrag(DependencyObject source, SoundDragData sound, Point position)
        {
            if (_isSoundDragActive)
                FinishSoundDrag(drop: false);

            _isSoundDragActive = true;
            _activeSoundDragData = sound;
            ClearPendingSoundDrag();
            if (sound.SourceChoice is not null)
                sound.SourceChoice.IsDragging = true;

            if (sound.SourceAlert is not null)
                sound.SourceAlert.IsSoundDragging = true;

            ShowSoundDragPreview(sound, position);

            if (source is UIElement sourceElement && sourceElement.CaptureMouse())
            {
                _soundDragCaptureElement = sourceElement;
                _soundDragCaptureElement.LostMouseCapture += SoundDragCaptureElement_LostMouseCapture;
            }

            UpdateManualSoundDrag(position);
        }

        private bool TryGetSoundDragData(out SoundDragData? sound)
        {
            sound = _isSoundDragActive ? _activeSoundDragData : null;
            return sound is not null;
        }

        private void FinishSoundDrag(bool drop, Point? pagePosition = null)
        {
            try
            {
                var sound = _activeSoundDragData;
                if (drop && sound is not null)
                {
                    if (pagePosition is Point position)
                        UpdateManualSoundDrag(position);

                    if (IsSoundClearDropTarget && CanClearSoundFromPickerArea(sound))
                        ClearSoundAlertOption(sound.SourceAlert!);
                    else if (_soundDropTarget is not null && CanAssignSoundToAlert(sound))
                        ApplyDroppedSoundToAlert(_soundDropTarget, sound);
                }
            }
            finally
            {
                ReleaseSoundDragCapture();
                HideSoundDragPreview();
                ClearSoundDragState();
            }
        }

        private void SoundDragCaptureElement_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isSoundDragActive)
                FinishSoundDrag(drop: false);
        }

        private void ReleaseSoundDragCapture()
        {
            if (_soundDragCaptureElement is null)
                return;

            var capturedElement = _soundDragCaptureElement;
            _soundDragCaptureElement = null;
            capturedElement.LostMouseCapture -= SoundDragCaptureElement_LostMouseCapture;

            if (ReferenceEquals(Mouse.Captured, capturedElement))
                capturedElement.ReleaseMouseCapture();
        }

        private void UpdateManualSoundDrag(Point pagePosition)
        {
            if (_activeSoundDragData is not SoundDragData sound)
                return;

            if (!UpdateSoundDragFeedback(pagePosition))
            {
                ClearSoundDropTarget();
                IsSoundClearDropTarget = false;
                return;
            }

            if (CanClearSoundFromPickerArea(sound) && IsPointerOverSoundClearArea(pagePosition))
            {
                ClearSoundDropTarget();
                IsSoundClearDropTarget = true;
                return;
            }

            IsSoundClearDropTarget = false;
            DependencyObject? hitElement = InputHitTest(pagePosition) as DependencyObject;
            if (CanAssignSoundToAlert(sound)
                && TryFindSoundAlertRowTarget(hitElement, out _, out var option)
                && option is not null)
            {
                ShowSoundDropTarget(option);
                return;
            }

            ClearSoundDropTarget();
        }

        private bool UpdateSoundDragFeedback(Point pagePosition)
        {
            if (!IsPointInsideElement(this, pagePosition))
            {
                HideSoundDragPreview(clearContent: false);
                return false;
            }

            TryAutoScrollViewerWhileDragging(SoundAlertListScrollViewer, pagePosition);
            UpdateSoundDragPreviewPosition(pagePosition);
            return true;
        }

        private bool TryAutoScrollViewerWhileDragging(ScrollViewer scrollViewer, Point pagePosition)
        {
            if (!scrollViewer.IsVisible || scrollViewer.ScrollableHeight <= 0 || scrollViewer.ViewportHeight <= 0)
                return false;

            Point position = TranslatePoint(pagePosition, scrollViewer);
            if (!IsPointInsideElement(scrollViewer, position))
                return false;

            double edgeDistance = Math.Min(
                DragAutoScrollEdgeDistance,
                Math.Max(16, scrollViewer.ActualHeight / 4));

            if (position.Y < edgeDistance)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset - DragAutoScrollStep));
                return true;
            }

            if (position.Y > scrollViewer.ActualHeight - edgeDistance)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + DragAutoScrollStep));
                return true;
            }

            return false;
        }

        private bool TryScrollViewerWithMouseWheel(ScrollViewer scrollViewer, Point pagePosition, int delta)
        {
            if (!scrollViewer.IsVisible)
                return false;

            Point position = TranslatePoint(pagePosition, scrollViewer);
            if (!IsPointInsideElement(scrollViewer, position))
                return false;

            if (scrollViewer.ScrollableHeight <= 0)
                return true;

            double wheelNotches = Math.Max(1, Math.Abs(delta) / 120d);
            double scrollDelta = wheelNotches * 48;
            double nextOffset = delta > 0
                ? scrollViewer.VerticalOffset - scrollDelta
                : scrollViewer.VerticalOffset + scrollDelta;
            nextOffset = Math.Clamp(nextOffset, 0, scrollViewer.ScrollableHeight);
            if (Math.Abs(nextOffset - scrollViewer.VerticalOffset) < 0.1)
                return true;

            scrollViewer.ScrollToVerticalOffset(nextOffset);
            return true;
        }

        private void ApplyDroppedSoundToAlert(SoundAlertOption option, SoundDragData sound)
        {
            if (!CanAssignSoundToAlert(sound))
                return;

            if (ReferenceEquals(sound.SourceAlert, option))
                return;

            option.IsEnabled = true;
            option.SoundKey = sound.SoundKey;
            ApplyDefaultInfinitePlaybackForAssignedSound(option, sound);
            RefreshLockCountdownDescriptions();
            RefreshSoundPickerChoices();
            UpdateSoundAlertInputStates();
            RefreshDirtyState();
        }

        private static void ApplyDefaultInfinitePlaybackForAssignedSound(SoundAlertOption option, SoundDragData sound)
        {
            if (!option.SupportsInfinitePlayback)
                return;

            option.IsInfinitePlaybackEnabled = sound.IsLoopable
                ? option.DefaultInfinitePlaybackEnabled
                : false;
        }

        private void ClearSoundAlertOption(SoundAlertOption option)
        {
            option.IsEnabled = false;
            SyncSoundAlertsEnabledFromRows();
            RefreshDirtyState();
        }

        private static bool CanAssignSoundToAlert(SoundDragData sound)
        {
            return sound.SourceChoice is not null || sound.SourceAlert is not null;
        }

        private static bool CanClearSoundFromPickerArea(SoundDragData sound)
        {
            return sound.SourceAlert is not null;
        }

        private bool IsPointerOverSoundClearArea(Point pagePosition)
        {
            return IsPointInsideElement(SoundPickerPanel, TranslatePoint(pagePosition, SoundPickerPanel));
        }

        private void PreviewSoundChoice(SoundChoiceOption soundChoice)
        {
            MarkLastPreviewedSoundChoice(soundChoice.Key);
            _notificationSoundPlayer.StopChannel(SoundStudioPreviewChannelKey);
            _notificationSoundPlayer.PreviewAlert(
                soundChoice.Key,
                GetSoundAlertVolumePercent(),
                $"{soundChoice.DisplayName} sound preview");
        }

        private void PreviewSoundAlertOption(SoundAlertOption option)
        {
            MarkLastPreviewedSoundChoice(option.SoundKey);
            _notificationSoundPlayer.StopChannel(SoundStudioPreviewChannelKey);
            _notificationSoundPlayer.PreviewAlert(
                option.SoundKey,
                GetEffectiveSoundAlertVolumePercent(option),
                $"{option.DisplayName} sound preview");
        }

        private void MarkLastPreviewedSoundChoice(string soundKey)
        {
            string normalizedSoundKey = NotificationSoundPlayer.NormalizeSoundKey(soundKey);
            SoundChoiceOption? lastPreviewedChoice = _soundLibraryChoices.FirstOrDefault(
                choice => string.Equals(choice.Key, normalizedSoundKey, StringComparison.Ordinal));
            if (ReferenceEquals(_lastPreviewedSoundChoice, lastPreviewedChoice))
                return;

            if (_lastPreviewedSoundChoice is not null)
                _lastPreviewedSoundChoice.IsLastPreviewed = false;

            _lastPreviewedSoundChoice = lastPreviewedChoice;
            if (_lastPreviewedSoundChoice is not null)
                _lastPreviewedSoundChoice.IsLastPreviewed = true;
        }

        private void ClearLastPreviewedSoundChoice()
        {
            if (_lastPreviewedSoundChoice is null)
                return;

            _lastPreviewedSoundChoice.IsLastPreviewed = false;
            _lastPreviewedSoundChoice = null;
        }

        private void ClearPendingSoundDrag()
        {
            _pendingSoundDragChoice = null;
            _pendingSoundDragAlert = null;
        }

        private void ClearPendingSoundDragLater()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ClearPendingSoundDrag));
        }

        private void ShowSoundDropTarget(SoundAlertOption option)
        {
            if (ReferenceEquals(_soundDropTarget, option))
                return;

            ClearSoundDropTarget();
            _soundDropTarget = option;
            _soundDropTarget.IsSoundDropTarget = true;
        }

        private void ClearSoundDropTarget()
        {
            if (_soundDropTarget is not null)
                _soundDropTarget.IsSoundDropTarget = false;

            _soundDropTarget = null;
        }

        private void ShowSoundDragPreview(SoundDragData sound, Point position)
        {
            SoundDragPreviewText.Text = sound.DisplayName;
            UpdateSoundDragPreviewPosition(position);
            SoundDragPreviewOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateSoundDragPreviewPosition(Point position)
        {
            Canvas.SetLeft(SoundDragPreviewOverlay, position.X + 14);
            Canvas.SetTop(SoundDragPreviewOverlay, position.Y + 14);
            if (SoundDragPreviewOverlay.Visibility != Visibility.Visible)
                SoundDragPreviewOverlay.Visibility = Visibility.Visible;
        }

        private void HideSoundDragPreview(bool clearContent = true)
        {
            SoundDragPreviewOverlay.Visibility = Visibility.Collapsed;
            if (!clearContent)
                return;

            SoundDragPreviewText.Text = string.Empty;
        }

        private void ClearSoundDragState()
        {
            ClearSoundDropTarget();
            IsSoundClearDropTarget = false;

            if (_activeSoundDragData?.SourceChoice is not null)
                _activeSoundDragData.SourceChoice.IsDragging = false;

            if (_activeSoundDragData?.SourceAlert is not null)
                _activeSoundDragData.SourceAlert.IsSoundDragging = false;

            _isSoundDragActive = false;
            _activeSoundDragData = null;
            ClearPendingSoundDrag();
        }

        private void RefreshSoundPickerChoices()
        {
            string filterText = SoundPickerSearchBox.Text?.Trim() ?? string.Empty;
            IReadOnlyList<SoundChoiceOption> choices = string.IsNullOrWhiteSpace(filterText)
                ? _soundLibraryChoices
                : _soundLibraryChoices
                    .Where(choice => choice.DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            SoundPickerItemsControl.ItemsSource = choices;
            SoundPickerEmptyState.Visibility = choices.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SoundAlertThresholdBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateSoundAlertThresholdBox(sender as TextBox);
            RefreshLockCountdownDescriptions();
            RefreshDirtyState();
        }

        private void SoundAlertVolumeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateSoundAlertVolumeBox(sender as TextBox);
            RefreshDirtyState();
        }

        private void SoundAlertPlaybackDurationBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateSoundAlertPlaybackDurationBox(sender as TextBox);
            RefreshDirtyState();
        }

        private void RefreshDirtyState()
        {
            if (_isApplyingSettingsToControls)
                return;

            bool hasDirtySettings = CaptureCurrentSoundSettingsSnapshot() != CaptureSavedSoundSettingsSnapshot();
            DirtySoundBar.Visibility = hasDirtySettings ? Visibility.Visible : Visibility.Collapsed;
            CancelSoundChangesButton.IsEnabled = hasDirtySettings;
            if (hasDirtySettings)
            {
                _savedMessageTimer.Stop();
                FloatingSoundStatusBar.Visibility = Visibility.Collapsed;
            }
        }

        private SoundSettingsSnapshot CaptureCurrentSoundSettingsSnapshot()
        {
            return new SoundSettingsSnapshot(
                AreSoundAlertsEnabled(),
                GetSoundAlertVolumePercent(),
                CaptureCurrentSoundAlertConfigurationSnapshot());
        }

        private SoundSettingsSnapshot CaptureSavedSoundSettingsSnapshot()
        {
            return new SoundSettingsSnapshot(
                _settings.SoundAlertProfile != SoundAlertProfile.Off,
                ChampSelectSettings.NormalizeSoundAlertVolumePercent(_settings.SoundAlertVolumePercent),
                CaptureSavedSoundAlertConfigurationSnapshot());
        }

        private bool AreSoundAlertsEnabled()
        {
            return SoundAlertsEnabledCheckBox.IsChecked == true;
        }

        private void SyncSoundAlertsEnabledFromRows()
        {
            if (_isApplyingSettingsToControls || !AreSoundAlertsEnabled())
                return;

            if (GetSoundAlertOptions().Any(option => option.IsEnabled))
                return;

            SoundAlertsEnabledCheckBox.IsChecked = false;
        }

        private int GetSoundAlertVolumePercent()
        {
            return ChampSelectSettings.NormalizeSoundAlertVolumePercent((int)Math.Round(SoundAlertVolumeSlider.Value));
        }

        private int GetEffectiveSoundAlertVolumePercent(SoundAlertOption option)
        {
            return ChampSelectSettings.GetEffectiveSoundAlertVolumePercent(
                GetSoundAlertVolumePercent(),
                ParseSoundAlertVolumeOrDefault(option));
        }

        private void RefreshSoundAlertVolumeValueText()
        {
            if (SoundAlertVolumeValueText is null)
                return;

            SoundAlertVolumeValueText.Text = $"{GetSoundAlertVolumePercent()}%";
        }

        private void UpdateSoundAlertInputStates()
        {
            if (CustomAlertsSection is null
                || SoundAlertCustomPanel is null
                || SoundAlertsMasterRow is null
                || SoundPickerChoicesPanel is null)
            {
                return;
            }

            bool alertsEnabled = AreSoundAlertsEnabled();
            UpdateSoundAlertMasterRowState(alertsEnabled);
            SoundAlertVolumeSlider.IsEnabled = alertsEnabled;
            CustomAlertsSection.Visibility = Visibility.Visible;
            SoundAlertCustomPanel.Visibility = Visibility.Visible;
            SoundAlertCustomPanel.IsEnabled = alertsEnabled;
            SoundAlertCustomPanel.Opacity = alertsEnabled ? 1 : 0.58;
            SoundPickerChoicesPanel.IsEnabled = alertsEnabled;
            SoundPickerChoicesPanel.Opacity = alertsEnabled ? 1 : 0.58;
        }

        private void UpdateSoundAlertMasterRowState(bool enabled)
        {
            SoundAlertsMasterRow.Background = TryFindResource(enabled ? "AppInputFocusBrush" : "DangerSurfaceDeepBrush") as Brush
                ?? (enabled ? Brushes.Transparent : Brushes.DarkRed);
            SoundAlertsMasterRow.BorderBrush = TryFindResource(enabled ? "AccentBlueBrush" : "DangerAccentBrush") as Brush
                ?? (enabled ? Brushes.DodgerBlue : Brushes.IndianRed);
        }

        private void ApplySoundAlertSettingsToRows()
        {
            _settings.NormalizeSoundAlertOptions();
            foreach (var option in GetSoundAlertOptions())
            {
                var setting = _settings.GetSoundAlertSetting(option.AlertId);
                option.IsEnabled = setting.Enabled;
                option.SoundKey = NotificationSoundPlayer.NormalizeSoundKey(setting.SoundKey);
                option.VolumeText = ChampSelectSettings.NormalizeSoundAlertVolumePercent(setting.VolumePercent).ToString();
                option.ThresholdText = option.HasThreshold
                    ? ChampSelectSettings.NormalizeSoundAlertThresholdSeconds(setting.ThresholdSeconds, option.DefaultThresholdSeconds).ToString()
                    : string.Empty;
                option.IsInfinitePlaybackEnabled = option.SupportsInfinitePlayback
                    && (setting.InfinitePlaybackEnabled ?? option.DefaultInfinitePlaybackEnabled);
                option.PlaybackDurationText = option.HasPlaybackDuration
                    ? ChampSelectSettings.NormalizeSoundAlertPlaybackDurationSeconds(setting.PlaybackDurationSeconds).ToString()
                    : SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds.ToString();
                option.RefreshSelectedSound();
            }

            RefreshLockCountdownDescriptions();
        }

        private void RefreshLockCountdownDescriptions()
        {
            RefreshLockCountdownDescriptions(
                SoundAlertIds.PickLockCountdown,
                SoundAlertIds.PickLockSoon);
            RefreshLockCountdownDescriptions(
                SoundAlertIds.BanLockCountdown,
                SoundAlertIds.BanLockSoon);
        }

        private void RefreshLockCountdownDescriptions(string countdownAlertId, string closeAlertId)
        {
            var countdownOption = FindSoundAlertOption(countdownAlertId);
            var closeOption = FindSoundAlertOption(closeAlertId);
            if (countdownOption is null || closeOption is null)
                return;

            int countdownSeconds = ParseSoundAlertThresholdOrDefault(countdownOption);
            int closeSeconds = ParseSoundAlertThresholdOrDefault(closeOption);
            countdownOption.Description = !countdownOption.IsInfinitePlaybackEnabled
                ? $"Plays {countdownOption.SelectedSoundDisplayName} once at {FormatLeadSeconds(countdownSeconds)} before auto-lock."
                : countdownSeconds > closeSeconds
                ? $"Plays {countdownOption.SelectedSoundDisplayName} from {FormatLeadSeconds(countdownSeconds)} to {FormatLeadSeconds(closeSeconds)} before auto-lock. Replaced by the next countdown cue if enabled."
                : $"Plays {countdownOption.SelectedSoundDisplayName} from {FormatLeadSeconds(countdownSeconds)} until auto-lock when the next countdown cue is off.";
            closeOption.Description = closeOption.IsInfinitePlaybackEnabled
                ? $"Plays {closeOption.SelectedSoundDisplayName} from {FormatLeadSeconds(closeSeconds)} until auto-lock."
                : $"Plays {closeOption.SelectedSoundDisplayName} once at {FormatLeadSeconds(closeSeconds)} before auto-lock.";
        }

        private SoundAlertOption? FindSoundAlertOption(string alertId)
        {
            return GetSoundAlertOptions().FirstOrDefault(option => string.Equals(option.AlertId, alertId, StringComparison.Ordinal));
        }

        private static string FormatLeadSeconds(int seconds)
        {
            return $"{Math.Max(0, seconds)}s";
        }

        private Dictionary<string, SoundAlertSetting> CaptureSoundAlertSettings()
        {
            return GetSoundAlertOptions().ToDictionary(
                option => option.AlertId,
                option => new SoundAlertSetting
                {
                    Enabled = option.IsEnabled,
                    SoundKey = NotificationSoundPlayer.NormalizeSoundKey(option.SoundKey),
                    VolumePercent = ChampSelectSettings.NormalizeSoundAlertVolumePercent(ParseSoundAlertVolumeOrDefault(option)),
                    ThresholdSeconds = option.HasThreshold
                        ? ChampSelectSettings.NormalizeSoundAlertThresholdSeconds(ParseSoundAlertThresholdOrDefault(option), option.DefaultThresholdSeconds)
                        : null,
                    PlaybackDurationSeconds = option.HasPlaybackDuration
                        ? ChampSelectSettings.NormalizeSoundAlertPlaybackDurationSeconds(ParseSoundAlertPlaybackDurationOrDefault(option))
                        : null,
                    InfinitePlaybackEnabled = option.SupportsInfinitePlayback
                        ? option.IsInfinitePlaybackEnabled
                        : null
                },
                StringComparer.Ordinal);
        }

        private string CaptureCurrentSoundAlertConfigurationSnapshot()
        {
            return string.Join("|", GetSoundAlertOptions().Select(CreateSoundAlertSnapshot));
        }

        private string CaptureSavedSoundAlertConfigurationSnapshot()
        {
            _settings.NormalizeSoundAlertOptions();
            return string.Join("|", SoundAlertDefaults.Definitions.Select(definition =>
            {
                var setting = _settings.GetSoundAlertSetting(definition.Id);
                string soundKey = NotificationSoundPlayer.NormalizeSoundKey(setting.SoundKey);
                string volume = ChampSelectSettings.NormalizeSoundAlertVolumePercent(setting.VolumePercent).ToString();
                string threshold = definition.DefaultThresholdSeconds is null
                    ? string.Empty
                    : ChampSelectSettings.NormalizeSoundAlertThresholdSeconds(setting.ThresholdSeconds, definition.DefaultThresholdSeconds.Value).ToString();
                string playbackDuration = definition.DefaultPlaybackDurationSeconds is not null
                    && NotificationSoundPlayer.IsLoopableSoundKey(soundKey)
                    ? ChampSelectSettings.NormalizeSoundAlertPlaybackDurationSeconds(setting.PlaybackDurationSeconds).ToString()
                    : string.Empty;
                string infinitePlayback = definition.SupportsInfinitePlayback
                    ? (setting.InfinitePlaybackEnabled ?? definition.DefaultInfinitePlaybackEnabled).ToString()
                    : string.Empty;
                return $"{definition.Id}:{setting.Enabled}:{soundKey}:{volume}:{threshold}:{playbackDuration}:{infinitePlayback}";
            }));
        }

        private static string CreateSoundAlertSnapshot(SoundAlertOption option)
        {
            string volume = CreateNumericSnapshotText(option.VolumeText);
            string threshold = option.HasThreshold
                ? CreateNumericSnapshotText(option.ThresholdText)
                : string.Empty;
            string playbackDuration = option.HasPlaybackDuration
                ? CreateNumericSnapshotText(option.PlaybackDurationText)
                : string.Empty;
            string infinitePlayback = option.SupportsInfinitePlayback
                ? option.IsInfinitePlaybackEnabled.ToString()
                : string.Empty;
            return $"{option.AlertId}:{option.IsEnabled}:{NotificationSoundPlayer.NormalizeSoundKey(option.SoundKey)}:{volume}:{threshold}:{playbackDuration}:{infinitePlayback}";
        }

        private bool TryValidateSoundAlertSettings()
        {
            if (!AreSoundAlertsEnabled())
                return true;

            foreach (var option in GetSoundAlertOptions())
            {
                if (TryParseSoundAlertVolume(option.VolumeText, out _))
                    continue;

                ShowValidationMessage($"{option.DisplayName} volume must be a whole number between {ChampSelectSettings.MinSoundAlertVolumePercent} and {ChampSelectSettings.MaxSoundAlertVolumePercent}.");
                return false;
            }

            foreach (var option in GetSoundAlertOptions().Where(option => option.HasThreshold))
            {
                if (TryParseSoundAlertThreshold(option.ThresholdText, out _))
                    continue;

                ShowValidationMessage($"{option.DisplayName} lead time must be a whole number between {ChampSelectSettings.MinSoundAlertThresholdSeconds} and {ChampSelectSettings.MaxSoundAlertThresholdSeconds}.");
                return false;
            }

            foreach (var option in GetSoundAlertOptions().Where(option => option.HasPlaybackDuration))
            {
                if (TryParseSoundAlertPlaybackDuration(option.PlaybackDurationText, out _))
                    continue;

                ShowValidationMessage($"{option.DisplayName} play time must be a whole number between {ChampSelectSettings.MinSoundAlertPlaybackDurationSeconds} and {ChampSelectSettings.MaxSoundAlertPlaybackDurationSeconds}.");
                return false;
            }

            return true;
        }

        private void ValidateSoundAlertThresholdBox(TextBox? textBox)
        {
            if (textBox is null)
                return;

            bool isValid = textBox.DataContext is not SoundAlertOption option
                || !option.HasThreshold
                || TryParseSoundAlertThreshold(option.ThresholdText, out _);
            InputValidator.SetValidationState(textBox, isValid ? InputValidationState.Valid : InputValidationState.Invalid);
        }

        private void ValidateSoundAlertVolumeBox(TextBox? textBox)
        {
            if (textBox is null)
                return;

            bool isValid = textBox.DataContext is not SoundAlertOption option
                || TryParseSoundAlertVolume(option.VolumeText, out _);
            InputValidator.SetValidationState(textBox, isValid ? InputValidationState.Valid : InputValidationState.Invalid);
        }

        private void ValidateSoundAlertPlaybackDurationBox(TextBox? textBox)
        {
            if (textBox is null)
                return;

            bool isValid = textBox.DataContext is not SoundAlertOption option
                || !option.HasPlaybackDuration
                || TryParseSoundAlertPlaybackDuration(option.PlaybackDurationText, out _);
            InputValidator.SetValidationState(textBox, isValid ? InputValidationState.Valid : InputValidationState.Invalid);
        }

        private static int ParseSoundAlertVolumeOrDefault(SoundAlertOption option)
        {
            return TryParseSoundAlertVolume(option.VolumeText, out int volume)
                ? volume
                : ChampSelectSettings.DefaultSoundAlertVolumePercent;
        }

        private static bool TryParseSoundAlertVolume(string? text, out int volume)
        {
            volume = 0;
            if (!int.TryParse(text, out int value))
                return false;

            if (value < ChampSelectSettings.MinSoundAlertVolumePercent
                || value > ChampSelectSettings.MaxSoundAlertVolumePercent)
            {
                return false;
            }

            volume = value;
            return true;
        }

        private static int ParseSoundAlertThresholdOrDefault(SoundAlertOption option)
        {
            return TryParseSoundAlertThreshold(option.ThresholdText, out int threshold)
                ? threshold
                : option.DefaultThresholdSeconds;
        }

        private static bool TryParseSoundAlertThreshold(string? text, out int threshold)
        {
            threshold = 0;
            if (!int.TryParse(text, out int value))
                return false;

            if (value < ChampSelectSettings.MinSoundAlertThresholdSeconds
                || value > ChampSelectSettings.MaxSoundAlertThresholdSeconds)
            {
                return false;
            }

            threshold = value;
            return true;
        }

        private static int ParseSoundAlertPlaybackDurationOrDefault(SoundAlertOption option)
        {
            return TryParseSoundAlertPlaybackDuration(option.PlaybackDurationText, out int duration)
                ? duration
                : SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds;
        }

        private static bool TryParseSoundAlertPlaybackDuration(string? text, out int playbackDuration)
        {
            playbackDuration = 0;
            if (!int.TryParse(text, out int value))
                return false;

            if (value < ChampSelectSettings.MinSoundAlertPlaybackDurationSeconds
                || value > ChampSelectSettings.MaxSoundAlertPlaybackDurationSeconds)
            {
                return false;
            }

            playbackDuration = value;
            return true;
        }

        private static string CreateNumericSnapshotText(string? text)
        {
            return int.TryParse(text, out int value)
                ? value.ToString()
                : $"invalid:{text}";
        }

        private static bool IsPastDragThreshold(Point start, Point current)
        {
            return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance
                || Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
        }

        private static bool IsPointInsideElement(FrameworkElement element, Point point)
        {
            return point.X >= 0
                && point.Y >= 0
                && point.X <= element.ActualWidth
                && point.Y <= element.ActualHeight;
        }

        private static bool TryFindSoundAlertRowTarget(
            DependencyObject? start,
            out FrameworkElement? rowElement,
            out SoundAlertOption? option)
        {
            rowElement = null;
            option = null;

            for (DependencyObject? current = start; current is not null; current = GetParent(current))
            {
                if (current is FrameworkElement element
                    && element.AllowDrop
                    && element.DataContext is SoundAlertOption soundAlertOption)
                {
                    rowElement = element;
                    option = soundAlertOption;
                    return true;
                }
            }

            return false;
        }

        private static DependencyObject? GetParent(DependencyObject element)
        {
            if (element is FrameworkElement frameworkElement && frameworkElement.Parent is DependencyObject logicalParent)
                return logicalParent;

            if (element is FrameworkContentElement contentElement && contentElement.Parent is DependencyObject contentParent)
                return contentParent;

            return VisualTreeHelper.GetParent(element);
        }

        private IEnumerable<SoundAlertOption> GetSoundAlertOptions()
        {
            return _soundAlertGroups.SelectMany(group => group.Alerts);
        }

        private void ShowSavedMessage()
        {
            ShowStatusMessage("Sound settings saved.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowDefaultsRestoredMessage()
        {
            ShowStatusMessage("Default sounds restored.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowChangesCanceledMessage()
        {
            ShowStatusMessage("Sound changes canceled.", "TextSoftBrush", Brushes.SlateGray);
        }

        private void ShowValidationMessage(string message)
        {
            ShowStatusMessage(message, "DangerTextBrush", Brushes.IndianRed);
        }

        private void ShowStatusMessage(string message, string brushResourceKey, Brush fallbackBrush)
        {
            _savedMessageTimer.Stop();
            var messageBrush = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            FloatingSoundStatusText.Text = message;
            FloatingSoundStatusText.Foreground = messageBrush;
            FloatingSoundStatusBar.BorderBrush = messageBrush;
            FloatingSoundStatusBar.Visibility = Visibility.Visible;
            _savedMessageTimer.Start();
        }

        private sealed record SoundDragData(
            string SoundKey,
            string DisplayName,
            bool IsLoopable,
            SoundChoiceOption? SourceChoice,
            SoundAlertOption? SourceAlert)
        {
            public static SoundDragData FromChoice(SoundChoiceOption choice)
            {
                return new SoundDragData(choice.Key, choice.DisplayName, choice.IsLoopable, choice, null);
            }

            public static SoundDragData FromAlert(SoundAlertOption option)
            {
                return new SoundDragData(
                    option.SoundKey,
                    option.SelectedSoundDisplayName,
                    NotificationSoundPlayer.IsLoopableSoundKey(option.SoundKey),
                    null,
                    option);
            }
        }

        private sealed record SoundAlertGroupOption(string DisplayName, IReadOnlyList<SoundAlertOption> Alerts);

        private sealed class SoundAlertOption : INotifyPropertyChanged
        {
            private bool _isEnabled;
            private bool _isSoundDropTarget;
            private bool _isSoundDragging;
            private readonly bool _supportsPlaybackDuration;
            private readonly bool _defaultInfinitePlaybackEnabled;
            private string _soundKey;
            private string _description;
            private string _selectedSoundDisplayName = string.Empty;
            private bool _hasPlaybackDuration;
            private bool _isInfinitePlaybackEnabled;
            private string _volumeText;
            private string _thresholdText;
            private string _playbackDurationText;

            public SoundAlertOption(SoundAlertDefinition definition, IReadOnlyList<NotificationSoundOption> soundOptions)
            {
                AlertId = definition.Id;
                DisplayName = definition.DisplayName;
                _description = definition.Description;
                HasThreshold = definition.DefaultThresholdSeconds is not null;
                DefaultThresholdSeconds = definition.DefaultThresholdSeconds ?? SoundAlertDefaults.DefaultLockSoonThresholdSeconds;
                _supportsPlaybackDuration = definition.DefaultPlaybackDurationSeconds is not null;
                SupportsInfinitePlayback = definition.SupportsInfinitePlayback;
                _defaultInfinitePlaybackEnabled = definition.DefaultInfinitePlaybackEnabled;
                SoundChoices = soundOptions.Select(option => new SoundChoiceOption(option.Key, option.DisplayName, option.IsLoopable)).ToList();
                _isEnabled = definition.EnabledInMinimal;
                _isInfinitePlaybackEnabled = SupportsInfinitePlayback && _defaultInfinitePlaybackEnabled;
                _soundKey = NotificationSoundPlayer.NormalizeSoundKey(definition.DefaultSoundKey);
                _volumeText = ChampSelectSettings.DefaultSoundAlertVolumePercent.ToString();
                _thresholdText = definition.DefaultThresholdSeconds?.ToString() ?? string.Empty;
                _playbackDurationText = (definition.DefaultPlaybackDurationSeconds ?? SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds).ToString();
                RefreshSelectedSound();
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public string AlertId { get; }
            public string DisplayName { get; }
            public string Description
            {
                get => _description;
                set
                {
                    if (string.Equals(_description, value, StringComparison.Ordinal))
                        return;

                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
            public bool HasThreshold { get; }
            public int DefaultThresholdSeconds { get; }
            public bool SupportsInfinitePlayback { get; }
            public bool DefaultInfinitePlaybackEnabled => _defaultInfinitePlaybackEnabled;
            public bool CanShowInfinitePlaybackToggle => SupportsInfinitePlayback && IsEnabled;
            public bool HasPlaybackDuration
            {
                get => _hasPlaybackDuration;
                private set
                {
                    if (_hasPlaybackDuration == value)
                        return;

                    _hasPlaybackDuration = value;
                    OnPropertyChanged(nameof(HasPlaybackDuration));
                }
            }

            public IReadOnlyList<SoundChoiceOption> SoundChoices { get; }

            public bool IsSoundDropTarget
            {
                get => _isSoundDropTarget;
                set
                {
                    if (_isSoundDropTarget == value)
                        return;

                    _isSoundDropTarget = value;
                    OnPropertyChanged(nameof(IsSoundDropTarget));
                }
            }

            public bool IsSoundDragging
            {
                get => _isSoundDragging;
                set
                {
                    if (_isSoundDragging == value)
                        return;

                    _isSoundDragging = value;
                    OnPropertyChanged(nameof(IsSoundDragging));
                }
            }

            public string SelectedSoundDisplayName
            {
                get => _selectedSoundDisplayName;
                private set
                {
                    if (string.Equals(_selectedSoundDisplayName, value, StringComparison.Ordinal))
                        return;

                    _selectedSoundDisplayName = value;
                    OnPropertyChanged(nameof(SelectedSoundDisplayName));
                }
            }

            public bool IsEnabled
            {
                get => _isEnabled;
                set
                {
                    if (_isEnabled == value)
                        return;

                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                    OnPropertyChanged(nameof(CanShowInfinitePlaybackToggle));
                }
            }

            public bool IsInfinitePlaybackEnabled
            {
                get => _isInfinitePlaybackEnabled;
                set
                {
                    bool normalizedValue = SupportsInfinitePlayback && value;
                    if (_isInfinitePlaybackEnabled == normalizedValue)
                        return;

                    _isInfinitePlaybackEnabled = normalizedValue;
                    OnPropertyChanged(nameof(IsInfinitePlaybackEnabled));
                }
            }

            public string SoundKey
            {
                get => _soundKey;
                set
                {
                    string normalizedSoundKey = NotificationSoundPlayer.NormalizeSoundKey(value);
                    if (string.Equals(_soundKey, normalizedSoundKey, StringComparison.Ordinal))
                        return;

                    _soundKey = normalizedSoundKey;
                    OnPropertyChanged(nameof(SoundKey));
                    RefreshSelectedSound();
                }
            }

            public string VolumeText
            {
                get => _volumeText;
                set
                {
                    if (string.Equals(_volumeText, value, StringComparison.Ordinal))
                        return;

                    _volumeText = value;
                    OnPropertyChanged(nameof(VolumeText));
                }
            }

            public string ThresholdText
            {
                get => _thresholdText;
                set
                {
                    if (string.Equals(_thresholdText, value, StringComparison.Ordinal))
                        return;

                    _thresholdText = value;
                    OnPropertyChanged(nameof(ThresholdText));
                }
            }

            public string PlaybackDurationText
            {
                get => _playbackDurationText;
                set
                {
                    if (string.Equals(_playbackDurationText, value, StringComparison.Ordinal))
                        return;

                    _playbackDurationText = value;
                    OnPropertyChanged(nameof(PlaybackDurationText));
                }
            }

            public void RefreshSelectedSound()
            {
                SoundChoiceOption? selectedChoice = SoundChoices.FirstOrDefault(
                    choice => string.Equals(choice.Key, SoundKey, StringComparison.Ordinal));

                SelectedSoundDisplayName = selectedChoice?.DisplayName
                    ?? SoundChoices.FirstOrDefault()?.DisplayName
                    ?? "Default";
                HasPlaybackDuration = _supportsPlaybackDuration && selectedChoice?.IsLoopable == true;
                if (HasPlaybackDuration && string.IsNullOrWhiteSpace(PlaybackDurationText))
                    PlaybackDurationText = SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds.ToString();
            }

            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private sealed class SoundChoiceOption : INotifyPropertyChanged
        {
            private bool _isDragging;
            private bool _isLastPreviewed;

            public SoundChoiceOption(string key, string displayName, bool isLoopable)
            {
                Key = key;
                DisplayName = displayName;
                IsLoopable = isLoopable;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public string Key { get; }
            public string DisplayName { get; }
            public bool IsLoopable { get; }

            public bool IsDragging
            {
                get => _isDragging;
                set
                {
                    if (_isDragging == value)
                        return;

                    _isDragging = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragging)));
                }
            }

            public bool IsLastPreviewed
            {
                get => _isLastPreviewed;
                set
                {
                    if (_isLastPreviewed == value)
                        return;

                    _isLastPreviewed = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLastPreviewed)));
                }
            }
        }

        private readonly record struct SoundSettingsSnapshot(
            bool SoundAlertsEnabled,
            int SoundAlertVolumePercent,
            string SoundAlertConfiguration);
    }
}
