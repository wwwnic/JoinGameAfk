using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace JoinGameAfk.Presentation.View;

public partial class ReleaseUsageNoticeWindow : Window
{
    private const string RiotPolicyUrl = "https://developer.riotgames.com/policies/general";

    public ReleaseUsageNoticeWindow(string appVersion)
    {
        InitializeComponent();
        VersionTextBlock.Text = $"Installed version: {appVersion}";
    }

    public static string GetCurrentAppVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        version = version.Trim();
        int metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex > 0 ? version[..metadataIndex] : version;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PolicyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RiotPolicyUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                this,
                $"Open this page in your browser:\n{RiotPolicyUrl}",
                "Riot policy",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
