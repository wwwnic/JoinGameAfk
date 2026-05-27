using System.Windows;
using System.Windows.Controls;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;

namespace JoinGameAfk.View.Controls
{
    public partial class ReadyAcceptDashboardPanel : UserControl
    {
        public ReadyAcceptDashboardPanel()
        {
            InitializeComponent();
            Update(ClientPhase.Unknown, isWatcherRunning: false, isClientConnected: false, new DashboardStatus());
        }

        public void Update(
            ClientPhase phase,
            bool isWatcherRunning,
            bool isClientConnected,
            DashboardStatus status)
        {
            bool isStandbyIndicator = ShouldUseStandbyIndicator(phase, status);
            ClientPhase indicatorPhase = isStandbyIndicator
                ? ClientPhase.Unknown
                : phase;

            ReadyPhaseIndicator.Update(
                indicatorPhase,
                isWatcherRunning,
                isClientConnected,
                GetPhaseIndicatorState(indicatorPhase, status),
                status.ReadyCheckAutoAcceptDelayMilliseconds,
                status.ReadyCheckAutoAcceptTimeLeftMilliseconds,
                status.ReadyCheckAutoAcceptObservedAtUtc);

            ReadyAnimationPanel.Tag = isStandbyIndicator ? "Standby" : null;
            ReadyPhaseIndicator.Opacity = isStandbyIndicator ? 0.62 : 1;
            UpdateUnsupportedQueueMessage(status);
            UpdateStatusLabel(phase, isWatcherRunning, isClientConnected, status);
            UpdateSteps(phase, isWatcherRunning, isClientConnected, status);
        }

        private void UpdateStatusLabel(
            ClientPhase phase,
            bool isWatcherRunning,
            bool isClientConnected,
            DashboardStatus status)
        {
            ReadyStatePillText.Text = GetStatePillText(phase, isWatcherRunning, isClientConnected, status);
        }

        private void UpdateSteps(
            ClientPhase phase,
            bool isWatcherRunning,
            bool isClientConnected,
            DashboardStatus status)
        {
            ResetStep(LobbyStep, LobbyStepText);
            ResetStep(QueueStep, QueueStepText);
            ResetStep(ReadyCheckStep, ReadyCheckStepText);
            ResetStep(ChampionSelectStep, ChampionSelectStepText);
            ResetChampionSelectStepHeader();

            if (!isWatcherRunning || !isClientConnected)
                return;

            if (status.IsUnsupportedMode)
                SetChampionSelectWarningStep("Unsupported queue");

            if (status.IsUnsupportedMode && IsChampionSelectStandbyPhase(phase))
            {
                SetDoneStep(LobbyStep, LobbyStepText, "Complete");
                SetDoneStep(QueueStep, QueueStepText, "Complete");
                SetDoneStep(ReadyCheckStep, ReadyCheckStepText, "Complete");
                SetChampionSelectWarningStep("Not supported");
                return;
            }

            switch (phase)
            {
                case ClientPhase.Lobby:
                    SetActiveStep(LobbyStep, LobbyStepText, "Current");
                    break;
                case ClientPhase.Matchmaking:
                    SetDoneStep(LobbyStep, LobbyStepText, "Seen");
                    SetActiveStep(QueueStep, QueueStepText, "Current");
                    break;
                case ClientPhase.ReadyCheck:
                    SetDoneStep(LobbyStep, LobbyStepText, "Seen");
                    SetDoneStep(QueueStep, QueueStepText, "Seen");
                    SetActiveStep(ReadyCheckStep, ReadyCheckStepText, GetReadyCheckStepText(status));
                    break;
                case ClientPhase.ChampSelect:
                case ClientPhase.Planning:
                    SetDoneStep(LobbyStep, LobbyStepText, "Complete");
                    SetDoneStep(QueueStep, QueueStepText, "Complete");
                    SetDoneStep(ReadyCheckStep, ReadyCheckStepText, "Accepted");
                    SetActiveStep(ChampionSelectStep, ChampionSelectStepText, "Current");
                    break;
                case ClientPhase.InGame:
                    SetDoneStep(LobbyStep, LobbyStepText, "Complete");
                    SetDoneStep(QueueStep, QueueStepText, "Complete");
                    SetDoneStep(ReadyCheckStep, ReadyCheckStepText, "Complete");
                    SetDoneStep(ChampionSelectStep, ChampionSelectStepText, "Seen");
                    break;
            }
        }

        private static string GetReadyCheckStepText(DashboardStatus status)
        {
            if (!string.IsNullOrWhiteSpace(status.ReadyCheckResponse))
                return status.ReadyCheckResponse;

            return TryGetCountdownText(status, out string countdownText)
                ? countdownText
                : "Current";
        }

        private static string GetStatePillText(
            ClientPhase phase,
            bool isWatcherRunning,
            bool isClientConnected,
            DashboardStatus status)
        {
            if (!isWatcherRunning)
                return "Stopped";

            if (!isClientConnected)
                return "Client offline";

            if (phase == ClientPhase.ReadyCheck && !string.IsNullOrWhiteSpace(status.ReadyCheckResponse))
                return status.ReadyCheckResponse;

            if (status.IsUnsupportedMode && IsChampionSelectStandbyPhase(phase))
                return "Champion select standby";

            return phase switch
            {
                ClientPhase.Lobby => "Lobby",
                ClientPhase.Matchmaking => "In queue",
                ClientPhase.ReadyCheck => "Ready check",
                ClientPhase.ChampSelect or ClientPhase.Planning => "Handed off",
                ClientPhase.InGame => "In game",
                _ => "Watching"
            };
        }

        private static string GetPhaseIndicatorState(ClientPhase phase, DashboardStatus status)
        {
            return phase == ClientPhase.ReadyCheck
                ? status.ReadyCheckResponse
                : string.Empty;
        }

        private void UpdateUnsupportedQueueMessage(DashboardStatus status)
        {
            UnsupportedQueuePanel.Visibility = status.IsUnsupportedMode
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (!status.IsUnsupportedMode)
                return;

            string queueText = string.IsNullOrWhiteSpace(status.UnsupportedQueueText)
                ? "Current queue"
                : status.UnsupportedQueueText.Trim();
            UnsupportedQueueText.Text = $"{queueText} is not supported for champion select tools. Ready accept can still run.";
        }

        private static bool ShouldUseStandbyIndicator(ClientPhase phase, DashboardStatus status)
        {
            return status.IsUnsupportedMode && IsChampionSelectStandbyPhase(phase);
        }

        private static bool IsChampionSelectStandbyPhase(ClientPhase phase)
        {
            return phase is ClientPhase.ChampSelect or ClientPhase.Planning or ClientPhase.InGame;
        }

        private static bool TryGetCountdownText(DashboardStatus status, out string countdownText)
        {
            countdownText = string.Empty;

            if (status.ReadyCheckAutoAcceptDelayMilliseconds <= 0
                || status.ReadyCheckAutoAcceptTimeLeftMilliseconds < 0
                || status.ReadyCheckAutoAcceptObservedAtUtc == DateTime.MinValue)
            {
                return false;
            }

            double elapsedMilliseconds = Math.Max(0, (DateTime.UtcNow - status.ReadyCheckAutoAcceptObservedAtUtc).TotalMilliseconds);
            double remainingMilliseconds = Math.Max(0, status.ReadyCheckAutoAcceptTimeLeftMilliseconds - elapsedMilliseconds);
            countdownText = $"{remainingMilliseconds / 1000d:0.0}s";
            return true;
        }

        private static void ResetStep(Border step, TextBlock text)
        {
            step.Tag = null;
            text.Text = "Waiting";
        }

        private static void SetActiveStep(Border step, TextBlock text, string value)
        {
            step.Tag = "Active";
            text.Text = value;
        }

        private static void SetDoneStep(Border step, TextBlock text, string value)
        {
            step.Tag = "Done";
            text.Text = value;
        }

        private static void SetStandbyStep(Border step, TextBlock text, string value)
        {
            step.Tag = "Standby";
            text.Text = value;
        }

        private void ResetChampionSelectStepHeader()
        {
            ChampionSelectStepNumberText.Text = "4";
            ChampionSelectStepTitle.Text = "Draft";
        }

        private void SetChampionSelectWarningStep(string value)
        {
            ChampionSelectStep.Tag = "Warning";
            ChampionSelectStepNumberText.Text = "!";
            ChampionSelectStepTitle.Text = "Draft unavailable";
            ChampionSelectStepText.Text = value;
        }
    }
}
