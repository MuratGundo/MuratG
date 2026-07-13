using System.Text.Json;

namespace UCREW.SecurePatch;

internal sealed class LoginForm : Form
{
    private readonly ClientConfig _config;
    private readonly TextBox _emailBox;
    private readonly TextBox _passwordBox;
    private readonly CheckBox _rememberBox;
    private readonly Button _loginButton;
    private readonly Label _statusLabel;

    public LoginForm(ClientConfig config)
    {
        _config = config;

        Text = "U-CREW Giriş";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 350);
        BackColor = Color.FromArgb(7, 11, 18);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;

        string backgroundPath = ConfigLoader.ResolveAssetPath(config, config.BackgroundPath);
        BackgroundImage = LoadImageUnlocked(backgroundPath);
        BackgroundImageLayout = ImageLayout.Stretch;

        string logoPath = ConfigLoader.ResolveAssetPath(config, config.LogoPath);
        Image? logoImage = LoadImageUnlocked(logoPath);

        var title = new Label
        {
            Text = "U-CREW",
            Location = new Point(32, 25),
            Size = new Size(390, 42),
            ForeColor = Color.FromArgb(85, 255, 0),
            Font = new Font("Segoe UI Semibold", 24f, FontStyle.Bold)
        };
        Controls.Add(title);

        if (logoImage is not null)
        {
            title.Visible = false;
            Controls.Add(new PictureBox
            {
                Location = new Point(32, 18),
                Size = new Size(250, 62),
                BackColor = Color.Transparent,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = logoImage
            });
        }

        var subtitle = new Label
        {
            Text = string.IsNullOrWhiteSpace(config.WindowTitle)
                ? config.GameSlug + " Türkçe Yama"
                : config.WindowTitle,
            Location = new Point(35, 70),
            Size = new Size(390, 28),
            ForeColor = Color.FromArgb(205, 211, 222)
        };
        Controls.Add(subtitle);

        Controls.Add(new Label
        {
            Text = "E-posta",
            Location = new Point(35, 112),
            Size = new Size(390, 24)
        });

        _emailBox = new TextBox
        {
            Location = new Point(35, 138),
            Size = new Size(390, 30),
            Text = ReadRememberedEmail()
        };
        Controls.Add(_emailBox);

        Controls.Add(new Label
        {
            Text = "Şifre",
            Location = new Point(35, 178),
            Size = new Size(390, 24)
        });

        _passwordBox = new TextBox
        {
            Location = new Point(35, 204),
            Size = new Size(390, 30),
            UseSystemPasswordChar = true
        };
        Controls.Add(_passwordBox);

        _rememberBox = new CheckBox
        {
            Text = "Beni Hatırla",
            Location = new Point(35, 245),
            Size = new Size(180, 28),
            Checked = true,
            ForeColor = Color.White
        };
        Controls.Add(_rememberBox);

        _loginButton = new Button
        {
            Text = "Giriş Yap ve Yamayı Başlat",
            Location = new Point(218, 242),
            Size = new Size(207, 38),
            BackColor = Color.FromArgb(85, 255, 0),
            ForeColor = Color.FromArgb(5, 8, 12),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
        };
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.Click += async (_, _) => await LoginAsync();
        Controls.Add(_loginButton);

        _statusLabel = new Label
        {
            Text = "U-CREW hesabınızla giriş yapın.",
            Location = new Point(35, 298),
            Size = new Size(390, 30),
            ForeColor = Color.FromArgb(170, 180, 192)
        };
        Controls.Add(_statusLabel);

        AcceptButton = _loginButton;
        Shown += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_emailBox.Text))
                _emailBox.Focus();
            else
                _passwordBox.Focus();
        };
    }

    private async Task LoginAsync()
    {
        _loginButton.Enabled = false;
        _emailBox.Enabled = false;
        _passwordBox.Enabled = false;
        _rememberBox.Enabled = false;
        _statusLabel.ForeColor = Color.FromArgb(85, 255, 0);
        _statusLabel.Text = "Giriş yapılıyor…";

        try
        {
            var service = new LoginService();
            string token = await service.LoginAsync(
                _config,
                _emailBox.Text,
                _passwordBox.Text,
                CancellationToken.None);

            TokenProvider.SetRuntimeToken(token, _rememberBox.Checked);
            _statusLabel.Text = "Giriş başarılı.";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _statusLabel.ForeColor = Color.FromArgb(255, 110, 90);
            _statusLabel.Text = exception.Message;
            _loginButton.Enabled = true;
            _emailBox.Enabled = true;
            _passwordBox.Enabled = true;
            _rememberBox.Enabled = true;
            _passwordBox.SelectAll();
            _passwordBox.Focus();
        }
    }

    private static Image? LoadImageUnlocked(string path)
    {
        try
        {
            if (ConfigLoader.TryReadEmbeddedFile(path, out byte[] embeddedBytes))
            {
                using var memory = new MemoryStream(embeddedBytes, writable: false);
                using Image embeddedImage = Image.FromStream(memory);
                return new Bitmap(embeddedImage);
            }

            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using Image image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadRememberedEmail()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "U-CREW", "Launcher", "settings.json");

        try
        {
            if (!File.Exists(path))
                return string.Empty;

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Email", out JsonElement email))
                return email.GetString() ?? string.Empty;
        }
        catch
        {
        }

        return string.Empty;
    }
}
