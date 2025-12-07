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
    public class OverlayFormModern : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        
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
        private Label cpuValueLabel;
        private Label gpuValueLabel;
        private Panel cpuBar;
        private Panel gpuBar;
        private System.Threading.Timer? pollTimer;
        private System.Threading.Timer? topmostTimer;
        private volatile bool settingsChanged = false;
        private float? lastCpu;
        private float? lastGpu;
        private const int ActiveIntervalMs = 500;
        private const int IdleIntervalMs = 5000;
        private Queue<float> cpuBuffer = new Queue<float>(3);
        private Queue<float> gpuBuffer = new Queue<float>(3);
        private const float SpikeThreshold = 8.0f;
        
        // Futuristic colors
        private readonly Color ColorBackground = Color.FromArgb(10, 10, 20);      // Dark navy
        private readonly Color ColorCpuGlow = Color.FromArgb(0, 255, 200);        // Cyan
        private readonly Color ColorGpuGlow = Color.FromArgb(255, 0, 200);        // Magenta
        private readonly Color ColorBorder = Color.FromArgb(50, 150, 255);        // Neon blue
        
        private bool isDragging = false;
        private Point dragStartPoint;
        private double originalOpacity = 0.9;
        public bool isPositionLocked = true;

        public OverlayFormModern()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = ColorBackground;
            TransparencyKey = ColorBackground;
            Width = 280;
            Height = 100;
            Padding = new Padding(0);
            DoubleBuffered = true;
            
            CreateHandle();
            
            try
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_TOPMOST = 0x00000008;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int GWL_EXSTYLE = -20;
                
                int currentStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                SetWindowLong(this.Handle, GWL_EXSTYLE, currentStyle | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
            
            var screen = Screen.PrimaryScreen.Bounds;
            Location = new Point((screen.Width - Width) / 2, 20);
            
            // CPU Display
            cpuLabel = new Label
            {
                AutoSize = false,
                Text = "CPU",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorBackground,
                ForeColor = ColorCpuGlow,
                Location = new Point(8, 8),
                Size = new Size(50, 20),
                Visible = AppSettings.ShowCpu
            };
            
            cpuValueLabel = new Label
            {
                AutoSize = false,
                Text = "-- °C",
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorBackground,
                ForeColor = ColorCpuGlow,
                Location = new Point(55, 8),
                Size = new Size(60, 20),
                Visible = AppSettings.ShowCpu
            };
            
            cpuBar = new Panel
            {
                BackColor = ColorCpuGlow,
                Location = new Point(8, 30),
                Size = new Size(0, 6),
                Visible = AppSettings.ShowCpu
            };
            
            // GPU Display
            gpuLabel = new Label
            {
                AutoSize = false,
                Text = "GPU",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorBackground,
                ForeColor = ColorGpuGlow,
                Location = new Point(140, 8),
                Size = new Size(50, 20),
                Visible = AppSettings.ShowGpu
            };
            
            gpuValueLabel = new Label
            {
                AutoSize = false,
                Text = "-- °C",
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorBackground,
                ForeColor = ColorGpuGlow,
                Location = new Point(185, 8),
                Size = new Size(60, 20),
                Visible = AppSettings.ShowGpu
            };
            
            gpuBar = new Panel
            {
                BackColor = ColorGpuGlow,
                Location = new Point(140, 30),
                Size = new Size(0, 6),
                Visible = AppSettings.ShowGpu
            };
            
            Controls.Add(cpuLabel);
            Controls.Add(cpuValueLabel);
            Controls.Add(cpuBar);
            Controls.Add(gpuLabel);
            Controls.Add(gpuValueLabel);
            Controls.Add(gpuBar);
            
            this.MouseDown += OverlayForm_MouseDown;
            this.MouseMove += OverlayForm_MouseMove;
            this.MouseUp += OverlayForm_MouseUp;
            this.MouseEnter += OverlayForm_MouseEnter;
            this.MouseLeave += OverlayForm_MouseLeave;
            
            AppSettings.SettingsChanged += OnSettingsChanged;
            pollTimer = new System.Threading.Timer(PollTimerCallback, null, 0, ActiveIntervalMs);
            topmostTimer = new System.Threading.Timer(ReassertTopmost, null, 500, 500);
        }
        
        private void OnSettingsChanged()
        {
            SensorService.UpdateConfig(AppSettings.ShowCpu, AppSettings.ShowGpu);
            
            try
            {
                cpuLabel.ForeColor = ColorCpuGlow;
                gpuLabel.ForeColor = ColorGpuGlow;
                cpuValueLabel.ForeColor = ColorCpuGlow;
                gpuValueLabel.ForeColor = ColorGpuGlow;
                
                cpuLabel.Visible = AppSettings.ShowCpu;
                cpuValueLabel.Visible = AppSettings.ShowCpu;
                cpuBar.Visible = AppSettings.ShowCpu;
                
                gpuLabel.Visible = AppSettings.ShowGpu;
                gpuValueLabel.Visible = AppSettings.ShowGpu;
                gpuBar.Visible = AppSettings.ShowGpu;
            }
            catch { }
        }
        
        private void ReassertTopmost(object? state)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
                return;
            
            try
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }
        
        private void PollTimerCallback(object? state)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
                return;
            
            try
            {
                this.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        if (!AppSettings.ShowCpu && !AppSettings.ShowGpu)
                        {
                            if (lastCpu != null || lastGpu != null)
                            {
                                lastCpu = null;
                                lastGpu = null;
                                cpuValueLabel.Text = "-- °C";
                                gpuValueLabel.Text = "-- °C";
                                cpuBar.Width = 0;
                                gpuBar.Width = 0;
                            }
                            return;
                        }
                        
                        float? cpuMax = SensorService.GetCachedCpuTemp();
                        float? gpuMax = SensorService.GetCachedGpuTemp();
                        
                        float? displayCpu = null;
                        float? displayGpu = null;
                        
                        // CPU smoothing
                        if (cpuMax.HasValue)
                        {
                            cpuBuffer.Enqueue(cpuMax.Value);
                            if (cpuBuffer.Count > 3) cpuBuffer.Dequeue();
                            if (lastCpu.HasValue)
                            {
                                float diff = Math.Abs(cpuMax.Value - lastCpu.Value);
                                if (diff > SpikeThreshold)
                                    displayCpu = lastCpu;
                                else
                                    displayCpu = cpuBuffer.Average();
                            }
                            else
                            {
                                displayCpu = cpuBuffer.Average();
                            }
                        }
                        
                        // GPU smoothing
                        if (gpuMax.HasValue)
                        {
                            gpuBuffer.Enqueue(gpuMax.Value);
                            if (gpuBuffer.Count > 3) gpuBuffer.Dequeue();
                            if (lastGpu.HasValue)
                            {
                                float diff = Math.Abs(gpuMax.Value - lastGpu.Value);
                                if (diff > SpikeThreshold)
                                    displayGpu = lastGpu;
                                else
                                    displayGpu = gpuBuffer.Average();
                            }
                            else
                            {
                                displayGpu = gpuBuffer.Average();
                            }
                        }
                        
                        var cpuChanged = !NullableEquals(displayCpu, lastCpu);
                        var gpuChanged = !NullableEquals(displayGpu, lastGpu);
                        
                        if (cpuChanged || gpuChanged)
                        {
                            lastCpu = displayCpu;
                            lastGpu = displayGpu;
                            
                            try
                            {
                                if (AppSettings.ShowCpu)
                                {
                                    cpuValueLabel.Text = displayCpu.HasValue ? $"{displayCpu.Value:F1}°C" : "-- °C";
                                    // Update bar width (0-120 pixels for 30-100°C range)
                                    if (displayCpu.HasValue)
                                    {
                                        int barWidth = Math.Min(120, (int)((displayCpu.Value - 30) / 70 * 120));
                                        barWidth = Math.Max(0, barWidth);
                                        cpuBar.Width = barWidth;
                                    }
                                    else
                                    {
                                        cpuBar.Width = 0;
                                    }
                                }
                                
                                if (AppSettings.ShowGpu)
                                {
                                    gpuValueLabel.Text = displayGpu.HasValue ? $"{displayGpu.Value:F1}°C" : "-- °C";
                                    if (displayGpu.HasValue)
                                    {
                                        int barWidth = Math.Min(120, (int)((displayGpu.Value - 30) / 70 * 120));
                                        barWidth = Math.Max(0, barWidth);
                                        gpuBar.Width = barWidth;
                                    }
                                    else
                                    {
                                        gpuBar.Width = 0;
                                    }
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
            return Math.Abs(a.Value - b.Value) < 0.5f;
        }
        
        private void OverlayForm_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !isPositionLocked)
            {
                isDragging = true;
                dragStartPoint = e.Location;
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
                this.Opacity = originalOpacity;
            }
        }
        
        private void OverlayForm_MouseEnter(object? sender, EventArgs e)
        {
            if (!isDragging && !isPositionLocked)
            {
                this.Opacity = 1.0;
            }
        }
        
        private void OverlayForm_MouseLeave(object? sender, EventArgs e)
        {
            if (!isDragging && !isPositionLocked)
            {
                this.Opacity = originalOpacity;
            }
        }
        
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }
            
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
            const int WM_ACTIVATE = 0x0006;
            
            if (m.Msg == WM_ACTIVATE)
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            
            base.WndProc(ref m);
        }
        
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            // Draw futuristic border
            using (Pen borderPen = new Pen(ColorBorder, 1.5f))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
            
            // Draw corner accents
            using (Brush accentBrush = new SolidBrush(ColorBorder))
            {
                int cornerSize = 8;
                e.Graphics.FillRectangle(accentBrush, 0, 0, cornerSize, 2);
                e.Graphics.FillRectangle(accentBrush, Width - cornerSize, 0, cornerSize, 2);
                e.Graphics.FillRectangle(accentBrush, 0, Height - 2, cornerSize, 2);
                e.Graphics.FillRectangle(accentBrush, Width - cornerSize, Height - 2, cornerSize, 2);
            }
        }
    }
}
