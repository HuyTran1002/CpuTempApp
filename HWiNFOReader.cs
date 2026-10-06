using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace CpuTempApp
{
    /// <summary>
    /// Manages HWiNFO64 integration:
    ///   - Auto-discovers HWiNFO64 installation (registry + common paths)
    ///   - Enables Shared Memory Support via registry before launch
    ///   - Auto-launches HWiNFO64 minimised/sensors-only if not already running
    ///   - Reads CPU/GPU temperatures from shared memory (accuracy ±1°C)
    ///
    /// Works with Secure Boot ON and VBS ON — no kernel driver required on this side.
    /// </summary>
    public static class HWiNFOReader
    {
        // ── Shared Memory constants (HWiNFO SDK) ───────────────────────────
        private const string HWINFO_SM_NAME          = "Global\\HWiNFO_SENS_SM2";
        private const uint   HWINFO_SIGNATURE        = 0x53695748; // "HWiS"
        private const int    HWINFO_STRING_LEN       = 128;
        private const int    HWINFO_UNIT_LEN         = 16;
        private const uint   READING_TYPE_TEMP       = 1;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HWiNFO_SHARED_MEM
        {
            public uint  dwSignature;
            public uint  dwVersion;
            public uint  dwRevision;
            public long  poll_time;
            public uint  dwOffsetOfSensorSection;
            public uint  dwSizeOfSensorElement;
            public uint  dwNumSensorElements;
            public uint  dwOffsetOfReadingSection;
            public uint  dwSizeOfReadingElement;
            public uint  dwNumReadingElements;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        private struct HWiNFO_SENSOR_ELEMENT
        {
            public uint dwSensorType;
            public uint dwSensorIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWINFO_STRING_LEN)]
            public string szSensorNameOriginal;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWINFO_STRING_LEN)]
            public string szSensorNameUser;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        private struct HWiNFO_READING_ELEMENT
        {
            public uint   tReading;
            public uint   dwSensorIndex;
            public uint   dwReadingID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWINFO_STRING_LEN)]
            public string szLabelOriginal;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWINFO_STRING_LEN)]
            public string szLabelUser;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWINFO_UNIT_LEN)]
            public string szUnit;
            public double Value;
            public double ValueMin;
            public double ValueMax;
            public double ValueAvg;
        }

        // ── Status ──────────────────────────────────────────────────────────
        public enum HWiNFOStatus
        {
            /// <summary>HWiNFO64 shared memory is live and readable.</summary>
            Running,
            /// <summary>HWiNFO64 was found and is being launched; wait a moment.</summary>
            Launching,
            /// <summary>HWiNFO64 is not installed on this machine.</summary>
            NotInstalled,
        }

        private static volatile HWiNFOStatus _status = HWiNFOStatus.NotInstalled;
        public  static           HWiNFOStatus Status => _status;

        // ── Auto-launch and health monitoring ───────────────────────────────
        private static volatile bool     _launchAttempted    = false;
        private static string             _exePath            = null;
        private static DateTime?          _runningSince       = null;
        private static int                _deadlockPollCount  = 0;
        private static readonly TimeSpan MaxContinuousUptime = TimeSpan.FromHours(11.5); // Restart before 12h free limit

        // ── Registry paths HWiNFO64 uses for its settings ──────────────────
        // (both HKCU root, Settings, and Sensors sub-keys are written)
        private static readonly string[] HW_REG_KEYS =
        {
            @"Software\HWiNFO64",
            @"Software\HWiNFO64\Settings",
            @"Software\HWiNFO64\Sensors",
        };

        private static readonly string[] HW_INSTALL_REG_KEYS =
        {
            @"SOFTWARE\HWiNFO64",
            @"SOFTWARE\WOW6432Node\HWiNFO64",
        };

        // ────────────────────────────────────────────────────────────────────
        //  Public API
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Call once at startup. Finds HWiNFO64, enables shared memory in
        /// its registry settings and INI, and launches it minimised if not running.
        /// Non-blocking — launch happens on a background thread.
        /// </summary>
        public static void EnsureRunning()
        {
            // If already live, nothing to do
            if (IsSharedMemoryLive())
            {
                _status = HWiNFOStatus.Running;
                if (!_runningSince.HasValue) _runningSince = DateTime.UtcNow;
                _deadlockPollCount = 0;
                return;
            }

            _exePath = FindHWiNFO64Exe();

            if (_exePath == null)
            {
                _status = HWiNFOStatus.NotInstalled;
                Log("[HWiNFO] Not installed — could not find HWiNFO64.exe");
                return;
            }

            Log($"[HWiNFO] Found at: {_exePath}");

            // Write shared-memory registry key and INI so HWiNFO starts fully automated
            EnableSharedMemoryInRegistry();

            if (IsHWiNFOProcessRunning())
            {
                // Process is running but shared memory isn't live yet
                Log("[HWiNFO] Process already running; waiting for shared memory…");
                _status = HWiNFOStatus.Launching;
                StartWatchdog();
            }
            else if (!_launchAttempted)
            {
                _launchAttempted = true;
                _status = HWiNFOStatus.Launching;
                Log("[HWiNFO] Launching HWiNFO64 (minimized, silent via INI/Registry)...");

                // Launch on background thread so we don't block the sensor thread
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo(_exePath)
                        {
                            WorkingDirectory = Path.GetDirectoryName(_exePath),
                            Arguments       = "", // Note: Command-line parameters are HWiNFO Pro only. Free version uses INI/Registry.
                            UseShellExecute = true,
                            WindowStyle     = ProcessWindowStyle.Minimized
                        };
                        Process.Start(psi);
                        StartWatchdog();
                    }
                    catch (Exception ex)
                    {
                        Log($"[HWiNFO] Launch failed: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Called every poll cycle. Updates _status and returns true when ready.
        /// Also handles proactive restart before the 12-hour free shared-memory timeout,
        /// and auto-recovers if HWiNFO process is running but shared memory stopped responding.
        /// </summary>
        public static bool CheckStatus()
        {
            if (IsSharedMemoryLive())
            {
                if (_status != HWiNFOStatus.Running)
                {
                    Log("[HWiNFO] Shared memory is LIVE and active.");
                    _runningSince = DateTime.UtcNow;
                }
                _status = HWiNFOStatus.Running;
                _deadlockPollCount = 0;

                // Proactive refresh before 12h free limit expires
                if (_runningSince.HasValue && (DateTime.UtcNow - _runningSince.Value) > MaxContinuousUptime)
                {
                    Log("[HWiNFO] Approaching 12h free limit — performing silent background refresh...");
                    RestartHWiNFO();
                    return false;
                }

                return true;
            }

            // Shared memory is NOT live
            if (_status == HWiNFOStatus.Running)
            {
                // Was running but shared memory suddenly disappeared (e.g. 12h limit reached or closed)
                Log("[HWiNFO] Shared memory disconnected — recovering...");
                _status = HWiNFOStatus.Launching;
                _launchAttempted = false;
                RestartHWiNFO();
                return false;
            }

            // If process is running but shared memory refuses to appear for ~20 poll cycles (~10s)
            if (IsHWiNFOProcessRunning())
            {
                _deadlockPollCount++;
                if (_deadlockPollCount > 20)
                {
                    Log("[HWiNFO] Process stuck without shared memory — restarting silently...");
                    _deadlockPollCount = 0;
                    RestartHWiNFO();
                    return false;
                }
            }
            else
            {
                _deadlockPollCount = 0;
                _launchAttempted = false;
                EnsureRunning();
            }

            return false;
        }

        /// <summary>
        /// Silently terminates and restarts HWiNFO64 to reset shared memory.
        /// </summary>
        public static void RestartHWiNFO()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Log("[HWiNFO] Terminating existing HWiNFO process...");
                    KillHWiNFOProcesses();
                    Thread.Sleep(1500);

                    // Re-apply settings
                    EnableSharedMemoryInRegistry();

                    _runningSince = null;
                    _launchAttempted = false;
                    _status = HWiNFOStatus.Launching;
                    EnsureRunning();
                }
                catch (Exception ex)
                {
                    Log($"[HWiNFO] Restart failed: {ex.Message}");
                }
            });
        }

        public static void KillHWiNFOProcesses()
        {
            try
            {
                var procs = Process.GetProcessesByName("HWiNFO64")
                    .Concat(Process.GetProcessesByName("HWiNFO32"));
                foreach (var p in procs)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public class SensorReading
        {
            public float? CpuTemp { get; set; }
            public float? GpuTemp { get; set; }
        }

        /// <summary>
        /// Read CPU/GPU temperatures from HWiNFO64 shared memory.
        /// Returns nulls if not available.
        /// </summary>
        public static SensorReading ReadTemperatures(bool wantCpu, bool wantGpu)
        {
            var result = new SensorReading();
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(HWINFO_SM_NAME, MemoryMappedFileRights.Read);
                using var stream = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);

                // Read header
                var hdrBytes = new byte[Marshal.SizeOf<HWiNFO_SHARED_MEM>()];
                stream.Read(hdrBytes, 0, hdrBytes.Length);
                var hdr = BytesToStruct<HWiNFO_SHARED_MEM>(hdrBytes);
                if (hdr.dwSignature != HWINFO_SIGNATURE) return result;

                // Build sensor name lookup
                int sensorElemSize = (int)hdr.dwSizeOfSensorElement;
                var sensorNames    = new string[hdr.dwNumSensorElements];
                var buf            = new byte[Math.Max(sensorElemSize, Marshal.SizeOf<HWiNFO_SENSOR_ELEMENT>())];

                for (uint i = 0; i < hdr.dwNumSensorElements; i++)
                {
                    stream.Position = hdr.dwOffsetOfSensorSection + (long)i * sensorElemSize;
                    stream.Read(buf, 0, sensorElemSize);
                    var elem = BytesToStruct<HWiNFO_SENSOR_ELEMENT>(buf);
                    sensorNames[i] = (elem.szSensorNameUser ?? elem.szSensorNameOriginal ?? "").ToLowerInvariant();
                }

                // Scan temperature readings
                float? cpuPackage  = null;
                float? cpuCoreBest = null;
                float? gpuCore     = null;
                bool   gpuPref     = false;

                int readElemSize = (int)hdr.dwSizeOfReadingElement;
                var rbuf         = new byte[Math.Max(readElemSize, Marshal.SizeOf<HWiNFO_READING_ELEMENT>())];

                for (uint i = 0; i < hdr.dwNumReadingElements; i++)
                {
                    stream.Position = hdr.dwOffsetOfReadingSection + (long)i * readElemSize;
                    stream.Read(rbuf, 0, readElemSize);
                    var r = BytesToStruct<HWiNFO_READING_ELEMENT>(rbuf);

                    if (r.tReading != READING_TYPE_TEMP) continue;

                    float val = (float)r.Value;
                    if (val < 1f || val > 125f) continue;

                    var label = (r.szLabelUser ?? r.szLabelOriginal ?? "").ToLowerInvariant();
                    var sname = r.dwSensorIndex < sensorNames.Length ? sensorNames[r.dwSensorIndex] : "";

                    // ── CPU ──────────────────────────────────────────────
                    if (wantCpu)
                    {
                        bool isCpuSensor = sname.Contains("cpu")       ||
                                           sname.Contains("processor") ||
                                           sname.Contains("intel")     ||
                                           sname.Contains("ryzen")     ||
                                           sname.Contains("amd");

                        if (isCpuSensor)
                        {
                            // Highest priority: package / die temperature
                            if (label.Contains("package")      ||
                                label.Contains("cpu package")  ||
                                label.Contains("tdie")         ||
                                label.Contains("tctl/tdie"))
                            {
                                // Keep the highest package reading if multiple present
                                if (!cpuPackage.HasValue || val > cpuPackage.Value)
                                    cpuPackage = val;
                            }
                            // Second priority: highest individual core
                            else if (label.Contains("core") && !label.Contains("average"))
                            {
                                if (!cpuCoreBest.HasValue || val > cpuCoreBest.Value)
                                    cpuCoreBest = val;
                            }
                        }
                    }

                    // ── GPU ──────────────────────────────────────────────
                    if (wantGpu)
                    {
                        bool isGpuSensor = sname.Contains("gpu")     ||
                                           sname.Contains("nvidia")  ||
                                           sname.Contains("geforce") ||
                                           sname.Contains("radeon")  ||
                                           sname.Contains("rx ");

                        if (isGpuSensor)
                        {
                            if (label.Contains("core") ||
                                label.Contains("gpu temperature") ||
                                label.Contains("edge"))
                            {
                                if (!gpuPref || !gpuCore.HasValue || val > gpuCore.Value)
                                { gpuCore = val; gpuPref = true; }
                            }
                            else if (!gpuPref)
                            {
                                if (!gpuCore.HasValue || val > gpuCore.Value)
                                    gpuCore = val;
                            }
                        }
                    }
                }

                result.CpuTemp = cpuPackage ?? cpuCoreBest;
                result.GpuTemp = gpuCore;
            }
            catch { }

            return result;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        public static bool IsSharedMemoryLive()
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(HWINFO_SM_NAME, MemoryMappedFileRights.Read);
                using var acc = mmf.CreateViewAccessor(0, Marshal.SizeOf<HWiNFO_SHARED_MEM>(), MemoryMappedFileAccess.Read);
                acc.Read(0, out HWiNFO_SHARED_MEM hdr);
                return hdr.dwSignature == HWINFO_SIGNATURE;
            }
            catch { return false; }
        }

        private static bool IsHWiNFOProcessRunning()
        {
            try
            {
                return Process.GetProcessesByName("HWiNFO64").Length > 0 ||
                       Process.GetProcessesByName("HWiNFO32").Length > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Searches registry + common install paths for HWiNFO64.exe.
        /// Returns full path or null if not found.
        /// </summary>
        private static string FindHWiNFO64Exe()
        {
            // 0. Check application folder (for bundled/portable installations next to CpuTempApp)
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string path1 = Path.Combine(baseDir, "HWiNFO64.exe");
                if (File.Exists(path1)) return path1;

                string path2 = Path.Combine(baseDir, @"HWiNFO64\HWiNFO64.exe");
                if (File.Exists(path2)) return path2;
            }
            catch { }

            // 1. Check HKLM install registry
            foreach (var regKey in HW_INSTALL_REG_KEYS)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regKey);
                    if (key == null) continue;

                    foreach (var valueName in new[] { "InstallPath", "Path", "ExePath" })
                    {
                        var raw = key.GetValue(valueName)?.ToString();
                        if (string.IsNullOrEmpty(raw)) continue;

                        // Value might be the directory or the full exe path
                        string candidate = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? raw
                            : Path.Combine(raw, "HWiNFO64.exe");

                        if (File.Exists(candidate)) return candidate;
                    }
                }
                catch { }
            }

            // 2. Well-known install paths on all fixed drives (e.g. C:, D:, etc.)
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && (drive.DriveType == DriveType.Fixed))
                    {
                        var paths = new[]
                        {
                            Path.Combine(drive.Name, @"Program Files\HWiNFO64\HWiNFO64.exe"),
                            Path.Combine(drive.Name, @"Program Files (x86)\HWiNFO64\HWiNFO64.exe"),
                            Path.Combine(drive.Name, @"Program Files\HWiNFO\HWiNFO64.exe"),
                            Path.Combine(drive.Name, @"Program Files (x86)\HWiNFO\HWiNFO64.exe"),
                        };
                        foreach (var p in paths)
                        {
                            if (File.Exists(p)) return p;
                        }
                    }
                }
            }
            catch { }

            // 3. Check if it's on PATH / HKCU Uninstall entries
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\HWiNFO64_is1");
                var loc = key?.GetValue("InstallLocation")?.ToString();
                if (!string.IsNullOrEmpty(loc))
                {
                    var exe = Path.Combine(loc, "HWiNFO64.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }

            // 4. Machine-wide Uninstall
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\HWiNFO64_is1");
                var loc = key?.GetValue("InstallLocation")?.ToString();
                if (!string.IsNullOrEmpty(loc))
                {
                    var exe = Path.Combine(loc, "HWiNFO64.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Writes SHMEMEnabled=1 to all registry keys HWiNFO64 might read,
        /// so that when it starts, shared memory is active immediately.
        /// Also writes the INI file next to the exe for portable installs.
        /// </summary>
        private static void EnableSharedMemoryInRegistry()
        {
            // First, import full pre-configured .reg file if present
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dir = _exePath != null ? Path.GetDirectoryName(_exePath) : baseDir;
                string regFile = Path.Combine(dir, "HWiNFO64_settings.reg");
                if (!File.Exists(regFile))
                {
                    regFile = Path.Combine(baseDir, "HWiNFO64_settings.reg");
                }
                if (File.Exists(regFile))
                {
                    var psiReg = new ProcessStartInfo("regedit.exe", $"/s \"{regFile}\"")
                    {
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psiReg)?.WaitForExit(3000);
                    Log("[HWiNFO] Applied verified HWiNFO64_settings.reg via regedit /s");
                }
            }
            catch { }

            // Registry (installed version fallback/assurance)
            foreach (var regPath in HW_REG_KEYS)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(regPath, true);
                    key.SetValue("SHMEMEnabled", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("SensorsSM", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("SensorsOnly", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("OpenSensors", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("ShowSensors", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("ShowSummary", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("ShowWelcome", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("ShowWelcomeAndProgress", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("MinimizeSensors", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("MinimalizeSensors", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("MinimizeMainWnd", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("MinimalizeMainWnd", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("MinimizeOnStartup", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("SensorsAutoStart", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("UpdateCheck", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("BetaCheck", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("AutoUpdate", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("AutoUpdateBetaDisable", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("OpenGpuClocksSummary", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("RivaTunerAutoClose", 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch { }
            }

            // INI file (portable version — lives next to the exe)
            if (_exePath != null)
            {
                try
                {
                    string dir     = Path.GetDirectoryName(_exePath);
                    string iniPath = Path.Combine(dir, "HWiNFO64.INI");

                    string iniContent = "[Settings]\r\n" +
                                        "SensorsOnly=1\r\n" +
                                        "ShowSensors=1\r\n" +
                                        "OpenSensors=1\r\n" +
                                        "ShowSummary=0\r\n" +
                                        "ShowWelcome=0\r\n" +
                                        "ShowWelcomeAndProgress=0\r\n" +
                                        "MinimizeSensors=1\r\n" +
                                        "MinimalizeSensors=1\r\n" +
                                        "MinimizeMainWnd=1\r\n" +
                                        "MinimalizeMainWnd=1\r\n" +
                                        "MinimizeOnStartup=1\r\n" +
                                        "SHMEMEnabled=1\r\n" +
                                        "SensorsSM=1\r\n" +
                                        "SensorsAutoStart=1\r\n" +
                                        "AutoStart=0\r\n" +
                                        "UpdateCheck=0\r\n" +
                                        "BetaCheck=0\r\n" +
                                        "AutoUpdate=0\r\n" +
                                        "AutoUpdateBetaDisable=1\r\n" +
                                        "OpenGpuClocksSummary=0\r\n" +
                                        "RivaTunerAutoClose=0\r\n" +
                                        "Theme=1\r\n";

                    string existing = File.Exists(iniPath) ? File.ReadAllText(iniPath) : "";
                    if (!existing.Contains("SensorsSM=1") || !existing.Contains("OpenSensors=1") || !existing.Contains("MinimalizeSensors=1") || !existing.Contains("ShowWelcome=0"))
                    {
                        File.WriteAllText(iniPath, iniContent);
                        Log("[HWiNFO] Wrote full startup options with SensorsSM=1 to HWiNFO64.INI");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[HWiNFO] INI write failed: {ex.Message}");
                }
            }

            Log("[HWiNFO] Auto-config registry and INI applied.");
        }

        // ── Win32 Window Automation Watchdog ──────────────────────────────
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint BM_CLICK    = 0x00F5;
        private const uint BM_SETCHECK = 0x00F1;
        private const uint WM_KEYDOWN  = 0x0100;
        private const uint WM_KEYUP    = 0x0101;
        private const int  VK_RETURN   = 0x0D;

        private static volatile bool _watchdogRunning = false;

        public static void StartWatchdog()
        {
            if (_watchdogRunning) return;
            _watchdogRunning = true;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Log("[HWiNFO Watchdog] Started background window watchdog.");
                    var sw = Stopwatch.StartNew();

                    while (sw.ElapsedMilliseconds < 25000 && !IsSharedMemoryLive())
                    {
                        var hwProcs = Process.GetProcessesByName("HWiNFO64")
                            .Concat(Process.GetProcessesByName("HWiNFO32"))
                            .ToList();

                        if (hwProcs.Count > 0)
                        {
                            foreach (var proc in hwProcs)
                            {
                                int pid = proc.Id;
                                EnumWindows((hWnd, lParam) =>
                                {
                                    GetWindowThreadProcessId(hWnd, out uint wPid);
                                    if (wPid == pid)
                                    {
                                        var titleSb = new System.Text.StringBuilder(256);
                                        GetWindowText(hWnd, titleSb, 256);
                                        string title = titleSb.ToString();

                                        var classSb = new System.Text.StringBuilder(256);
                                        GetClassName(hWnd, classSb, 256);
                                        string className = classSb.ToString();

                                        // 1. Welcome dialog check (checks Sensors-only, clicks Run)
                                        if (title.IndexOf("Welcome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            (title.IndexOf("HWiNFO", StringComparison.OrdinalIgnoreCase) >= 0 && className == "#32770"))
                                        {
                                            EnumChildWindows(hWnd, (hChild, childParam) =>
                                            {
                                                var childText = new System.Text.StringBuilder(256);
                                                GetWindowText(hChild, childText, 256);
                                                string txt = childText.ToString();

                                                // Ensure "Sensors-only" checkbox is checked
                                                if (txt.IndexOf("Sensors", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    SendMessage(hChild, BM_SETCHECK, (IntPtr)1, IntPtr.Zero);
                                                }

                                                // Click "Run" / "Start" button
                                                if (txt.Equals("Run", StringComparison.OrdinalIgnoreCase) ||
                                                    txt.Equals("&Run", StringComparison.OrdinalIgnoreCase) ||
                                                    txt.Equals("Start", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    SendMessage(hChild, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                                                    Log($"[HWiNFO Watchdog] Auto-clicked '{txt}' button on '{title}'.");
                                                }

                                                return true;
                                            }, IntPtr.Zero);

                                            // Fallback: send Enter key to dialog
                                            SendMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                                            SendMessage(hWnd, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
                                        }

                                        // 2. Alert / Warning dialogs (dismiss silently)
                                        if (title.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            title.IndexOf("Expired", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            title.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            title.IndexOf("Notice", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            EnumChildWindows(hWnd, (hChild, childParam) =>
                                            {
                                                var childText = new System.Text.StringBuilder(256);
                                                GetWindowText(hChild, childText, 256);
                                                string txt = childText.ToString();

                                                if (txt.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                                                    txt.Equals("Close", StringComparison.OrdinalIgnoreCase) ||
                                                    txt.Equals("No", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    SendMessage(hChild, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                                                    Log($"[HWiNFO Watchdog] Dismissed popup '{title}' via '{txt}'.");
                                                }
                                                return true;
                                            }, IntPtr.Zero);
                                        }
                                    }
                                    return true;
                                }, IntPtr.Zero);
                            }
                        }

                        Thread.Sleep(300);
                    }

                    if (IsSharedMemoryLive())
                    {
                        Log("[HWiNFO Watchdog] Shared memory is active. Watchdog completed.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[HWiNFO Watchdog] Error: {ex.Message}");
                }
                finally
                {
                    _watchdogRunning = false;
                }
            });
        }

        private static T BytesToStruct<T>(byte[] bytes) where T : struct
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try   { return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject()); }
            finally { handle.Free(); }
        }

        private static void Log(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensor_debug.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
            catch { }
        }
    }
}
