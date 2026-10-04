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
        private readonly ISshConnectionProbe _connectionProbe;
        private readonly MountManager _mountManager;
        private readonly Func<HashSet<string>> _getLogicalDrives;
        private readonly Action<string, Exception> _logOperationFailure;
        private readonly UpdateService _updateService;
        private readonly UpdateCoordinator _updateCoordinator;
        private readonly UpdateActivityGate _updateActivityGate =
            new UpdateActivityGate();
        private readonly Dictionary<string, MountedDrive> _mountedDrives =
            new Dictionary<string, MountedDrive>(StringComparer.OrdinalIgnoreCase);

        private readonly ComboBox _profiles = new ComboBox();
        private readonly DataGridView _profileGrid = new DataGridView();
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _host = new TextBox();
        private readonly NumericUpDown _port = new NumericUpDown();
        private readonly TextBox _username = new TextBox();
        private readonly TextBox _remoteRoot = new TextBox();
        private readonly ComboBox _driveLetter = new ComboBox();
        private readonly ComboBox _authenticationMode = new ComboBox();
        private readonly TextBox _privateKeyPath = new TextBox();
        private readonly TextBox _password = new TextBox();
        private readonly Label _authCredIconLabel = new Label();
        private readonly Label _authCredTextLabel = new Label();
        private readonly TextBox _hostFingerprint = new TextBox();
        private readonly CheckBox _readOnly = new CheckBox();
        private readonly CheckBox _autoMountOnStartup = new CheckBox();
        private readonly Label _status = new Label();
        private readonly Button _newButton = new Button();
        private readonly Button _saveButton = new Button();
        private readonly Button _deleteButton = new Button();
        private readonly Button _mountAllButton = new Button();
        private readonly Button _unmountAllButton = new Button();
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
        private IDisposable _updatePreparationLease;

        private List<DriveProfile> _profileItems = new List<DriveProfile>();
        private string _selectedProfileName;
        private bool _isSynchronizingProfileGrid;
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
            ISshConnectionProbe connectionProbe,
            MountManager mountManager,
            UpdateService updateService,
            Func<HashSet<string>> getLogicalDrives = null,
            Action<string, Exception> logOperationFailure = null)
        {
            _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
            _connectionProbe = connectionProbe ?? throw new ArgumentNullException(nameof(connectionProbe));
            _mountManager = mountManager ?? throw new ArgumentNullException(nameof(mountManager));
            _getLogicalDrives = getLogicalDrives ?? GetMountedLogicalDrives;
            _logOperationFailure = logOperationFailure ?? CrashLogger.Log;
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _updateCoordinator = new UpdateCoordinator(this);

            Text = $"3waSshDrive - {Application.ProductVersion}";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1080, 750);
            Size = new Size(1100, 760);
            BackColor = Color.FromArgb(240, 246, 254);
            Font = new Font("Segoe UI", 9.2F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;

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
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
                AutoSize = true,
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

            _installDriverButton.Text = "⚙ 安裝驅動";
            _installDriverButton.AutoSize = true;
            _installDriverButton.FlatStyle = FlatStyle.Flat;
            _installDriverButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            _installDriverButton.BackColor = Color.FromArgb(241, 245, 249);
            _installDriverButton.ForeColor = Color.FromArgb(71, 85, 105);
            _installDriverButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _installDriverButton.Cursor = Cursors.Hand;
            _installDriverButton.Height = 32;
            _installDriverButton.Padding = new Padding(10, 2, 10, 2);
            _installDriverButton.Margin = new Padding(8, 6, 0, 0);
            _installDriverButton.Visible = false;
            ApplyRoundedRegion(_installDriverButton, 6);

            rightHeader.Controls.Add(aboutButton);
            rightHeader.Controls.Add(_installDriverButton);
            rightHeader.Controls.Add(_checkUpdatesButton);
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
                RowCount = 4
            };
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

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

            leftLayout.Controls.Add(BuildProfileFleet(), 0, 1);

            // Form Fields Table
            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 10,
                Margin = new Padding(0, 2, 0, 2)
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var r = 0; r < 10; r++)
            {
                fields.RowStyles.Add(new RowStyle(SizeType.Percent, 10f));
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
            _readOnly.Text = "唯讀";
            _readOnly.AutoSize = true;
            _readOnly.Anchor = AnchorStyles.Left;
            _autoMountOnStartup.Text = "自動掛載";
            _autoMountOnStartup.AutoSize = true;
            _autoMountOnStartup.Anchor = AnchorStyles.Left;

            var options = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            options.Controls.Add(_readOnly);
            options.Controls.Add(_autoMountOnStartup);

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
            _cancelOperationButton.Text = "取消連線";
            _cancelOperationButton.AutoSize = true;
            _cancelOperationButton.Click += (sender, args) => CancelSelectedOperation();
            profileButtons.Controls.Add(_cancelOperationButton);

            AddRow(fields, 0, "❯", "Profile", _profiles, profileButtons);
            AddRow(fields, 1, "👤", "Name", _name, null);
            AddRow(fields, 2, "🌐", "Host", _host, null);
            AddRow(fields, 3, "🖧", "Port", _port, null);
            AddRow(fields, 4, "👤", "Username", _username, null);
            AddRow(fields, 5, "📁", "Remote root", _remoteRoot, null);
            AddRow(fields, 6, "💽", "Drive letter", _driveLetter, null);
            AddRow(fields, 7, "🛡", "Authentication", _authenticationMode, null);
            AddCredentialRow(fields, 8);
            AddRow(fields, 9, "⚙", "Options", options, null);
            leftLayout.Controls.Add(fields, 0, 2);

            // Action Buttons Bar
            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 0)
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31)); // Test & Mount
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23)); // Mount
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23)); // Unmount
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23)); // Open Explorer
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // 1. Test & Mount (Primary highlighted blue card)
            _testAndMountButton.Text = "▶  Test & Mount\n    測試並掛載";
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

            actions.Controls.Add(_testAndMountButton, 0, 0);
            actions.Controls.Add(_mountButton, 1, 0);
            actions.Controls.Add(_unmountButton, 2, 0);
            actions.Controls.Add(_explorerButton, 3, 0);
            leftLayout.Controls.Add(actions, 0, 3);

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

                    // ponytail: Wrap in hardware-accelerated WPF Border with CornerRadius to eliminate GDI Region clipping lag.
                    var mascotGrid = new System.Windows.Controls.Grid
                    {
                        ClipToBounds = true
                    };
                    _mascotMedia.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    _mascotMedia.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                    _mascotMedia.ScrubbingEnabled = false;
                    mascotGrid.Children.Add(_mascotMedia);
                    mascotGrid.MouseLeftButtonUp += (s, e) => CycleMascotQuote();

                    var mascotBorder = new System.Windows.Controls.Border
                    {
                        CornerRadius = new System.Windows.CornerRadius(12),
                        ClipToBounds = true,
                        BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240)),
                        BorderThickness = new System.Windows.Thickness(1),
                        Child = mascotGrid
                    };

                    var host = new System.Windows.Forms.Integration.ElementHost
                    {
                        Dock = DockStyle.Fill,
                        Child = mascotBorder,
                        BackColor = Color.FromArgb(240, 246, 254)
                    };
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
            tip.SetToolTip(_readOnly, "Read-only (唯讀模式)");
            tip.SetToolTip(
                _autoMountOnStartup,
                "Auto-mount on startup (啟動後自動掛載)");

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

        private Control BuildProfileFleet()
        {
            var fleet = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0, 0, 0, 4),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            fleet.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fleet.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fleet.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ApplyRoundedRegion(fleet, 8, Color.FromArgb(226, 232, 240));

            var header = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(8, 4, 8, 0),
                Margin = new Padding(0)
            };
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "▦ Profile fleet",
                Font = new Font("Segoe UI", 9.2F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Margin = new Padding(0, 0, 8, 0)
            });
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "選取一列即可編輯；各 Profile 可同時掛載",
                Font = new Font("Segoe UI", 8.3F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Margin = new Padding(0, 2, 0, 0)
            });

            ConfigureProfileGrid();

            var bulkActions = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(8, 1, 8, 3),
                Margin = new Padding(0)
            };
            ConfigureFleetButton(
                _mountAllButton,
                "▶ Mount all",
                Color.FromArgb(219, 234, 254),
                Color.FromArgb(29, 78, 216));
            ConfigureFleetButton(
                _unmountAllButton,
                "⏏ Unmount all",
                Color.FromArgb(241, 245, 249),
                Color.FromArgb(71, 85, 105));
            bulkActions.Controls.Add(_mountAllButton);
            bulkActions.Controls.Add(_unmountAllButton);

            fleet.Controls.Add(header, 0, 0);
            fleet.Controls.Add(_profileGrid, 0, 1);
            fleet.Controls.Add(bulkActions, 0, 2);
            return fleet;
        }

        private void ConfigureProfileGrid()
        {
            _profileGrid.Dock = DockStyle.Fill;
            _profileGrid.Margin = new Padding(8, 2, 8, 0);
            _profileGrid.AllowUserToAddRows = false;
            _profileGrid.AllowUserToDeleteRows = false;
            _profileGrid.AllowUserToResizeRows = false;
            _profileGrid.AllowUserToOrderColumns = false;
            _profileGrid.AutoGenerateColumns = false;
            _profileGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _profileGrid.BackgroundColor = Color.White;
            _profileGrid.BorderStyle = BorderStyle.FixedSingle;
            _profileGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _profileGrid.ColumnHeadersBorderStyle =
                DataGridViewHeaderBorderStyle.Single;
            _profileGrid.ColumnHeadersHeightSizeMode =
                DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _profileGrid.EnableHeadersVisualStyles = false;
            _profileGrid.GridColor = Color.FromArgb(226, 232, 240);
            _profileGrid.MultiSelect = false;
            _profileGrid.ReadOnly = true;
            _profileGrid.RowHeadersVisible = false;
            _profileGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _profileGrid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(30, 41, 59),
                SelectionBackColor = Color.FromArgb(219, 234, 254),
                SelectionForeColor = Color.FromArgb(30, 64, 175)
            };
            _profileGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft
            };

            _profileGrid.Columns.Add(CreateProfileGridColumn("Name", "Name", 16, 80));
            _profileGrid.Columns.Add(CreateProfileGridColumn("Host", "Host", 18, 90));
            _profileGrid.Columns.Add(CreateProfileGridColumn("RemoteRoot", "Remote root", 26, 120));
            _profileGrid.Columns.Add(CreateProfileGridColumn("Drive", "Drive", 8, 48));
            _profileGrid.Columns.Add(CreateProfileGridColumn("Auth", "Auth", 12, 64));
            _profileGrid.Columns.Add(CreateProfileGridColumn("Startup", "Startup", 10, 55));
            _profileGrid.Columns.Add(CreateProfileGridColumn("Status", "Status", 12, 65));
        }

        private static DataGridViewTextBoxColumn CreateProfileGridColumn(
            string name,
            string headerText,
            float fillWeight,
            int minimumWidth)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = headerText,
                FillWeight = fillWeight,
                MinimumWidth = minimumWidth,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private static void ConfigureFleetButton(
            Button button,
            string text,
            Color backColor,
            Color foreColor)
        {
            button.Text = text;
            button.AutoSize = true;
            button.Height = 25;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0, 0, 6, 0);
            ApplyRoundedRegion(button, 5);
        }

        private void WireEvents()
        {
            _profiles.SelectedIndexChanged += (sender, args) => LoadSelectedProfile();
            _profileGrid.SelectionChanged += (sender, args) =>
                LoadProfileSelectedInGrid();
            _authenticationMode.SelectedIndexChanged += (sender, args) =>
            {
                UpdateAuthenticationControls();
                RefreshActionState();
            };
            _name.TextChanged += (sender, args) => RefreshActionState();
            _host.TextChanged += (sender, args) => RefreshActionState();
            _username.TextChanged += (sender, args) => RefreshActionState();
            _remoteRoot.TextChanged += (sender, args) => RefreshActionState();
            _privateKeyPath.TextChanged += (sender, args) => RefreshActionState();
            _password.TextChanged += (sender, args) => RefreshActionState();
            _autoMountOnStartup.CheckedChanged += (sender, args) => RefreshActionState();
            _driveLetter.SelectedIndexChanged += (sender, args) => RefreshActionState();
            _port.ValueChanged += (sender, args) => RefreshActionState();
            _newButton.Click += (sender, args) => NewProfile();
            _saveButton.Click += (sender, args) => ExecuteUi(SaveCurrentProfile);
            _deleteButton.Click += (sender, args) => ExecuteUi(DeleteCurrentProfile);
            _browseButton.Click += (sender, args) => BrowseForPrivateKey();
            _testAndMountButton.Click += async (sender, args) =>
                await TestAndMountAsync();
            _testButton.Click += async (sender, args) =>
                await TestAndTrustAsync();
            _mountButton.Click += async (sender, args) =>
                await MountAsync();
            _installDriverButton.Click += async (sender, args) =>
                await RunBusyAsync(InstallDriverAsync);
            _unmountButton.Click += async (sender, args) =>
                await UnmountAsync();
            _mountAllButton.Click += async (sender, args) =>
                await MountAllProfilesAsync();
            _unmountAllButton.Click += async (sender, args) =>
                await UnmountAllProfilesAsync();
            _explorerButton.Click += (sender, args) => ExecuteUi(OpenExplorer);
            _mascotPicture.Click += (sender, args) => CycleMascotQuote();
            _speechBubble.Click += (sender, args) => CycleMascotQuote();
            _mascotSpeech.Click += (sender, args) => CycleMascotQuote();
            _mascotName.Click += (sender, args) => CycleMascotQuote();
            _checkUpdatesButton.Click += async (sender, args) =>
                await CheckForUpdatesAsync(manual: true);
            Shown += async (sender, args) =>
            {
                await AutoMountProfilesOnStartupAsync();
                await CheckForUpdatesAsync(manual: false);
            };
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

            RefreshProfileGrid();
        }

        private void RefreshProfileGrid()
        {
            _isSynchronizingProfileGrid = true;
            try
            {
                _profileGrid.Rows.Clear();
                foreach (var profile in _profileItems)
                {
                    var mounted = _mountedDrives.TryGetValue(
                        profile.DriveLetter ?? string.Empty,
                        out var drive);
                    var pending = FindOperation(profile.Name, profile.DriveLetter);
                    var result = ProfileResultStatus(profile.Name, profile.DriveLetter);
                    var status = pending != null ? pending.Status : result != null ? result : !mounted
                        ? "未掛載"
                        : drive.IsConnecting ? "正在重新連線" : drive.IsConnected ? "已掛載" : "連線中斷";
                    var index = _profileGrid.Rows.Add(
                        profile.Name,
                        profile.Host,
                        profile.RemoteRoot,
                        profile.DriveLetter,
                        profile.AuthenticationMode == AuthenticationMode.Password
                            ? "Password"
                            : "Private key",
                        profile.AutoMountOnStartup ? "啟用" : "—",
                        status);
                    var row = _profileGrid.Rows[index];
                    row.Tag = profile.Name;
                    row.Cells["Status"].Style.ForeColor = mounted && drive.IsConnected
                        ? Color.FromArgb(22, 163, 74)
                        : mounted
                            ? Color.FromArgb(217, 119, 6)
                            : Color.FromArgb(100, 116, 139);
                }
            }
            finally
            {
                _isSynchronizingProfileGrid = false;
            }

            SelectProfileGrid(_selectedProfileName);
        }

        private void SelectProfileGrid(string profileName)
        {
            _isSynchronizingProfileGrid = true;
            try
            {
                _profileGrid.ClearSelection();
                if (string.IsNullOrWhiteSpace(profileName))
                    return;

                foreach (DataGridViewRow row in _profileGrid.Rows)
                {
                    if (!string.Equals(
                            row.Tag as string,
                            profileName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    row.Selected = true;
                    _profileGrid.CurrentCell = row.Cells[0];
                    return;
                }
            }
            finally
            {
                _isSynchronizingProfileGrid = false;
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
            SelectProfileGrid(profile.Name);
            var mounted = IsMounted(profile.DriveLetter);
            SetStatus(mounted
                ? "Mounted at " + profile.DriveLetter
                : "Profile loaded");
            ApplySelectedOperationStatus();
            SetMascotSpeech(mounted
                ? $"「{profile.Name}」已掛載於 {profile.DriveLetter}！隨時可用 Antigravity 進行開發～✨"
                : $"已切換至「{profile.Name}」設定檔！點擊 Mount 即可掛載到 {profile.DriveLetter} 喔～");
        }

        private void NewProfile()
        {
            _selectedProfileName = null;
            _profiles.SelectedIndex = -1;
            SelectProfileGrid(null);
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
            RefreshProfileGrid();
            SetMascotSpeech("設定檔已刪除完畢。");
        }

        private void LoadProfileSelectedInGrid()
        {
            if (_isSynchronizingProfileGrid ||
                _profileGrid.SelectedRows.Count != 1)
            {
                return;
            }

            var profileName = _profileGrid.SelectedRows[0].Tag as string;
            if (string.IsNullOrWhiteSpace(profileName) ||
                string.Equals(
                    profileName,
                    _selectedProfileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var index = _profiles.FindStringExact(profileName);
            if (index >= 0)
                _profiles.SelectedIndex = index;
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
            => UpsertProfile(profile, _selectedProfileName, selectProfile: true);

        private void UpsertProfile(DriveProfile profile, string originalName, bool selectProfile)
        {
            ValidateProfileCanBeSaved(profile, originalName);

            if (!string.IsNullOrWhiteSpace(originalName))
            {
                _profileItems.RemoveAll(item =>
                    string.Equals(item.Name, originalName, StringComparison.OrdinalIgnoreCase));
            }

            _profileItems.RemoveAll(item =>
                string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            _profileItems.Add(profile);
            _profileItems = _profileItems
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _profileStore.Save(_profileItems);
            if (selectProfile) _selectedProfileName = profile.Name;
            RefreshProfileSelector(selectProfile ? profile.Name : _selectedProfileName);
        }

        private void ValidateProfileCanBeSaved(DriveProfile profile, string originalName)
        {
            var candidates = _profileItems
                .Where(item => !string.Equals(
                    item.Name,
                    originalName,
                    StringComparison.OrdinalIgnoreCase))
                .Concat(new[] { profile });
            var errors = ProfileSetValidator.ValidateUniqueNamesAndDriveLetters(
                candidates);
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    string.Join(Environment.NewLine, errors));
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
                ReadOnly = _readOnly.Checked,
                AutoMountOnStartup =
                    SelectedAuthenticationMode() == AuthenticationMode.PrivateKey &&
                    _autoMountOnStartup.Checked
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
            _autoMountOnStartup.Checked = profile.AutoMountOnStartup &&
                profile.AuthenticationMode == AuthenticationMode.PrivateKey;
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

        private async Task<bool> RunBusyAsync(
            Func<Task> operation,
            bool showError = true)
        {
            if (_busy || _isApplyingUpdate || _profileOperations.Count != 0)
                return false;
            if (!_updateActivityGate.TryBeginActivity(out var activity))
                return false;

            using (activity)
            {
                SetBusy(true);
                try
                {
                    await operation();
                    return true;
                }
                catch (Exception exception)
                {
                    if (showError)
                        ShowError(exception);
                    else
                        SetStatus("自動掛載失敗：" + exception.Message, false);
                    SetMascotSpeech("嗚哇！操作好像遇到問題了，請檢查設定或網路喔＞＜");
                    return false;
                }
                finally
                {
                    SetBusy(false);
                }
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
            RefreshActionState();
        }

        private bool IsFormReadyForMount()
        {
            if (string.IsNullOrWhiteSpace(_host.Text)) return false;
            if (string.IsNullOrWhiteSpace(_username.Text)) return false;
            if (string.IsNullOrWhiteSpace(_remoteRoot.Text) || !_remoteRoot.Text.Trim().StartsWith("/")) return false;
            if (string.IsNullOrWhiteSpace(SelectedDriveLetter())) return false;
            if (_port.Value < 1 || _port.Value > 65535) return false;

            var auth = SelectedAuthenticationMode();
            if (auth == AuthenticationMode.PrivateKey)
            {
                if (string.IsNullOrWhiteSpace(_privateKeyPath.Text)) return false;
            }
            else
            {
                if (string.IsNullOrEmpty(_password.Text)) return false;
            }

            return true;
        }

        private bool IsFormReadyForSave()
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) return false;
            if (string.IsNullOrWhiteSpace(_host.Text)) return false;
            if (string.IsNullOrWhiteSpace(_username.Text)) return false;
            if (string.IsNullOrWhiteSpace(SelectedDriveLetter())) return false;
            return true;
        }

        private void RefreshActionState()
        {
            var actionsDisabled = _busy || _isApplyingUpdate;
            var selectedBusy = SelectedProfileIsBusy();
            var profileActionsDisabled = actionsDisabled || selectedBusy;
            _cancelOperationButton.Enabled = selectedBusy && !_isApplyingUpdate;
            foreach (Control field in new Control[] { _name, _host, _port, _username, _remoteRoot, _driveLetter, _hostFingerprint })
                field.Enabled = !profileActionsDisabled;
            UseWaitCursor = actionsDisabled;
            _profiles.Enabled = !actionsDisabled;
            _profileGrid.Enabled = !actionsDisabled;
            _newButton.Enabled = !actionsDisabled;
            _testButton.Enabled = !profileActionsDisabled && IsFormReadyForMount();
            _unmountButton.Enabled = !profileActionsDisabled && _mountedDrives.Count > 0;
            _saveButton.Enabled = !profileActionsDisabled && IsFormReadyForSave();
            _deleteButton.Enabled = !profileActionsDisabled && _profiles.SelectedIndex >= 0 && _profileItems.Count > 0;
            _explorerButton.Enabled = !actionsDisabled;
            _authenticationMode.Enabled = !profileActionsDisabled;
            _installDriverButton.Enabled = !actionsDisabled;
            _checkUpdatesButton.Enabled =
                !actionsDisabled && !_isCheckingUpdates;
            _readOnly.Enabled = !profileActionsDisabled;
            _autoMountOnStartup.Enabled =
                !profileActionsDisabled &&
                SelectedAuthenticationMode() == AuthenticationMode.PrivateKey;

            var runtime = WinFspRuntimePreflight.CheckX64();
            var canMount = !profileActionsDisabled && runtime.IsValid && IsFormReadyForMount();
            _testAndMountButton.Enabled = canMount;
            _mountButton.Enabled = canMount;
            _mountAllButton.Enabled = !actionsDisabled && runtime.IsValid &&
                _profileItems.Count > 0;
            _unmountAllButton.Enabled = !actionsDisabled &&
                _mountedDrives.Count > 0;
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
            var actionsEnabled = !_busy && !_isApplyingUpdate && !SelectedProfileIsBusy();

            if (usingPrivateKey)
            {
                _authCredIconLabel.Text = "🔑";
                _authCredTextLabel.Text = "Private key";
                _privateKeyPath.Visible = true;
                _privateKeyPath.Enabled = actionsEnabled;
                _browseButton.Visible = true;
                _browseButton.Enabled = actionsEnabled;
                _password.Visible = false;
            }
            else
            {
                _authCredIconLabel.Text = "🔒";
                _authCredTextLabel.Text = "Password";
                _privateKeyPath.Visible = false;
                _browseButton.Visible = false;
                _password.Visible = true;
                _password.Enabled = actionsEnabled;
                _autoMountOnStartup.Checked = false;
            }
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
            if (!_isExplicitExit && e.CloseReason != CloseReason.UserClosing && _profileOperations.Count != 0)
            {
                e.Cancel = true;
                ExitApplication();
                return;
            }
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
            _ = ExitApplicationAsync();
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

        private void AddCredentialRow(TableLayoutPanel table, int row)
        {
            _authCredIconLabel.AutoSize = true;
            _authCredIconLabel.Text = "🔑";
            _authCredIconLabel.ForeColor = Color.FromArgb(59, 130, 246);
            _authCredIconLabel.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            _authCredIconLabel.Anchor = AnchorStyles.Right;
            _authCredIconLabel.Margin = new Padding(0, 4, 4, 4);

            _authCredTextLabel.AutoSize = true;
            _authCredTextLabel.Text = "Private key";
            _authCredTextLabel.ForeColor = Color.FromArgb(30, 41, 59);
            _authCredTextLabel.Font = new Font("Segoe UI", 9.2F, FontStyle.Regular);
            _authCredTextLabel.Anchor = AnchorStyles.Left;
            _authCredTextLabel.Margin = new Padding(0, 4, 8, 4);

            _privateKeyPath.Dock = DockStyle.Fill;
            _privateKeyPath.Margin = new Padding(0, 2, 8, 2);

            _password.Dock = DockStyle.Fill;
            _password.Margin = new Padding(0, 2, 8, 2);
            _password.Visible = false;

            _browseButton.AutoSize = true;
            _browseButton.Margin = new Padding(0, 1, 0, 1);

            table.Controls.Add(_authCredIconLabel, 0, row);
            table.Controls.Add(_authCredTextLabel, 1, row);
            table.Controls.Add(_privateKeyPath, 2, row);
            table.Controls.Add(_password, 2, row);
            table.Controls.Add(_browseButton, 3, row);
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

        internal static Image LoadLoadingMascotImage()
        {
            try
            {
                var assembly = typeof(MainForm).Assembly;
                using (var stream = assembly.GetManifestResourceStream("ThreeWa.SshDrive.App.Assets.mascot_loading.jpg"))
                {
                    if (stream != null)
                        return Image.FromStream(stream);
                }
            }
            catch
            {
            }

            var candidates = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "mascot_loading.jpg"),
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(MainForm).Assembly.Location) ?? "", "Assets", "mascot_loading.jpg"),
                @"D:\mytools\3waSshDrive\src\3waSshDrive.App\Assets\mascot_loading.jpg"
            };

            foreach (var path in candidates)
            {
                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        return Image.FromFile(path);
                    }
                    catch
                    {
                    }
                }
            }

            return LoadMascotImage();
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

        internal static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
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

        internal static void ApplyRoundedRegion(Control control, int radius, Color borderColor = default(Color))
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
