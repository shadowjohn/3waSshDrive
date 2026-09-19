using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace ThreeWa.SshDrive.App
{
    internal sealed class CheckingUpdateDialog : Form
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly System.Windows.Forms.Timer _dotsTimer = new System.Windows.Forms.Timer();
        private readonly Label _statusLabel = new Label();
        private readonly PictureBox _pictureBox = new PictureBox();
        private int _dotCount = 0;

        internal CheckingUpdateDialog(CancellationTokenSource cancellationTokenSource = null)
        {
            _cancellationTokenSource = cancellationTokenSource;

            Text = "3waSshDrive - 檢查更新中";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(300, 390);
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.2F, FontStyle.Regular);

            BuildInterface();

            _dotsTimer.Interval = 400;
            _dotsTimer.Tick += (s, e) =>
            {
                _dotCount = (_dotCount + 1) % 4;
                _statusLabel.Text = "芳寶正在檢查最新更新中" + new string('.', _dotCount);
            };
            _dotsTimer.Start();

            FormClosed += (s, e) =>
            {
                _dotsTimer.Stop();
                _dotsTimer.Dispose();
                _pictureBox.Image?.Dispose();
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (Owner != null)
            {
                Location = new Point(
                    Owner.Location.X + (Owner.Width - Width) / 2,
                    Owner.Location.Y + (Owner.Height - Height) / 2);
            }
        }

        private void BuildInterface()
        {
            var container = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(16, 16, 16, 14),
                BackColor = Color.White
            };
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 240)); // Mascot image
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // Title with animated dots
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));  // Subtitle
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));  // Progress bar
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Cancel button

            // 1. Loading Mascot PictureBox
            _pictureBox.Dock = DockStyle.Fill;
            _pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            _pictureBox.BackColor = Color.FromArgb(240, 246, 254);
            _pictureBox.Margin = new Padding(0, 0, 0, 8);
            _pictureBox.Image = MainForm.LoadLoadingMascotImage();
            MainForm.ApplyRoundedRegion(_pictureBox, 12, Color.FromArgb(226, 232, 240));
            container.Controls.Add(_pictureBox, 0, 0);

            // 2. Title Label
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.Text = "芳寶正在檢查最新更新中...";
            _statusLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            _statusLabel.ForeColor = Color.FromArgb(15, 23, 42);
            _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            _statusLabel.Margin = new Padding(0);
            container.Controls.Add(_statusLabel, 0, 1);

            // 3. Subtitle Label
            var subtitleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "連線取得最新版本資訊…",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0)
            };
            container.Controls.Add(subtitleLabel, 0, 2);

            // 4. Progress bar (Marquee)
            var progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 25,
                Margin = new Padding(24, 6, 24, 6)
            };
            container.Controls.Add(progressBar, 0, 3);

            // 5. Cancel Button
            var cancelButton = new Button
            {
                Text = "取消",
                AutoSize = true,
                Anchor = AnchorStyles.None,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderColor = Color.FromArgb(226, 232, 240) },
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Height = 26,
                Padding = new Padding(12, 1, 12, 1),
                Margin = new Padding(0, 4, 0, 0)
            };
            MainForm.ApplyRoundedRegion(cancelButton, 6);
            cancelButton.Click += (s, e) =>
            {
                try
                {
                    _cancellationTokenSource?.Cancel();
                }
                catch
                {
                }
                Close();
            };
            container.Controls.Add(cancelButton, 0, 4);

            Controls.Add(container);

            MainForm.ApplyRoundedRegion(this, 14, Color.FromArgb(203, 213, 225));
        }
    }
}
