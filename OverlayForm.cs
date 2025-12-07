using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace CpuTempApp
{
    public class OverlayForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        
        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize, 
            IntPtr hdcSrc, ref Point pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }
        
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOACTIVATE = 0x0010;

        private Label cpuLabel;
        private Label gpuLabel;
        private Computer computer;
        private System.Threading.Timer? pollTimer;
        private System.Threading.Timer? topmostTimer;
        private volatile bool settingsChanged = false;
        private float? lastCpu;
        private float? lastGpu;
        private const int ActiveIntervalMs = 1000;  // 1 second polling for stable readings
        private const int IdleIntervalMs = 5000;   // slower when idle to save CPU
        private Queue<float> cpuBuffer = new Queue<float>(5); // buffer last 5 readings for smoothing
        private Queue<float> gpuBuffer = new Queue<float>(5); // buffer last 5 readings for smoothing
        private const float SpikeThreshold = 5.0f; // reject outlier spikes >5°C
        
        // For dragging the overlay
        private bool isDragging = false;
        private Point dragStartPoint;
        private double originalOpacity = 1.0; // Track original opacity

        // Overlay no longer creates a tray icon; ControlForm owns the tray icon.

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;  // Use black as transparency key instead
            TransparencyKey = Color.Black;
            Width = 160;
            Height = 30;
            Padding = new Padding(0);
            
            // Make overlay appear above fullscreen games using unmanaged window style
            CreateHandle();  // Create the window handle immediately
            
            // Set extended window style for layered window
            try
            {
                const int WS_EX_LAYERED = 0x00080000;      // Layered window
                const int WS_EX_TOPMOST = 0x00000008;      // Always on top
                const int WS_EX_TRANSPARENT = 0x00000020;  // Click-through (disabled for dragging)
                const int WS_EX_NOACTIVATE = 0x08000000;   // Don't activate when clicked
                const int WS_EX_TOOLWINDOW = 0x00000080;   // Tool window (not in alt-tab)
                const int GWL_EXSTYLE = -20;
                
                // Get current style and set LAYERED + TOPMOST + NOACTIVATE + TOOLWINDOW
                int currentStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                SetWindowLong(this.Handle, GWL_EXSTYLE, currentStyle | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                
                // Force to HWND_TOPMOST with no activation
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
            
            // Position at top center of screen, flush with top edge
            var screen = Screen.PrimaryScreen.Bounds;
            Location = new Point((screen.Width - Width) / 2, 0);

            // Labels with black background to match transparency key - horizontal layout
            cpuLabel = new Label 
            { 
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft, 
                Font = new Font("Segoe UI", 9, FontStyle.Bold), 
                BackColor = Color.Black,  // Matches TransparencyKey = transparent
                ForeColor = AppSettings.TextColor, 
                Padding = new Padding(0),
                Margin = new Padding(0),
                Location = new Point(0, 0),
                Cursor = Cursors.Hand,  // Hand cursor for dragging feedback
                Visible = AppSettings.ShowCpu  // Only visible if ShowCpu is enabled
            };
            
            gpuLabel = new Label 
            { 
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft, 
                Font = new Font("Segoe UI", 9, FontStyle.Bold), 
                BackColor = Color.Black,  // Matches TransparencyKey = transparent
                ForeColor = AppSettings.TextColor, 
                Padding = new Padding(0),
                Margin = new Padding(0),
                Location = new Point(0, 0),  // Will be positioned after CPU label
                Cursor = Cursors.Hand,  // Hand cursor for dragging feedback
                Visible = AppSettings.ShowGpu  // Only visible if ShowGpu is enabled
            };

            Controls.Add(cpuLabel);
            Controls.Add(gpuLabel);

            // Enable dragging by mouse
            this.MouseDown += OverlayForm_MouseDown;
            this.MouseMove += OverlayForm_MouseMove;
            this.MouseUp += OverlayForm_MouseUp;
            this.MouseEnter += OverlayForm_MouseEnter;
            this.MouseLeave += OverlayForm_MouseLeave;
            
            cpuLabel.MouseDown += OverlayForm_MouseDown;
            cpuLabel.MouseMove += OverlayForm_MouseMove;
            cpuLabel.MouseUp += OverlayForm_MouseUp;
            cpuLabel.MouseEnter += OverlayForm_MouseEnter;
            cpuLabel.MouseLeave += OverlayForm_MouseLeave;
            
            gpuLabel.MouseDown += OverlayForm_MouseDown;
            gpuLabel.MouseMove += OverlayForm_MouseMove;
            gpuLabel.MouseUp += OverlayForm_MouseUp;
            gpuLabel.MouseEnter += OverlayForm_MouseEnter;
            gpuLabel.MouseLeave += OverlayForm_MouseLeave;

            // Set text and position labels
            cpuLabel.Text = "CPU";
            gpuLabel.Text = "GPU";
            // GPU label position will be dynamically adjusted based on CPU label width
            
            // Adjust form width to fit content exactly
            int totalWidth = 0;
            if (AppSettings.ShowCpu)
                totalWidth += cpuLabel.PreferredWidth;
            if (AppSettings.ShowGpu)
            {
                if (AppSettings.ShowCpu)
                    totalWidth = Math.Max(totalWidth + 70, 70 + gpuLabel.PreferredWidth);
                else
                    totalWidth = gpuLabel.PreferredWidth;
            }
            
            // Ensure minimum width
            if (totalWidth < 80)
                totalWidth = 150;
                
            this.Width = totalWidth;
            
            // Re-center after width adjustment
            var screenBounds = Screen.PrimaryScreen.Bounds;
            Location = new Point((screenBounds.Width - this.Width) / 2, 0);

            // init hardware and start timer-based polling
            computer = new Computer { IsCpuEnabled = AppSettings.ShowCpu, IsGpuEnabled = AppSettings.ShowGpu };
            try { computer.Open(); } catch { }

            AppSettings.SettingsChanged += OnSettingsChanged;

            // Use Threading Timer to avoid suspension during fullscreen
            pollTimer = new System.Threading.Timer(PollTimerCallback, null, 0, ActiveIntervalMs);
            
            // Timer to re-assert topmost status every 500ms (helps with fullscreen apps)
            topmostTimer = new System.Threading.Timer(ReassertTopmost, null, 500, 500);
        }

        // Write CPU/GPU temperature sensors only to a temp file
        public string DumpSensorInfo()
        {
            var sb = new StringBuilder();
            try
            {
                if (computer == null)
                {
                    sb.AppendLine("No LibreHardwareMonitor Computer instance available.");
                }
                else
                {
                    foreach (var hw in computer.Hardware)
                    {
                        DumpTemperatureSensors(hw, sb, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Exception while dumping sensors: " + ex);
            }

            var path = Path.Combine(Path.GetTempPath(), "CpuTempApp-sensors.txt");
            try { File.WriteAllText(path, sb.ToString(), Encoding.UTF8); } catch { }
            return path;
        }

        private void DumpTemperatureSensors(IHardware hw, StringBuilder sb, int indent)
        {
            var pad = new string(' ', indent * 2);
            
            // Only show CPU and GPU hardware
            if (hw.HardwareType == HardwareType.Cpu || hw.HardwareType == HardwareType.GpuAmd || 
                hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuIntel)
            {
                sb.AppendLine($"{pad}{hw.HardwareType}: {hw.Name}");
                try
                {
                    foreach (var s in hw.Sensors)
                    {
                        // Only show temperature sensors
                        if (s.SensorType == SensorType.Temperature)
                        {
                            sb.AppendLine($"{pad}  {s.Name}: {s.Value?.ToString("F1") ?? "N/A"}°C");
                        }
                    }
                }
                catch { }
            }

            try
            {
                foreach (var sh in hw.SubHardware)
                {
                    DumpTemperatureSensors(sh, sb, indent + 1);
                }
            }
            catch { }
        }

        private void OnSettingsChanged()
        {
            // signal poll loop to reconfigure computer on next iteration
            settingsChanged = true;
            
            // Update label colors and visibility when settings change
            try
            {
                cpuLabel.ForeColor = AppSettings.TextColor;
                gpuLabel.ForeColor = AppSettings.TextColor;
                
                // Immediately show labels with placeholder text for instant feedback
                if (AppSettings.ShowCpu)
                {
                    cpuLabel.Text = "CPU: --°C";
                    cpuLabel.Visible = true;
                }
                else
                {
                    cpuLabel.Visible = false;
                }
                
                if (AppSettings.ShowGpu)
                {
                    gpuLabel.Text = "GPU: --°C";
                    gpuLabel.Visible = true;
                }
                else
                {
                    gpuLabel.Visible = false;
                }
                
                // Position GPU label immediately
                if (AppSettings.ShowCpu && AppSettings.ShowGpu)
                {
                    gpuLabel.Location = new Point(cpuLabel.Width + 10, 0);
                }
                else if (AppSettings.ShowGpu)
                {
                    gpuLabel.Location = new Point(0, 0);
                }
                
                // Update form width
                int formWidth = 10;
                if (AppSettings.ShowCpu)
                    formWidth += cpuLabel.Width;
                if (AppSettings.ShowGpu)
                {
                    if (AppSettings.ShowCpu)
                        formWidth += 10 + gpuLabel.Width;
                    else
                        formWidth += gpuLabel.Width;
                }
                this.Width = Math.Max(formWidth, 150);
            }
            catch { }
        }

        private void ReassertTopmost(object? state)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
                return;

            try
            {
                // Re-enforce topmost position
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        private void PollTimerCallback(object? state)
        {
            // Run on UI thread using BeginInvoke
            if (this.IsDisposed || !this.IsHandleCreated)
                return;

            try
            {
                this.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        if (settingsChanged)
                {
                    settingsChanged = false;
                    try { computer?.Close(); } catch { }
                    computer = new Computer { IsCpuEnabled = AppSettings.ShowCpu, IsGpuEnabled = AppSettings.ShowGpu };
                    try { computer.Open(); } catch { }
                }

                if (!AppSettings.ShowCpu && !AppSettings.ShowGpu)
                {
                    // nothing to show — clear labels
                    if (lastCpu != null || lastGpu != null)
                    {
                        lastCpu = null; lastGpu = null;
                        cpuLabel.Text = "";
                        gpuLabel.Text = "";
                    }
                    return;
                }

                    float? cpuMax = null;
                    float? gpuMax = null;

                    try
                    {
                        if (computer != null)
                        {
                            foreach (var hw in computer.Hardware)
                            {
                                try { hw.Update(); } catch { }
                                bool cpuPreferred = false, gpuPreferred = false;
                                TraverseHardware(hw, ref cpuMax, ref gpuMax, ref cpuPreferred, ref gpuPreferred);
                            }
                        }
                    }
                    catch { }

                // Apply smoothing to both CPU and GPU for stable readings
                float? displayCpu = null;
                float? displayGpu = null;
                
                // CPU: Apply moving average smoothing
                if (cpuMax.HasValue)
                    {
                        cpuBuffer.Enqueue(cpuMax.Value);
                        if (cpuBuffer.Count > 5) cpuBuffer.Dequeue();
                        
                        // Reject obvious spikes
                        if (lastCpu.HasValue)
                        {
                            float diff = Math.Abs(cpuMax.Value - lastCpu.Value);
                            if (diff > SpikeThreshold)
                            {
                                // Spike detected, use last value
                                displayCpu = lastCpu;
                            }
                            else
                            {
                                // Normal fluctuation, use average
                                displayCpu = cpuBuffer.Average();
                            }
                        }
                        else
                        {
                            displayCpu = cpuBuffer.Average();
                        }
                    }
                    
                    // GPU: Apply moving average smoothing
                    if (gpuMax.HasValue)
                    {
                        gpuBuffer.Enqueue(gpuMax.Value);
                        if (gpuBuffer.Count > 5) gpuBuffer.Dequeue();
                        
                        // Reject obvious spikes
                        if (lastGpu.HasValue)
                        {
                            float diff = Math.Abs(gpuMax.Value - lastGpu.Value);
                            if (diff > SpikeThreshold)
                            {
                                // Spike detected, use last value
                                displayGpu = lastGpu;
                            }
                            else
                            {
                                // Normal fluctuation, use average
                                displayGpu = gpuBuffer.Average();
                            }
                        }
                        else
                        {
                            displayGpu = gpuBuffer.Average();
                        }
                    }

                // update UI only when values changed
                var cpuChanged = !NullableEquals(displayCpu, lastCpu);
                var gpuChanged = !NullableEquals(displayGpu, lastGpu);

                if (cpuChanged || gpuChanged)
                {
                    lastCpu = displayCpu; lastGpu = displayGpu;
                    try
                    {
                        // Calculate position BEFORE setting text
                        int cpuWidth = 0;
                        if (AppSettings.ShowCpu && displayCpu.HasValue)
                        {
                            string cpuText = $"CPU: {displayCpu.Value:F1}°C";
                            using (Graphics g = this.CreateGraphics())
                            {
                                SizeF size = g.MeasureString(cpuText, cpuLabel.Font);
                                cpuWidth = (int)Math.Ceiling(size.Width);
                            }
                        }
                        
                        if (AppSettings.ShowCpu)
                        {
                            cpuLabel.Text = displayCpu.HasValue ? $"CPU: {displayCpu.Value:F1}°C" : "CPU: N/A";
                            cpuLabel.Visible = true;
                        }
                        else
                        {
                            cpuLabel.Text = "";
                            cpuLabel.Visible = false;
                        }

                        if (AppSettings.ShowGpu)
                        {
                            gpuLabel.Text = displayGpu.HasValue ? $"GPU: {displayGpu.Value:F1}°C" : "GPU: N/A";
                            gpuLabel.Visible = true;
                        }
                        else
                        {
                            gpuLabel.Text = "";
                            gpuLabel.Visible = false;
                        }
                        
                        // Dynamically position GPU label based on CPU label width + spacing
                        if (AppSettings.ShowCpu && AppSettings.ShowGpu)
                        {
                            gpuLabel.Location = new Point(cpuWidth > 0 ? cpuWidth + 10 : cpuLabel.Width + 10, 0);
                        }
                        else if (AppSettings.ShowGpu)
                        {
                            gpuLabel.Location = new Point(0, 0);
                        }
                        
                        // Update form width to fit both labels
                        int formWidth = 10; // padding
                        if (AppSettings.ShowCpu)
                            formWidth += cpuLabel.Width;
                        if (AppSettings.ShowGpu)
                        {
                            if (AppSettings.ShowCpu)
                                formWidth += 10 + gpuLabel.Width; // spacing + GPU width
                            else
                                formWidth += gpuLabel.Width;
                        }
                        this.Width = Math.Max(formWidth, 150);
                    }
                    catch { }
                }
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private static bool NullableEquals(float? a, float? b)
        {
            if (!a.HasValue && !b.HasValue) return true;
            if (a.HasValue != b.HasValue) return false;
            return Math.Abs(a.Value - b.Value) < 0.5f; // faster response like AIDA64
        }

        private float? FindSensorValueByName(string name)
        {
            try
            {
                if (computer == null) return null;
                var target = name.ToLowerInvariant();
                foreach (var hw in computer.Hardware)
                {
                    try
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType != LibreHardwareMonitor.Hardware.SensorType.Temperature) continue;
                            var sname = (s.Name ?? string.Empty).ToLowerInvariant();
                            if (sname == target || sname.Contains(target) || target.Contains(sname))
                            {
                                return s.Value;
                            }
                        }
                        foreach (var sh in hw.SubHardware)
                        {
                            foreach (var s in sh.Sensors)
                            {
                                if (s.SensorType != LibreHardwareMonitor.Hardware.SensorType.Temperature) continue;
                                var sname = (s.Name ?? string.Empty).ToLowerInvariant();
                                if (sname == target || sname.Contains(target) || target.Contains(sname))
                                {
                                    return s.Value;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        // polling handled on background thread in PollLoopAsync

        private void TraverseHardware(IHardware hardware, ref float? cpuMax, ref float? gpuMax, ref bool cpuPreferred, ref bool gpuPreferred)
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
                        // Intel: "CPU Package" is the most accurate single sensor
                        // AMD: "Tctl" may have offset, but still good fallback
                        else if (sname.Contains("package") || sname.Contains("tctl"))
                        {
                            cpuPackage = v;
                        }
                        // Fallback: exact "CPU" sensor or "CPU (Tctl/Tdie)"
                        else if (sname == "cpu" || sname == "cpu (tctl/tdie)" || sname.Contains("cpu package"))
                        {
                            if (!cpuPackage.HasValue) // Don't override if already have package
                                cpuPackage = v;
                        }
                        // Collect individual core temps for max/average calculation
                        else if ((sname.Contains("core") || sname.Contains("cpu core")) && !sname.Contains("average"))
                        {
                            cpuCoreTemps ??= new List<float>();
                            cpuCoreTemps.Add(v);
                        }
                    }
                    // GPU: Core temp (phổ biến nhất trên Nvidia/AMD/Intel)
                    else if (hardware.HardwareType == HardwareType.GpuAmd ||
                             hardware.HardwareType == HardwareType.GpuNvidia ||
                             hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        // Priority 1: GPU Core/Edge temp (có trên hầu hết laptop gaming)
                        if (sname.Contains("core") || sname.Contains("edge") || sname.Contains("gpu temperature"))
                        {
                            if (!gpuPreferred || !gpuMax.HasValue || v > gpuMax.Value)
                            {
                                gpuMax = v;
                                gpuPreferred = true;
                            }
                        }
                        // Fallback: any GPU temp sensor
                        else if (!gpuPreferred)
                        {
                            if (!gpuMax.HasValue || v > gpuMax.Value) gpuMax = v;
                        }
                    }
                }

                foreach (var sub in hardware.SubHardware)
                {
                    TraverseHardware(sub, ref cpuMax, ref gpuMax, ref cpuPreferred, ref gpuPreferred);
                }

                // CPU: Apply priority logic AFTER collecting all sensors
                if (hardware.HardwareType == HardwareType.Cpu && !cpuPreferred)
                {
                    // Priority 1: Tdie (real die temp, no offset - AMD only)
                    if (cpuTdie.HasValue)
                    {
                        cpuMax = cpuTdie;
                        cpuPreferred = true;
                    }
                    // Priority 2: Package temperature (best for Intel, good for AMD)
                    // Intel CPUs: CPU Package is the official TDP sensor
                    // AMD CPUs: Tctl (may have offset, but Package is still accurate enough)
                    else if (cpuPackage.HasValue)
                    {
                        cpuMax = cpuPackage;
                        cpuPreferred = true;
                    }
                    // Priority 3: CCD max (chiplet die - AMD Ryzen specific)
                    else if (cpuCCD.HasValue)
                    {
                        cpuMax = cpuCCD;
                        cpuPreferred = true;
                    }
                    // Priority 4: Maximum core temperature (hottest core)
                    // Intel: Core temps are very accurate and reliable
                    // AMD: Core temps can be used as last resort
                    else if (cpuCoreTemps != null && cpuCoreTemps.Count > 0)
                    {
                        cpuMax = cpuCoreTemps.Max(); // Use MAX to detect thermal throttling
                        cpuPreferred = true;
                    }
                }
            }
            catch { }
        }

        // Overlay does not create a tray icon. ControlForm is the single owner of the tray icon.

        // Dragging functionality
        private void OverlayForm_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStartPoint = e.Location;
                // Reduce opacity during drag for visual feedback
                this.Opacity = 0.7;
            }
        }

        private void OverlayForm_MouseMove(object? sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point newLocation = this.Location;
                newLocation.X += e.X - dragStartPoint.X;
                newLocation.Y += e.Y - dragStartPoint.Y;
                this.Location = newLocation;
            }
        }

        private void OverlayForm_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
                // Restore original opacity when drag ends
                this.Opacity = originalOpacity;
            }
        }
        
        private void OverlayForm_MouseEnter(object? sender, EventArgs e)
        {
            // Slight brightness increase when hovering to show it's interactive
            if (!isDragging)
            {
                this.Opacity = 0.85;
            }
        }
        
        private void OverlayForm_MouseLeave(object? sender, EventArgs e)
        {
            // Restore original opacity when mouse leaves
            if (!isDragging)
            {
                this.Opacity = originalOpacity;
            }
        }

        private void OpenControlForm()
        {
            // Show settings control form as modal with this as owner
            using (var f = new ControlForm())
            {
                f.ShowDialog(this);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Hide to tray when user closes overlay
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }
            
            // Clean up: stop timer, close computer
            try
            {
                pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                pollTimer?.Dispose();
                topmostTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                topmostTimer?.Dispose();
            }
            catch { }

            try { computer?.Close(); } catch { }
            try { AppSettings.SettingsChanged -= OnSettingsChanged; } catch { }
            base.OnFormClosing(e);
        }
        
        protected override void WndProc(ref Message m)
        {
            // WM_ACTIVATE message - when fullscreen game activated, re-assert our topmost status
            const int WM_ACTIVATE = 0x0006;
            
            if (m.Msg == WM_ACTIVATE)
            {
                // Re-enforce topmost position when other windows activate
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            
            base.WndProc(ref m);
        }
    }
}