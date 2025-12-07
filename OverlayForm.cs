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
        private System.Threading.Timer? pollTimer;
        private System.Threading.Timer? topmostTimer;
        private volatile bool settingsChanged = false;
        private float? lastCpu;
        private float? lastGpu;
        private const int ActiveIntervalMs = 500;   // 500ms UI update (reads from SensorService cache which polls at 500ms)
        private const int IdleIntervalMs = 5000;   // slower when idle to save CPU
        private Queue<float> cpuBuffer = new Queue<float>(3); // 3-sample buffer for smooth but responsive readings
        private Queue<float> gpuBuffer = new Queue<float>(3); // 3-sample buffer for smooth but responsive readings
        private const float SpikeThreshold = 8.0f; // reject outlier spikes >8°C (wider tolerance for thermal variations)
        
        // For dragging the overlay
        private bool isDragging = false;
        private Point dragStartPoint;
        private double originalOpacity = 1.0; // Track original opacity
        public bool isPositionLocked = true;  // Position locked by default (prevent accidental drag)

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

            AppSettings.SettingsChanged += OnSettingsChanged;
            // Use Threading Timer to avoid suspension during fullscreen
            pollTimer = new System.Threading.Timer(PollTimerCallback, null, 0, ActiveIntervalMs);
            
            // Timer to re-assert topmost status every 500ms (helps with fullscreen apps)
            topmostTimer = new System.Threading.Timer(ReassertTopmost, null, 500, 500);
        }

        
        private void OnSettingsChanged()
        {
            // Update SensorService with new settings
            SensorService.UpdateConfig(AppSettings.ShowCpu, AppSettings.ShowGpu);
            
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
                        
                        // CPU: Apply moving average smoothing
                        if (cpuMax.HasValue)
                        {
                            cpuBuffer.Enqueue(cpuMax.Value);
                            if (cpuBuffer.Count > 3) cpuBuffer.Dequeue();
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
                            if (gpuBuffer.Count > 3) gpuBuffer.Dequeue();
                            
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
                            cpuLabel.Text = displayCpu.HasValue ? $"CPU: {displayCpu.Value,5:F1}°C" : "CPU: N/A";
                            cpuLabel.Visible = true;
                        }
                        else
                        {
                            cpuLabel.Text = "";
                            cpuLabel.Visible = false;
                        }

                        if (AppSettings.ShowGpu)
                        {
                            gpuLabel.Text = displayGpu.HasValue ? $"GPU: {displayGpu.Value,5:F1}°C" : "GPU: N/A";
                            gpuLabel.Visible = true;
                        }
                        else
                        {
                            gpuLabel.Text = "";
                            gpuLabel.Visible = false;
                        }
                        
                        // Calculate widths
                        int cpuLabelWidth = AppSettings.ShowCpu ? cpuLabel.Width : 0;
                        int gpuLabelWidth = AppSettings.ShowGpu ? gpuLabel.Width : 0;
                        const int spacing = 10; // space between CPU and GPU
                        
                        int totalWidth = cpuLabelWidth;
                        if (AppSettings.ShowCpu && AppSettings.ShowGpu)
                            totalWidth += spacing;
                        totalWidth += gpuLabelWidth;
                        
                        // Form width - make it wide enough with symmetric padding
                        this.Width = Math.Max(totalWidth + 30, 200); // 15px padding each side for symmetry
                        
                        // Position CPU at left with padding, GPU at right with padding (symmetric)
                        int leftPadding = 15;
                        int rightPadding = 15;
                        
                        if (AppSettings.ShowCpu)
                        {
                            cpuLabel.Location = new Point(leftPadding, 0);
                        }
                        
                        if (AppSettings.ShowGpu)
                        {
                            // GPU positioned from right: form.Width - rightPadding - gpuLabel.Width
                            int gpuX = this.Width - rightPadding - gpuLabelWidth;
                            gpuLabel.Location = new Point(Math.Max(gpuX, leftPadding + cpuLabelWidth + spacing), 0);
                        }
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

        
        // Overlay does not create a tray icon. ControlForm is the single owner of the tray icon.

        // Dragging functionality
        private void OverlayForm_MouseDown(object? sender, MouseEventArgs e)
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
            // Slight brightness increase when hovering to show it's interactive (only if unlocked)
            if (!isDragging && !isPositionLocked)
            {
                this.Opacity = 0.85;
            }
        }
        
        private void OverlayForm_MouseLeave(object? sender, EventArgs e)
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
                pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                pollTimer?.Dispose();
                topmostTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                topmostTimer?.Dispose();
            }
            catch { }

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