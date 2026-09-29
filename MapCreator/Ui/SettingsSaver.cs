using System;
using Avalonia.Threading;
using MapCreator.Classes;

namespace MapCreator.Ui
{
    /// <summary>
    /// Saves AppSettings.Current once after a burst of changes (a group click, typing in a box)
    /// </summary>
    internal static class SettingsSaver
    {
        private static bool pending;

        public static void Request()
        {
            if (pending)
            {
                return;
            }

            pending = true;
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        /// <summary>
        /// Saves now if a save is waiting (the window closes)
        /// </summary>
        public static void Flush()
        {
            if (!pending)
            {
                return;
            }

            pending = false;
            try
            {
                AppSettings.Current.Save();
            }
            catch (Exception ex)
            {
                // Runs from the dispatcher, where any exception would end the program
                AppLog.Log(string.Format("Settings not saved ({0})", ex.Message), LogLevel.Warning);
            }
        }
    }
}
