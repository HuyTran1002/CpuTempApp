using System;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace CpuTempApp
{
    static class Program
    {
        [STAThread]
        static void Main()
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

                // show welcome and options if you want (same as before)
                using (var welcome = new WelcomeFormModern())
                {
                    if (welcome.ShowDialog() != DialogResult.OK) return;
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
                    // run ControlForm as main window; it will create the overlay and tray
                    Application.Run(new ControlFormModern());
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