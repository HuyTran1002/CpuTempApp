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
            ClientSize = new Size(420, 420);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = ColorBackground;
            DoubleBuffered = true;

            // Title
            var titleLabel = new Label
            {
                Text = "⚙️ SETTINGS",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ColorAccent,
                BackColor = ColorBackground,
                Dock = DockStyle.Top,
                Height = 50,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(20, 0, 0, 0)
            };
            Controls.Add(titleLabel);

            // Main Panel
            var mainPanel = new Panel
            {
                BackColor = ColorPanel,
                Dock = DockStyle.Top,
                Height = 200,
                Margin = new Padding(15, 10, 15, 10)
            };
            mainPanel.Location = new Point(15, 60);

            // CPU Checkbox
            chkCpu = new CheckBox
            {
                Text = "Display CPU Temperature",
                Location = new Point(20, 20),
                Size = new Size(300, 24),
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
                Location = new Point(20, 60),
                Size = new Size(300, 24),
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = ColorText,
                BackColor = ColorPanel,
                AutoSize = false,
                Appearance = Appearance.Normal
            };

            // Color Picker
            var colorLabel = new Label
            {
                Text = "Text Color:",
                Location = new Point(20, 105),
                Size = new Size(80, 24),
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = ColorText,
                BackColor = ColorPanel,
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnColorPicker = new Button
            {
                Text = "Pick Color",
                Location = new Point(110, 105),
                Size = new Size(100, 28),
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
                Location = new Point(220, 105),
                Size = new Size(60, 28),
                BorderStyle = BorderStyle.FixedSingle
            };

            // Edit Position button
            btnEditPosition = new Button
            {
                Text = "Edit Position",
                Location = new Point(20, 150),
                Size = new Size(120, 32),
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

            // Button Panel
            var buttonPanel = new Panel
            {
                BackColor = ColorBackground,
                Dock = DockStyle.Bottom,
                Height = 60,
                Padding = new Padding(20)
            };

            btnApply = new Button
            {
                Text = "APPLY",
                Size = new Size(100, 36),
                Location = new Point(120, 12),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorAccent,
                ForeColor = ColorBackground,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK,
                Cursor = Cursors.Hand
            };
            btnApply.FlatAppearance.BorderSize = 0;

            btnCancel = new Button
            {
                Text = "HIDE",
                Size = new Size(100, 36),
                Location = new Point(230, 12),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(150, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;

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
                AppSettings.ShowCpu = chkCpu.Checked;
                AppSettings.ShowGpu = chkGpu.Checked;
                if (this.Modal) Close();
            };

            // Edit Position button
            btnEditPosition.Click += (s, e) =>
            {
                if (overlay != null)
                {
                    overlay.isPositionLocked = !overlay.isPositionLocked;
                    if (overlay.isPositionLocked)
                    {
                        btnEditPosition.Text = "Edit Position";
                        MessageBox.Show("Position locked. Click 'Edit Position' again to unlock for dragging.", "Position Locked", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        btnEditPosition.Text = "✓ Locked";
                        btnEditPosition.BackColor = Color.FromArgb(50, 150, 50);
                        MessageBox.Show("Position unlocked. Drag the overlay to reposition.", "Position Unlocked", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
