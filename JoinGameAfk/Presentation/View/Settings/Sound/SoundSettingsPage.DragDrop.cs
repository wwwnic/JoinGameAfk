using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.Settings.Sound
{
    public partial class SoundSettingsPage
    {
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
                || !option.HasAssignedSound)
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
                || !option.HasAssignedSound
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
            option.SoundKey = null;
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
            string? soundKey = option.SoundKey;
            if (string.IsNullOrWhiteSpace(soundKey))
                return;

            MarkLastPreviewedSoundChoice(soundKey);
            _notificationSoundPlayer.StopChannel(SoundStudioPreviewChannelKey);
            _notificationSoundPlayer.PreviewAlert(
                soundKey,
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
    }
}
