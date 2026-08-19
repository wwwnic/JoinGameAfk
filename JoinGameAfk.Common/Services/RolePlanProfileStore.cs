using JoinGameAfk.Constant;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;

namespace JoinGameAfk.Services
{
    public sealed class RolePlanProfileStore
    {
        public const int MaximumNameLength = 80;

        private const RolePlanProfileSections SupportedSections =
            RolePlanProfileSections.RolePlans | RolePlanProfileSections.ChampionPictures;

        private readonly object _syncRoot = new();
        private readonly string _filePath;
        private readonly string _iconDirectoryPath;

        public RolePlanProfileStore()
            : this(AppStorage.RolePlanProfilesFilePath, AppStorage.RolePlanProfileIconsDirectoryPath)
        {
        }

        public RolePlanProfileStore(string filePath, string iconDirectoryPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(iconDirectoryPath);
            _filePath = filePath;
            _iconDirectoryPath = iconDirectoryPath;
        }

        public IReadOnlyList<RolePlanProfile> LoadProfiles()
        {
            lock (_syncRoot)
            {
                var file = LoadFile();
                return file.Profiles.Select(CloneProfile).ToList();
            }
        }

        public RolePlanProfile AddProfile(
            string name,
            RolePlanProfileSections includedSections,
            IReadOnlyDictionary<Position, PositionPreference>? rolePlans,
            IReadOnlyDictionary<int, string>? championPictures,
            int? iconChampionId = null,
            string? iconSourcePath = null)
        {
            string normalizedName = NormalizeName(name);
            includedSections &= SupportedSections;
            if (includedSections == RolePlanProfileSections.None)
                throw new ArgumentException("A profile must include role plans, champion pictures, or both.", nameof(includedSections));

            if (includedSections.HasFlag(RolePlanProfileSections.RolePlans) && rolePlans is null)
                throw new ArgumentNullException(nameof(rolePlans));

            if (includedSections.HasFlag(RolePlanProfileSections.ChampionPictures) && championPictures is null)
                throw new ArgumentNullException(nameof(championPictures));

            DateTime savedAtUtc = DateTime.UtcNow;
            var profile = new RolePlanProfile
            {
                Id = Guid.NewGuid(),
                Name = normalizedName,
                IncludedSections = includedSections,
                CreatedAtUtc = savedAtUtc,
                UpdatedAtUtc = savedAtUtc,
                IconChampionId = iconChampionId is > 0 ? iconChampionId : null,
                RolePlans = includedSections.HasFlag(RolePlanProfileSections.RolePlans)
                    ? ClonePreferences(rolePlans)
                    : null,
                ChampionPictures = includedSections.HasFlag(RolePlanProfileSections.ChampionPictures)
                    ? NormalizePictures(championPictures)
                    : null
            };

            string? copiedIconPath = null;
            lock (_syncRoot)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(iconSourcePath))
                    {
                        copiedIconPath = CopyIcon(profile.Id, iconSourcePath);
                        profile.IconFileName = Path.GetFileName(copiedIconPath);
                    }

                    var file = LoadFile();
                    file.Profiles.Insert(0, CloneProfile(profile));
                    SaveFile(file);
                }
                catch
                {
                    TryDelete(copiedIconPath);
                    throw;
                }
            }

            return CloneProfile(profile);
        }

        public RolePlanProfile UpdateProfile(
            Guid profileId,
            string name,
            RolePlanProfileSections includedSections,
            IReadOnlyDictionary<Position, PositionPreference>? rolePlans,
            IReadOnlyDictionary<int, string>? championPictures,
            int iconChampionId,
            string iconSourcePath)
        {
            if (profileId == Guid.Empty)
                throw new ArgumentException("A profile ID is required.", nameof(profileId));

            string normalizedName = NormalizeName(name);
            includedSections &= SupportedSections;
            if (includedSections == RolePlanProfileSections.None)
                throw new ArgumentException("A profile must include role plans, champion pictures, or both.", nameof(includedSections));

            if (includedSections.HasFlag(RolePlanProfileSections.RolePlans) && rolePlans is null)
                throw new ArgumentNullException(nameof(rolePlans));

            if (includedSections.HasFlag(RolePlanProfileSections.ChampionPictures) && championPictures is null)
                throw new ArgumentNullException(nameof(championPictures));

            if (iconChampionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(iconChampionId), "A profile icon champion is required.");

            if (!File.Exists(iconSourcePath))
                throw new FileNotFoundException("The selected profile picture is no longer available.", iconSourcePath);

            string extension = Path.GetExtension(iconSourcePath);
            if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Profile pictures must use a JPEG champion tile.", nameof(iconSourcePath));
            }

            lock (_syncRoot)
            {
                var file = LoadFile();
                var profile = file.Profiles.FirstOrDefault(candidate => candidate.Id == profileId)
                    ?? throw new InvalidOperationException("The selected profile no longer exists.");

                Directory.CreateDirectory(_iconDirectoryPath);
                string destinationPath = Path.Combine(_iconDirectoryPath, $"{profile.Id:N}.jpg");
                string temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
                string backupPath = $"{destinationPath}.{Guid.NewGuid():N}.bak";
                bool destinationWasReplaced = false;
                bool backupWasCreated = false;
                try
                {
                    File.Copy(iconSourcePath, temporaryPath, overwrite: false);
                    if (File.Exists(destinationPath))
                    {
                        File.Move(destinationPath, backupPath, overwrite: false);
                        backupWasCreated = true;
                    }

                    File.Move(temporaryPath, destinationPath, overwrite: false);
                    destinationWasReplaced = true;

                    profile.Name = normalizedName;
                    profile.IncludedSections = includedSections;
                    profile.RolePlans = includedSections.HasFlag(RolePlanProfileSections.RolePlans)
                        ? ClonePreferences(rolePlans)
                        : null;
                    profile.ChampionPictures = includedSections.HasFlag(RolePlanProfileSections.ChampionPictures)
                        ? NormalizePictures(championPictures)
                        : null;
                    profile.IconChampionId = iconChampionId;
                    profile.IconFileName = Path.GetFileName(destinationPath);
                    profile.UpdatedAtUtc = DateTime.UtcNow;
                    SaveFile(file);
                    TryDelete(backupPath);
                    return CloneProfile(profile);
                }
                catch
                {
                    TryDelete(temporaryPath);
                    if (destinationWasReplaced)
                        TryDelete(destinationPath);
                    if (backupWasCreated && File.Exists(backupPath))
                        File.Move(backupPath, destinationPath, overwrite: false);
                    throw;
                }
            }
        }

        public bool DeleteProfile(Guid profileId)
        {
            if (profileId == Guid.Empty)
                return false;

            lock (_syncRoot)
            {
                var file = LoadFile();
                var profile = file.Profiles.FirstOrDefault(candidate => candidate.Id == profileId);
                if (profile is null)
                    return false;

                file.Profiles.Remove(profile);
                SaveFile(file);
                TryDelete(GetIconPath(profile));
                return true;
            }
        }

        public RolePlanProfile MoveProfile(Guid profileId, int offset)
        {
            if (profileId == Guid.Empty)
                throw new ArgumentException("A profile ID is required.", nameof(profileId));

            if (offset == 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "A profile must move up or down.");

            lock (_syncRoot)
            {
                var file = LoadFile();
                int currentIndex = file.Profiles.FindIndex(profile => profile.Id == profileId);
                if (currentIndex < 0)
                    throw new InvalidOperationException("The selected profile no longer exists.");

                int targetIndex = Math.Clamp(currentIndex + Math.Sign(offset), 0, file.Profiles.Count - 1);
                RolePlanProfile profile = file.Profiles[currentIndex];
                if (targetIndex == currentIndex)
                    return CloneProfile(profile);

                file.Profiles.RemoveAt(currentIndex);
                file.Profiles.Insert(targetIndex, profile);
                SaveFile(file);
                return CloneProfile(profile);
            }
        }

        public string? GetIconPath(RolePlanProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (!TryGetSafeJpegFileName(profile.IconFileName, out string fileName))
                return null;

            return Path.Combine(_iconDirectoryPath, fileName);
        }

        private RolePlanProfilesFile LoadFile()
        {
            return JsonSettingsStore.Load(
                _filePath,
                () => new RolePlanProfilesFile(),
                NormalizeFile);
        }

        private void SaveFile(RolePlanProfilesFile file)
        {
            JsonSettingsStore.Save(_filePath, file, NormalizeFile);
        }

        private string CopyIcon(Guid profileId, string sourcePath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("The selected profile picture is no longer available.", sourcePath);

            string extension = Path.GetExtension(sourcePath);
            if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Profile pictures must use a JPEG champion tile.", nameof(sourcePath));
            }

            Directory.CreateDirectory(_iconDirectoryPath);
            string destinationPath = Path.Combine(_iconDirectoryPath, $"{profileId:N}.jpg");
            string temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(sourcePath, temporaryPath, overwrite: false);
                File.Move(temporaryPath, destinationPath, overwrite: false);
                return destinationPath;
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void NormalizeFile(RolePlanProfilesFile file)
        {
            file.Version = AppStorage.RolePlanProfilesFileVersion;
            file.Profiles ??= [];

            var seenIds = new HashSet<Guid>();
            file.Profiles = file.Profiles
                .Where(profile => profile is not null)
                .Select(NormalizeProfile)
                .Where(profile => profile is not null && seenIds.Add(profile.Id))
                .Cast<RolePlanProfile>()
                .ToList();
        }

        private static RolePlanProfile? NormalizeProfile(RolePlanProfile profile)
        {
            profile.Name = profile.Name?.Trim() ?? string.Empty;
            if (profile.Name.Length == 0)
                return null;

            if (profile.Name.Length > MaximumNameLength)
                profile.Name = profile.Name[..MaximumNameLength];

            if (profile.Id == Guid.Empty)
                profile.Id = Guid.NewGuid();

            profile.IncludedSections &= SupportedSections;
            if (profile.IncludedSections == RolePlanProfileSections.None)
                return null;

            if (profile.CreatedAtUtc == default)
                profile.CreatedAtUtc = DateTime.UtcNow;

            if (profile.UpdatedAtUtc == default)
                profile.UpdatedAtUtc = profile.CreatedAtUtc;

            if (profile.IconChampionId is <= 0)
                profile.IconChampionId = null;

            if (!TryGetSafeJpegFileName(profile.IconFileName, out string iconFileName))
                profile.IconFileName = null;
            else
                profile.IconFileName = iconFileName;

            profile.RolePlans = profile.IncludedSections.HasFlag(RolePlanProfileSections.RolePlans)
                ? ClonePreferences(profile.RolePlans)
                : null;
            profile.ChampionPictures = profile.IncludedSections.HasFlag(RolePlanProfileSections.ChampionPictures)
                ? NormalizePictures(profile.ChampionPictures)
                : null;
            return profile;
        }

        private static string NormalizeName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            string normalized = name.Trim();
            if (normalized.Length > MaximumNameLength)
                throw new ArgumentOutOfRangeException(nameof(name), $"Profile names can contain at most {MaximumNameLength} characters.");

            return normalized;
        }

        private static Dictionary<Position, PositionPreference> ClonePreferences(
            IReadOnlyDictionary<Position, PositionPreference>? preferences)
        {
            var result = new Dictionary<Position, PositionPreference>();
            foreach (Position position in Enum.GetValues<Position>().Where(position => position != Position.None))
            {
                PositionPreference? preference = preferences?.GetValueOrDefault(position);
                result[position] = new PositionPreference
                {
                    PickChampionIds = NormalizeChampionIds(preference?.PickChampionIds),
                    BanChampionIds = NormalizeChampionIds(preference?.BanChampionIds)
                };
            }

            return result;
        }

        private static List<int> NormalizeChampionIds(IEnumerable<int>? championIds)
        {
            if (championIds is null)
                return [];

            var seen = new HashSet<int>();
            return championIds.Where(championId => championId > 0 && seen.Add(championId)).ToList();
        }

        private static Dictionary<int, string> NormalizePictures(IReadOnlyDictionary<int, string>? pictures)
        {
            if (pictures is null)
                return [];

            return pictures
                .Where(entry => entry.Key > 0 && TryGetSafeJpegFileName(entry.Value, out _))
                .ToDictionary(
                    entry => entry.Key,
                    entry => Path.GetFileName(entry.Value.Trim()));
        }

        private static bool TryGetSafeJpegFileName(string? value, out string fileName)
        {
            fileName = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            string safeFileName = Path.GetFileName(trimmed);
            if (!string.Equals(trimmed, safeFileName, StringComparison.Ordinal)
                || !safeFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fileName = safeFileName;
            return true;
        }

        private static RolePlanProfile CloneProfile(RolePlanProfile profile)
        {
            return new RolePlanProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                IncludedSections = profile.IncludedSections,
                CreatedAtUtc = profile.CreatedAtUtc,
                UpdatedAtUtc = profile.UpdatedAtUtc,
                IconChampionId = profile.IconChampionId,
                IconFileName = profile.IconFileName,
                RolePlans = profile.RolePlans is null ? null : ClonePreferences(profile.RolePlans),
                ChampionPictures = profile.ChampionPictures is null ? null : NormalizePictures(profile.ChampionPictures)
            };
        }

        private static void TryDelete(string? filePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
            }
        }
    }
}
