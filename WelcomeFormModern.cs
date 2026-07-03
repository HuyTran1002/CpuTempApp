using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CpuTempApp
{
    public class WelcomeFormModern : Form
    {
        private readonly Color ColorBackground = Color.FromArgb(10, 10, 20);      // Dark navy
        private readonly Color ColorText = Color.FromArgb(200, 200, 220);          // Light gray
        private readonly Color ColorAccent = Color.FromArgb(0, 255, 200);          // Cyan
        private readonly Color ColorButton = Color.FromArgb(0, 150, 255);          // Neon blue
        private readonly Color ColorButtonHover = Color.FromArgb(0, 200, 255);    // Bright blue

        public WelcomeFormModern()
        {
            Text = "CPU Temp Monitor - Welcome";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(500, 350);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            BackColor = ColorBackground;
            DoubleBuffered = true;

            // Title Label
            var titleLabel = new Label
            {
                Text = "CPU TEMP MONITOR",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = ColorAccent,
                BackColor = ColorBackground,
                Height = 60,
                Dock = DockStyle.Top
            };
            Controls.Add(titleLabel);

            // Subtitle
            var subtitleLabel = new Label
            {
                Text = "Advanced System Temperature Monitoring",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = ColorText,
                BackColor = ColorBackground,
                Height = 30,
                Dock = DockStyle.Top
            };
            Controls.Add(subtitleLabel);

            // Info Panel
            var infoPanel = new Panel
            {
                BackColor = Color.FromArgb(20, 20, 35),
                Height = 80,
                Dock = DockStyle.Top,
                Padding = new Padding(20, 15, 20, 15)
            };

            var infoText = new Label
            {
                Text = "📊 Real-time CPU/GPU Temperature Display\n🚀 Optimized Performance & Accuracy\n🎮 Fullscreen Game Compatible",
                AutoSize = false,
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = ColorText,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft
            };
            infoPanel.Controls.Add(infoText);
            Controls.Add(infoPanel);

            // ── HWiNFO64 requirement check ──────────────────────────────
            bool hwInfoInstalled = HWiNFOReader.IsSharedMemoryLive() ||
                                   !string.IsNullOrEmpty(GetPrivateHWiNFOPath());

            var hwPanel = new Panel
            {
                BackColor = hwInfoInstalled
                    ? Color.FromArgb(15, 40, 20)   // Dark green — OK
                    : Color.FromArgb(40, 15, 15),  // Dark red — missing
                Height = 48,
                Dock = DockStyle.Top,
                Padding = new Padding(16, 0, 16, 0),
            };

            var hwLabel = new Label
            {
                Text = hwInfoInstalled
                    ? "✅  HWiNFO64 detected — sensor data will be accurate"
                    : "⚠️  HWiNFO64 not found — please install it for accurate temperatures",
                AutoSize = false,
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = hwInfoInstalled ? Color.FromArgb(100, 255, 150) : Color.FromArgb(255, 180, 80),
                BackColor = Color.Transparent,
                Width = hwInfoInstalled ? 400 : 310,
                Height = 48,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(0, 0),
            };
            hwPanel.Controls.Add(hwLabel);

            if (!hwInfoInstalled)
            {
                var btnDownload = new Button
                {
                    Text = "Download HWiNFO64 ",
                    Width = 160,
                    Height = 30,
                    BackColor = Color.FromArgb(200, 120, 0),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Location = new Point(316, 9),
                };
                btnDownload.FlatAppearance.BorderSize = 0;
                btnDownload.Click += (s, e) =>
                    Process.Start(new ProcessStartInfo("https://www.hwinfo.com/download/") { UseShellExecute = true });
                hwPanel.Controls.Add(btnDownload);
            }

            Controls.Add(hwPanel);

            // Spacer
            var spacer = new Panel { Height = 20, Dock = DockStyle.Top, BackColor = ColorBackground };
            Controls.Add(spacer);

            // Continue Button
            var btnContinue = new Button
            {
                Text = "ENTER",
                Width = 140,
                Height = 44,
                DialogResult = DialogResult.OK,
                BackColor = ColorButton,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };

            btnContinue.FlatAppearance.BorderSize = 0;
            btnContinue.Location = new Point((ClientSize.Width - btnContinue.Width) / 2, ClientSize.Height - 70);
            btnContinue.MouseEnter += (s, e) => { btnContinue.BackColor = ColorButtonHover; };
            btnContinue.MouseLeave += (s, e) => { btnContinue.BackColor = ColorButton; };
            Controls.Add(btnContinue);

            AcceptButton = btnContinue;
        }

        /// Thin wrapper — lets WelcomeForm ask HWiNFOReader if the exe is findable
        /// without exposing internal API surface.
        private static string GetPrivateHWiNFOPath()
        {
            // We call EnsureRunning() in a no-op way just to trigger discovery;
            // status is set as a side effect.
            // Instead, use the public status after a quick sync check:
            HWiNFOReader.EnsureRunning();
            return HWiNFOReader.Status != HWiNFOReader.HWiNFOStatus.NotInstalled ? "found" : null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Draw futuristic border
            using (Pen borderPen = new Pen(ColorAccent, 2f))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }

            // Draw corner accents
            using (Brush accentBrush = new SolidBrush(ColorAccent))
            {
                int cornerSize = 15;
                e.Graphics.FillRectangle(accentBrush, 0, 0, cornerSize, 3);
                e.Graphics.FillRectangle(accentBrush, Width - cornerSize, 0, cornerSize, 3);
                e.Graphics.FillRectangle(accentBrush, 0, Height - 3, cornerSize, 3);
                e.Graphics.FillRectangle(accentBrush, Width - cornerSize, Height - 3, cornerSize, 3);
            }
        }
    }
}
