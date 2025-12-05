using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;

namespace CpuTempApp
{
    public class UpdateChecker
    {
        private const string VERSION_CHECK_URL = "https://raw.githubusercontent.com/HuyTran1002/CpuTempApp/main/version.txt";
        private const string DOWNLOAD_URL = "https://github.com/HuyTran1002/CpuTempApp/releases/latest";
        
        public static Version CurrentVersion
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version ?? new Version(1, 0, 0);
            }
        }

        public static async Task<(bool hasUpdate, string latestVersion)> CheckForUpdateAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                
                var versionString = await client.GetStringAsync(VERSION_CHECK_URL);
                versionString = versionString.Trim();
                
                if (Version.TryParse(versionString, out var latestVersion))
                {
                    bool hasUpdate = latestVersion > CurrentVersion;
                    return (hasUpdate, versionString);
                }
            }
            catch
            {
                // Network error or file not found - silently ignore
            }
            
            return (false, CurrentVersion.ToString());
        }

        public static void ShowUpdateDialog(string latestVersion)
        {
            var result = MessageBox.Show(
                $"A new version ({latestVersion}) is available!\n\n" +
                $"Current version: {CurrentVersion}\n" +
                $"Latest version: {latestVersion}\n\n" +
                $"Would you like to download the update now?",
                "Update Available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information
            );

            if (result == DialogResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = DOWNLOAD_URL,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }
}
