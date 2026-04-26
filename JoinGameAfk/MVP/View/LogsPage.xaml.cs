using System;
using System.Windows.Controls;

namespace JoinGameAfk.View
{
    public partial class LogsPage : Page
    {
        public LogsPage()
        {
            InitializeComponent();
        }

        public void WriteLine(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
            });
        }

        public void WriteErrorLine(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ERROR: {message}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
            });
        }
    }
}
