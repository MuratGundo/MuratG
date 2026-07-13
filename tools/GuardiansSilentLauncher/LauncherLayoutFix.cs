namespace UCREW.GuardiansTR;

internal static class LauncherLayoutFix
{
    public static void Apply(LauncherForm form)
    {
        form.AutoScaleMode = AutoScaleMode.Dpi;

        foreach (Control control in EnumerateControls(form))
        {
            switch (control)
            {
                case Panel panel
                    when panel.Location == new Point(34, 354) &&
                         panel.Size == new Size(892, 150):
                    panel.Location = new Point(34, 346);
                    panel.Size = new Size(892, 164);
                    break;

                case Label label
                    when string.Equals(
                        label.Text,
                        "GALAKSİNİN KORUYUCULARI • TÜRKÇE YAMA",
                        StringComparison.Ordinal):
                    label.Location = new Point(24, 15);
                    label.UseCompatibleTextRendering = true;
                    break;

                case Label label
                    when string.Equals(
                        label.Text,
                        "Başlatıcı hazırlanıyor…",
                        StringComparison.Ordinal):
                    label.Location = new Point(24, 36);
                    label.Size = new Size(844, 50);
                    label.Font = new Font(
                        "Segoe UI Semibold",
                        19.5f,
                        FontStyle.Bold);
                    label.TextAlign = ContentAlignment.MiddleLeft;
                    label.Padding = new Padding(0, 0, 0, 2);
                    label.UseCompatibleTextRendering = true;
                    break;

                case Label label
                    when string.Equals(
                        label.Text,
                        "U-CREW güvenli yama sistemi başlatılıyor.",
                        StringComparison.Ordinal):
                    label.Location = new Point(25, 86);
                    label.Size = new Size(842, 27);
                    label.Font = new Font(
                        "Segoe UI",
                        10f,
                        FontStyle.Regular);
                    label.TextAlign = ContentAlignment.MiddleLeft;
                    label.Padding = new Padding(0, 0, 0, 1);
                    label.UseCompatibleTextRendering = true;
                    break;

                case Panel panel
                    when panel.Location == new Point(25, 116) &&
                         panel.Size == new Size(842, 10):
                    panel.Location = new Point(25, 126);
                    break;

                case Label label
                    when string.Equals(
                        label.Text,
                        "U-CREW • u-crew.net",
                        StringComparison.Ordinal):
                    label.Location = new Point(34, 516);
                    label.UseCompatibleTextRendering = true;
                    break;
            }
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (Control descendant in EnumerateControls(child))
            {
                yield return descendant;
            }
        }
    }
}
