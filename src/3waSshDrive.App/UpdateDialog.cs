using System;
using System.Drawing;
using System.Windows.Forms;
using ThreeWa.SshDrive.App.Updates;

namespace ThreeWa.SshDrive.App
{
    internal sealed class UpdateDialog : Form
    {
        private readonly Label _statusLabel = new Label();
        private readonly ProgressBar _progressBar = new ProgressBar();
        private readonly Button _updateButton = new Button();
        private readonly Button _laterButton = new Button();
        private bool _operationInProgress;

        internal UpdateDialog(UpdateDialogModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            Text = "3waSshDrive 更新";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(580, 460);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.2F, FontStyle.Regular);

            BuildInterface(model);
            FormClosing += OnFormClosing;
        }

        internal event EventHandler UpdateRequested;

        internal void SetDownloading()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(SetDownloading));
                return;
            }

            _operationInProgress = true;
            _updateButton.Enabled = false;
            _laterButton.Enabled = false;
            _progressBar.Value = 0;
            _progressBar.Visible = true;
            _statusLabel.Text = "正在下載並驗證新版…";
        }

        internal void SetProgress(int progress)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int>(SetProgress), progress);
                return;
            }

            _progressBar.Value = Math.Max(0, Math.Min(100, progress));
        }

        internal void SetFailure(string message)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetFailure), message);
                return;
            }

            _operationInProgress = false;
            _updateButton.Enabled = false;
            _laterButton.Text = "關閉";
            _laterButton.Enabled = true;
            _statusLabel.Text = message ?? string.Empty;
        }

        private void BuildInterface(UpdateDialogModel model)
        {
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Color.White
            };
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                AutoSize = true,
                Text = "有新版可以安裝",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Margin = new Padding(0, 0, 0, 14)
            };
            page.Controls.Add(title, 0, 0);

            var versions = new Label
            {
                AutoSize = true,
                Text = "目前版本：" + model.CurrentVersion + Environment.NewLine +
                    "最新版本：" + model.TargetVersion,
                ForeColor = Color.FromArgb(51, 65, 85),
                Margin = new Padding(0, 0, 0, 14)
            };
            page.Controls.Add(versions, 0, 1);

            var notesTitle = new Label
            {
                AutoSize = true,
                Text = "版本說明",
                Font = new Font("Segoe UI", 9.2F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Margin = new Padding(0, 0, 0, 6)
            };
            page.Controls.Add(notesTitle, 0, 2);

            var notes = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Text = model.ReleaseNotes,
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(51, 65, 85),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 0, 14)
            };
            page.Controls.Add(notes, 0, 3);

            _statusLabel.AutoSize = true;
            _statusLabel.Text = "現在更新會先安全卸載磁碟，完成後自動重新開啟。";
            _statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
            _statusLabel.Margin = new Padding(0, 0, 0, 8);
            page.Controls.Add(_statusLabel, 0, 4);

            _progressBar.Dock = DockStyle.Top;
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Visible = false;
            _progressBar.Margin = new Padding(0, 0, 0, 14);
            page.Controls.Add(_progressBar, 0, 5);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _updateButton.Text = "更新";
            _updateButton.AutoSize = true;
            _updateButton.Padding = new Padding(16, 5, 16, 5);
            _updateButton.BackColor = Color.FromArgb(37, 99, 235);
            _updateButton.ForeColor = Color.White;
            _updateButton.FlatStyle = FlatStyle.Flat;
            _updateButton.FlatAppearance.BorderSize = 0;
            _updateButton.Click += OnUpdateClick;

            _laterButton.Text = "稍後";
            _laterButton.AutoSize = true;
            _laterButton.Padding = new Padding(16, 5, 16, 5);
            _laterButton.DialogResult = DialogResult.Cancel;
            _laterButton.Margin = new Padding(0, 0, 8, 0);

            actions.Controls.Add(_updateButton);
            actions.Controls.Add(_laterButton);
            page.Controls.Add(actions, 0, 6);

            Controls.Add(page);
            AcceptButton = _updateButton;
            CancelButton = _laterButton;
        }

        private void OnUpdateClick(object sender, EventArgs e)
        {
            if (_operationInProgress)
                return;

            SetDownloading();
            UpdateRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_operationInProgress && e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true;
        }
    }
}
