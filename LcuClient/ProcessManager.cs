using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using LcuClient.Model;

namespace LcuClient
{
    public partial class Lcu
    {
        public class ProcessManager
        {
            private readonly string _processName;
            private Process? _cachedProcess;

            public ProcessManager(string processName)
            {
                _processName = processName;
            }

            // Get the first running process (or null)
            public Process? GetProcess()
            {
                // If we already have a cached process and it hasn't exited, return it
                if (_cachedProcess != null && !_cachedProcess.HasExited)
                    return _cachedProcess;

                _cachedProcess = Process.GetProcessesByName(_processName).FirstOrDefault();

                return _cachedProcess;
            }

            public AuthModel? GetLeagueAuth()
            {
                Process? client = GetProcess();
                if (client == null)
                    return null;

                string? commandLine = GetCommandLine(client.Id);
                if (string.IsNullOrEmpty(commandLine))
                    return null;

                // Extract port and auth token
                var portMatch = Regex.Match(commandLine, @"--app-port=""?(\d+)""?");
                var tokenMatch = Regex.Match(commandLine, @"--remoting-auth-token=([a-zA-Z0-9_-]+)");

                if (!portMatch.Success || !tokenMatch.Success)
                    return null;

                string port = portMatch.Groups[1].Value;
                string authToken = tokenMatch.Groups[1].Value;

                // Encode auth token
                string authBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{authToken}"));

                return new AuthModel(port, authBase64);
            }

            private string? GetCommandLine(int pid)
            {
                string query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}";
                using var searcher = new ManagementObjectSearcher(query);
                using var results = searcher.Get();

                foreach (ManagementObject result in results)
                {
                    return result["CommandLine"]?.ToString();
                }

                return null;
            }
        }
    }
}