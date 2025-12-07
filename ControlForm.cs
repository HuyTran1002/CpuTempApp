using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace CpuTempApp
{
    public class ControlForm : Form
    {
        private CheckBox chkCpu;
        private CheckBox chkGpu;
        private Button btnApply;
        private Button btnCancel;
        private Button btnColorPicker;
        private Label lblColorPreview;
        private Button btnEditPosition;
        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayMenu;
        private OverlayFormModern overlay;
        private bool allowClose = false;

        public ControlForm()
        {
            Text = "Settings";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(320, 220);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            chkCpu = new CheckBox { Text = "Show CPU", Location = new Point(16, 16), AutoSize = true };
            chkGpu = new CheckBox { Text = "Show GPU", Location = new Point(16, 44), AutoSize = true };
            
            // Color picker button
            btnColorPicker = new Button { Text = "Text Color", Size = new Size(100, 28), Location = new Point(16, 76) };
            lblColorPreview = new Label { Size = new Size(40, 28), Location = new Point(124, 76), BackColor = AppSettings.TextColor, BorderStyle = BorderStyle.FixedSingle };

            // Edit Position button
            btnEditPosition = new Button { Text = "Edit Position", Size = new Size(100, 28), Location = new Point(16, 114) };

            btnApply = new Button { Text = "Apply", Size = new Size(90, 28), Location = new Point(80, 170), DialogResult = DialogResult.OK };
            btnCancel = new Button { Text = "Hide", Size = new Size(90, 28), Location = new Point(180, 170), DialogResult = DialogResult.Cancel };

            Controls.Add(chkCpu);
            Controls.Add(chkGpu);
            Controls.Add(btnColorPicker);
            Controls.Add(lblColorPreview);
            Controls.Add(btnEditPosition);
            Controls.Add(btnApply);
            Controls.Add(btnCancel);

            // initialize from AppSettings
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
                        lblColorPreview.BackColor = colorDialog.Color;
                    }
                }
            };

            // tray and overlay
            CreateTrayIcon();

            try
            {
                overlay = new OverlayFormModern();
                overlay.Show();
            }
            catch { }

            FormClosing += ControlForm_FormClosing;

            btnApply.Click += (s, e) =>
            {
                // Apply new settings (this will raise the SettingsChanged event)
                AppSettings.ShowCpu = chkCpu.Checked;
                AppSettings.ShowGpu = chkGpu.Checked;
                // if this form is modal the caller expects it to close
                if (this.Modal) Close();
            };

            // Edit Position button - toggle between locked and unlocked
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
                        btnEditPosition.Text = "Lock Position";
                        MessageBox.Show("Position unlocked. You can now drag the overlay to a new position.", "Position Unlocked", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };

            // Cancel (now "Hide") should minimize to tray
            btnCancel.Click += (s, e) =>
            {
                HideToTray();
            };
        }

        private void ControlForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Only show dialog if user is trying to close (not from programmatic allowClose)
            if (e.CloseReason == CloseReason.UserClosing && !allowClose)
            {
                e.Cancel = true;  // Cancel the close initially
                
                var result = MessageBox.Show(
                    "Do you want to exit the application or minimize to tray?",
                    "CPU Temp App",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                
                if (result == DialogResult.Yes)
                {
                    // User chose to exit - set flag and close for real
                    allowClose = true;
                    Close();  // This will call FormClosing again but allowClose will prevent dialog
                }
                else if (result == DialogResult.No)
                {
                    // User chose to minimize to tray
                    HideToTray();
                }
                // If Cancel, do nothing (keep dialog open)
            }
        }
        private void CreateTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Open", null, (s, e) => ShowFromTray());
            var checkUpdateItem = new ToolStripMenuItem("Check for Updates", null, async (s, e) => await CheckForUpdatesAsync());
            var dumpItem = new ToolStripMenuItem("Dump sensors", null, (s, e) =>
            {
                // Dump sensors feature removed - now handled by SensorService
                MessageBox.Show("Sensor diagnostics moved to SensorService (background thread).", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());
            trayMenu.Items.Add(openItem);
            trayMenu.Items.Add(checkUpdateItem);
            trayMenu.Items.Add(dumpItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exitItem);

            // Load icon from file
            Icon trayIconImage = SystemIcons.Application;
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temperature_icon_175973.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    trayIconImage = new Icon(iconPath);
                }
            }
            catch { }

            notifyIcon = new NotifyIcon
            {
                Icon = trayIconImage,
                Text = "CpuTempApp",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            notifyIcon.DoubleClick += (s, e) => ShowFromTray();
        }

        private void ExitApplication()
        {
            // Exit directly from tray menu without asking - set allowClose flag first
            allowClose = true;
            try { notifyIcon.Visible = false; notifyIcon.Dispose(); } catch { }
            try { overlay?.Close(); } catch { }
            Close();
            Application.Exit();
        }

        private void HideToTray()
        {
            try
            {
                this.Hide();
                this.ShowInTaskbar = false;
            }
            catch { }
        }

        private void ShowFromTray()
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke((Action)ShowFromTray);
                    return;
                }
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = true;
                // bring to front and ensure it's above overlay
                this.TopMost = true;
                this.Activate();
                this.BringToFront();
                var t = new System.Windows.Forms.Timer { Interval = 200 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); this.TopMost = false; };
                t.Start();
            }
            catch { }
        }
        
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                notifyIcon.ShowBalloonTip(2000, "Kiểm Tra Cập Nhật", "Đang kiểm tra cập nhật...", ToolTipIcon.Info);
                
                var (hasUpdate, latestVersion) = await UpdateChecker.CheckForUpdateAsync();
                
                if (hasUpdate)
                {
                    // Show auto-update dialog like IDM
                    UpdateChecker.ShowAutoUpdateDialog(latestVersion);
                }
                else
                {
                    notifyIcon.ShowBalloonTip(2000, "Kiểm Tra Cập Nhật", "Bạn đang dùng phiên bản mới nhất!", ToolTipIcon.Info);
                }
            }
            catch
            {
                notifyIcon.ShowBalloonTip(2000, "Kiểm Tra Cập Nhật", "Không thể kiểm tra cập nhật.", ToolTipIcon.Error);
            }
        }
    }
}