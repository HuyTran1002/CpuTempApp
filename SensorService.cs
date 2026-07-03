using System;
using System.Threading;

namespace CpuTempApp
{
    /// <summary>
    /// Background sensor polling service.
    /// Reads CPU/GPU temperatures exclusively from HWiNFO64 shared memory.
    ///
    /// On first start the service:
    ///   1. Finds HWiNFO64 installation automatically
    ///   2. Enables Shared Memory Support via registry / INI
    ///   3. Launches HWiNFO64 minimised if it is not already running
    ///   4. Polls shared memory every 500 ms once HWiNFO64 is live
    ///
    /// If HWiNFO64 is not installed the overlay shows "N/A" and the log
    /// contains a clear diagnostic message.
    /// </summary>
    public static class SensorService
    {
        private static Thread           sensorThread;
        private static volatile bool    isRunning  = false;
        private static bool             wantCpu    = true;
        private static bool             wantGpu    = true;

        // Thread-safe cached sensor values
        private static float?           cachedCpuTemp = null;
        private static float?           cachedGpuTemp = null;
        private static readonly object  cacheLock = new object();

        // How long to wait (ms) between launch and first shared-memory check
        private const int LaunchWaitMs     = 8_000;  // HWiNFO64 needs ~8 s to start
        private const int PollIntervalMs   = 500;
        private const int RetryIntervalMs  = 5_000;  // when waiting for HWiNFO64 to appear

        // ── Public accessors ────────────────────────────────────────────────

        public static float? GetCachedCpuTemp() { lock (cacheLock) { return cachedCpuTemp; } }
        public static float? GetCachedGpuTemp() { lock (cacheLock) { return cachedGpuTemp; } }

        /// <summary>Human-readable status for diagnostics / ControlForm tooltip.</summary>
        public static string StatusMessage => HWiNFOReader.Status switch
        {
            HWiNFOReader.HWiNFOStatus.Running     => "HWiNFO64 (shared memory)",
            HWiNFOReader.HWiNFOStatus.Launching   => "Waiting for HWiNFO64…",
            HWiNFOReader.HWiNFOStatus.NotInstalled => "HWiNFO64 not installed",
            _                                      => "N/A",
        };

        // ── Lifecycle ────────────────────────────────────────────────────────

        /// <summary>Start the background sensor thread.</summary>
        public static void Start(bool showCpu, bool showGpu)
        {
            if (isRunning) return;
            wantCpu   = showCpu;
            wantGpu   = showGpu;
            isRunning = true;

            // Write startup log header
            WriteLog($"=== CpuTempApp SensorService started at {DateTime.Now} ===\n" +
                     $"ShowCpu={showCpu}, ShowGpu={showGpu}");

            // Kick off HWiNFO64 discovery + launch on a pool thread so we don't block UI
            ThreadPool.QueueUserWorkItem(_ => HWiNFOReader.EnsureRunning());

            sensorThread = new Thread(SensorLoop)
            {
                IsBackground = false,
                Priority     = ThreadPriority.AboveNormal,
                Name         = "CpuTempAppSensorThread",
            };
            sensorThread.Start();
        }

        /// <summary>Stop the background sensor thread.</summary>
        public static void Stop()
        {
            isRunning = false;
            try { sensorThread?.Join(3_000); } catch { }
        }

        /// <summary>Call when sensor settings (show cpu/gpu) change.</summary>
        public static void UpdateConfig(bool showCpu, bool showGpu)
        {
            wantCpu = showCpu;
            wantGpu = showGpu;
        }

        // ── Background loop ──────────────────────────────────────────────────

        private static void SensorLoop()
        {
            // Give HWiNFO64 time to start before first poll
            bool initialWaitDone = false;

            while (isRunning)
            {
                try
                {
                    // Check whether HWiNFO shared memory is available
                    bool live = HWiNFOReader.CheckStatus();

                    if (!live)
                    {
                        // Clear cached values so overlay shows "N/A" while waiting
                        lock (cacheLock)
                        {
                            cachedCpuTemp = null;
                            cachedGpuTemp = null;
                        }

                        if (!initialWaitDone)
                        {
                            // First time: wait longer so HWiNFO64 can boot up
                            Thread.Sleep(LaunchWaitMs);
                            initialWaitDone = true;
                        }
                        else
                        {
                            // Subsequent waits: shorter retry interval
                            Thread.Sleep(RetryIntervalMs);
                        }

                        continue;
                    }

                    initialWaitDone = true;

                    // Read temperatures from shared memory
                    var reading = HWiNFOReader.ReadTemperatures(wantCpu, wantGpu);

                    lock (cacheLock)
                    {
                        cachedCpuTemp = reading.CpuTemp;
                        cachedGpuTemp = reading.GpuTemp;
                    }
                }
                catch { }

                Thread.Sleep(PollIntervalMs);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void WriteLog(string msg)
        {
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensor_debug.log"),
                    msg + "\n");
            }
            catch { }
        }
    }
}
