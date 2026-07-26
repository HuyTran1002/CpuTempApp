using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CpuTempApp
{
    public static class AppSettings
    {
        private static readonly string RegistryPath = @"HKEY_CURRENT_USER\Software\CpuTempApp";
        private static bool _showCpu = true;
        private static bool _showGpu = true;
        private static Color _textColor = Color.Cyan;  // Default color is cyan
        private static int _overlayX = -1;  // -1 means center (default)
        private static int _overlayY = 0;
        private static bool _isFirstRun = true;

        static AppSettings()
        {
            // Load settings from Registry when app starts
            LoadSettings();
        }

        private static void LoadSettings()
        {
            try
            {
                var key = Registry.CurrentUser.OpenSubKey(@"Software\CpuTempApp");
                if (key != null)
                {
                    _overlayX = (int)(key.GetValue("OverlayX") ?? -1);
                    _overlayY = (int)(key.GetValue("OverlayY") ?? 0);
                    _showCpu = Convert.ToBoolean(key.GetValue("ShowCpu") ?? true);
                    _showGpu = Convert.ToBoolean(key.GetValue("ShowGpu") ?? true);
                    _isFirstRun = Convert.ToInt32(key.GetValue("FirstRun") ?? 1) == 1;
                    
                    // Load text color (stored as ARGB int)
                    int colorArgb = (int)(key.GetValue("TextColorArgb") ?? Color.Cyan.ToArgb());
                    _textColor = Color.FromArgb(colorArgb);
                    
                    System.Diagnostics.Debug.WriteLine($"[AppSettings] Loaded from Registry: X={_overlayX}, Y={_overlayY}, Color={_textColor}, ShowCpu={_showCpu}, ShowGpu={_showGpu}, FirstRun={_isFirstRun}");
                    key.Close();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[AppSettings] Registry key not found, using defaults");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] LoadSettings error: {ex.Message}");
                // If loading fails, use defaults
                _overlayX = -1;
                _overlayY = 0;
                _textColor = Color.Cyan;
                _showCpu = true;
                _showGpu = true;
                _isFirstRun = true;
            }
        }

        // Public method to reload from Registry (for when OverlayForm is recreated)
        public static void ReloadFromRegistry()
        {
            System.Diagnostics.Debug.WriteLine("[AppSettings] ReloadFromRegistry called");
            LoadSettings();
        }

        private static void SaveSettings()
        {
            try
            {
                var key = Registry.CurrentUser.CreateSubKey(@"Software\CpuTempApp");
                if (key != null)
                {
                    key.SetValue("OverlayX", _overlayX);
                    key.SetValue("OverlayY", _overlayY);
                    key.SetValue("TextColorArgb", _textColor.ToArgb());
                    key.SetValue("ShowCpu", _showCpu);
                    key.SetValue("ShowGpu", _showGpu);
                    key.Flush();  // Force flush to Registry
                    System.Diagnostics.Debug.WriteLine($"[AppSettings] Saved to Registry: X={_overlayX}, Y={_overlayY}, Color={_textColor}, ShowCpu={_showCpu}, ShowGpu={_showGpu}");
                    key.Close();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] SaveSettings error: {ex.Message}");
                // If saving fails, continue anyway
            }
        }

        public static bool ShowCpu
        {
            get => _showCpu;
            set
            {
                if (_showCpu == value) return;
                _showCpu = value;
                SaveSettings();
                RaiseSettingsChanged();
            }
        }

        public static bool ShowGpu
        {
            get => _showGpu;
            set
            {
                if (_showGpu == value) return;
                _showGpu = value;
                SaveSettings();
                RaiseSettingsChanged();
            }
        }

        public static Color TextColor
        {
            get => _textColor;
            set
            {
                if (_textColor == value) return;
                _textColor = value;
                SaveSettings();  // Save to Registry
                RaiseSettingsChanged();
            }
        }

        public static event Action? SettingsChanged;
        public static void RaiseSettingsChanged() => SettingsChanged?.Invoke();

        // Optional: allow selecting an exact sensor name to use for display
        private static string _selectedCpuSensor = string.Empty;
        private static string _selectedGpuSensor = string.Empty;
        private static bool _cpuSensorIsDistanceToTjMax = false;
        private static int _cpuTjMax = 100;

        public static string SelectedCpuSensor
        {
            get => _selectedCpuSensor;
            set
            {
                if (_selectedCpuSensor == value) return;
                _selectedCpuSensor = value ?? string.Empty;
                RaiseSettingsChanged();
            }
        }

        public static string SelectedGpuSensor
        {
            get => _selectedGpuSensor;
            set
            {
                if (_selectedGpuSensor == value) return;
                _selectedGpuSensor = value ?? string.Empty;
                RaiseSettingsChanged();
            }
        }

        public static bool CpuSensorIsDistanceToTjMax
        {
            get => _cpuSensorIsDistanceToTjMax;
            set
            {
                if (_cpuSensorIsDistanceToTjMax == value) return;
                _cpuSensorIsDistanceToTjMax = value;
                RaiseSettingsChanged();
            }
        }

        public static int CpuTjMax
        {
            get => _cpuTjMax;
            set
            {
                if (_cpuTjMax == value) return;
                _cpuTjMax = value;
                RaiseSettingsChanged();
            }
        }

        public static int OverlayX
        {
            get => _overlayX;
            set 
            { 
                _overlayX = value;
                SaveSettings();
            }
        }

        public static int OverlayY
        {
            get => _overlayY;
            set 
            { 
                _overlayY = value;
                SaveSettings();
            }
        }

        public static void ResetOverlayPosition()
        {
            _overlayX = -1;
            _overlayY = 0;
            SaveSettings();
        }

        // Update position in-memory only (for drag preview, don't save to Registry yet)
        public static void SetOverlayPositionTemp(int x, int y)
        {
            _overlayX = x;
            _overlayY = y;
            // Don't call SaveSettings() - only update in-memory
        }

        public static bool IsFirstRun
        {
            get => _isFirstRun;
            set
            {
                _isFirstRun = value;
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\CpuTempApp");
                    key?.SetValue("FirstRun", _isFirstRun ? 1 : 0);
                }
                catch { }
            }
        }

        public static bool StartWithWindows
        {
            get
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", "/query /tn \"CpuTempMonitor\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    p?.WaitForExit();
                    return p != null && p.ExitCode == 0;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    string exePath = Application.ExecutablePath;
                    if (value)
                    {
                        // Create scheduled task with highest privileges (bypasses UAC block on Windows boot)
                        var args = $"/create /tn \"CpuTempMonitor\" /tr \"\\\"{exePath}\\\" /autostart\" /sc onlogon /rl highest /f";
                        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = System.Diagnostics.Process.Start(psi);
                        p?.WaitForExit();

                        // Clean up legacy registry Run entry if exists
                        try
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                            key?.DeleteValue("CpuTempMonitor", false);
                        }
                        catch { }
                    }
                    else
                    {
                        // Delete scheduled task
                        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", "/delete /tn \"CpuTempMonitor\" /f")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = System.Diagnostics.Process.Start(psi);
                        p?.WaitForExit();

                        // Clean up registry Run entry too
                        try
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                            key?.DeleteValue("CpuTempMonitor", false);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
    }
}