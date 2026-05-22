namespace JoinGameAfk.Model
{
    public enum SoundAlertPlaybackCommand
    {
        PlayAlert,
        StopChannel,
        PreloadAlert
    }

    public sealed record SoundAlertPlaybackRequest(
        SoundAlertPlaybackCommand Command,
        string? AlertId = null,
        string Context = "",
        string? ChannelKey = null,
        int? PlaybackDurationSeconds = null)
    {
        public static SoundAlertPlaybackRequest PlayAlert(
            string alertId,
            string context,
            string? channelKey = null,
            int? playbackDurationSeconds = null)
        {
            return new SoundAlertPlaybackRequest(
                SoundAlertPlaybackCommand.PlayAlert,
                alertId,
                context,
                channelKey,
                playbackDurationSeconds);
        }

        public static SoundAlertPlaybackRequest StopChannel(string channelKey)
        {
            return new SoundAlertPlaybackRequest(
                SoundAlertPlaybackCommand.StopChannel,
                ChannelKey: channelKey);
        }

        public static SoundAlertPlaybackRequest PreloadAlert(string alertId, string context)
        {
            return new SoundAlertPlaybackRequest(
                SoundAlertPlaybackCommand.PreloadAlert,
                alertId,
                context);
        }
    }
}
