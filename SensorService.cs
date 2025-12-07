using System;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace CpuTempApp
{
    /// <summary>
    /// Independent background sensor service
    /// Runs on a separate thread that won't be suspended by fullscreen apps
    /// </summary>
    public static class SensorService
    {
        private static Thread? sensorThread;
        private static volatile bool isRunning = false;
        private static Computer? computer;
        
        // Thread-safe cached values
        private static float? cachedCpuTemp = null;
        private static float? cachedGpuTemp = null;
        private static object cacheLock = new object();
        
        public static float? GetCachedCpuTemp()
        {
            lock (cacheLock)
            {
                return cachedCpuTemp;
            }
        }
        
        public static float? GetCachedGpuTemp()
        {
            lock (cacheLock)
            {
                return cachedGpuTemp;
            }
        }
        
        /// <summary>
        /// Start the independent sensor polling thread
        /// </summary>
        public static void Start(bool showCpu, bool showGpu)
        {
            if (isRunning) return;
            
            isRunning = true;
            computer = new Computer { IsCpuEnabled = showCpu, IsGpuEnabled = showGpu };
            try { computer.Open(); } catch { }
            
            sensorThread = new Thread(SensorThreadProc)
            {
                IsBackground = false,  // Keep process alive
                Priority = ThreadPriority.AboveNormal,  // Higher priority to avoid suspension
                Name = "CpuTempAppSensorThread"
            };
            sensorThread.Start();
        }
        
        /// <summary>
        /// Stop the sensor polling thread
        /// </summary>
        public static void Stop()
        {
            isRunning = false;
            if (sensorThread != null)
            {
                try { sensorThread.Join(3000); } catch { }
            }
            try { computer?.Close(); } catch { }
        }
        
        /// <summary>
        /// Update sensor configuration (e.g., when settings change)
        /// </summary>
        public static void UpdateConfig(bool showCpu, bool showGpu)
        {
            try { computer?.Close(); } catch { }
            computer = new Computer { IsCpuEnabled = showCpu, IsGpuEnabled = showGpu };
            try { computer.Open(); } catch { }
        }
        
        // Background thread: Poll sensors continuously
        private static void SensorThreadProc()
        {
            while (isRunning)
            {
                try
                {
                    if (computer != null)
                    {
                        float? cpuTemp = null;
                        float? gpuTemp = null;
                        
                        // Update all hardware sensors
                        try
                        {
                            foreach (var hw in computer.Hardware)
                            {
                                try { hw.Update(); } catch { }
                                bool cpuPreferred = false, gpuPreferred = false;
                                TraverseSensors(hw, ref cpuTemp, ref gpuTemp, ref cpuPreferred, ref gpuPreferred);
                            }
                        }
                        catch { }
                        
                        // Cache the results
                        lock (cacheLock)
                        {
                            cachedCpuTemp = cpuTemp;
                            cachedGpuTemp = gpuTemp;
                        }
                    }
                    
                    // Poll every 200ms for very responsive updates (5x per second)
                    Thread.Sleep(200);
                }
                catch { }
            }
        }
        
        // Copy of TraverseHardware logic from OverlayForm
        private static void TraverseSensors(IHardware hardware, ref float? cpuMax, ref float? gpuMax, ref bool cpuPreferred, ref bool gpuPreferred)
        {
            try
            {
                List<float>? cpuCoreTemps = null;
                float? cpuPackage = null;
                float? cpuTdie = null;
                float? cpuCCD = null;
                
                foreach (var sensor in hardware.Sensors)
                {
                    if (!sensor.Value.HasValue) continue;
                    if (sensor.SensorType != SensorType.Temperature) continue;
                    var v = sensor.Value.GetValueOrDefault();
                    var sname = (sensor.Name ?? string.Empty).ToLowerInvariant();

                    // CPU: prioritize accuracy and real die temperature
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        // AMD: Tdie = real die temperature (no offset), highest priority
                        if (sname.Contains("tdie") || sname.Equals("cpu die (average)"))
                        {
                            cpuTdie = v;
                        }
                        // AMD: CCD temperature (chiplet die, very accurate for Ryzen)
                        else if (sname.Contains("ccd") && sname.Contains("temp"))
                        {
                            if (!cpuCCD.HasValue || v > cpuCCD.Value)
                                cpuCCD = v;
                        }
                        // Intel/AMD: Package temperature
                        else if (sname.Contains("package") || sname.Contains("tctl"))
                        {
                            cpuPackage = v;
                        }
                        // Fallback: exact "CPU" sensor or "CPU (Tctl/Tdie)"
                        else if (sname == "cpu" || sname == "cpu (tctl/tdie)" || sname.Contains("cpu package"))
                        {
                            if (!cpuPackage.HasValue)
                                cpuPackage = v;
                        }
                        // Collect individual core temps
                        else if ((sname.Contains("core") || sname.Contains("cpu core")) && !sname.Contains("average"))
                        {
                            cpuCoreTemps ??= new List<float>();
                            cpuCoreTemps.Add(v);
                        }
                    }
                    // GPU: Core temp
                    else if (hardware.HardwareType == HardwareType.GpuAmd ||
                             hardware.HardwareType == HardwareType.GpuNvidia ||
                             hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        if (sname.Contains("core") || sname.Contains("edge") || sname.Contains("gpu temperature"))
                        {
                            if (!gpuPreferred || !gpuMax.HasValue || v > gpuMax.Value)
                            {
                                gpuMax = v;
                                gpuPreferred = true;
                            }
                        }
                        else if (!gpuPreferred)
                        {
                            if (!gpuMax.HasValue || v > gpuMax.Value) gpuMax = v;
                        }
                    }
                }

                foreach (var sub in hardware.SubHardware)
                {
                    TraverseSensors(sub, ref cpuMax, ref gpuMax, ref cpuPreferred, ref gpuPreferred);
                }

                // CPU: Apply priority logic
                if (hardware.HardwareType == HardwareType.Cpu && !cpuPreferred)
                {
                    if (cpuTdie.HasValue)
                    {
                        cpuMax = cpuTdie;
                        cpuPreferred = true;
                    }
                    else if (cpuPackage.HasValue)
                    {
                        cpuMax = cpuPackage;
                        cpuPreferred = true;
                    }
                    else if (cpuCCD.HasValue)
                    {
                        cpuMax = cpuCCD;
                        cpuPreferred = true;
                    }
                    else if (cpuCoreTemps != null && cpuCoreTemps.Count > 0)
                    {
                        cpuMax = cpuCoreTemps.Max();
                        cpuPreferred = true;
                    }
                }
            }
            catch { }
        }
    }
}
