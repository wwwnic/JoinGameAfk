using System.Text.Json;
using System.Text.Json.Serialization;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;

namespace JoinGameAfk.Model
{
    public sealed record ChampionInfo(int Id, string Name)
    {
        public List<Position> Roles { get; init; } = [];
    }

    public sealed record ChampionCatalogSyncInfo(string? DataDragonVersion, int ChampionCount, string FilePath);

    public sealed record ChampionCatalogRefreshResult(string DataDragonVersion, int ChampionCount, string FilePath);

    public sealed record ChampionCatalogRemoteData(string DataDragonVersion, IReadOnlyList<ChampionCatalogRemoteChampion> Champions);

    public sealed record ChampionCatalogRemoteChampion(int Id, string Name);

    public interface IChampionCatalogRemoteService
    {
        Task<ChampionCatalogRemoteData> FetchLatestChampionCatalogAsync(CancellationToken cancellationToken = default);
    }

    public static class ChampionCatalog
    {
        private static readonly object CatalogLock = new();

        private static readonly JsonSerializerOptions CatalogSerializerOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly IReadOnlyList<ChampionInfo> DefaultChampions =
        [
            Champion(266, "Aatrox", Position.Top),
            Champion(103, "Ahri", Position.Mid),
            Champion(84, "Akali", Position.Top, Position.Mid),
            Champion(166, "Akshan", Position.Top, Position.Mid),
            Champion(12, "Alistar", Position.Support),
            Champion(799, "Ambessa", Position.Top, Position.Mid),
            Champion(32, "Amumu", Position.Jungle, Position.Support),
            Champion(34, "Anivia", Position.Mid),
            Champion(1, "Annie", Position.Mid, Position.Support),
            Champion(523, "Aphelios", Position.Adc),
            Champion(22, "Ashe", Position.Adc, Position.Support),
            Champion(136, "Aurelion Sol", Position.Mid),
            Champion(893, "Aurora", Position.Top, Position.Mid),
            Champion(268, "Azir", Position.Mid),
            Champion(432, "Bard", Position.Support),
            Champion(200, "Bel'Veth", Position.Jungle),
            Champion(53, "Blitzcrank", Position.Support),
            Champion(63, "Brand", Position.Jungle, Position.Mid, Position.Support),
            Champion(201, "Braum", Position.Support),
            Champion(233, "Briar", Position.Jungle),
            Champion(51, "Caitlyn", Position.Adc),
            Champion(164, "Camille", Position.Top),
            Champion(69, "Cassiopeia", Position.Top, Position.Mid),
            Champion(31, "Cho'Gath", Position.Top, Position.Mid),
            Champion(42, "Corki", Position.Mid, Position.Adc),
            Champion(122, "Darius", Position.Top),
            Champion(131, "Diana", Position.Jungle, Position.Mid),
            Champion(119, "Draven", Position.Adc),
            Champion(36, "Dr. Mundo", Position.Top, Position.Jungle),
            Champion(245, "Ekko", Position.Jungle, Position.Mid),
            Champion(60, "Elise", Position.Jungle),
            Champion(28, "Evelynn", Position.Jungle),
            Champion(81, "Ezreal", Position.Adc),
            Champion(9, "Fiddlesticks", Position.Jungle),
            Champion(114, "Fiora", Position.Top),
            Champion(105, "Fizz", Position.Mid),
            Champion(3, "Galio", Position.Mid, Position.Support),
            Champion(41, "Gangplank", Position.Top),
            Champion(86, "Garen", Position.Top),
            Champion(150, "Gnar", Position.Top),
            Champion(79, "Gragas", Position.Top, Position.Jungle, Position.Mid),
            Champion(104, "Graves", Position.Jungle),
            Champion(887, "Gwen", Position.Top, Position.Jungle),
            Champion(120, "Hecarim", Position.Jungle),
            Champion(74, "Heimerdinger", Position.Top, Position.Mid, Position.Support),
            Champion(910, "Hwei", Position.Mid, Position.Support),
            Champion(420, "Illaoi", Position.Top),
            Champion(39, "Irelia", Position.Top, Position.Mid),
            Champion(427, "Ivern", Position.Jungle),
            Champion(40, "Janna", Position.Support),
            Champion(59, "Jarvan IV", Position.Jungle, Position.Top),
            Champion(24, "Jax", Position.Top, Position.Jungle),
            Champion(126, "Jayce", Position.Top, Position.Mid),
            Champion(202, "Jhin", Position.Adc),
            Champion(222, "Jinx", Position.Adc),
            Champion(145, "Kai'Sa", Position.Adc),
            Champion(429, "Kalista", Position.Adc),
            Champion(43, "Karma", Position.Top, Position.Mid, Position.Support),
            Champion(30, "Karthus", Position.Jungle, Position.Mid, Position.Adc),
            Champion(38, "Kassadin", Position.Mid),
            Champion(55, "Katarina", Position.Mid),
            Champion(10, "Kayle", Position.Top, Position.Mid),
            Champion(141, "Kayn", Position.Jungle),
            Champion(85, "Kennen", Position.Top, Position.Mid),
            Champion(121, "Kha'Zix", Position.Jungle),
            Champion(203, "Kindred", Position.Jungle),
            Champion(240, "Kled", Position.Top, Position.Mid),
            Champion(96, "Kog'Maw", Position.Adc, Position.Mid),
            Champion(897, "K'Sante", Position.Top),
            Champion(7, "LeBlanc", Position.Mid),
            Champion(64, "Lee Sin", Position.Jungle),
            Champion(89, "Leona", Position.Support),
            Champion(876, "Lillia", Position.Top, Position.Jungle),
            Champion(127, "Lissandra", Position.Top, Position.Mid),
            Champion(236, "Lucian", Position.Mid, Position.Adc),
            Champion(117, "Lulu", Position.Top, Position.Support),
            Champion(99, "Lux", Position.Mid, Position.Support),
            Champion(54, "Malphite", Position.Top, Position.Mid, Position.Support),
            Champion(90, "Malzahar", Position.Mid),
            Champion(57, "Maokai", Position.Top, Position.Jungle, Position.Support),
            Champion(11, "Master Yi", Position.Jungle),
            Champion(21, "Miss Fortune", Position.Adc),
            Champion(62, "Wukong", Position.Top, Position.Jungle),
            Champion(82, "Mordekaiser", Position.Top, Position.Jungle),
            Champion(25, "Morgana", Position.Jungle, Position.Mid, Position.Support),
            Champion(950, "Naafiri", Position.Top, Position.Mid),
            Champion(267, "Nami", Position.Support),
            Champion(75, "Nasus", Position.Top),
            Champion(111, "Nautilus", Position.Jungle, Position.Support),
            Champion(518, "Neeko", Position.Mid, Position.Support),
            Champion(76, "Nidalee", Position.Jungle),
            Champion(895, "Nilah", Position.Adc),
            Champion(56, "Nocturne", Position.Jungle, Position.Mid),
            Champion(20, "Nunu & Willump", Position.Jungle, Position.Mid),
            Champion(2, "Olaf", Position.Top, Position.Jungle),
            Champion(61, "Orianna", Position.Mid),
            Champion(516, "Ornn", Position.Top),
            Champion(80, "Pantheon", Position.Top, Position.Mid, Position.Support),
            Champion(78, "Poppy", Position.Top, Position.Jungle, Position.Support),
            Champion(555, "Pyke", Position.Support),
            Champion(246, "Qiyana", Position.Jungle, Position.Mid),
            Champion(133, "Quinn", Position.Top),
            Champion(497, "Rakan", Position.Support),
            Champion(33, "Rammus", Position.Top, Position.Jungle),
            Champion(421, "Rek'Sai", Position.Jungle),
            Champion(526, "Rell", Position.Jungle, Position.Support),
            Champion(888, "Renata Glasc", Position.Support),
            Champion(58, "Renekton", Position.Top, Position.Mid),
            Champion(107, "Rengar", Position.Top, Position.Jungle),
            Champion(92, "Riven", Position.Top),
            Champion(68, "Rumble", Position.Top, Position.Jungle, Position.Mid),
            Champion(13, "Ryze", Position.Top, Position.Mid),
            Champion(360, "Samira", Position.Adc),
            Champion(113, "Sejuani", Position.Top, Position.Jungle),
            Champion(235, "Senna", Position.Adc, Position.Support),
            Champion(147, "Seraphine", Position.Mid, Position.Adc, Position.Support),
            Champion(875, "Sett", Position.Top, Position.Mid, Position.Support),
            Champion(35, "Shaco", Position.Jungle, Position.Support),
            Champion(98, "Shen", Position.Top, Position.Support),
            Champion(102, "Shyvana", Position.Top, Position.Jungle),
            Champion(27, "Singed", Position.Top, Position.Mid),
            Champion(14, "Sion", Position.Top),
            Champion(15, "Sivir", Position.Adc),
            Champion(72, "Skarner", Position.Top, Position.Jungle),
            Champion(37, "Sona", Position.Support),
            Champion(16, "Soraka", Position.Support),
            Champion(50, "Swain", Position.Mid, Position.Adc, Position.Support),
            Champion(517, "Sylas", Position.Top, Position.Jungle, Position.Mid),
            Champion(134, "Syndra", Position.Mid),
            Champion(223, "Tahm Kench", Position.Top, Position.Support),
            Champion(163, "Taliyah", Position.Jungle, Position.Mid),
            Champion(91, "Talon", Position.Jungle, Position.Mid),
            Champion(44, "Taric", Position.Support),
            Champion(17, "Teemo", Position.Top),
            Champion(412, "Thresh", Position.Support),
            Champion(18, "Tristana", Position.Mid, Position.Adc),
            Champion(48, "Trundle", Position.Top, Position.Jungle),
            Champion(23, "Tryndamere", Position.Top, Position.Mid),
            Champion(4, "Twisted Fate", Position.Mid),
            Champion(29, "Twitch", Position.Jungle, Position.Adc, Position.Support),
            Champion(77, "Udyr", Position.Top, Position.Jungle),
            Champion(6, "Urgot", Position.Top),
            Champion(110, "Varus", Position.Adc),
            Champion(67, "Vayne", Position.Top, Position.Adc),
            Champion(45, "Veigar", Position.Mid, Position.Adc, Position.Support),
            Champion(161, "Vel'Koz", Position.Mid, Position.Support),
            Champion(711, "Vex", Position.Mid),
            Champion(254, "Vi", Position.Jungle),
            Champion(234, "Viego", Position.Jungle),
            Champion(112, "Viktor", Position.Mid),
            Champion(8, "Vladimir", Position.Top, Position.Mid),
            Champion(106, "Volibear", Position.Top, Position.Jungle),
            Champion(19, "Warwick", Position.Top, Position.Jungle),
            Champion(498, "Xayah", Position.Adc),
            Champion(101, "Xerath", Position.Mid, Position.Support),
            Champion(5, "Xin Zhao", Position.Jungle),
            Champion(157, "Yasuo", Position.Top, Position.Mid, Position.Adc),
            Champion(777, "Yone", Position.Top, Position.Mid),
            Champion(83, "Yorick", Position.Top),
            Champion(350, "Yuumi", Position.Support),
            Champion(154, "Zac", Position.Top, Position.Jungle, Position.Support),
            Champion(238, "Zed", Position.Jungle, Position.Mid),
            Champion(221, "Zeri", Position.Adc),
            Champion(115, "Ziggs", Position.Mid, Position.Adc),
            Champion(26, "Zilean", Position.Mid, Position.Support),
            Champion(142, "Zoe", Position.Mid),
            Champion(143, "Zyra", Position.Jungle, Position.Mid, Position.Support),
        ];

        private static readonly IReadOnlyDictionary<int, ChampionInfo> DefaultChampionsById =
            DefaultChampions.ToDictionary(champion => champion.Id);

        private static ChampionCatalogState? _catalog;

        public static event EventHandler? CatalogChanged;

        public static IReadOnlyList<ChampionInfo> All => CurrentCatalog.All;

        public static bool TryGetById(int championId, out ChampionInfo? champion)
        {
            if (CurrentCatalog.ById.TryGetValue(championId, out var match))
            {
                champion = match;
                return true;
            }

            champion = null;
            return false;
        }

        public static bool TryGetByName(string championName, out ChampionInfo? champion)
        {
            if (CurrentCatalog.ByName.TryGetValue(Normalize(championName), out var match))
            {
                champion = match;
                return true;
            }

            champion = null;
            return false;
        }

        public static string FormatWithName(int championId)
        {
            return TryGetById(championId, out var champion)
                ? champion!.Name
                : "Unknown Champion";
        }

        public static ChampionCatalogSyncInfo GetLocalSyncInfo()
        {
            string filePath = AppStorage.ChampionFilePath;

            try
            {
                if (!File.Exists(filePath))
                    return new ChampionCatalogSyncInfo(null, 0, filePath);

                var catalogFile = DeserializeCatalogFile(File.ReadAllText(filePath));
                if (catalogFile is null || catalogFile.Champions.Count == 0)
                    return new ChampionCatalogSyncInfo(null, 0, filePath);

                int championCount = NormalizeChampions(catalogFile.Champions).Count;
                return new ChampionCatalogSyncInfo(catalogFile.DataDragonVersion, championCount, filePath);
            }
            catch
            {
                return new ChampionCatalogSyncInfo(null, 0, filePath);
            }
        }

        public static async Task<ChampionCatalogRefreshResult> RefreshFromDataDragonAsync(
            IChampionCatalogRemoteService remoteService,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(remoteService);

            var rolesByChampionId = LoadKnownRolesByChampionId();

            var remoteCatalog = await remoteService.FetchLatestChampionCatalogAsync(cancellationToken)
                .ConfigureAwait(false);
            string dataDragonVersion = remoteCatalog.DataDragonVersion.Trim();

            if (string.IsNullOrWhiteSpace(dataDragonVersion))
                throw new InvalidOperationException("Riot Data Dragon returned no version.");

            var champions = remoteCatalog.Champions
                .Select(champion => CreateChampionFromRemote(champion, rolesByChampionId))
                .Where(champion => champion is not null)
                .Select(champion => champion!)
                .ToList();

            if (champions.Count == 0)
                throw new InvalidOperationException("Riot Data Dragon returned no champions.");

            AppStorage.EnsureDirectoryExists();
            string filePath = AppStorage.ChampionFilePath;
            SaveCatalogFile(filePath, champions, dataDragonVersion);
            SetCatalogState(champions);

            return new ChampionCatalogRefreshResult(dataDragonVersion, champions.Count, filePath);
        }

        private static string Normalize(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static ChampionCatalogState CurrentCatalog
        {
            get
            {
                lock (CatalogLock)
                {
                    _catalog ??= LoadCatalog();
                    return _catalog;
                }
            }
        }

        private static ChampionCatalogState LoadCatalog()
        {
            IReadOnlyList<ChampionInfo> champions = LoadFromExternalFile() ?? DefaultChampions;

            return CreateCatalogState(champions);
        }

        private static IReadOnlyList<ChampionInfo>? LoadFromExternalFile()
        {
            try
            {
                string filePath = AppStorage.ChampionFilePath;
                EnsureChampionFileExists(filePath);

                if (!File.Exists(filePath))
                    return null;

                var catalogFile = DeserializeCatalogFile(File.ReadAllText(filePath));
                if (catalogFile is null || catalogFile.Champions.Count == 0)
                    return null;

                var champions = NormalizeChampions(catalogFile.Champions);

                if (catalogFile.Version < AppStorage.ChampionFileVersion)
                    SaveCatalogFile(filePath, champions, catalogFile.DataDragonVersion);

                return champions;
            }
            catch
            {
                return null;
            }
        }

        private static ChampionInfo? CreateChampionFromRemote(
            ChampionCatalogRemoteChampion champion,
            IReadOnlyDictionary<int, List<Position>> rolesByChampionId)
        {
            if (champion.Id <= 0
                || string.IsNullOrWhiteSpace(champion.Name))
            {
                return null;
            }

            return new ChampionInfo(champion.Id, champion.Name.Trim())
            {
                Roles = rolesByChampionId.TryGetValue(champion.Id, out var roles)
                    ? roles.ToList()
                    : []
            };
        }

        private static IReadOnlyDictionary<int, List<Position>> LoadKnownRolesByChampionId()
        {
            try
            {
                string filePath = AppStorage.ChampionFilePath;
                EnsureChampionFileExists(filePath);

                if (File.Exists(filePath))
                {
                    var catalogFile = DeserializeCatalogFile(File.ReadAllText(filePath));
                    if (catalogFile is not null && catalogFile.Champions.Count > 0)
                    {
                        return NormalizeChampions(catalogFile.Champions)
                            .ToDictionary(champion => champion.Id, champion => champion.Roles.ToList());
                    }
                }
            }
            catch
            {
            }

            return DefaultChampions.ToDictionary(champion => champion.Id, champion => champion.Roles.ToList());
        }

        private static void EnsureChampionFileExists(string filePath)
        {
            if (File.Exists(filePath))
                return;

            string legacyFilePath = Path.Combine(AppContext.BaseDirectory, AppStorage.ChampionFileName);
            if (File.Exists(legacyFilePath))
            {
                AppStorage.EnsureDirectoryExists();
                File.Copy(legacyFilePath, filePath, overwrite: false);
                return;
            }

            AppStorage.EnsureDirectoryExists();
            SaveCatalogFile(filePath, DefaultChampions);
        }

        private static ChampionCatalogFile? DeserializeCatalogFile(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => new ChampionCatalogFile
                {
                    Version = 0,
                    Champions = JsonSerializer.Deserialize<List<ChampionInfo>>(json, CatalogSerializerOptions) ?? []
                },
                JsonValueKind.Object => JsonSerializer.Deserialize<ChampionCatalogFile>(json, CatalogSerializerOptions),
                _ => null
            };
        }

        private static void SaveCatalogFile(
            string filePath,
            IReadOnlyList<ChampionInfo> champions,
            string? dataDragonVersion = null)
        {
            var catalogFile = new ChampionCatalogFile
            {
                Version = AppStorage.ChampionFileVersion,
                DataDragonVersion = dataDragonVersion,
                Champions = NormalizeChampions(champions).ToList()
            };

            string json = JsonSerializer.Serialize(catalogFile, CatalogSerializerOptions);
            File.WriteAllText(filePath, json);
        }

        private static void SetCatalogState(IReadOnlyList<ChampionInfo> champions)
        {
            ChampionCatalogState state = CreateCatalogState(champions);

            lock (CatalogLock)
            {
                _catalog = state;
            }

            CatalogChanged?.Invoke(null, EventArgs.Empty);
        }

        private static ChampionCatalogState CreateCatalogState(IEnumerable<ChampionInfo> champions)
        {
            var normalizedChampions = NormalizeChampions(champions);

            return new ChampionCatalogState(
                normalizedChampions,
                normalizedChampions.ToDictionary(champion => champion.Id),
                normalizedChampions.ToDictionary(champion => Normalize(champion.Name), StringComparer.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<ChampionInfo> NormalizeChampions(IEnumerable<ChampionInfo> champions)
        {
            return champions
                .Where(champion => champion.Id > 0 && !string.IsNullOrWhiteSpace(champion.Name))
                .GroupBy(champion => champion.Id)
                .Select(group => NormalizeChampion(group.First()))
                .OrderBy(champion => champion.Name)
                .ToList();
        }

        private static ChampionInfo NormalizeChampion(ChampionInfo champion)
        {
            var roles = NormalizeRoles(champion.Roles);
            if (roles.Count == 0 && DefaultChampionsById.TryGetValue(champion.Id, out var defaultChampion))
                roles = defaultChampion.Roles.ToList();

            return champion with
            {
                Name = champion.Name.Trim(),
                Roles = roles
            };
        }

        private static List<Position> NormalizeRoles(IEnumerable<Position>? roles)
        {
            if (roles is null)
                return [];

            var validRoles = new HashSet<Position>
            {
                Position.Top,
                Position.Jungle,
                Position.Mid,
                Position.Adc,
                Position.Support
            };

            return roles
                .Where(validRoles.Contains)
                .Distinct()
                .ToList();
        }

        private static ChampionInfo Champion(int id, string name, params Position[] roles)
        {
            return new ChampionInfo(id, name)
            {
                Roles = roles.ToList()
            };
        }

        private sealed record ChampionCatalogState(
            IReadOnlyList<ChampionInfo> All,
            IReadOnlyDictionary<int, ChampionInfo> ById,
            IReadOnlyDictionary<string, ChampionInfo> ByName);

        private sealed class ChampionCatalogFile
        {
            public int Version { get; set; } = AppStorage.ChampionFileVersion;

            public string? DataDragonVersion { get; set; }

            public List<ChampionInfo> Champions { get; set; } = [];
        }

    }
}
