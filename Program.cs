using System;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace CpuTempApp
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "CpuTempApp_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    // Đã có instance khác đang chạy, kích hoạt cửa sổ cũ nếu muốn
                    MessageBox.Show("Ứng dụng đã chạy!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ApplicationConfiguration.Initialize();

                // Check for autostart command line flag
                bool isAutostart = false;
                if (args != null)
                {
                    foreach (var arg in args)
                    {
                        if (arg.Equals("/autostart", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("/startup", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("-autostart", StringComparison.OrdinalIgnoreCase))
                        {
                            isAutostart = true;
                            break;
                        }
                    }
                }

                // Show welcome screen ONLY on manual first run
                if (AppSettings.IsFirstRun && !isAutostart)
                {
                    using (var welcome = new WelcomeFormModern())
                    {
                        if (welcome.ShowDialog() != DialogResult.OK) return;
                    }
                    AppSettings.IsFirstRun = false;
                }

                // Start independent sensor service (before creating UI)
                // This thread won't be suspended by fullscreen apps
                SensorService.Start(AppSettings.ShowCpu, AppSettings.ShowGpu);

                // Check for updates immediately after startup (async, non-blocking)
                Task.Run(async () =>
                {
                    // Wait 500ms for UI to fully load
                    await Task.Delay(500);
                    try
                    {
                        var (hasUpdate, latestVersion) = await UpdateChecker.CheckForUpdateAsync();
                        if (hasUpdate)
                        {
                            // Show update notification on UI thread
                            if (Application.OpenForms.Count > 0)
                            {
                                Application.OpenForms[0].BeginInvoke((Action)(() =>
                                {
                                    // Show auto-update dialog (like IDM)
                                    UpdateChecker.ShowAutoUpdateDialog(latestVersion);
                                }));
                            }
                        }
                    }
                    catch { }
                });

                try
                {
                    // run ControlForm as main window; pass isAutostart to hide UI on Windows boot
                    Application.Run(new ControlFormModern(isAutostart));
                }
                finally
                {
                    // Stop sensor service on app exit
                    SensorService.Stop();
                }
            }
        }
    }
}