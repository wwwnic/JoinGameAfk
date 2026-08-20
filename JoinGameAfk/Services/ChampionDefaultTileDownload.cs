namespace JoinGameAfk.Services
{
    public sealed record ChampionDefaultTileDownloadProgress(
        string SourceVersion,
        int CheckedTileCount,
        int DownloadedTileCount,
        int UnchangedTileCount,
        int FailedTileCount,
        int TotalTileCount,
        string Message);

    public sealed record ChampionDefaultTileDownloadResult(
        string SourceVersion,
        int TileCount,
        int DownloadedTileCount,
        int UnchangedTileCount,
        int FailedTileCount,
        string CacheDirectory,
        DateTime LastDownloadedAtUtc);
}
