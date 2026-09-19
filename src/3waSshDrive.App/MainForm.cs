using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ThreeWa.SshDrive.App.Diagnostics;
using ThreeWa.SshDrive.App.Updates;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Profiles;
using ThreeWa.SshDrive.FileSystem.Mounting;
using ThreeWa.SshDrive.Sftp;

namespace ThreeWa.SshDrive.App
{
    internal sealed partial class MainForm : Form, IUpdatePreparation
    {
        private readonly ProfileStore _profileStore;
        private readonly SshConnectionProbe _connectionProbe;
        private readonly MountManager _mountManager;
        private readonly UpdateService _updateService;
        private readonly UpdateCoordinator _updateCoordinator;
        private readonly Dictionary<string, MountedDrive> _mountedDrives =
            new Dictionary<string, MountedDrive>(StringComparer.OrdinalIgnoreCase);

        private readonly ComboBox _profiles = new ComboBox();
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _host = new TextBox();
        private readonly NumericUpDown _port = new NumericUpDown();
        private readonly TextBox _username = new TextBox();
        private readonly TextBox _remoteRoot = new TextBox();
        private readonly ComboBox _driveLetter = new ComboBox();
        private readonly ComboBox _authenticationMode = new ComboBox();
        private readonly TextBox _privateKeyPath = new TextBox();
        private readonly TextBox _password = new TextBox();
        private readonly TextBox _hostFingerprint = new TextBox();
        private readonly CheckBox _readOnly = new CheckBox();
        private readonly Label _status = new Label();
        private readonly Button _newButton = new Button();
        private readonly Button _saveButton = new Button();
        private readonly Button _deleteButton = new Button();
        private readonly Button _browseButton = new Button();
        private readonly Button _testAndMountButton = new Button();
        private readonly Button _testButton = new Button();
        private readonly Button _mountButton = new Button();
        private readonly Button _unmountButton = new Button();
        private readonly Button _explorerButton = new Button();
        private readonly Button _installDriverButton = new Button();
        private readonly Button _checkUpdatesButton = new Button();
        private readonly Label _statusDot = new Label();
        private readonly Label _statusTimestamp = new Label();
        private readonly NotifyIcon _notifyIcon = new NotifyIcon();
        private readonly PictureBox _mascotPicture = new PictureBox();
        private System.Windows.Controls.MediaElement _mascotMedia;
        private readonly Timer _mascotLoopTimer = new Timer();
        private readonly Timer _reconnectTimer = new Timer();
        private bool _isReconnecting;
        private readonly Label _mascotName = new Label();
        private readonly Label _mascotSpeech = new Label();
        private readonly Panel _speechBubble = new Panel();
        private int _mascotQuoteIndex;
        private static readonly string[] MascotQuotes = new[]
        {
            "把遠端 Linux 掛載成本地磁碟，用 Antigravity 寫程式超順手～✨",
            "3WA 問題解決專家工作室，隨時為您待命～ฅ^•ﻌ•^ฅ",
            "讀寫功能已全面開放！直接在 Windows 編輯 Linux 上的檔案吧～",
            "現在有極速 Metadata 快取，檔案總管瀏覽滑順不卡頓！🚀",
            "右上角按 X 會最小化到右下角系統匣，我會默默守護連線～☕",
            "今天寫程式辛苦啦！多喝水、站起來活動一下筋骨喔～",
            "支援 Private Key 與 Password 兩種登入模式，安全又便利！",
            "點擊我隨時聽我說話～喵～ฅ'ω'ฅ"
        };
        private bool _isExplicitExit;
        private bool _isApplyingUpdate;
        private bool _isCheckingUpdates;

        private List<DriveProfile> _profileItems = new List<DriveProfile>();
        private string _selectedProfileName;
        private bool _busy;

        public MainForm()
            : this(
                ProfileStore.CreateDefault(),
                new SshConnectionProbe(),
                new MountManager(),
                UpdateService.CreateDefault())
        {
        }

        internal MainForm(
            ProfileStore profileStore,
            SshConnectionProbe connectionProbe,
            MountManager mountManager,
            UpdateService updateService)
        {
            _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
            _connectionProbe = connectionProbe ?? throw new ArgumentNullException(nameof(connectionProbe));
            _mountManager = mountManager ?? throw new ArgumentNullException(nameof(mountManager));
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _updateCoordinator = new UpdateCoordinator(this);

            Text = $"3waSshDrive - {Application.ProductVersion}";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1080, 750);
            Size = new Size(1100, 760);
            BackColor = Color.FromArgb(240, 246, 254);
            Font = new Font("Segoe UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point);

            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            var appIcon = LoadAppIcon();
            if (appIcon != null)
                Icon = appIcon;

            BuildInterface();
            WireEvents();
            LoadProfiles();
            RefreshDriverStatus();
            SetupTrayIcon();
        }

        private void BuildInterface()
        {
            var bgImage = LoadBackgroundImage();
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 16, 24, 14),
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.FromArgb(240, 246, 254),
                BackgroundImage = bgImage,
                BackgroundImageLayout = ImageLayout.Stretch
            };
            typeof(TableLayoutPanel)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(page, true, null);
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            // 1. Header Bar (Transparent background to let scenic background show through)
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 8)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var logoBox = new PictureBox
            {
                Size = new Size(64, 58),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = LoadHeaderLogo(),
                Margin = new Padding(0, 2, 8, 0)
            };
            header.Controls.Add(logoBox, 0, 0);

            var titlePanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 0, 0)
            };
            var title = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                BackColor = Color.Transparent,
                Text = "3waSshDrive",
                Margin = new Padding(0, 0, 0, 2)
            };
            var subTitle = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.Transparent,
                Text = "Linux workspace  →  Windows drive"
            };
            titlePanel.Controls.Add(title);
            titlePanel.Controls.Add(subTitle);
            header.Controls.Add(titlePanel, 1, 0);

            var rightHeader = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 8, 0, 0)
            };

            var aboutButton = new Button
            {
                Text = "ℹ 關於",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderColor = Color.FromArgb(203, 213, 225) },
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Height = 32,
                Padding = new Padding(10, 2, 10, 2),
                Margin = new Padding(12, 6, 0, 0)
            };
            ApplyRoundedRegion(aboutButton, 6);
            aboutButton.Click += (sender, args) => ShowAboutDialog();

            _checkUpdatesButton.Text = "↻ 檢查更新";
            _checkUpdatesButton.AutoSize = true;
            _checkUpdatesButton.FlatStyle = FlatStyle.Flat;
            _checkUpdatesButton.FlatAppearance.BorderColor =
                Color.FromArgb(203, 213, 225);
            _checkUpdatesButton.BackColor = Color.FromArgb(241, 245, 249);
            _checkUpdatesButton.ForeColor = Color.FromArgb(71, 85, 105);
            _checkUpdatesButton.Font = new Font(
                "Segoe UI",
                9F,
                FontStyle.Bold);
            _checkUpdatesButton.Cursor = Cursors.Hand;
            _checkUpdatesButton.Height = 32;
            _checkUpdatesButton.Padding = new Padding(10, 2, 10, 2);
            _checkUpdatesButton.Margin = new Padding(8, 6, 0, 0);
            ApplyRoundedRegion(_checkUpdatesButton, 6);

            var sloganPanel = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 0, 0)
            };
            sloganPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sloganPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sloganPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var slogan1 = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.Transparent,
                Text = "Linux 的工作空間",
                Anchor = AnchorStyles.Right
            };
            var slogan2 = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.Transparent,
                Text = "就在 Windows 觸手可及",
                Anchor = AnchorStyles.Right
            };
            sloganPanel.Controls.Add(slogan1, 0, 0);
            sloganPanel.Controls.Add(slogan2, 0, 1);

            rightHeader.Controls.Add(aboutButton);
            rightHeader.Controls.Add(_checkUpdatesButton);
            rightHeader.Controls.Add(sloganPanel);
            header.Controls.Add(rightHeader, 2, 0);

            page.Controls.Add(header, 0, 0);

            // 2. Main Content Split: Left (Settings Card) vs Right (Mascot Panel)
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 335));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Left Card (White rounded card look)
            var leftCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(16, 12, 16, 12),
                Margin = new Padding(0, 0, 14, 0)
            };
            ApplyRoundedRegion(leftCard, 14, Color.FromArgb(226, 232, 240));

            var leftLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

            // Section Header
            var sectionHeader = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            var sectionIcon = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                ForeColor = Color.FromArgb(37, 99, 235),
                Text = "⚙",
                Margin = new Padding(0, 1, 4, 0)
            };
            var sectionTitle = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Text = "連線設定 Connection Settings",
                Margin = new Padding(0, 2, 8, 0)
            };
            var sectionDesc = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 8.8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Text = "Mount a Linux workspace as a Windows drive over SSH.",
                Margin = new Padding(0, 5, 0, 0)
            };
            sectionHeader.Controls.Add(sectionIcon);
            sectionHeader.Controls.Add(sectionTitle);
            sectionHeader.Controls.Add(sectionDesc);
            leftLayout.Controls.Add(sectionHeader, 0, 0);

            // Form Fields Table
            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 11,
                Margin = new Padding(0, 2, 0, 2)
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var r = 0; r < 11; r++)
            {
                fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 11f));
            }

            ConfigureComboBox(_profiles);
            ConfigureComboBox(_driveLetter);
            ConfigureComboBox(_authenticationMode);
            _driveLetter.DropDown += (s, e) => RefreshDriveLetters();
            RefreshDriveLetters();
            _authenticationMode.Items.Add("Private key");
            _authenticationMode.Items.Add("Password");
            _authenticationMode.SelectedIndex = 0;

            _port.Minimum = 1;
            _port.Maximum = 65535;
            _port.Value = 22;
            _port.Width = 100;
            _remoteRoot.Text = "/";
            _password.UseSystemPasswordChar = true;
            _hostFingerprint.ReadOnly = true;
            _readOnly.Text = "Read-only (唯讀模式)";
            _readOnly.AutoSize = true;
            _readOnly.Anchor = AnchorStyles.Left;

            // Profile Button Group: + New, Save, Delete
            _newButton.Text = "+ New";
            _newButton.FlatStyle = FlatStyle.Flat;
            _newButton.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
            _newButton.BackColor = Color.FromArgb(239, 246, 255);
            _newButton.ForeColor = Color.FromArgb(29, 78, 216);
            _newButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _newButton.Cursor = Cursors.Hand;
            _newButton.Height = 27;
            ApplyRoundedRegion(_newButton, 6);

            _saveButton.Text = "💾 Save";
            _saveButton.FlatStyle = FlatStyle.Flat;
            _saveButton.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
            _saveButton.BackColor = Color.FromArgb(239, 246, 255);
            _saveButton.ForeColor = Color.FromArgb(29, 78, 216);
            _saveButton.Cursor = Cursors.Hand;
            _saveButton.Height = 27;
            ApplyRoundedRegion(_saveButton, 6);

            _deleteButton.Text = "🗑 Delete";
            _deleteButton.FlatStyle = FlatStyle.Flat;
            _deleteButton.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
            _deleteButton.BackColor = Color.FromArgb(254, 242, 242);
            _deleteButton.ForeColor = Color.FromArgb(185, 28, 28);
            _deleteButton.Cursor = Cursors.Hand;
            _deleteButton.Height = 27;
            ApplyRoundedRegion(_deleteButton, 6);

            _browseButton.Text = "📁 Browse…";
            _browseButton.FlatStyle = FlatStyle.Flat;
            _browseButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            _browseButton.BackColor = Color.FromArgb(248, 250, 252);
            _browseButton.Height = 26;
            _browseButton.Cursor = Cursors.Hand;
            ApplyRoundedRegion(_browseButton, 6);

            var profileButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            profileButtons.Controls.Add(_newButton);
            profileButtons.Controls.Add(_saveButton);
            profileButtons.Controls.Add(_deleteButton);

            AddRow(fields, 0, "❯", "Profile", _profiles, profileButtons);
            AddRow(fields, 1, "👤", "Name", _name, null);
            AddRow(fields, 2, "🌐", "Host", _host, null);
            AddRow(fields, 3, "🖧", "Port", _port, null);
            AddRow(fields, 4, "👤", "Username", _username, null);
            AddRow(fields, 5, "📁", "Remote root", _remoteRoot, null);
            AddRow(fields, 6, "💽", "Drive letter", _driveLetter, null);
            AddRow(fields, 7, "🛡", "Authentication", _authenticationMode, null);
            AddRow(fields, 8, "🔑", "Private key", _privateKeyPath, _browseButton);
            AddRow(fields, 9, "🔒", "Password", _password, null);
            AddRow(fields, 10, "⚙", "Options", _readOnly, null);
            leftLayout.Controls.Add(fields, 0, 1);

            // Action Buttons Bar
            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 0)
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); // Test & Mount
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24)); // Mount
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24)); // Unmount
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24)); // Open Explorer
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // Driver install (if needed)

            // 1. Test & Mount (Primary highlighted blue card)
            _testAndMountButton.Text = "▶  Test & Mount\n    測試連線並掛載";
            _testAndMountButton.Dock = DockStyle.Fill;
            _testAndMountButton.FlatStyle = FlatStyle.Flat;
            _testAndMountButton.FlatAppearance.BorderSize = 0;
            _testAndMountButton.BackColor = Color.FromArgb(29, 114, 232);
            _testAndMountButton.ForeColor = Color.White;
            _testAndMountButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _testAndMountButton.Cursor = Cursors.Hand;
            _testAndMountButton.Padding = new Padding(0, 2, 0, 2);
            _testAndMountButton.Margin = new Padding(0, 0, 8, 0);
            ApplyRoundedRegion(_testAndMountButton, 8);

            // 2. Mount
            _mountButton.Text = "💽  Mount\n     掛載";
            _mountButton.Dock = DockStyle.Fill;
            _mountButton.FlatStyle = FlatStyle.Flat;
            _mountButton.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            _mountButton.BackColor = Color.FromArgb(248, 250, 252);
            _mountButton.ForeColor = Color.FromArgb(30, 41, 59);
            _mountButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _mountButton.Cursor = Cursors.Hand;
            _mountButton.Padding = new Padding(0, 2, 0, 2);
            _mountButton.Margin = new Padding(0, 0, 8, 0);
            ApplyRoundedRegion(_mountButton, 8);

            // 3. Unmount
            _unmountButton.Text = "⏏  Unmount\n    卸載";
            _unmountButton.Dock = DockStyle.Fill;
            _unmountButton.FlatStyle = FlatStyle.Flat;
            _unmountButton.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            _unmountButton.BackColor = Color.FromArgb(248, 250, 252);
            _unmountButton.ForeColor = Color.FromArgb(71, 85, 105);
            _unmountButton.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            _unmountButton.Cursor = Cursors.Hand;
            _unmountButton.Padding = new Padding(0, 2, 0, 2);
            _unmountButton.Margin = new Padding(0, 0, 8, 0);
            ApplyRoundedRegion(_unmountButton, 8);

            // 4. Open Explorer
            _explorerButton.Text = "📁  Open\n    開啟資料夾";
            _explorerButton.Dock = DockStyle.Fill;
            _explorerButton.FlatStyle = FlatStyle.Flat;
            _explorerButton.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            _explorerButton.BackColor = Color.FromArgb(248, 250, 252);
            _explorerButton.ForeColor = Color.FromArgb(71, 85, 105);
            _explorerButton.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            _explorerButton.Cursor = Cursors.Hand;
            _explorerButton.Padding = new Padding(0, 2, 0, 2);
            _explorerButton.Margin = new Padding(0);
            ApplyRoundedRegion(_explorerButton, 8);

            // 5. Install Driver Button (hidden when driver is present)
            _installDriverButton.Text = "⚙ 安裝 WinFsp 驅動";
            _installDriverButton.FlatStyle = FlatStyle.Flat;
            _installDriverButton.FlatAppearance.BorderColor = Color.FromArgb(252, 211, 77);
            _installDriverButton.BackColor = Color.FromArgb(254, 243, 199);
            _installDriverButton.ForeColor = Color.FromArgb(180, 83, 9);
            _installDriverButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _installDriverButton.Cursor = Cursors.Hand;
            _installDriverButton.Visible = false;
            _installDriverButton.Height = 44;
            _installDriverButton.Margin = new Padding(8, 0, 0, 0);
            ApplyRoundedRegion(_installDriverButton, 8);

            actions.Controls.Add(_testAndMountButton, 0, 0);
            actions.Controls.Add(_mountButton, 1, 0);
            actions.Controls.Add(_unmountButton, 2, 0);
            actions.Controls.Add(_explorerButton, 3, 0);
            actions.Controls.Add(_installDriverButton, 4, 0);
            leftLayout.Controls.Add(actions, 0, 2);

            leftCard.Controls.Add(leftLayout);
            mainLayout.Controls.Add(leftCard, 0, 0);

            // Mascot Right Column (Speech Bubble + Mascot Illustration Card)
            var mascotColumn = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            mascotColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            mascotColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Speech Bubble (Warm amber card background with rounded feel)
            _speechBubble.Dock = DockStyle.Fill;
            _speechBubble.BackColor = Color.FromArgb(255, 251, 240);
            _speechBubble.BorderStyle = BorderStyle.None;
            _speechBubble.Padding = new Padding(14, 8, 14, 8);
            _speechBubble.Cursor = Cursors.Hand;
            _speechBubble.Margin = new Padding(0, 0, 0, 8);
            ApplyRoundedRegion(_speechBubble, 12, Color.FromArgb(254, 215, 170));

            _mascotName.Text = "💡  芳寶 (Fang-Fang)";
            _mascotName.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            _mascotName.ForeColor = Color.FromArgb(217, 119, 6);
            _mascotName.Dock = DockStyle.Top;
            _mascotName.AutoSize = true;
            _mascotName.Cursor = Cursors.Hand;
            _mascotName.Margin = new Padding(0, 0, 0, 4);

            _mascotSpeech.Text = MascotQuotes[0];
            _mascotSpeech.Font = new Font("Segoe UI", 9.2F, FontStyle.Regular);
            _mascotSpeech.ForeColor = Color.FromArgb(51, 65, 85);
            _mascotSpeech.Dock = DockStyle.Fill;
            _mascotSpeech.Cursor = Cursors.Hand;

            _speechBubble.Controls.Add(_mascotSpeech);
            _speechBubble.Controls.Add(_mascotName);

            // Mascot Picture (Loaded with mascot_card.png)
            _mascotPicture.Dock = DockStyle.Fill;
            _mascotPicture.SizeMode = PictureBoxSizeMode.Zoom;
            _mascotPicture.Cursor = Cursors.Hand;
            _mascotPicture.Image = LoadMascotImage();

            Control mascotDisplay = _mascotPicture;
            var videoPath = FindMascotVideoPath();
            if (!string.IsNullOrEmpty(videoPath))
            {
                try
                {
                    _mascotMedia = new System.Windows.Controls.MediaElement
                    {
                        LoadedBehavior = System.Windows.Controls.MediaState.Manual,
                        UnloadedBehavior = System.Windows.Controls.MediaState.Manual,
                        IsMuted = true,
                        Stretch = System.Windows.Media.Stretch.UniformToFill,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    _mascotMedia.Source = new Uri(videoPath, UriKind.Absolute);
                    _mascotLoopTimer.Interval = 5000;
                    _mascotLoopTimer.Tick += (s, e) =>
                    {
                        _mascotLoopTimer.Stop();
                        if (_mascotMedia != null && !_isExplicitExit && Visible)
                        {
                            _mascotMedia.Position = TimeSpan.Zero;
                            _mascotMedia.Play();
                        }
                    };
                    _mascotMedia.MediaEnded += (s, e) =>
                    {
                        _mascotLoopTimer.Stop();
                        _mascotLoopTimer.Start();
                    };
                    _mascotMedia.MouseLeftButtonUp += (s, e) => CycleMascotQuote();

                    // ponytail: Wrap in ClipToBounds Grid with VerticalAlignment.Center so the character stays centered when height is lower.
                    var mascotGrid = new System.Windows.Controls.Grid
                    {
                        ClipToBounds = true
                    };
                    _mascotMedia.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    _mascotMedia.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                    mascotGrid.Children.Add(_mascotMedia);
                    mascotGrid.MouseLeftButtonUp += (s, e) => CycleMascotQuote();

                    void UpdateMascotLayout()
                    {
                        var w = mascotGrid.ActualWidth;
                        var h = mascotGrid.ActualHeight;
                        if (w <= 0 || h <= 0) return;
                        const double videoAspect = 1280.0 / 720.0;
                        var containerAspect = h / w;
                        if (containerAspect < videoAspect)
                        {
                            _mascotMedia.Width = w;
                            _mascotMedia.Height = w * videoAspect;
                        }
                        else
                        {
                            _mascotMedia.Height = h;
                            _mascotMedia.Width = h / videoAspect;
                        }
                    }

                    mascotGrid.SizeChanged += (s, e) => UpdateMascotLayout();
                    _mascotMedia.MediaOpened += (s, e) => UpdateMascotLayout();

                    var host = new System.Windows.Forms.Integration.ElementHost
                    {
                        Dock = DockStyle.Fill,
                        Child = mascotGrid,
                        BackColor = Color.FromArgb(240, 246, 254)
                    };
                    ApplyRoundedRegion(host, 12, Color.FromArgb(226, 232, 240));
                    _mascotMedia.Play();
                    mascotDisplay = host;
                }
                catch
                {
                    _mascotMedia = null;
                }
            }

            var tip = new ToolTip();
            tip.SetToolTip(_mascotPicture, "點我互動！(Click me)");
            if (mascotDisplay != _mascotPicture)
                tip.SetToolTip(mascotDisplay, "點我互動！(Click me)");
            tip.SetToolTip(_speechBubble, "點我互動！(Click me)");

            mascotColumn.Controls.Add(_speechBubble, 0, 0);
            mascotColumn.Controls.Add(mascotDisplay, 0, 1);

            mainLayout.Controls.Add(mascotColumn, 1, 0);
            page.Controls.Add(mainLayout, 0, 1);

            // 3. Bottom Status Bar (Status indicator + message + timestamp)
            var statusBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 6, 0, 0)
            };
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _statusDot.Text = "●";
            _statusDot.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _statusDot.ForeColor = Color.FromArgb(34, 197, 94); // Vibrant Green
            _statusDot.BackColor = Color.Transparent;
            _statusDot.AutoSize = true;
            _statusDot.Anchor = AnchorStyles.Left;

            _status.Text = "Profile loaded";
            _status.Font = new Font("Segoe UI", 8.8F, FontStyle.Regular);
            _status.ForeColor = Color.FromArgb(71, 85, 105);
            _status.BackColor = Color.Transparent;
            _status.AutoSize = true;
            _status.Anchor = AnchorStyles.Left;

            _statusTimestamp.Text = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
            _statusTimestamp.Font = new Font("Segoe UI", 8.8F, FontStyle.Regular);
            _statusTimestamp.ForeColor = Color.FromArgb(148, 163, 184);
            _statusTimestamp.BackColor = Color.Transparent;
            _statusTimestamp.AutoSize = true;
            _statusTimestamp.Anchor = AnchorStyles.Right;

            statusBar.Controls.Add(_statusDot, 0, 0);
            statusBar.Controls.Add(_status, 1, 0);
            statusBar.Controls.Add(_statusTimestamp, 2, 0);
            page.Controls.Add(statusBar, 0, 2);

            Controls.Add(page);
            AcceptButton = _testAndMountButton;
        }

        private void WireEvents()
        {
            _profiles.SelectedIndexChanged += (sender, args) => LoadSelectedProfile();
            _authenticationMode.SelectedIndexChanged += (sender, args) =>
                UpdateAuthenticationControls();
            _newButton.Click += (sender, args) => NewProfile();
            _saveButton.Click += (sender, args) => ExecuteUi(SaveCurrentProfile);
            _deleteButton.Click += (sender, args) => ExecuteUi(DeleteCurrentProfile);
            _browseButton.Click += (sender, args) => BrowseForPrivateKey();
            _testAndMountButton.Click += async (sender, args) =>
                await RunBusyAsync(TestAndMountAsync);
            _testButton.Click += async (sender, args) =>
                await RunBusyAsync(TestAndTrustAsync);
            _mountButton.Click += async (sender, args) =>
                await RunBusyAsync(MountAsync);
            _installDriverButton.Click += async (sender, args) =>
                await RunBusyAsync(InstallDriverAsync);
            _unmountButton.Click += async (sender, args) =>
                await RunBusyAsync(UnmountAsync);
            _explorerButton.Click += (sender, args) => ExecuteUi(OpenExplorer);
            _mascotPicture.Click += (sender, args) => CycleMascotQuote();
            _speechBubble.Click += (sender, args) => CycleMascotQuote();
            _mascotSpeech.Click += (sender, args) => CycleMascotQuote();
            _mascotName.Click += (sender, args) => CycleMascotQuote();
            _checkUpdatesButton.Click += async (sender, args) =>
                await CheckForUpdatesAsync(manual: true);
            Shown += async (sender, args) =>
                await CheckForUpdatesAsync(manual: false);
            FormClosing += OnFormClosing;
            FormClosed += (sender, args) =>
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _mascotLoopTimer.Stop();
                _mascotLoopTimer.Dispose();
                _reconnectTimer.Stop();
                _reconnectTimer.Dispose();
                _mascotMedia?.Close();
                DisposeMountedDrives();
            };
            UpdateAuthenticationControls();

            _reconnectTimer.Interval = 10000;
            _reconnectTimer.Tick += async (s, e) => await CheckAndReconnectDrivesAsync();
            _reconnectTimer.Start();
        }

        private void LoadProfiles()
        {
            _profileItems = _profileStore.Load()
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            RefreshProfileSelector(null);

            if (_profileItems.Count == 0)
                NewProfile();
            else
                _profiles.SelectedIndex = 0;
        }

        private void RefreshProfileSelector(string selectName)
        {
            _profiles.BeginUpdate();
            try
            {
                _profiles.Items.Clear();
                foreach (var profile in _profileItems)
                    _profiles.Items.Add(profile.Name);

                if (!string.IsNullOrWhiteSpace(selectName))
                {
                    var index = _profiles.FindStringExact(selectName);
                    if (index >= 0)
                        _profiles.SelectedIndex = index;
                }
            }
            finally
            {
                _profiles.EndUpdate();
            }
        }

        private void LoadSelectedProfile()
        {
            if (_profiles.SelectedItem == null)
                return;

            var selectedName = _profiles.SelectedItem.ToString();
            var profile = _profileItems.FirstOrDefault(item =>
                string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
                return;

            _selectedProfileName = profile.Name;
            WriteForm(profile);
            var mounted = IsMounted(profile.DriveLetter);
            SetStatus(mounted
                ? "Mounted at " + profile.DriveLetter
                : "Profile loaded");
            SetMascotSpeech(mounted
                ? $"「{profile.Name}」已掛載於 {profile.DriveLetter}！隨時可用 Antigravity 進行開發～✨"
                : $"已切換至「{profile.Name}」設定檔！點擊 Mount 即可掛載到 {profile.DriveLetter} 喔～");
        }

        private void NewProfile()
        {
            _selectedProfileName = null;
            _profiles.SelectedIndex = -1;
            WriteForm(new DriveProfile());
            SetStatus("New profile");
            SetMascotSpeech("已建立新的設定檔草稿，填寫完成後點擊 Save 即可保存！");
            _name.Focus();
        }

        private void SaveCurrentProfile()
        {
            var profile = ReadForm();
            var errors = DriveProfileValidator.Validate(profile);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            UpsertProfile(profile);
            SetStatus("Profile saved");
            SetMascotSpeech($"設定檔「{profile.Name}」已成功保存！✨");
        }

        private void DeleteCurrentProfile()
        {
            if (string.IsNullOrWhiteSpace(_selectedProfileName))
                return;

            var profile = _profileItems.FirstOrDefault(item =>
                string.Equals(item.Name, _selectedProfileName, StringComparison.OrdinalIgnoreCase));
            if (profile != null && IsMounted(profile.DriveLetter))
                throw new InvalidOperationException("Unmount this profile before deleting it.");

            _profileItems.RemoveAll(item =>
                string.Equals(item.Name, _selectedProfileName, StringComparison.OrdinalIgnoreCase));
            _profileStore.Save(_profileItems);
            RefreshProfileSelector(null);
            NewProfile();
            SetMascotSpeech("設定檔已刪除完畢。");
        }

        private async Task TestAndTrustAsync()
        {
            var profile = ReadForm();
            ValidateForProbe(profile);
            SetStatus("Connecting and reading the SSH host key…");
            SetMascotSpeech("正在連線測試並讀取 SSH 主機金鑰中，請稍候片刻…🔍");

            var result = await Task.Run(() => _connectionProbe.Probe(profile));
            profile.HostKeyFingerprintSha256 = result.HostKeyFingerprintSha256;
            _hostFingerprint.Text = result.HostKeyFingerprintSha256;
            UpsertProfile(profile);
            SetStatus("Trusted " + result.HostKeyFingerprintSha256);
            SetMascotSpeech("SSH 連線測試成功！主機指紋已安全記錄～✨");
        }

        private async Task TestAndMountAsync()
        {
            await TestAndTrustAsync();
            await MountAsync();
        }

        private async Task MountAsync()
        {
            var profile = ReadForm();
            var errors = DriveProfileValidator.Validate(profile);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            var runtime = WinFspRuntimePreflight.CheckX64();
            if (!runtime.IsValid)
            {
                RefreshDriverStatus();
                throw new InvalidOperationException(runtime.Error);
            }

            var drive = profile.DriveLetter.ToUpperInvariant();
            if (_mountedDrives.ContainsKey(drive))
                throw new InvalidOperationException(drive + " is already mounted by 3waSshDrive.");
            if (GetMountedLogicalDrives().Contains(drive))
                throw new InvalidOperationException($"磁碟機代號 {drive} 已被 Windows 系統或其他裝置使用 (已掛載)，請選擇其他槽位。");

            SetStatus("Mounting " + profile.Name + " at " + drive + "…");
            SetMascotSpeech($"正在將遠端 Linux 掛載至 {drive} 槽…連線中 ⏳");
            var mounted = await Task.Run(() => _mountManager.Mount(profile));
            _mountedDrives.Add(drive, mounted);
            RefreshDriveLetters();
            UpsertProfile(profile);
            SetStatus("Mounted " + profile.Name + " at " + drive);
            SetMascotSpeech($"已成功掛載到 {drive} 槽！Antigravity 開發全速啟動～(๑•̀ㅂ•́)و✧");
        }

        private async Task InstallDriverAsync()
        {
            SetStatus("正在透過 winget 安裝 WinFsp 驅動，請於跳出的管理員提權視窗點選「是」…");
            SetMascotSpeech("正在安裝 WinFsp 驅動中，請於系統提權視窗點選「是」喔～📦");
            await Task.Run(() => WinFspInstaller.InstallAsync());
            RefreshDriverStatus();
            SetStatus("WinFsp 驅動安裝完成且驗證通過，已可正常掛載。");
            SetMascotSpeech("WinFsp 驅動已安裝妥當！現在可以掛載磁碟機了～✨");
        }

        private bool RefreshDriverStatus()
        {
            var runtime = WinFspRuntimePreflight.CheckX64();
            if (runtime.IsValid)
            {
                _installDriverButton.Visible = false;
                _mountButton.Enabled = !_busy && !_isApplyingUpdate;
                return true;
            }

            _installDriverButton.Visible = true;
            _mountButton.Enabled = false;
            SetStatus("WinFsp 未安裝或校驗失敗: " + runtime.Error);
            return false;
        }

        private async Task UnmountAsync()
        {
            var drive = SelectedDriveLetter();
            if (!_mountedDrives.TryGetValue(drive, out var mounted))
                throw new InvalidOperationException(drive + " is not mounted by 3waSshDrive.");

            SetStatus("Unmounting " + drive + "…");
            SetMascotSpeech($"正在卸載磁碟機 {drive}…⏳");
            await Task.Run(() => mounted.Dispose());
            _mountedDrives.Remove(drive);
            RefreshDriveLetters();
            SetStatus("Unmounted " + drive);
            SetMascotSpeech($"磁碟機 {drive} 已卸載，辛苦啦～隨時點我重新掛載喔！☕");
        }

        private async Task CheckAndReconnectDrivesAsync()
        {
            if (_isReconnecting || _busy || _isApplyingUpdate ||
                _mountedDrives.Count == 0)
                return;

            var disconnected = _mountedDrives.Values.Where(d => !d.IsConnected).ToList();
            if (disconnected.Count == 0)
                return;

            _isReconnecting = true;
            try
            {
                foreach (var drive in disconnected)
                {
                    SetStatus($"磁碟機 {drive.DriveLetter} 連線中斷，正在自動重新連線…", false);
                    SetMascotSpeech($"偵測到 {drive.DriveLetter} 槽連線中斷，芳寶正在重新連線中…⏳");

                    try
                    {
                        await Task.Run(() => drive.EnsureConnected());
                        SetStatus($"Mounted at {drive.DriveLetter}", true);
                        SetMascotSpeech($"已成功自動重新連線至 {drive.DriveLetter} 槽！繼續工作吧～✨");
                        RefreshDriveLetters();

                        if (!Visible)
                        {
                            _notifyIcon.ShowBalloonTip(
                                3000,
                                "3waSshDrive - 自動重新連線",
                                $"磁碟機 {drive.DriveLetter} 已成功自動重新連線！",
                                ToolTipIcon.Info);
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"磁碟機 {drive.DriveLetter} 重新連線失敗，等待下次重試…", false);
                        CrashLogger.Log("AutoReconnect", ex);
                    }
                }
            }
            finally
            {
                _isReconnecting = false;
            }
        }

        private void OpenExplorer()
        {
            var drive = SelectedDriveLetter();
            if (!IsMounted(drive))
                throw new InvalidOperationException(drive + " is not mounted by 3waSshDrive.");

            Process.Start(new ProcessStartInfo
            {
                FileName = drive + @"\",
                UseShellExecute = true
            });
        }

        private void BrowseForPrivateKey()
        {
            using (var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = "SSH private keys|id_*;*.pem;*.key|All files|*.*",
                Title = "Select SSH private key"
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _privateKeyPath.Text = dialog.FileName;
            }
        }

        private void UpsertProfile(DriveProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(_selectedProfileName))
            {
                _profileItems.RemoveAll(item =>
                    string.Equals(item.Name, _selectedProfileName, StringComparison.OrdinalIgnoreCase));
            }

            _profileItems.RemoveAll(item =>
                string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            _profileItems.Add(profile);
            _profileItems = _profileItems
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _profileStore.Save(_profileItems);
            _selectedProfileName = profile.Name;
            RefreshProfileSelector(profile.Name);
        }

        private DriveProfile ReadForm()
        {
            return new DriveProfile
            {
                Name = _name.Text.Trim(),
                Host = _host.Text.Trim(),
                Port = decimal.ToInt32(_port.Value),
                Username = _username.Text.Trim(),
                RemoteRoot = _remoteRoot.Text.Trim(),
                DriveLetter = SelectedDriveLetter(),
                AuthenticationMode = SelectedAuthenticationMode(),
                PrivateKeyPath = _privateKeyPath.Text.Trim(),
                Password = SelectedAuthenticationMode() == AuthenticationMode.Password
                    ? _password.Text
                    : null,
                HostKeyFingerprintSha256 = _hostFingerprint.Text.Trim(),
                ReadOnly = _readOnly.Checked
            };
        }

        private void WriteForm(DriveProfile profile)
        {
            _name.Text = profile.Name ?? string.Empty;
            _host.Text = profile.Host ?? string.Empty;
            _port.Value = profile.Port >= 1 && profile.Port <= 65535
                ? profile.Port
                : 22;
            _username.Text = profile.Username ?? string.Empty;
            _remoteRoot.Text = string.IsNullOrWhiteSpace(profile.RemoteRoot)
                ? "/"
                : profile.RemoteRoot;
            SelectDriveLetter(profile.DriveLetter);
            _authenticationMode.SelectedIndex =
                profile.AuthenticationMode == AuthenticationMode.Password ? 1 : 0;
            _privateKeyPath.Text = profile.PrivateKeyPath ?? string.Empty;
            _password.Text = profile.Password ?? string.Empty;
            _hostFingerprint.Text = profile.HostKeyFingerprintSha256 ?? string.Empty;
            _readOnly.Checked = profile.ReadOnly;
            UpdateAuthenticationControls();
        }

        private static void ValidateForProbe(DriveProfile profile)
        {
            var fingerprint = profile.HostKeyFingerprintSha256;
            profile.HostKeyFingerprintSha256 = "SHA256:pending";
            var errors = DriveProfileValidator.Validate(profile);
            profile.HostKeyFingerprintSha256 = fingerprint;
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        private async Task RunBusyAsync(Func<Task> operation)
        {
            if (_busy || _isApplyingUpdate)
                return;

            SetBusy(true);
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                ShowError(exception);
                SetMascotSpeech("嗚哇！操作好像遇到問題了，請檢查設定或網路喔＞＜");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ExecuteUi(Action operation)
        {
            try
            {
                operation();
            }
            catch (Exception exception)
            {
                ShowError(exception);
                SetMascotSpeech("執行操作時發生錯誤，請檢查輸入內容喔～＞＜");
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            var actionsDisabled = busy || _isApplyingUpdate;
            UseWaitCursor = actionsDisabled;
            _profiles.Enabled = !actionsDisabled;
            _newButton.Enabled = !actionsDisabled;
            _testButton.Enabled = !actionsDisabled;
            _unmountButton.Enabled = !actionsDisabled;
            _saveButton.Enabled = !actionsDisabled;
            _deleteButton.Enabled = !actionsDisabled;
            _explorerButton.Enabled = !actionsDisabled;
            _authenticationMode.Enabled = !actionsDisabled;
            _installDriverButton.Enabled = !actionsDisabled;
            _checkUpdatesButton.Enabled =
                !actionsDisabled && !_isCheckingUpdates;
            _readOnly.Enabled = !actionsDisabled;

            var runtime = WinFspRuntimePreflight.CheckX64();
            _testAndMountButton.Enabled = !actionsDisabled && runtime.IsValid;
            _mountButton.Enabled = !actionsDisabled && runtime.IsValid;
            _installDriverButton.Visible = !runtime.IsValid;

            UpdateAuthenticationControls();
        }

        private void ShowError(Exception exception)
        {
            SetStatus("Error: " + exception.Message, false);
            MessageBox.Show(
                this,
                exception.Message,
                "3waSshDrive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private void SetStatus(string message, bool? isSuccess = null)
        {
            _status.Text = message;
            _statusTimestamp.Text = DateTime.Now.ToString("yyyy/MM/dd HH:mm");

            if (isSuccess.HasValue)
            {
                _statusDot.ForeColor = isSuccess.Value
                    ? Color.FromArgb(34, 197, 94)  // Vibrant green
                    : Color.FromArgb(239, 68, 68);  // Vibrant red
            }
            else
            {
                var lower = message.ToLowerInvariant();
                if (lower.Contains("error") || lower.Contains("失敗"))
                    _statusDot.ForeColor = Color.FromArgb(239, 68, 68);
                else if (lower.Contains("mounted") || lower.Contains("trusted") || lower.Contains("ready") || lower.Contains("loaded") || lower.Contains("saved"))
                    _statusDot.ForeColor = Color.FromArgb(34, 197, 94);
                else
                    _statusDot.ForeColor = Color.FromArgb(59, 130, 246);
            }
        }

        private string SelectedDriveLetter()
        {
            var text = _driveLetter.SelectedItem?.ToString() ?? "Z:";
            if (text.Length >= 2 && text[1] == ':')
                return text.Substring(0, 2).ToUpperInvariant();
            return "Z:";
        }

        private void SelectDriveLetter(string driveLetter)
        {
            var target = (string.IsNullOrWhiteSpace(driveLetter) ? "Z:" : driveLetter).Trim().ToUpperInvariant();
            if (!target.EndsWith(":"))
                target += ":";

            for (var i = 0; i < _driveLetter.Items.Count; i++)
            {
                var itemText = _driveLetter.Items[i]?.ToString() ?? string.Empty;
                if (itemText.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                {
                    _driveLetter.SelectedIndex = i;
                    return;
                }
            }

            if (_driveLetter.Items.Count > 0 && _driveLetter.SelectedIndex < 0)
                _driveLetter.SelectedIndex = _driveLetter.Items.Count - 1;
        }

        private void RefreshDriveLetters()
        {
            var selected = SelectedDriveLetter();
            var mounted = GetMountedLogicalDrives();
            foreach (var key in _mountedDrives.Keys)
                mounted.Add(key.ToUpperInvariant());

            _driveLetter.BeginUpdate();
            try
            {
                _driveLetter.Items.Clear();
                for (var letter = 'D'; letter <= 'Z'; letter++)
                {
                    var driveStr = letter + ":";
                    var isMounted = mounted.Contains(driveStr);
                    _driveLetter.Items.Add(isMounted ? $"{driveStr} (已掛載)" : driveStr);
                }
                SelectDriveLetter(selected);
            }
            finally
            {
                _driveLetter.EndUpdate();
            }
        }

        private static HashSet<string> GetMountedLogicalDrives()
        {
            // ponytail: stdlib Environment.GetLogicalDrives checks all active system/network/virtual volumes.
            var mounted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var drive in Environment.GetLogicalDrives())
                {
                    var trimmed = drive.TrimEnd('\\').ToUpperInvariant();
                    if (!string.IsNullOrEmpty(trimmed))
                        mounted.Add(trimmed);
                }
            }
            catch
            {
            }
            return mounted;
        }

        private AuthenticationMode SelectedAuthenticationMode()
        {
            return _authenticationMode.SelectedIndex == 1
                ? AuthenticationMode.Password
                : AuthenticationMode.PrivateKey;
        }

        private void UpdateAuthenticationControls()
        {
            var usingPrivateKey =
                SelectedAuthenticationMode() == AuthenticationMode.PrivateKey;
            var actionsEnabled = !_busy && !_isApplyingUpdate;
            _privateKeyPath.Enabled = usingPrivateKey && actionsEnabled;
            _browseButton.Enabled = usingPrivateKey && actionsEnabled;
            _password.Enabled = !usingPrivateKey && actionsEnabled;
        }

        private bool IsMounted(string driveLetter)
        {
            return !string.IsNullOrWhiteSpace(driveLetter) &&
                   _mountedDrives.ContainsKey(driveLetter.ToUpperInvariant());
        }

        private void DisposeMountedDrives()
        {
            foreach (var mounted in _mountedDrives.Values.ToList())
            {
                try
                {
                    mounted.Dispose();
                }
                catch
                {
                    // The application is closing; continue releasing the remaining mounts.
                }
            }

            _mountedDrives.Clear();
            RefreshDriveLetters();
        }

        private void SetupTrayIcon()
        {
            _notifyIcon.Text = "3waSshDrive";
            _notifyIcon.Icon = Icon ?? SystemIcons.Application;
            _notifyIcon.Visible = true;

            var menu = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem("展開視窗");
            showItem.Click += (sender, args) => RestoreFromTray();

            var updateItem = new ToolStripMenuItem("檢查更新");
            updateItem.Click += async (sender, args) =>
            {
                RestoreFromTray();
                await CheckForUpdatesAsync(manual: true);
            };

            var quitItem = new ToolStripMenuItem("離開 (QUIT)");
            quitItem.Click += (sender, args) => ExitApplication();

            menu.Items.Add(showItem);
            menu.Items.Add(updateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quitItem);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (sender, args) => RestoreFromTray();
            _notifyIcon.BalloonTipClicked += (sender, args) => RestoreFromTray();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isExplicitExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _mascotLoopTimer.Stop();
                _mascotMedia?.Pause();
                Hide();
                _notifyIcon.ShowBalloonTip(
                    1500,
                    "3waSshDrive",
                    "已縮小至系統匣常駐，掛載中的磁碟將保持連線。",
                    ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            if (_mascotMedia != null)
            {
                _mascotLoopTimer.Stop();
                _mascotMedia.Play();
            }
        }

        private void ExitApplication()
        {
            _isExplicitExit = true;
            Close();
        }

        private static void ConfigureComboBox(ComboBox comboBox)
        {
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Dock = DockStyle.Fill;
        }

        private static void AddRow(
            TableLayoutPanel table,
            int row,
            string iconText,
            string labelText,
            Control field,
            Control action)
        {
            var iconLabel = new Label
            {
                AutoSize = true,
                Text = iconText,
                ForeColor = Color.FromArgb(59, 130, 246),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 4, 4, 4)
            };

            var label = new Label
            {
                AutoSize = true,
                Text = labelText,
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI", 9.2F, FontStyle.Regular),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 8, 4)
            };

            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(0, 2, 8, 2);

            table.Controls.Add(iconLabel, 0, row);
            table.Controls.Add(label, 1, row);
            table.Controls.Add(field, 2, row);
            if (action != null)
            {
                action.AutoSize = true;
                action.Margin = new Padding(0, 1, 0, 1);
                table.Controls.Add(action, 3, row);
            }
        }

        private void SetMascotSpeech(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetMascotSpeech(text)));
                return;
            }
            _mascotSpeech.Text = text;
        }

        private void CycleMascotQuote()
        {
            _mascotQuoteIndex = (_mascotQuoteIndex + 1) % MascotQuotes.Length;
            SetMascotSpeech(MascotQuotes[_mascotQuoteIndex]);
        }

        private void ShowAboutDialog()
        {
            var aboutText =
                $"3waSshDrive\n" +
                $"──────────────────────────────\n\n" +
                $"• 版本：{Application.ProductVersion}\n" +
                $"• 作者 (Author)：羽山秋人\n" +
                $"• 團隊：3WA 問題解決專家工作室\n" +
                $"• 信箱：linainverseshadow@gmail.com\n" +
                $"• 原始碼：https://github.com/shadowjohn/3waSshDrive\n" +
                $"• License：GPLv3\n\n" +
                $"• 專案說明：\n" +
                $"  透過 WinFsp 與 SFTP 將遠端 Linux 工作空間掛載為 Windows 磁碟機。\n" +
                $"  支援高速讀寫、Metadata 快取、主機指紋校驗與背景常駐。\n\n" +
                $"• 看板娘：芳寶 (Fang-Fang)\n" +
                $"  Linux × Windows = More Freedom. 💙";

            MessageBox.Show(
                this,
                aboutText,
                "關於 3waSshDrive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static string FindMascotVideoPath()
        {
            var fileNames = new[] { "mascot2.mp4", "mascot.mp4" };
            var baseDirs = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"),
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(MainForm).Assembly.Location) ?? "", "Assets"),
                @"D:\mytools\3waSshDrive\src\3waSshDrive.App\Assets"
            };

            foreach (var name in fileNames)
            {
                foreach (var dir in baseDirs)
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    var path = System.IO.Path.Combine(dir, name);
                    if (System.IO.File.Exists(path))
                        return path;
                }
            }

            return null;
        }

        private static Image LoadMascotImage()
        {
            try
            {
                var assembly = typeof(MainForm).Assembly;
                using (var stream = assembly.GetManifestResourceStream("ThreeWa.SshDrive.App.Assets.mascot_card.png"))
                {
                    if (stream != null)
                        return Image.FromStream(stream);
                }
            }
            catch
            {
            }

            var cardLocalPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "mascot_card.png");
            if (System.IO.File.Exists(cardLocalPath))
            {
                try
                {
                    return Image.FromFile(cardLocalPath);
                }
                catch
                {
                }
            }

            try
            {
                var assembly = typeof(MainForm).Assembly;
                using (var stream = assembly.GetManifestResourceStream("ThreeWa.SshDrive.App.Assets.mascot.jpg"))
                {
                    if (stream != null)
                        return Image.FromStream(stream);
                }
            }
            catch
            {
            }

            var localPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "mascot.jpg");
            if (System.IO.File.Exists(localPath))
            {
                try
                {
                    return Image.FromFile(localPath);
                }
                catch
                {
                }
            }

            return null;
        }

        private static Image LoadHeaderLogo()
        {
            try
            {
                var assembly = typeof(MainForm).Assembly;
                using (var stream = assembly.GetManifestResourceStream("ThreeWa.SshDrive.App.Assets.header_logo.png"))
                {
                    if (stream != null)
                        return Image.FromStream(stream);
                }
            }
            catch
            {
            }

            var localPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "header_logo.png");
            if (System.IO.File.Exists(localPath))
            {
                try
                {
                    return Image.FromFile(localPath);
                }
                catch
                {
                }
            }

            return null;
        }

        private static Image LoadBackgroundImage()
        {
            var extensions = new[] { "jpg", "png" };
            var assembly = typeof(MainForm).Assembly;

            foreach (var ext in extensions)
            {
                try
                {
                    using (var stream = assembly.GetManifestResourceStream($"ThreeWa.SshDrive.App.Assets.background.{ext}"))
                    {
                        if (stream != null)
                            return Image.FromStream(stream);
                    }
                }
                catch
                {
                }

                var localPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", $"background.{ext}");
                if (System.IO.File.Exists(localPath))
                {
                    try
                    {
                        return Image.FromFile(localPath);
                    }
                    catch
                    {
                    }
                }

                var devPath = System.IO.Path.Combine(@"D:\mytools\3waSshDrive\src\3waSshDrive.App\Assets", $"background.{ext}");
                if (System.IO.File.Exists(devPath))
                {
                    try
                    {
                        return Image.FromFile(devPath);
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                var assembly = typeof(MainForm).Assembly;
                using (var stream = assembly.GetManifestResourceStream("ThreeWa.SshDrive.App.Assets.app.ico"))
                {
                    if (stream != null)
                        return new Icon(stream);
                }
            }
            catch
            {
            }

            var localPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(localPath))
            {
                try
                {
                    return new Icon(localPath);
                }
                catch
                {
                }
            }

            return null;
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int diameter = radius * 2;
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void ApplyRoundedRegion(Control control, int radius, Color borderColor = default(Color))
        {
            void Update()
            {
                if (control.Width <= 0 || control.Height <= 0) return;
                using (var path = CreateRoundedRectanglePath(new Rectangle(0, 0, control.Width, control.Height), radius))
                {
                    control.Region = new Region(path);
                }
            }

            control.SizeChanged += (s, e) => Update();
            if (control.IsHandleCreated)
                Update();
            else
                control.HandleCreated += (s, e) => Update();

            if (borderColor != Color.Empty && borderColor != Color.Transparent)
            {
                control.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = CreateRoundedRectanglePath(new Rectangle(0, 0, control.Width - 1, control.Height - 1), radius))
                    using (var pen = new Pen(borderColor, 1.2f))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                };
            }
        }
    }
}
