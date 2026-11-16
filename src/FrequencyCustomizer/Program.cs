namespace FrequencyCustomizer;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "FrequencyCustomizer.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("Frequency Customizer is already running (check the system tray).",
                "Frequency Customizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
