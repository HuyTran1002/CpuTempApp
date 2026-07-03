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

        // ── Auto-launch state ───────────────────────────────────────────────
        private static volatile bool _launchAttempted = false;
        private static string        _exePath         = null;

        // ── Registry paths HWiNFO64 uses for its settings ──────────────────
        // (both HKCU root and HKCU\Settings sub-key are tried)
        private static readonly string[] HW_REG_KEYS =
        {
            @"Software\HWiNFO64",
            @"Software\HWiNFO64\Settings",
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
        /// its registry settings, and launches it minimised if not running.
        /// Non-blocking — launch happens on a background thread.
        /// </summary>
        public static void EnsureRunning()
        {
            // If already live, nothing to do
            if (IsSharedMemoryLive()) { _status = HWiNFOStatus.Running; return; }

            _exePath = FindHWiNFO64Exe();

            if (_exePath == null)
            {
                _status = HWiNFOStatus.NotInstalled;
                Log("[HWiNFO] Not installed — could not find HWiNFO64.exe");
                return;
            }

            Log($"[HWiNFO] Found at: {_exePath}");

            // Write shared-memory registry key so HWiNFO starts with it enabled
            EnableSharedMemoryInRegistry();

            if (IsHWiNFOProcessRunning())
            {
                // Process is running but shared memory isn't live yet — maybe it needs
                // a moment, or shared memory was just disabled. We'll keep polling.
                Log("[HWiNFO] Process already running; waiting for shared memory…");
                _status = HWiNFOStatus.Launching;
            }
            else if (!_launchAttempted)
            {
                _launchAttempted = true;
                _status = HWiNFOStatus.Launching;
                Log("[HWiNFO] Launching HWiNFO64 minimised…");

                // Launch on background thread so we don't block the sensor thread
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo(_exePath)
                        {
                            Arguments       = "",
                            UseShellExecute = true
                        };
                        Process.Start(psi);
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
        /// </summary>
        public static bool CheckStatus()
        {
            if (IsSharedMemoryLive())
            {
                _status = HWiNFOStatus.Running;
                return true;
            }

            // If process died or was never found, retry discovery periodically
            if (_status == HWiNFOStatus.Running)
            {
                _status = HWiNFOStatus.Launching;
                _launchAttempted = false;     // Allow re-launch
                EnsureRunning();
            }

            return false;
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
            // Registry (installed version)
            foreach (var regPath in HW_REG_KEYS)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(regPath, true);
                    key.SetValue("SHMEMEnabled", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("SensorsOnly", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("MinimizeOnStartup", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("MinimizeSensors", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("ShowWelcomeAndProgress", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("UpdateCheck", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("BetaCheck", 0, Microsoft.Win32.RegistryValueKind.DWord);
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

                    // Merge or create the INI file with all required settings
                    string existing = File.Exists(iniPath) ? File.ReadAllText(iniPath) : "";
                    if (!existing.Contains("SHMEMEnabled") || !existing.Contains("SensorsOnly"))
                    {
                        existing = "[Settings]\r\nSHMEMEnabled=1\r\nSensorsOnly=1\r\nMinimizeOnStartup=1\r\nMinimizeSensors=1\r\nShowWelcomeAndProgress=0\r\nUpdateCheck=0\r\nBetaCheck=0\r\n";
                        File.WriteAllText(iniPath, existing);
                        Log("[HWiNFO] Wrote startup options to HWiNFO64.INI");
                    }
                }
                catch { }
            }

            Log("[HWiNFO] Registry SHMEMEnabled=1 written");
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
