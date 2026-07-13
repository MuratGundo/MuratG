using System.Security.Cryptography;
using System.Text;

namespace UCREW.SecurePatch;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            ClientConfig config = ConfigLoader.Load(args);
            string mutexSuffix = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(config.GameSlug)))[..16];

            using var mutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\UCREW_SecurePatch_" + mutexSuffix,
                createdNew: out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "Bu oyun için U-CREW güvenli yama istemcisi zaten çalışıyor.",
                    "U-CREW Güvenli Yama",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (var loginForm = new LoginForm(config))
            {
                if (loginForm.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }

            Application.Run(new LauncherForm(config));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "U-CREW Güvenli Yama",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
