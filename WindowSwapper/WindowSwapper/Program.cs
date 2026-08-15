namespace WindowSwapper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(true, "WindowSwapper.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("WindowSwapper is already running (check your system tray).",
                "WindowSwapper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new TrayApplicationContext());
    }
}
