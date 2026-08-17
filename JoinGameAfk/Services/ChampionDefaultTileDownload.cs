namespace JoinGameAfk.Services
{
    public sealed record ChampionDefaultTileDownloadProgress(
        string SourceVersion,
        int CheckedChampionCount,
        int DownloadedTileCount,
        int UnchangedTileCount,
        int FailedTileCount,
        int TotalChampionCount,
        string Message);

    public sealed record ChampionDefaultTileDownloadResult(
        string SourceVersion,
        int ChampionCount,
        int DownloadedTileCount,
        int UnchangedTileCount,
        int FailedTileCount,
        string CacheDirectory,
        DateTime LastDownloadedAtUtc);
}
