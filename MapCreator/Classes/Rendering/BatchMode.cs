using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Renders without the window: MapCreatorNext.exe --render 163,164|nf+outdoor|all [--size 2048] [--dir name] [--log render.log] [--parallel 4]
    /// [--labels dir] [--label-size 1024] [--labels-only] [--no-keeps] [--no-depth-water] [--no-console] [--ui].
    /// Other options come from the settings the window saved; nothing is saved back.
    /// A console window shows the progress unless --no-console is given; with --ui the window opens and renders instead.
    /// </summary>
    internal static class BatchMode
    {
        // The console window stays open this long after the batch, so unattended runs still end
        private const int CLOSE_AFTER_SECONDS = 30;

        /// <summary>
        /// Runs the batch and returns the exit code (0 when every zone rendered), or null without --render
        /// </summary>
        public static int? TryRun(string[] args)
        {
            var settings = ParseArgs(args, out var zoneTerms, out var logName);
            if (zoneTerms.Count == 0 || IsUiRun(args))
            {
                return null;
            }

            using var log = new FileLog(Path.Combine(settings.TargetPath, logName));
            using var dashboard = HasFlag(args, "--no-console") ? null : new BatchDashboard(log);
            var reporter = (IRenderReporter)dashboard ?? log;
            AppLog.Reporter = reporter;
            try
            {
                return Run(settings, zoneTerms, reporter, dashboard);
            }
            finally
            {
                dashboard?.Finish(TimeSpan.FromSeconds(CLOSE_AFTER_SECONDS));
            }
        }

        /// <summary>
        /// --ui with --render: the window opens and renders with these arguments instead of the console
        /// </summary>
        public static bool IsUiRun(string[] args)
        {
            return HasFlag(args, "--ui");
        }

        /// <summary>
        /// The saved settings with the command line overrides; zoneTerms is empty without --render
        /// </summary>
        public static RenderSettings ParseArgs(string[] args, out List<string> zoneTerms, out string logName)
        {
            zoneTerms = new List<string>();
            var size = 0;
            string directory = null;
            logName = "render.log";
            var parallel = 0;
            string labelDirectory = null;
            var labelSize = 0;
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
                    case "--labels":
                        labelDirectory = args[i + 1];
                        break;
                    case "--label-size":
                        labelSize = Convert.ToInt32(args[i + 1]);
                        break;
                }
            }

            var saved = RenderSettings.FromSettings(AppSettings.Current);
            return saved with
            {
                MapSize = size > 0 ? size : saved.MapSize,
                Parallel = parallel > 0 ? parallel : saved.Parallel,
                DirectoryPattern = directory ?? saved.DirectoryPattern,
                FileType = "PNG",
                FilePattern = "z{id}",
                LabelsOnly = HasFlag(args, "--labels-only"),
                LabelDirectory = labelDirectory,
                LabelSize = labelSize,
                DrawKeeps = saved.DrawKeeps && !HasFlag(args, "--no-keeps"),
                DepthShadedWater = saved.DepthShadedWater && !HasFlag(args, "--no-depth-water")
            };
        }

        private static int Run(RenderSettings settings, List<string> zoneTerms, IRenderReporter reporter, BatchDashboard dashboard)
        {
            if (!GameFolderLocator.CheckGamePath())
            {
                return 1;
            }

            var zoneIds = ZoneGroups.Resolve(string.Join(",", zoneTerms), message => reporter.Log(message, LogLevel.Warning)).ToList();
            var knownZoneIds = zoneIds.Where(DataWrapper.IsKnownZone).ToList();
            foreach (var zoneId in zoneIds.Except(knownZoneIds))
            {
                reporter.Log(string.Format("Skipped: zone {0} is not in the zone list.", zoneId), LogLevel.Warning);
            }

            var zones = knownZoneIds.Select(DataWrapper.GetZoneSelectionByZoneId).ToList();
            var threads = Math.Clamp(settings.Parallel, 1, Math.Max(1, zones.Count));
            var target = Path.Combine(settings.TargetPath, settings.DirectoryPattern ?? "");
            reporter.Log(string.Format("Rendering {0} zones at {1} px, {2} at a time, into {3}", zones.Count, settings.MapSize, threads, target), LogLevel.Notice);
            dashboard?.Start(string.Format("{0} px into {1}", settings.MapSize, target), zones.Count, threads);

            var timer = Stopwatch.StartNew();
            var batch = new ZoneBatch(settings, reporter)
                        {
                            ZoneIdsInLog = true,
                            ZoneStarted = (zone, _) => dashboard?.ZoneStarted(zone),
                            ZoneFinished = (zone, outcome) => dashboard?.ZoneFinished(zone, outcome)
                        };
            batch.Run(zones);
            batch.LogSummary(zones.Count, timer.Elapsed);
            return batch.Failed == 0 ? 0 : 1;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        }
    }
}
