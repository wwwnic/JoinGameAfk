using System.Windows;

namespace JoinGameAfk.Theme
{
    public static class TabButtonState
    {
        public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
            "IsActive",
            typeof(bool),
            typeof(TabButtonState),
            new PropertyMetadata(false));

        public static bool GetIsActive(DependencyObject element)
        {
            return (bool)element.GetValue(IsActiveProperty);
        }

        public static void SetIsActive(DependencyObject element, bool value)
        {
            element.SetValue(IsActiveProperty, value);
        }
    }
}
