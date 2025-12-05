using System;
using System.Drawing;
using System.Windows.Forms;

namespace CpuTempApp
{
    public class SensorOptionsForm : Form
    {
        private CheckBox chkCpu;
        private CheckBox chkGpu;
        private ComboBox cmbCpuSensor;
        private ComboBox cmbGpuSensor;
        private CheckBox chkCpuDistance;
        private NumericUpDown nudCpuTjMax;
        private Button btnApply;
        private Button btnCancel;

        // track initial state to know when user changed things
        private bool initialCpu;
        private bool initialGpu;

        public SensorOptionsForm()
        {
            Text = "Chọn cảm biến";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 160);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;

            chkCpu = new CheckBox
            {
                Text = "Hiển thị nhiệt độ CPU",
                AutoSize = true,
                Location = new Point(20, 20)
            };
            chkGpu = new CheckBox
            {
                Text = "Hiển thị nhiệt độ GPU",
                AutoSize = true,
                Location = new Point(20, 50)
            };

            chkCpu.CheckedChanged += OnChoiceChanged;
            chkGpu.CheckedChanged += OnChoiceChanged;

            cmbCpuSensor = new ComboBox { Location = new Point(200, 18), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbGpuSensor = new ComboBox { Location = new Point(200, 48), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };

            chkCpuDistance = new CheckBox { Text = "Distance to TjMax sensor (invert)", AutoSize = true, Location = new Point(20, 80) };
            nudCpuTjMax = new NumericUpDown { Minimum = 60, Maximum = 130, Value = 100, Location = new Point(260, 80), Width = 80 };

            Controls.Add(cmbCpuSensor);
            Controls.Add(cmbGpuSensor);
            Controls.Add(chkCpuDistance);
            Controls.Add(nudCpuTjMax);

            btnApply = new Button
            {
                Text = "Apply",
                Enabled = false,
                DialogResult = DialogResult.OK,
                Size = new Size(90, 30)
            };
            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 30)
            };

            btnApply.Location = new Point(ClientSize.Width / 2 - btnApply.Width - 8, 100);
            btnCancel.Location = new Point(ClientSize.Width / 2 + 8, 100);

            Controls.Add(chkCpu);
            Controls.Add(chkGpu);
            Controls.Add(btnApply);
            Controls.Add(btnCancel);

            // initialize from AppSettings so checkbox state is preserved across opens
            chkCpu.Checked = AppSettings.ShowCpu;
            chkGpu.Checked = AppSettings.ShowGpu;

            // remember initial state
            initialCpu = chkCpu.Checked;
            initialGpu = chkGpu.Checked;

            // populate sensor lists (best-effort) by opening a temporary LibreHardwareMonitor Computer
            try
            {
                var comp = new LibreHardwareMonitor.Hardware.Computer { IsCpuEnabled = true, IsGpuEnabled = true };
                try { comp.Open(); } catch { }
                foreach (var hw in comp.Hardware)
                {
                    try
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType != LibreHardwareMonitor.Hardware.SensorType.Temperature) continue;
                            var name = s.Name ?? string.Empty;
                            // add to CPU list if hardware is CPU
                            if (hw.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu)
                                cmbCpuSensor.Items.Add(name);
                            if (hw.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.GpuAmd || hw.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia || hw.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.GpuIntel || (hw.Name ?? string.Empty).ToLowerInvariant().Contains("gpu"))
                                cmbGpuSensor.Items.Add(name);
                        }
                    }
                    catch { }
                }
                try { comp.Close(); } catch { }
            }
            catch { }

            // select previously chosen sensors if any
            if (!string.IsNullOrEmpty(AppSettings.SelectedCpuSensor)) cmbCpuSensor.SelectedItem = AppSettings.SelectedCpuSensor;
            if (!string.IsNullOrEmpty(AppSettings.SelectedGpuSensor)) cmbGpuSensor.SelectedItem = AppSettings.SelectedGpuSensor;
            chkCpuDistance.Checked = AppSettings.CpuSensorIsDistanceToTjMax;
            nudCpuTjMax.Value = AppSettings.CpuTjMax;

            // set apply enabled only if current state differs from initial state
            UpdateApplyEnabled();

            // Apply handler saves choices (DialogResult = OK on button will close dialog)
            btnApply.Click += (s, e) => ApplyChoices();
        }

        private void OnChoiceChanged(object? sender, EventArgs e)
        {
            UpdateApplyEnabled();
        }

        private void UpdateApplyEnabled()
        {
            // enable Apply only when user changed any checkbox relative to initial state
            bool changed = (chkCpu.Checked != initialCpu) || (chkGpu.Checked != initialGpu) ||
                (cmbCpuSensor.SelectedItem?.ToString() ?? string.Empty) != AppSettings.SelectedCpuSensor ||
                (cmbGpuSensor.SelectedItem?.ToString() ?? string.Empty) != AppSettings.SelectedGpuSensor ||
                chkCpuDistance.Checked != AppSettings.CpuSensorIsDistanceToTjMax ||
                (int)nudCpuTjMax.Value != AppSettings.CpuTjMax;
            btnApply.Enabled = changed;
        }

        private void ApplyChoices()
        {
            // Apply via property setters which raise SettingsChanged
            AppSettings.ShowCpu = chkCpu.Checked;
            AppSettings.ShowGpu = chkGpu.Checked;

            AppSettings.SelectedCpuSensor = cmbCpuSensor.SelectedItem?.ToString() ?? string.Empty;
            AppSettings.SelectedGpuSensor = cmbGpuSensor.SelectedItem?.ToString() ?? string.Empty;
            AppSettings.CpuSensorIsDistanceToTjMax = chkCpuDistance.Checked;
            AppSettings.CpuTjMax = (int)nudCpuTjMax.Value;

            // update initial states so if user keeps dialog open and toggles back, Apply updates correctly
            initialCpu = chkCpu.Checked;
            initialGpu = chkGpu.Checked;

            // DialogResult.OK set on the button will close the dialog automatically
        }
    }
}