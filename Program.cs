using DailyMacro.UI;
using DailyMacro.Services;

namespace DailyMacro;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ShowCrash(ex);
        };

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            ShowCrash(ex);
        }
    }

    private static void ShowCrash(Exception ex)
    {
        try
        {
            var folder = MacroStore.Folder;
            File.WriteAllText(Path.Combine(folder, "crash.log"), ex.ToString());
        }
        catch
        {
            // ignore log failures
        }

        MessageBox.Show(
            "O " + AppInfo.Name + " não conseguiu abrir:\n\n" + ex.Message,
            AppInfo.Name,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
