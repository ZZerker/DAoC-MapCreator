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

            var settings = Properties.Settings.Default;
            if (!Classes.GameFolderLocator.IsGameFolder(settings.game_path))
            {
                var gameFolder = Classes.GameFolderLocator.Find();
                if (gameFolder != null)
                {
                    settings.game_path = gameFolder;
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
