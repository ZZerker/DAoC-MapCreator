using System;
using System.Windows.Forms;

namespace MapCreator
{
	internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Game files and settings use "." decimals; render threads inherit this
            var culture = new System.Globalization.CultureInfo("en-US");
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = culture;

            ApplicationConfiguration.Initialize();

            var settings = Classes.AppSettings.Current;
            if (!Classes.GameFolderLocator.IsGameFolder(settings.GamePath))
            {
                var gameFolder = Classes.GameFolderLocator.Find();
                if (gameFolder != null)
                {
                    settings.GamePath = gameFolder;
                    settings.Save();
                }
            }

            var exitCode = Classes.Rendering.BatchMode.TryRun(args);
            if (exitCode != null)
            {
                Environment.ExitCode = exitCode.Value;
                return;
            }

            Application.Run(new MainForm());
        }
    }
}
