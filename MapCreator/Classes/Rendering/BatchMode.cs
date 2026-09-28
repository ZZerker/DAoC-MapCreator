using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Renders without the window: MapCreator.exe --render 163,164|nf+outdoor|all [--size 2048] [--dir name] [--log render.log] [--parallel 4]
    /// [--labels-only] [--no-keeps] [--no-depth-water]. Other options come from the settings the window saved; nothing is saved back.
    /// </summary>
    internal static class BatchMode
    {
        /// <summary>
        /// Runs the batch and returns the exit code (0 when every zone rendered), or null without --render
        /// </summary>
        public static int? TryRun(string[] args)
        {
            var zoneTerms = new List<string>();
            var size = 0;
            string directory = null;
            var logName = "render.log";
            var parallel = 0;
            for (var i = 0; i < args.Length - 1; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--render":
                        zoneTerms.Add(args[i + 1]);
                        break;
                    case "--size":
                        size = Convert.ToInt32(args[i + 1]);
                        break;
                    case "--dir":
                        directory = args[i + 1];
                        break;
                    case "--log":
                        logName = args[i + 1];
                        break;
                    case "--parallel":
                        parallel = Convert.ToInt32(args[i + 1]);
                        break;
                }
            }

            if (zoneTerms.Count == 0)
            {
                return null;
            }

            var saved = RenderSettings.FromSaved(Properties.Settings.Default);
            var settings = saved with
            {
                MapSize = size > 0 ? size : saved.MapSize,
                Parallel = parallel > 0 ? parallel : saved.Parallel,
                DirectoryPattern = directory ?? saved.DirectoryPattern,
                FileType = "PNG",
                FilePattern = "z{id}",
                LabelsOnly = HasFlag(args, "--labels-only"),
                DrawKeeps = saved.DrawKeeps && !HasFlag(args, "--no-keeps"),
                DepthShadedWater = saved.DepthShadedWater && !HasFlag(args, "--no-depth-water")
            };

            using var log = new FileLog(Path.Combine(settings.TargetPath, logName));
            AppLog.Reporter = log;
            if (!MpkWrapper.CheckGamePath())
            {
                return 1;
            }

            var zoneIds = ZoneGroups.Resolve(string.Join(",", zoneTerms), message => log.Log(message, LogLevel.Warning)).ToList();
            var knownZoneIds = zoneIds.Where(DataWrapper.IsKnownZone).ToList();
            foreach (var zoneId in zoneIds.Except(knownZoneIds))
            {
                log.Log(string.Format("Skipped: zone {0} is not in the zone list.", zoneId), LogLevel.Warning);
            }

            var zones = knownZoneIds.Select(DataWrapper.GetZoneSelectionByZoneId).ToList();
            log.Log(string.Format("Rendering {0} zones at {1} px, {2} at a time, into {3}", zones.Count, settings.MapSize, Math.Max(1, settings.Parallel), Path.Combine(settings.TargetPath, settings.DirectoryPattern ?? "")), LogLevel.Notice);

            var timer = Stopwatch.StartNew();
            var batch = new ZoneBatch(settings, log) { ZoneIdsInLog = true };
            batch.Run(zones);
            log.Log(string.Format("Done: {0} zones in {1:hh\\:mm\\:ss}, {2} failed", zones.Count, timer.Elapsed, batch.Failed), batch.Failed == 0 ? LogLevel.Success : LogLevel.Error);
            return batch.Failed == 0 ? 0 : 1;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        }
    }
}
