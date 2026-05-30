using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Presentation.View.Controls;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View
{
    internal sealed class ChampionReferenceItem
    {
        private readonly ChampionChipLabel _chipLabel;

        public ChampionReferenceItem(ChampionInfo champion, bool isPictureEditMode)
        {
            Champion = champion;
            _chipLabel = ChampionChipLabelFormatter.Format(champion.Name);
            PortraitImageSource = ChampionTileCatalog.GetSelectedOption(champion)?.ImageSource;
            ToolTipText = isPictureEditMode
                ? $"{_chipLabel.ToolTipName}\nClick to change this champion picture."
                : $"{_chipLabel.ToolTipName}\nClick to add to the selected list, or drag into a pick/ban list.";
        }

        public ChampionInfo Champion { get; }
        public string Name => Champion.Name;
        public ImageSource? PortraitImageSource { get; }
        public string ChipDisplayText => _chipLabel.Text;
        public double ChipDisplayFontSize => _chipLabel.FontSize;
        public string ToolTipText { get; }
    }

    internal sealed record ChampionChipLabel(string Text, double FontSize, string ToolTipName);

    internal static class ChampionChipLabelFormatter
    {
        public const double DefaultFontSize = 10;
        private const double SmallFontSize = 9.25;
        private const double MinimumFontSize = 8;
        private const string LabelBreakSeedResourceName = "JoinGameAfk.Assets.champion-chip-label-breaks.json";

        private static readonly Lazy<IReadOnlyDictionary<string, string>> ConfiguredBreaks = new(LoadConfiguredBreaks);

        public static ChampionChipLabel Format(string name)
        {
            string normalizedName = string.IsNullOrWhiteSpace(name) ? "Unknown Champion" : name.Trim();
            string text = ConfiguredBreaks.Value.TryGetValue(normalizedName, out string? configuredText)
                ? configuredText
                : normalizedName;

            return new ChampionChipLabel(text, GetFontSize(text), normalizedName);
        }

        private static IReadOnlyDictionary<string, string> LoadConfiguredBreaks()
        {
            EnsureConfiguredBreaksFileExists();

            string filePath = AppStorage.ChampionChipLabelBreaksFilePath;
            if (!File.Exists(filePath))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string json = File.ReadAllText(filePath);
                var configuredBreaks = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    json,
                    new JsonSerializerOptions
                    {
                        AllowTrailingCommas = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    });

                return configuredBreaks?
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                    .ToDictionary(
                        entry => entry.Key.Trim(),
                        entry => NormalizeConfiguredBreak(entry.Value),
                        StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void EnsureConfiguredBreaksFileExists()
        {
            if (File.Exists(AppStorage.ChampionChipLabelBreaksFilePath))
                return;

            try
            {
                AppStorage.EnsureDirectoryExists();
                string json = LoadSeedConfiguredBreaksJson();
                File.WriteAllText(AppStorage.ChampionChipLabelBreaksFilePath, CreateConfiguredBreaksFileContents(json));
            }
            catch
            {
            }
        }

        private static string LoadSeedConfiguredBreaksJson()
        {
            using Stream? stream = typeof(ChampionChipLabelFormatter).Assembly.GetManifestResourceStream(LabelBreakSeedResourceName);
            if (stream is null)
                return $"{{{Environment.NewLine}}}";

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string CreateConfiguredBreaksFileContents(string json)
        {
            string header = string.Join(Environment.NewLine,
            [
                "// JoinGameAfk champion chip label breaks.",
                "// Edit this file to control where long champion names wrap in the Champion Priorities UI.",
                "// Keys are exact champion names. Values are displayed labels; use \\n inside a string to force a line break.",
                ""
            ]);

            return $"{header}{json.Trim()}{Environment.NewLine}";
        }

        private static double GetFontSize(string text)
        {
            int longestLineLength = text
                .Split('\n')
                .Max(line => line.Length);

            if (longestLineLength <= 8)
                return DefaultFontSize;

            if (longestLineLength <= 10)
                return SmallFontSize;

            return MinimumFontSize;
        }

        private static string NormalizeConfiguredBreak(string text)
        {
            return text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();
        }
    }

    public class RoleFilterOption : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public RoleFilterOption(Position position, string displayText)
        {
            Position = position;
            DisplayText = displayText;
            IconText = CreateIconText(position);
        }

        public Position Position { get; }
        public string DisplayText { get; }
        public string IconText { get; }

        public string AutomationName => Position == JoinGameAfk.Enums.Position.None
            ? "Show all champions"
            : $"Filter {DisplayText} champions";

        public string ToolTipText => AutomationName;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        private static string CreateIconText(Position position)
        {
            return position switch
            {
                Position.None => "All",
                Position.Top => "Top",
                Position.Jungle => "Jg",
                Position.Mid => "Mid",
                Position.Adc => "ADC",
                Position.Support => "Sup",
                _ => "?"
            };
        }
    }

    public class ChampionSelectionItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isDuplicateDropTarget;
        private bool _isSwapDropTarget;
        private bool _isMoveOriginDropTarget;
        private bool _isSelected;
        private bool _isDragging;
        private string _displayText = "";
        private string _chipDisplayText = "";
        private double _chipDisplayFontSize = ChampionChipLabelFormatter.DefaultFontSize;
        private string _toolTipText = "";
        private ImageSource? _portraitImageSource;

        public int ChampionId { get; init; }
        public PositionRow Row { get; init; } = null!;
        public bool IsPick { get; init; }

        public string DisplayText
        {
            get => _displayText;
            set
            {
                if (EqualityComparer<string>.Default.Equals(_displayText, value))
                    return;

                _displayText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
                ApplyChipLabel(value);
            }
        }

        public string ChipDisplayText
        {
            get => _chipDisplayText;
            private set => SetProperty(ref _chipDisplayText, value);
        }

        public double ChipDisplayFontSize
        {
            get => _chipDisplayFontSize;
            private set => SetProperty(ref _chipDisplayFontSize, value);
        }

        public string ToolTipText
        {
            get => _toolTipText;
            private set => SetProperty(ref _toolTipText, value);
        }

        public ImageSource? PortraitImageSource
        {
            get => _portraitImageSource;
            set => SetProperty(ref _portraitImageSource, value);
        }

        public bool IsDuplicateDropTarget
        {
            get => _isDuplicateDropTarget;
            set => SetProperty(ref _isDuplicateDropTarget, value);
        }

        public bool IsSwapDropTarget
        {
            get => _isSwapDropTarget;
            set => SetProperty(ref _isSwapDropTarget, value);
        }

        public bool IsMoveOriginDropTarget
        {
            get => _isMoveOriginDropTarget;
            set => SetProperty(ref _isMoveOriginDropTarget, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsDragging
        {
            get => _isDragging;
            set => SetProperty(ref _isDragging, value);
        }

        private void ApplyChipLabel(string displayText)
        {
            var chipLabel = ChampionChipLabelFormatter.Format(displayText);
            ChipDisplayText = chipLabel.Text;
            ChipDisplayFontSize = chipLabel.FontSize;
            ToolTipText = $"{chipLabel.ToolTipName}\nSelect chips for batch delete, drag selected chips to the trash, or press Backspace/Delete to remove selected champions.";
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PositionRow : INotifyPropertyChanged
    {
        private string _pickChampionIds = "";
        private string _banChampionIds = "";
        private Brush _pickBorderBrush = Brushes.Transparent;
        private Brush _banBorderBrush = Brushes.Transparent;
        private Brush _pickBackgroundBrush = Brushes.Transparent;
        private Brush _banBackgroundBrush = Brushes.Transparent;
        private Visibility _pickEndGhostVisibility = Visibility.Collapsed;
        private Visibility _banEndGhostVisibility = Visibility.Collapsed;
        private double _pickEndGhostLeft;
        private double _pickEndGhostTop;
        private double _pickEndGhostMinHeight;
        private double _banEndGhostLeft;
        private double _banEndGhostTop;
        private double _banEndGhostMinHeight;
        private DropActionKind _pickEndGhostEffect = DropActionKind.None;
        private DropActionKind _banEndGhostEffect = DropActionKind.None;

        public event PropertyChangedEventHandler? PropertyChanged;

        public PositionRow()
        {
            PickChampions.CollectionChanged += PickChampions_CollectionChanged;
            BanChampions.CollectionChanged += BanChampions_CollectionChanged;
        }

        public Position Position { get; set; }
        public string PositionName { get; set; } = "";
        public ObservableCollection<ChampionSelectionItem> PickChampions { get; } = [];
        public ObservableCollection<ChampionSelectionItem> BanChampions { get; } = [];

        public string PickChampionIds
        {
            get => _pickChampionIds;
            set => SetProperty(ref _pickChampionIds, value);
        }

        public string BanChampionIds
        {
            get => _banChampionIds;
            set => SetProperty(ref _banChampionIds, value);
        }

        public Brush PickBorderBrush
        {
            get => _pickBorderBrush;
            set => SetProperty(ref _pickBorderBrush, value);
        }

        public Brush BanBorderBrush
        {
            get => _banBorderBrush;
            set => SetProperty(ref _banBorderBrush, value);
        }

        public Brush PickBackgroundBrush
        {
            get => _pickBackgroundBrush;
            set => SetProperty(ref _pickBackgroundBrush, value);
        }

        public Brush BanBackgroundBrush
        {
            get => _banBackgroundBrush;
            set => SetProperty(ref _banBackgroundBrush, value);
        }

        public Visibility PickEndGhostVisibility
        {
            get => _pickEndGhostVisibility;
            set
            {
                if (EqualityComparer<Visibility>.Default.Equals(_pickEndGhostVisibility, value))
                    return;

                _pickEndGhostVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickEndGhostVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickPlaceholderVisibility)));
            }
        }

        public Visibility BanEndGhostVisibility
        {
            get => _banEndGhostVisibility;
            set
            {
                if (EqualityComparer<Visibility>.Default.Equals(_banEndGhostVisibility, value))
                    return;

                _banEndGhostVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BanEndGhostVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BanPlaceholderVisibility)));
            }
        }

        public double PickEndGhostLeft
        {
            get => _pickEndGhostLeft;
            set => SetProperty(ref _pickEndGhostLeft, value);
        }

        public double PickEndGhostTop
        {
            get => _pickEndGhostTop;
            set => SetProperty(ref _pickEndGhostTop, value);
        }

        public double PickEndGhostMinHeight
        {
            get => _pickEndGhostMinHeight;
            set => SetProperty(ref _pickEndGhostMinHeight, value);
        }

        public double BanEndGhostLeft
        {
            get => _banEndGhostLeft;
            set => SetProperty(ref _banEndGhostLeft, value);
        }

        public double BanEndGhostTop
        {
            get => _banEndGhostTop;
            set => SetProperty(ref _banEndGhostTop, value);
        }

        public double BanEndGhostMinHeight
        {
            get => _banEndGhostMinHeight;
            set => SetProperty(ref _banEndGhostMinHeight, value);
        }

        public DropActionKind PickEndGhostEffect
        {
            get => _pickEndGhostEffect;
            set => SetProperty(ref _pickEndGhostEffect, value);
        }

        public DropActionKind BanEndGhostEffect
        {
            get => _banEndGhostEffect;
            set => SetProperty(ref _banEndGhostEffect, value);
        }

        public Visibility PickPlaceholderVisibility => PickChampions.Count == 0 && PickEndGhostVisibility != Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility BanPlaceholderVisibility => BanChampions.Count == 0 && BanEndGhostVisibility != Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        private void PickChampions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickPlaceholderVisibility)));
        }

        private void BanChampions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BanPlaceholderVisibility)));
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}