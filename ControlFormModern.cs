using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace CpuTempApp
{
    public class ControlFormModern : Form
    {
        private CheckBox chkCpu;
        private CheckBox chkGpu;
        private Button btnApply;
        private Button btnCancel;
        private Button btnColorPicker;
        private Panel pnlColorPreview;
        private Button btnEditPosition;
        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayMenu;
        private OverlayForm overlay;
        private bool allowClose = false;
        private bool hasUnlockedPosition = false;  // Track if position has been unlocked

        private readonly Color ColorBackground = Color.FromArgb(10, 10, 20);
        private readonly Color ColorPanel = Color.FromArgb(20, 20, 35);
        private readonly Color ColorText = Color.FromArgb(200, 200, 220);
        private readonly Color ColorAccent = Color.FromArgb(0, 255, 200);
        private readonly Color ColorButton = Color.FromArgb(0, 150, 255);
        private readonly Color ColorButtonHover = Color.FromArgb(0, 200, 255);

        public ControlFormModern()
        {
            Text = "CPU Temp Monitor - Settings";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 280);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = ColorBackground;
            DoubleBuffered = true;

            // Close Button (X)
            var btnClose = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = ColorAccent,
                BackColor = ColorBackground,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(35, 35),
                Location = new Point(ClientSize.Width - 40, 5),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => 
            { 
                if (hasUnlockedPosition)
                {
                    MessageBox.Show(this, "Please lock the position first before closing the app.", "Position Not Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                var result = MessageBox.Show(this, "Are you sure you want to exit?", "Confirm Exit", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    allowClose = true;
                    Application.Exit();
                }
            };
            Controls.Add(btnClose);

            // Reset Position Button (small, next to X)
            var btnReset = new Button
            {
                Text = "⟲",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = ColorAccent,
                BackColor = ColorBackground,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(35, 35),
                Location = new Point(ClientSize.Width - 70, 3),
                Cursor = Cursors.Hand
            };
            btnReset.FlatAppearance.BorderSize = 0;
            btnReset.Click += (s, e) =>
            {
                AppSettings.ResetOverlayPosition();
                if (overlay != null)
                {
                    var screen = Screen.PrimaryScreen.Bounds;
                    overlay.Location = new Point((screen.Width - overlay.Width) / 2, 0);
                }
                MessageBox.Show("Overlay position reset to center.", "Reset", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            Controls.Add(btnReset);

            // Developer Signature
            var devLabel = new Label
            {
                Text = "Made by Dev Huy",
                Font = new Font("Segoe UI", 8, FontStyle.Italic),
                ForeColor = Color.FromArgb(100, 255, 200),
                BackColor = ColorBackground,
                AutoSize = true,
                Location = new Point(15, 15)
            };
            Controls.Add(devLabel);

            // Main Settings Panel - Centered & Compact
            var mainPanel = new Panel
            {
                BackColor = ColorPanel,
                Location = new Point(15, 45),
                Size = new Size(370, 130),
                Padding = new Padding(18, 15, 18, 15)
            };

            // CPU Checkbox
            chkCpu = new CheckBox
            {
                Text = "Display CPU Temperature",
                Location = new Point(18, 12),
                Size = new Size(330, 22),
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = ColorText,
                BackColor = ColorPanel,
                AutoSize = false,
                Appearance = Appearance.Normal
            };

            // GPU Checkbox
            chkGpu = new CheckBox
            {
                Text = "Display GPU Temperature",
                Location = new Point(18, 40),
                Size = new Size(330, 22),
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = ColorText,
                BackColor = ColorPanel,
                AutoSize = false,
                Appearance = Appearance.Normal
            };

            // Color Section - Horizontal Layout
            var colorLabel = new Label
            {
                Text = "Color:",
                Location = new Point(18, 72),
                Size = new Size(50, 28),
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = ColorText,
                BackColor = ColorPanel,
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnColorPicker = new Button
            {
                Text = "Pick",
                Location = new Point(72, 72),
                Size = new Size(60, 28),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = ColorButton,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnColorPicker.FlatAppearance.BorderSize = 0;

            pnlColorPreview = new Panel
            {
                BackColor = AppSettings.TextColor,
                Location = new Point(138, 72),
                Size = new Size(60, 28),
                BorderStyle = BorderStyle.FixedSingle
            };

            // Edit Position button - Right aligned
            btnEditPosition = new Button
            {
                Text = "Edit Position",
                Location = new Point(240, 72),
                Size = new Size(108, 28),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = ColorButton,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnEditPosition.FlatAppearance.BorderSize = 0;

            mainPanel.Controls.Add(chkCpu);
            mainPanel.Controls.Add(chkGpu);
            mainPanel.Controls.Add(colorLabel);
            mainPanel.Controls.Add(btnColorPicker);
            mainPanel.Controls.Add(pnlColorPreview);
            mainPanel.Controls.Add(btnEditPosition);
            Controls.Add(mainPanel);

            // Bottom Button Panel - Centered
            var buttonPanel = new Panel
            {
                BackColor = ColorBackground,
                Location = new Point(0, 190),
                Size = new Size(ClientSize.Width, 80),
                Padding = new Padding(0, 12, 0, 12)
            };

            btnApply = new Button
            {
                Text = "APPLY",
                Size = new Size(95, 38),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorAccent,
                ForeColor = ColorBackground,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK,
                Cursor = Cursors.Hand
            };
            btnApply.FlatAppearance.BorderSize = 0;
            btnApply.Location = new Point((ClientSize.Width / 2) - 105, 14);

            btnCancel = new Button
            {
                Text = "HIDE",
                Size = new Size(95, 38),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(150, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Location = new Point((ClientSize.Width / 2) + 10, 14);

            buttonPanel.Controls.Add(btnApply);
            buttonPanel.Controls.Add(btnCancel);
            Controls.Add(buttonPanel);

            // Initialize from AppSettings
            chkCpu.Checked = AppSettings.ShowCpu;
            chkGpu.Checked = AppSettings.ShowGpu;

            // Color picker click
            btnColorPicker.Click += (s, e) =>
            {
                using (var colorDialog = new ColorDialog())
                {
                    colorDialog.Color = AppSettings.TextColor;
                    if (colorDialog.ShowDialog() == DialogResult.OK)
                    {
                        AppSettings.TextColor = colorDialog.Color;
                        pnlColorPreview.BackColor = colorDialog.Color;
                    }
                }
            };

            // Hover effects
            btnColorPicker.MouseEnter += (s, e) => { btnColorPicker.BackColor = ColorButtonHover; };
            btnColorPicker.MouseLeave += (s, e) => { btnColorPicker.BackColor = ColorButton; };
            btnEditPosition.MouseEnter += (s, e) => { btnEditPosition.BackColor = ColorButtonHover; };
            btnEditPosition.MouseLeave += (s, e) => { btnEditPosition.BackColor = ColorButton; };
            btnApply.MouseEnter += (s, e) => { btnApply.BackColor = Color.FromArgb(0, 220, 180); };
            btnApply.MouseLeave += (s, e) => { btnApply.BackColor = ColorAccent; };
            btnCancel.MouseEnter += (s, e) => { btnCancel.BackColor = Color.FromArgb(180, 60, 60); };
            btnCancel.MouseLeave += (s, e) => { btnCancel.BackColor = Color.FromArgb(150, 50, 50); };

            // Create tray icon and overlay
            CreateTrayIcon();

            try
            {
                overlay = new OverlayForm();
                overlay.Show();
            }
            catch { }

            FormClosing += ControlForm_FormClosing;

            btnApply.Click += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[ControlForm] APPLY clicked: ShowCpu={chkCpu.Checked}, ShowGpu={chkGpu.Checked}");
                AppSettings.ShowCpu = chkCpu.Checked;
                AppSettings.ShowGpu = chkGpu.Checked;
                System.Diagnostics.Debug.WriteLine($"[ControlForm] After APPLY: AppSettings.ShowCpu={AppSettings.ShowCpu}, AppSettings.ShowGpu={AppSettings.ShowGpu}");
                if (this.Modal) Close();
            };

            // Edit Position button
            btnEditPosition.Click += (s, e) =>
            {
                if (overlay != null)
                {
                    overlay.isPositionLocked = !overlay.isPositionLocked;
                    System.Diagnostics.Debug.WriteLine($"[ControlForm] Toggle position lock: isPositionLocked={overlay.isPositionLocked}");
                    if (overlay.isPositionLocked)
                    {
                        // LOCKING - position is now locked
                        btnEditPosition.Text = "Edit Position";
                        btnEditPosition.BackColor = Color.FromArgb(0, 150, 255);
                        // Save the new position when locking
                        System.Diagnostics.Debug.WriteLine($"[ControlForm] LOCKING position: X={overlay.Location.X}, Y={overlay.Location.Y}");
                        AppSettings.OverlayX = overlay.Location.X;
                        AppSettings.OverlayY = overlay.Location.Y;
                        System.Diagnostics.Debug.WriteLine($"[ControlForm] Position saved to AppSettings");
                        hasUnlockedPosition = false;  // Reset flag after locking
                        MessageBox.Show("Position locked and saved. Click 'Edit Position' again to unlock for dragging.", "Position Locked", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        // UNLOCKING - position is now unlocked for dragging
                        System.Diagnostics.Debug.WriteLine($"[ControlForm] UNLOCKING position");
                        btnEditPosition.Text = "✓ Unlocked";
                        btnEditPosition.BackColor = Color.FromArgb(200, 100, 50);
                        hasUnlockedPosition = true;  // Mark that position is unlocked
                        MessageBox.Show("Position unlocked. Drag the overlay to reposition, then lock again to save.", "Position Unlocked", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };

            // FormClosing
            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing && !allowClose)
                {
                    e.Cancel = true;
                    this.Hide();
                }
            };
            // Hide instead of exit on X button
            btnCancel.Click += (s, e) =>
            {
                if (hasUnlockedPosition)
                {
                    MessageBox.Show("Please lock the position first before hiding the app.", "Position Not Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                this.Hide();
            };
        }

        private void CreateTrayIcon()
        {
            notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = "CPU Temp Monitor"
            };

            // Try to load the app icon
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temperature_icon_175973.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    notifyIcon.Icon = new Icon(iconPath);
                }
            }
            catch { }

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Settings", null, (s, e) => ShowSettings());
            trayMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            notifyIcon.ContextMenuStrip = trayMenu;
            notifyIcon.DoubleClick += (s, e) => ShowSettings();
        }

        private void ShowSettings()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
        }

        private void ExitApplication()
        {
            allowClose = true;
            Application.Exit();
        }

        private void ControlForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Check if position is unlocked when trying to close via X button or File menu
            if (hasUnlockedPosition && e.CloseReason == CloseReason.UserClosing && !allowClose)
            {
                MessageBox.Show("Please lock the position first before closing the app.", "Position Not Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            
            if (!allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                if (overlay != null)
                {
                    try { overlay.Close(); } catch { }
                }

                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }

                if (trayMenu != null)
                {
                    trayMenu.Dispose();
                }
            }
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
