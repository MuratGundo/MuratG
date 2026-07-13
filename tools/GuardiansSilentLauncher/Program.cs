using System.Windows.Forms;

namespace UCREW.GuardiansTR;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\UCREW_Guardians_TR_Launcher",
            createdNew: out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "Türkçe başlatıcı zaten çalışıyor.",
                "U-CREW Guardians Türkçe",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var form = new LauncherForm(args);
        LauncherLayoutFix.Apply(form);
        Application.Run(form);
    }
}
