using System.Windows;
using System.Windows.Controls;

namespace JoinGameAfk.Presentation.View.Controls
{
    public enum DropActionKind
    {
        None,
        Swap,
        Append,
        Insert,
        DuplicateRemoval,
        MoveAppend
    }

    public partial class DropActionGlyph : UserControl
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind),
            typeof(DropActionKind),
            typeof(DropActionGlyph),
            new PropertyMetadata(DropActionKind.None));

        public DropActionGlyph()
        {
            InitializeComponent();
        }

        public DropActionKind Kind
        {
            get => (DropActionKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }
    }
}
