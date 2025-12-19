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
        private System.Windows.Forms.Timer pollTimer;
        private System.Threading.Timer topmostTimer;
        private volatile bool settingsChanged = false;
        private float? lastCpu;
        private float? lastGpu;
        private bool gpuJustEnabled = false;  // Track if GPU was just enabled
        private bool cpuJustEnabled = false;  // Track if CPU was just enabled
        private const int ActiveIntervalMs = 500;   // 500ms UI update (reads from SensorService cache which polls at 500ms)
        private const int IdleIntervalMs = 5000;   // slower when idle to save CPU
        private Queue<float> cpuBuffer = new Queue<float>(3); // 3-sample buffer for smooth but responsive readings
        private Queue<float> gpuBuffer = new Queue<float>(3); // 3-sample buffer for smooth but responsive readings
        private const float SpikeThreshold = 8.0f; // reject outlier spikes >8°C (wider tolerance for thermal variations)
        private bool isPositionDragged = false;  // Track if position has been manually dragged
        
        // For dragging the overlay
        private bool isDragging = false;
        private Point dragStartPoint;
        private double originalOpacity = 1.0; // Track original opacity
        public bool isPositionLocked = true;  // Position locked by default (prevent accidental drag)
        
        // Color feedback system (smooth, lightweight)
        private System.Threading.Timer colorResetTimer;
            // Used to track how long CPU temp is missing
            private DateTime? lastCpuNullTime = null;
        private DateTime lastColorChangeTime = DateTime.MinValue;
        private const int ColorFeedbackDurationMs = 150;

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
            Height = 40;  // Increased from 30 to 40 for more breathing room
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
            
            // Reload position from Registry before positioning
            AppSettings.ReloadFromRegistry();
            System.Diagnostics.Debug.WriteLine($"[OverlayForm] After reload: OverlayX={AppSettings.OverlayX}, OverlayY={AppSettings.OverlayY}");
            
            // Temporary position - will be set properly after width is calculated
            Location = new Point(0, 0);

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
            const int spacing = 4;  // 4px spacing between CPU and GPU
            const int padding = 10; // padding on each side
            int totalWidth = padding;
            if (AppSettings.ShowCpu)
                totalWidth += cpuLabel.PreferredWidth;
            if (AppSettings.ShowGpu)
            {
                if (AppSettings.ShowCpu)
                    totalWidth += spacing + gpuLabel.PreferredWidth;
                else
                    totalWidth += gpuLabel.PreferredWidth;
            }
            totalWidth += padding;
            
            // Ensure minimum width
            if (totalWidth < 150)
                totalWidth = 150;
                
            this.Width = totalWidth;
            
            // Position labels with padding
            cpuLabel.Location = new Point(padding, 0);
            if (AppSettings.ShowCpu && AppSettings.ShowGpu)
            {
                gpuLabel.Location = new Point(padding + cpuLabel.Width + spacing, 0);
            }
            else if (AppSettings.ShowGpu)
            {
                gpuLabel.Location = new Point(padding, 0);
            }
            
            // Position overlay at correct location after width is calculated
            var screen = Screen.PrimaryScreen.Bounds;
            if (AppSettings.OverlayX == -1)
            {
                // Default center position: calculate center based on actual width
                System.Diagnostics.Debug.WriteLine($"[OverlayForm] Positioning to CENTER with width {totalWidth}");
                Location = new Point((screen.Width - this.Width) / 2, AppSettings.OverlayY);
            }
            else
            {
                // Restore saved position
                System.Diagnostics.Debug.WriteLine($"[OverlayForm] Positioning to SAVED: X={AppSettings.OverlayX}, Y={AppSettings.OverlayY}");
                Location = new Point(AppSettings.OverlayX, AppSettings.OverlayY);
            }

            AppSettings.SettingsChanged += OnSettingsChanged;
            // Sử dụng System.Threading.Timer để cập nhật nhiệt độ, tránh phụ thuộc UI thread
            pollTimer = null;
            var pollThreadingTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    // Đảm bảo cập nhật UI đúng thread
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke((Action)(() => PollTimerCallback(null)));
                    }
                }
                catch { }
            }, null, 0, ActiveIntervalMs);

            // Timer to re-assert topmost status every 500ms (helps with fullscreen apps)
            topmostTimer = new System.Threading.Timer(ReassertTopmost, null, 500, 500);

            // Color reset timer - runs every 50ms to reset colors when needed
            colorResetTimer = new System.Threading.Timer(ColorResetCallback, null, 50, 50);
        }

        
        private void OnSettingsChanged()
        {
            // Update SensorService with new settings
            SensorService.UpdateConfig(AppSettings.ShowCpu, AppSettings.ShowGpu);
            
            // Track if GPU/CPU just got enabled
            if (AppSettings.ShowGpu && string.IsNullOrEmpty(gpuLabel.Text))
                gpuJustEnabled = true;
            if (AppSettings.ShowCpu && string.IsNullOrEmpty(cpuLabel.Text))
                cpuJustEnabled = true;
            
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
                const int spacing = 4;  // 4px spacing between CPU and GPU
                const int padding = 10;
                cpuLabel.Location = new Point(padding, 0);
                if (AppSettings.ShowCpu && AppSettings.ShowGpu)
                {
                    gpuLabel.Location = new Point(padding + cpuLabel.Width + spacing, 0);
                }
                else if (AppSettings.ShowGpu)
                {
                    gpuLabel.Location = new Point(padding, 0);
                }
                
                // Update form width
                int formWidth = padding;
                if (AppSettings.ShowCpu)
                    formWidth += cpuLabel.Width;
                if (AppSettings.ShowGpu)
                {
                    if (AppSettings.ShowCpu)
                        formWidth += spacing + gpuLabel.Width;
                    else
                        formWidth += gpuLabel.Width;
                }
                formWidth += padding;
                this.Width = Math.Max(formWidth, 150);
            }
            catch { }
        }

        private void ReassertTopmost(object state)
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

        
        // Đảm bảo luôn chạy trên UI thread
        private void PollTimerCallback(object state)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
                return;

            // Settings are now handled by SensorService
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

            // Get sensor values from independent SensorService (won't be suspended by fullscreen)
            float? cpuMax = SensorService.GetCachedCpuTemp();
            float? gpuMax = SensorService.GetCachedGpuTemp();

            // Apply smoothing to both CPU and GPU for stable readings
            float? displayCpu = null;
            float? displayGpu = null;

            // CPU: Apply moving average smoothing with tolerant fallback
            if (cpuMax.HasValue)
            {
                cpuBuffer.Enqueue(cpuMax.Value);
                if (cpuBuffer.Count > 3) cpuBuffer.Dequeue();
                var avgCpu = cpuBuffer.Average();
                if (lastCpu.HasValue)
                {
                    float diff = Math.Abs(avgCpu - lastCpu.Value);
                    if (diff > SpikeThreshold)
                    {
                        displayCpu = lastCpu;
                    }
                    else
                    {
                        displayCpu = avgCpu;
                    }
                }
                else
                {
                    displayCpu = avgCpu;
                }
                lastCpuNullTime = null; // reset null timer
            }
            else
            {
                // If CPU temp is missing, keep last value for up to 2 seconds before showing N/A
                if (lastCpu.HasValue)
                {
                    if (lastCpuNullTime == null)
                        lastCpuNullTime = DateTime.Now;
                    if ((DateTime.Now - lastCpuNullTime.Value).TotalSeconds < 2)
                    {
                        displayCpu = lastCpu;
                    }
                    else
                    {
                        displayCpu = null;
                        // Optionally log for diagnostics
                        System.Diagnostics.Debug.WriteLine($"[OverlayForm] CPU temp missing for over 2s at {DateTime.Now}");
                    }
                }
            }

            // GPU: Apply moving average smoothing
            if (gpuMax.HasValue)
            {
                gpuBuffer.Enqueue(gpuMax.Value);
                if (gpuBuffer.Count > 3) gpuBuffer.Dequeue();

                // Use the buffered average for spike detection to avoid reacting to single noisy samples
                var avgGpu = gpuBuffer.Average();
                if (lastGpu.HasValue)
                {
                    float diff = Math.Abs(avgGpu - lastGpu.Value);
                    if (diff > SpikeThreshold)
                    {
                        // Spike detected, keep last displayed value
                        displayGpu = lastGpu;
                    }
                    else
                    {
                        // Normal fluctuation, use buffered average
                        displayGpu = avgGpu;
                    }
                }
                else
                {
                    displayGpu = avgGpu;
                }
            }

            // update UI only when values changed
            var cpuChanged = !NullableEquals(displayCpu, lastCpu);
            var gpuChanged = !NullableEquals(displayGpu, lastGpu);

            if (cpuChanged || gpuChanged)
            {
                if (AppSettings.ShowCpu)
                {
                    if (displayCpu.HasValue)
                    {
                        cpuLabel.Text = $"CPU: {displayCpu.Value,5:F1}°C";
                        cpuJustEnabled = false;
                    }
                    else if (cpuJustEnabled)
                    {
                        cpuLabel.Text = "CPU: --°C";
                    }
                    else
                    {
                        cpuLabel.Text = "CPU: N/A";
                    }
                    cpuLabel.Visible = true;
                }
                else
                {
                    cpuLabel.Visible = false;
                }

                if (AppSettings.ShowGpu)
                {
                    if (displayGpu.HasValue)
                    {
                        gpuLabel.Text = $"GPU: {displayGpu.Value,5:F1}°C";
                        gpuJustEnabled = false;
                    }
                    else if (gpuJustEnabled)
                    {
                        gpuLabel.Text = "GPU: --°C";
                    }
                    else
                    {
                        gpuLabel.Text = "GPU: N/A";
                    }
                    gpuLabel.Visible = true;
                }
                else
                {
                    gpuLabel.Visible = false;
                }

                // Color feedback: highlight when temperature changes
                if (cpuChanged)
                {
                    cpuLabel.ForeColor = Color.FromArgb(0, 255, 255); // Cyan highlight
                    lastColorChangeTime = DateTime.Now;
                }
                if (gpuChanged)
                {
                    gpuLabel.ForeColor = Color.FromArgb(0, 255, 255); // Cyan highlight
                    lastColorChangeTime = DateTime.Now;
                }

                // Update GPU position after CPU width changes
                const int spacing = 4;  // 4px spacing between CPU and GPU
                const int padding = 10;
                cpuLabel.Location = new Point(padding, 0);
                if (AppSettings.ShowCpu && AppSettings.ShowGpu)
                {
                    gpuLabel.Location = new Point(padding + cpuLabel.Width + spacing, 0);
                }
                else if (AppSettings.ShowGpu)
                {
                    gpuLabel.Location = new Point(padding, 0);
                }

                // Update form width to fit content
                int formWidth = padding;
                if (AppSettings.ShowCpu)
                    formWidth += cpuLabel.Width;
                if (AppSettings.ShowGpu)
                {
                    if (AppSettings.ShowCpu)
                        formWidth += spacing + gpuLabel.Width;
                    else
                        formWidth += gpuLabel.Width;
                }
                formWidth += padding;
                this.Width = Math.Max(formWidth, 150);

                // Re-center if using default position (after width change)
                if (AppSettings.OverlayX == -1)
                {
                    var screen = Screen.PrimaryScreen.Bounds;
                    this.Location = new Point((screen.Width - this.Width) / 2, AppSettings.OverlayY);
                }

                // Update last values
                lastCpu = displayCpu;
                lastGpu = displayGpu;
            }
        }

        private static bool NullableEquals(float? a, float? b)
        {
            if (!a.HasValue && !b.HasValue) return true;
            if (a.HasValue != b.HasValue) return false;
            return Math.Abs(a.Value - b.Value) < 0.1f; // faster response like AIDA64
        }

        
        // Overlay does not create a tray icon. ControlForm is the single owner of the tray icon.

        // Dragging functionality
        private void OverlayForm_MouseDown(object sender, MouseEventArgs e)
        {
            // Only allow dragging when position is unlocked
            if (e.Button == MouseButtons.Left && !isPositionLocked)
            {
                isDragging = true;
                dragStartPoint = e.Location;
                // Reduce opacity during drag for visual feedback
                this.Opacity = 0.7;
            }
        }

        private void OverlayForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point newLocation = this.Location;
                newLocation.X += e.X - dragStartPoint.X;
                newLocation.Y += e.Y - dragStartPoint.Y;
                this.Location = newLocation;
            }
        }

        private void OverlayForm_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
                isPositionDragged = true;  // Mark that position has been manually dragged
                // Update in-memory position immediately to prevent re-centering (don't save to Registry yet)
                if (!isPositionLocked)
                {
                    AppSettings.SetOverlayPositionTemp(this.Location.X, this.Location.Y);
                    System.Diagnostics.Debug.WriteLine($"[OverlayForm] Drag complete: X={this.Location.X}, Y={this.Location.Y} (temp, not saved)");
                }
                // Restore original opacity when drag ends
                this.Opacity = originalOpacity;
            }
        }
        
        private void OverlayForm_MouseEnter(object sender, EventArgs e)
        {
            // Slight brightness increase when hovering to show it's interactive (only if unlocked)
            if (!isDragging && !isPositionLocked)
            {
                this.Opacity = 0.85;
            }
        }
        
        private void OverlayForm_MouseLeave(object sender, EventArgs e)
        {
            // Restore original opacity when mouse leaves
            if (!isDragging && !isPositionLocked)
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
                topmostTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                topmostTimer?.Dispose();
                colorResetTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                colorResetTimer?.Dispose();
            }
            catch { }

            try { AppSettings.SettingsChanged -= OnSettingsChanged; } catch { }
            base.OnFormClosing(e);
        }
        
        // Reset text color after highlight duration
        private void ColorResetCallback(object state)
        {
            if (DateTime.Now - lastColorChangeTime > TimeSpan.FromMilliseconds(ColorFeedbackDurationMs))
            {
                try
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        if (cpuLabel.ForeColor.ToArgb() == Color.FromArgb(0, 255, 255).ToArgb())
                        {
                            cpuLabel.ForeColor = AppSettings.TextColor;
                        }
                        if (gpuLabel.ForeColor.ToArgb() == Color.FromArgb(0, 255, 255).ToArgb())
                        {
                            gpuLabel.ForeColor = AppSettings.TextColor;
                        }
                    }));
                }
                catch { }
            }
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