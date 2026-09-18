using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Profiles;
using ThreeWa.SshDrive.FileSystem.Mounting;
using ThreeWa.SshDrive.Sftp;

namespace ThreeWa.SshDrive.App
{
    internal sealed class MainForm : Form
    {
        private readonly ProfileStore _profileStore;
        private readonly SshConnectionProbe _connectionProbe;
        private readonly MountManager _mountManager;
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
        private readonly Label _status = new Label();
        private readonly Button _newButton = new Button();
        private readonly Button _saveButton = new Button();
        private readonly Button _deleteButton = new Button();
        private readonly Button _browseButton = new Button();
        private readonly Button _testButton = new Button();
        private readonly Button _mountButton = new Button();
        private readonly Button _unmountButton = new Button();
        private readonly Button _explorerButton = new Button();

        private List<DriveProfile> _profileItems = new List<DriveProfile>();
        private string _selectedProfileName;
        private bool _busy;

        public MainForm()
            : this(
                ProfileStore.CreateDefault(),
                new SshConnectionProbe(),
                new MountManager())
        {
        }

        internal MainForm(
            ProfileStore profileStore,
            SshConnectionProbe connectionProbe,
            MountManager mountManager)
        {
            _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
            _connectionProbe = connectionProbe ?? throw new ArgumentNullException(nameof(connectionProbe));
            _mountManager = mountManager ?? throw new ArgumentNullException(nameof(mountManager));

            Text = "3waSshDrive";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 520);
            Size = new Size(840, 580);
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

            BuildInterface();
            WireEvents();
            LoadProfiles();
        }

        private void BuildInterface()
        {
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 4
            };
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
                Text = "Linux workspace → Windows drive",
                Margin = new Padding(0, 0, 0, 14)
            };
            page.Controls.Add(title, 0, 0);

            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 11,
                AutoSize = true
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            ConfigureComboBox(_profiles);
            ConfigureComboBox(_driveLetter);
            ConfigureComboBox(_authenticationMode);
            for (var letter = 'D'; letter <= 'Z'; letter++)
                _driveLetter.Items.Add(letter + ":");
            _driveLetter.SelectedItem = "Z:";
            _authenticationMode.Items.Add("Private key");
            _authenticationMode.Items.Add("Password");
            _authenticationMode.SelectedIndex = 0;

            _port.Minimum = 1;
            _port.Maximum = 65535;
            _port.Value = 22;
            _port.Width = 120;
            _remoteRoot.Text = "/";
            _password.UseSystemPasswordChar = true;
            _hostFingerprint.ReadOnly = true;

            _newButton.Text = "New";
            _saveButton.Text = "Save";
            _deleteButton.Text = "Delete";
            _browseButton.Text = "Browse…";
            _testButton.Text = "Test && Trust";
            _mountButton.Text = "Mount";
            _unmountButton.Text = "Unmount";
            _explorerButton.Text = "Open Explorer";

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

            AddRow(fields, 0, "Profile", _profiles, profileButtons);
            AddRow(fields, 1, "Name", _name, null);
            AddRow(fields, 2, "Host", _host, null);
            AddRow(fields, 3, "Port", _port, null);
            AddRow(fields, 4, "Username", _username, null);
            AddRow(fields, 5, "Remote root", _remoteRoot, null);
            AddRow(fields, 6, "Drive letter", _driveLetter, null);
            AddRow(fields, 7, "Authentication", _authenticationMode, null);
            AddRow(fields, 8, "Private key", _privateKeyPath, _browseButton);
            AddRow(fields, 9, "Password", _password, null);
            AddRow(fields, 10, "Host fingerprint", _hostFingerprint, null);
            page.Controls.Add(fields, 0, 1);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 14, 0, 8)
            };
            actions.Controls.Add(_testButton);
            actions.Controls.Add(_mountButton);
            actions.Controls.Add(_unmountButton);
            actions.Controls.Add(_explorerButton);
            page.Controls.Add(actions, 0, 2);

            _status.AutoSize = true;
            _status.ForeColor = Color.FromArgb(70, 70, 70);
            _status.Text = "Ready";
            page.Controls.Add(_status, 0, 3);

            Controls.Add(page);
            AcceptButton = _mountButton;
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
            _testButton.Click += async (sender, args) =>
                await RunBusyAsync(TestAndTrustAsync);
            _mountButton.Click += async (sender, args) =>
                await RunBusyAsync(MountAsync);
            _unmountButton.Click += async (sender, args) =>
                await RunBusyAsync(UnmountAsync);
            _explorerButton.Click += (sender, args) => ExecuteUi(OpenExplorer);
            FormClosed += (sender, args) => DisposeMountedDrives();
            UpdateAuthenticationControls();
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
            SetStatus(IsMounted(profile.DriveLetter)
                ? "Mounted at " + profile.DriveLetter
                : "Profile loaded");
        }

        private void NewProfile()
        {
            _selectedProfileName = null;
            _profiles.SelectedIndex = -1;
            WriteForm(new DriveProfile());
            SetStatus("New profile");
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
        }

        private async Task TestAndTrustAsync()
        {
            var profile = ReadForm();
            ValidateForProbe(profile);
            SetStatus("Connecting and reading the SSH host key…");

            var result = await Task.Run(() => _connectionProbe.Probe(profile));
            profile.HostKeyFingerprintSha256 = result.HostKeyFingerprintSha256;
            _hostFingerprint.Text = result.HostKeyFingerprintSha256;
            UpsertProfile(profile);
            SetStatus("Trusted " + result.HostKeyFingerprintSha256);
        }

        private async Task MountAsync()
        {
            var profile = ReadForm();
            var errors = DriveProfileValidator.Validate(profile);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            var runtime = WinFspRuntimePreflight.CheckX64();
            if (!runtime.IsValid)
                throw new InvalidOperationException(runtime.Error);

            var drive = profile.DriveLetter.ToUpperInvariant();
            if (_mountedDrives.ContainsKey(drive))
                throw new InvalidOperationException(drive + " is already mounted by 3waSshDrive.");

            SetStatus("Mounting " + profile.Name + " at " + drive + "…");
            var mounted = await Task.Run(() => _mountManager.Mount(profile));
            _mountedDrives.Add(drive, mounted);
            UpsertProfile(profile);
            SetStatus("Mounted " + profile.Name + " at " + drive);
        }

        private async Task UnmountAsync()
        {
            var drive = SelectedDriveLetter();
            if (!_mountedDrives.TryGetValue(drive, out var mounted))
                throw new InvalidOperationException(drive + " is not mounted by 3waSshDrive.");

            SetStatus("Unmounting " + drive + "…");
            await Task.Run(() => mounted.Dispose());
            _mountedDrives.Remove(drive);
            SetStatus("Unmounted " + drive);
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
                HostKeyFingerprintSha256 = _hostFingerprint.Text.Trim()
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
            _driveLetter.SelectedItem = string.IsNullOrWhiteSpace(profile.DriveLetter)
                ? "Z:"
                : profile.DriveLetter.ToUpperInvariant();
            _authenticationMode.SelectedIndex =
                profile.AuthenticationMode == AuthenticationMode.Password ? 1 : 0;
            _privateKeyPath.Text = profile.PrivateKeyPath ?? string.Empty;
            _password.Text = profile.Password ?? string.Empty;
            _hostFingerprint.Text = profile.HostKeyFingerprintSha256 ?? string.Empty;
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
            if (_busy)
                return;

            SetBusy(true);
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                ShowError(exception);
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
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UseWaitCursor = busy;
            _testButton.Enabled = !busy;
            _mountButton.Enabled = !busy;
            _unmountButton.Enabled = !busy;
            _saveButton.Enabled = !busy;
            _deleteButton.Enabled = !busy;
            _authenticationMode.Enabled = !busy;
            UpdateAuthenticationControls();
        }

        private void ShowError(Exception exception)
        {
            SetStatus("Error: " + exception.Message);
            MessageBox.Show(
                this,
                exception.Message,
                "3waSshDrive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private void SetStatus(string message)
        {
            _status.Text = message;
        }

        private string SelectedDriveLetter()
        {
            return (_driveLetter.SelectedItem?.ToString() ?? "Z:").ToUpperInvariant();
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
            _privateKeyPath.Enabled = usingPrivateKey && !_busy;
            _browseButton.Enabled = usingPrivateKey && !_busy;
            _password.Enabled = !usingPrivateKey && !_busy;
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
        }

        private static void ConfigureComboBox(ComboBox comboBox)
        {
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Dock = DockStyle.Fill;
        }

        private static void AddRow(
            TableLayoutPanel table,
            int row,
            string labelText,
            Control field,
            Control action)
        {
            var label = new Label
            {
                AutoSize = true,
                Text = labelText,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 7, 10, 7)
            };
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(0, 4, 10, 4);

            table.Controls.Add(label, 0, row);
            table.Controls.Add(field, 1, row);
            if (action != null)
            {
                action.AutoSize = true;
                action.Margin = new Padding(0, 3, 0, 3);
                table.Controls.Add(action, 2, row);
            }
        }
    }
}
