using System;
using System.Windows.Controls;
using JoinGameAfk.Services;

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
            Dispatcher.TryInvoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
            });
        }

        public void WriteErrorLine(string message)
        {
            Dispatcher.TryInvoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ERROR: {message}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
            });
        }
    }
}
