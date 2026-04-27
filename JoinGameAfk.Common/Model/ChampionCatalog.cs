using System.Text.Json;
using JoinGameAfk.Constant;

namespace JoinGameAfk.Model
{
    public sealed record ChampionInfo(int Id, string Name);

    public static class ChampionCatalog
    {
        private const string ChampionFileName = "champions.json";

        private static readonly IReadOnlyList<ChampionInfo> DefaultChampions =
        [
            new(266, "Aatrox"),
            new(103, "Ahri"),
            new(84, "Akali"),
            new(166, "Akshan"),
            new(12, "Alistar"),
            new(799, "Ambessa"),
            new(32, "Amumu"),
            new(34, "Anivia"),
            new(1, "Annie"),
            new(523, "Aphelios"),
            new(22, "Ashe"),
            new(136, "Aurelion Sol"),
            new(893, "Aurora"),
            new(268, "Azir"),
            new(432, "Bard"),
            new(200, "Bel'Veth"),
            new(53, "Blitzcrank"),
            new(63, "Brand"),
            new(201, "Braum"),
            new(233, "Briar"),
            new(51, "Caitlyn"),
            new(164, "Camille"),
            new(69, "Cassiopeia"),
            new(31, "Cho'Gath"),
            new(42, "Corki"),
            new(122, "Darius"),
            new(131, "Diana"),
            new(119, "Draven"),
            new(36, "Dr. Mundo"),
            new(245, "Ekko"),
            new(60, "Elise"),
            new(28, "Evelynn"),
            new(81, "Ezreal"),
            new(9, "Fiddlesticks"),
            new(114, "Fiora"),
            new(105, "Fizz"),
            new(3, "Galio"),
            new(41, "Gangplank"),
            new(86, "Garen"),
            new(150, "Gnar"),
            new(79, "Gragas"),
            new(104, "Graves"),
            new(887, "Gwen"),
            new(120, "Hecarim"),
            new(74, "Heimerdinger"),
            new(910, "Hwei"),
            new(420, "Illaoi"),
            new(39, "Irelia"),
            new(427, "Ivern"),
            new(40, "Janna"),
            new(59, "Jarvan IV"),
            new(24, "Jax"),
            new(126, "Jayce"),
            new(202, "Jhin"),
            new(222, "Jinx"),
            new(145, "Kai'Sa"),
            new(429, "Kalista"),
            new(43, "Karma"),
            new(30, "Karthus"),
            new(38, "Kassadin"),
            new(55, "Katarina"),
            new(10, "Kayle"),
            new(141, "Kayn"),
            new(85, "Kennen"),
            new(121, "Kha'Zix"),
            new(203, "Kindred"),
            new(240, "Kled"),
            new(96, "Kog'Maw"),
            new(897, "K'Sante"),
            new(7, "LeBlanc"),
            new(64, "Lee Sin"),
            new(89, "Leona"),
            new(876, "Lillia"),
            new(127, "Lissandra"),
            new(236, "Lucian"),
            new(117, "Lulu"),
            new(99, "Lux"),
            new(54, "Malphite"),
            new(90, "Malzahar"),
            new(57, "Maokai"),
            new(11, "Master Yi"),
            new(21, "Miss Fortune"),
            new(62, "Wukong"),
            new(82, "Mordekaiser"),
            new(25, "Morgana"),
            new(950, "Naafiri"),
            new(267, "Nami"),
            new(75, "Nasus"),
            new(111, "Nautilus"),
            new(518, "Neeko"),
            new(76, "Nidalee"),
            new(895, "Nilah"),
            new(56, "Nocturne"),
            new(20, "Nunu & Willump"),
            new(2, "Olaf"),
            new(61, "Orianna"),
            new(516, "Ornn"),
            new(80, "Pantheon"),
            new(78, "Poppy"),
            new(555, "Pyke"),
            new(246, "Qiyana"),
            new(133, "Quinn"),
            new(497, "Rakan"),
            new(33, "Rammus"),
            new(421, "Rek'Sai"),
            new(526, "Rell"),
            new(888, "Renata Glasc"),
            new(58, "Renekton"),
            new(107, "Rengar"),
            new(92, "Riven"),
            new(68, "Rumble"),
            new(13, "Ryze"),
            new(360, "Samira"),
            new(113, "Sejuani"),
            new(235, "Senna"),
            new(147, "Seraphine"),
            new(875, "Sett"),
            new(35, "Shaco"),
            new(98, "Shen"),
            new(102, "Shyvana"),
            new(27, "Singed"),
            new(14, "Sion"),
            new(15, "Sivir"),
            new(72, "Skarner"),
            new(37, "Sona"),
            new(16, "Soraka"),
            new(50, "Swain"),
            new(517, "Sylas"),
            new(134, "Syndra"),
            new(223, "Tahm Kench"),
            new(163, "Taliyah"),
            new(91, "Talon"),
            new(44, "Taric"),
            new(17, "Teemo"),
            new(412, "Thresh"),
            new(18, "Tristana"),
            new(48, "Trundle"),
            new(23, "Tryndamere"),
            new(4, "Twisted Fate"),
            new(29, "Twitch"),
            new(77, "Udyr"),
            new(6, "Urgot"),
            new(110, "Varus"),
            new(67, "Vayne"),
            new(45, "Veigar"),
            new(161, "Vel'Koz"),
            new(711, "Vex"),
            new(254, "Vi"),
            new(234, "Viego"),
            new(112, "Viktor"),
            new(8, "Vladimir"),
            new(106, "Volibear"),
            new(19, "Warwick"),
            new(498, "Xayah"),
            new(101, "Xerath"),
            new(5, "Xin Zhao"),
            new(157, "Yasuo"),
            new(777, "Yone"),
            new(83, "Yorick"),
            new(350, "Yuumi"),
            new(154, "Zac"),
            new(238, "Zed"),
            new(221, "Zeri"),
            new(115, "Ziggs"),
            new(26, "Zilean"),
            new(142, "Zoe"),
            new(143, "Zyra")
        ];

        private static readonly Lazy<ChampionCatalogState> _catalog = new(LoadCatalog);

        public static IReadOnlyList<ChampionInfo> All => _catalog.Value.All;

        public static bool TryGetById(int championId, out ChampionInfo? champion)
        {
            if (_catalog.Value.ById.TryGetValue(championId, out var match))
            {
                champion = match;
                return true;
            }

            champion = null;
            return false;
        }

        public static bool TryGetByName(string championName, out ChampionInfo? champion)
        {
            if (_catalog.Value.ByName.TryGetValue(Normalize(championName), out var match))
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

        private static string Normalize(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static ChampionCatalogState LoadCatalog()
        {
            IReadOnlyList<ChampionInfo> champions = LoadFromExternalFile() ?? DefaultChampions;

            return new ChampionCatalogState(
                champions,
                champions.ToDictionary(champion => champion.Id),
                champions.ToDictionary(champion => Normalize(champion.Name), StringComparer.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<ChampionInfo>? LoadFromExternalFile()
        {
            try
            {
                string filePath = AppStorage.ChampionFilePath;
                EnsureChampionFileExists(filePath);

                if (!File.Exists(filePath))
                    return null;

                var champions = JsonSerializer.Deserialize<List<ChampionInfo>>(File.ReadAllText(filePath));
                if (champions is null || champions.Count == 0)
                    return null;

                return champions
                    .Where(champion => champion.Id > 0 && !string.IsNullOrWhiteSpace(champion.Name))
                    .GroupBy(champion => champion.Id)
                    .Select(group => group.First() with { Name = group.First().Name.Trim() })
                    .OrderBy(champion => champion.Name)
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureChampionFileExists(string filePath)
        {
            if (File.Exists(filePath))
                return;

            string legacyFilePath = Path.Combine(AppContext.BaseDirectory, ChampionFileName);
            if (File.Exists(legacyFilePath))
            {
                AppStorage.EnsureDirectoryExists();
                File.Copy(legacyFilePath, filePath, overwrite: false);
                return;
            }

            AppStorage.EnsureDirectoryExists();

            string json = JsonSerializer.Serialize(DefaultChampions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        private sealed record ChampionCatalogState(
            IReadOnlyList<ChampionInfo> All,
            IReadOnlyDictionary<int, ChampionInfo> ById,
            IReadOnlyDictionary<string, ChampionInfo> ByName);
    }
}
