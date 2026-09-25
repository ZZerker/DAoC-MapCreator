using System;
using System.Windows.Forms;

namespace MapCreator
{
	internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
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

            Application.Run(new MainForm(args));
        }
    }
}
