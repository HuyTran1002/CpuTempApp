using System;
using System.Drawing;
using System.Windows.Forms;

namespace CpuTempApp
{
    public class WelcomeForm : Form
    {
        public WelcomeForm()
        {
            Text = "Dev'Huy xin chào";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(420, 180);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;

            var label = new Label
            {
                Text = "Hãy xác nhận Huy rất đẹp trai để tiếp tục!",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 90,
                Font = new Font("Segoe UI", 12, FontStyle.Regular)
            };
            Controls.Add(label);

            var btnContinue = new Button
            {
                Text = "Xác nhận",
                Width = 120,
                Height = 36,
                DialogResult = DialogResult.OK,
            };

            btnContinue.Location = new Point((ClientSize.Width - btnContinue.Width) / 2, label.Bottom + 10);
            btnContinue.Anchor = AnchorStyles.Bottom;
            Controls.Add(btnContinue);

            AcceptButton = btnContinue;
        }
    }
}