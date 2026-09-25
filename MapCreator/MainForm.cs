//
// MapCreator
// Copyright(C) 2017 Stefan Schäfer <merec@merec.org>
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along
// with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using MapCreator.Classes;
using MapCreator.Classes.MapCreation;

namespace MapCreator
{

    public partial class MainForm : Form
    {
        /// <summary>
        /// Self reference
        /// </summary>
        private static MainForm self = null;

        /// <summary>
        /// The Zones to draw
        /// </summary>
        private List<ZoneSelection> selectedZones = new List<ZoneSelection>();

        /// <summary>
        /// The Zones to draw
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<ZoneSelection> SelectedZones
        {
            get => this.selectedZones;
            set => this.selectedZones = value;
        }

        /// <summary>
        /// Current Game Expansion
        /// </summary>
        private GameExpansion expansion = GameExpansion.Classic;

        /// <summary>
        /// Current Game Expansion
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GameExpansion Expansion
        {
            get => this.expansion;
            set => this.expansion = value;
        }

        /// <summary>
        /// The target map size
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int TargetMapSize
        {
            get => Convert.ToInt32(this.widthTextBox.Value);
            set => this.widthTextBox.Value = Convert.ToDecimal(value);
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public MainForm()
        {
            // Language settings
            var ci = new System.Globalization.CultureInfo("en-US");
            System.Threading.Thread.CurrentThread.CurrentCulture = ci;
            System.Threading.Thread.CurrentThread.CurrentUICulture = ci;

            this.InitializeComponent();
            self = this;
            this.Initialize();

            // Load last selected zones
            if (!string.IsNullOrEmpty(Properties.Settings.Default.lastCreatedMaps))
            {
                foreach (var zoneId in Properties.Settings.Default.lastCreatedMaps.Split(','))
                {
                    try
                    {
	                    this.SelectedZones.Add(DataWrapper.GetZoneSelectionByZoneId(zoneId));
                    }
                    catch { }
                }

                this.UpdateSelectedZoneListBox();
            }
        }

        /// <summary>
        /// Batch mode: MapCreator.exe --render 163,164 [--size 2048] [--dir nf_2048] [--log render.log]
        /// </summary>
        private readonly bool batchMode = false;

        private readonly string batchLogFile = null;

        public MainForm(string[] args) : this()
        {
            var batchZoneIds = new List<string>();
            var batchSize = 0;
            string batchDirectory = null;
            var batchLogName = "render.log";
            for (var i = 0; i < args.Length - 1; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--render":
                        batchZoneIds.AddRange(args[i + 1].Split(',').Select(z => z.Trim()).Where(z => z.Length > 0));
                        break;
                    case "--size":
                        batchSize = Convert.ToInt32(args[i + 1]);
                        break;
                    case "--dir":
                        batchDirectory = args[i + 1];
                        break;
                    case "--log":
                        batchLogName = args[i + 1];
                        break;
                }
            }

            if (batchZoneIds.Count == 0)
            {
                return;
            }

            this.batchMode = true;
            this.SelectedZones = batchZoneIds.Select(z => DataWrapper.GetZoneSelectionByZoneId(z)).ToList();
            this.UpdateSelectedZoneListBox();

            var logDirectory = !string.IsNullOrEmpty(Properties.Settings.Default.targetMapPath) ? Properties.Settings.Default.targetMapPath : Application.StartupPath;
            Directory.CreateDirectory(logDirectory);
            this.batchLogFile = Path.Combine(logDirectory, batchLogName);
            File.WriteAllText(this.batchLogFile, "");

            // Settings bindings overwrite control values on load
            this.Shown += (sender, e) =>
            {
                if (batchSize > 0)
                {
	                this.TargetMapSize = batchSize;
                }
                if (batchDirectory != null)
                {
	                this.directoryPatternTextBox.Text = batchDirectory;
                }

                this.fileTypeComboBox.Text = "PNG";
                this.filePatternTextBox.Text = "z{id}";
                this.enableLogCheckBox.Checked = true;
                this.enableResultPreview.Checked = false;

                this.renderButton_Click(null, null);
                this.Close();
            };
        }

        public void Initialize()
        {
            // Set river color
            var mapRiversColor = Properties.Settings.Default.mapRiverColor;
            this.mapRiversColorTextBox.Text = string.Format("{0}{1}{2}", mapRiversColor.R.ToString("X2"), mapRiversColor.G.ToString("X2"), mapRiversColor.B.ToString("X2"));

            var mapBoundsColor = Properties.Settings.Default.mapBoundsColor;
            this.mapBoundsColorTextBox.Text = string.Format("{0}{1}{2}", mapBoundsColor.R.ToString("X2"), mapBoundsColor.G.ToString("X2"), mapBoundsColor.B.ToString("X2"));
        }

        private void UpdateSelectedZoneListBox()
        {
	        this.selectedMapsListBox.DataSource = null;
	        this.selectedMapsListBox.DataSource = this.SelectedZones.OrderBy(z => z.Id).ToList();
	        this.selectedMapsCounterLabel.Text = this.SelectedZones.Count.ToString();
	        this.queueTotalLabel.Text = this.selectedMapsCounterLabel.Text;
	        this.queueProcessedLabel.Text = "0";
	        this.currentMapLabel.Text = "| - |";
        }

        /// <summary>
        /// Zone Selector
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void selectMapsButton_Click(object sender, EventArgs e)
        {
            var form = new SelectMapsForm();
            form.Preselect(this.selectedZones);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
	            this.SelectedZones = form.SelectedZones;
	            this.UpdateSelectedZoneListBox();

                Properties.Settings.Default.lastCreatedMaps = string.Join(",", this.SelectedZones.Select(z => z.Id));
                Properties.Settings.Default.Save();
            }
        }

        /// <summary>
        /// Reset selected zones
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void selecetedMapsResetButton_Click(object sender, EventArgs e)
        {
	        this.SelectedZones.Clear();
	        this.UpdateSelectedZoneListBox();
            //selectedMapsListBox.DataSource = null;
            //selectedMapsListBox.DataSource = SelectedZones;
            //selectedMapsCounterLabel.Text = SelectedZones.Count.ToString();
        }

        #region Logging

        /// <summary>
        /// Log Levels
        /// </summary>
        public enum LogLevel
        {
            Normal = 1,
            Success = 2,
            Notice = 3,
            Warning = 4,
            Error = 5
        }

        /// <summary>
        /// Logs somthing
        /// </summary>
        /// <param name="text"></param>
        /// <param name="logLevel"></param>
        public static void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
            self.LogText(text, logLevel);
        }

        /// <summary>
        /// LogLevels per line for drawing
        /// </summary>
        public List<LogLevel> LogListBoxLogLevels = new List<LogLevel>();

        /// <summary>
        /// LogText delegate
        /// </summary>
        /// <param name="text"></param>
        /// <param name="logLevel"></param>
        private delegate void LogDelegate(string text, LogLevel logLevel);

        /// <summary>
        /// Adds text to the LogBox
        /// </summary>
        /// <param name="text"></param>
        /// <param name="logLevel"></param>
        public void LogText(string text, LogLevel logLevel = LogLevel.Normal)
        {
            if (!this.enableLogCheckBox.Checked)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.Invoke(new LogDelegate(this.LogText), text, logLevel);
                return;
            }

            if (this.batchLogFile != null)
            {
                File.AppendAllText(this.batchLogFile, string.Format("{0:HH:mm:ss} {1,-7} {2}{3}", DateTime.Now, logLevel, text, Environment.NewLine));
            }

            // Cut on 3000 rows
            if (this.logListBox.Items.Count == 3000)
            {
	            this.logListBox.Items.Clear();
	            this.LogListBoxLogLevels.Clear();
            }

            this.logListBox.Items.Add(string.Format("{0}:{1}:{2}  {3}", DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second, text));
            this.LogListBoxLogLevels.Add(logLevel);
            this.logListBox.SelectedIndex = this.logListBox.Items.Count - 1;
            this.logListBox.SelectedIndex = -1;
        }

        /// <summary>
        /// Draws ListBox items
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void logListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (!this.enableLogCheckBox.Checked)
            {
                return;
            }

            var listBox = sender as ListBox;
            if (listBox == null || listBox.Items.Count == 0) return;

            e.DrawBackground();

            Brush newBrush;
            switch (this.LogListBoxLogLevels[e.Index])
            {
                case LogLevel.Success:
                    newBrush = Brushes.LimeGreen;
                    break;
                case LogLevel.Notice:
                    newBrush = Brushes.CornflowerBlue;
                    break;
                case LogLevel.Warning:
                    newBrush = Brushes.Orange;
                    break;
                case LogLevel.Error:
                    newBrush = Brushes.Red;
                    break;
                default:
                    newBrush = Brushes.Black;
                    break;
            }

            e.Graphics.DrawString((listBox).Items[e.Index].ToString(), e.Font, newBrush, e.Bounds, StringFormat.GenericDefault);
            e.DrawFocusRectangle();
        }

        #endregion

        #region StatusBar

        /// <summary>
        /// The delegate method for InitProgressBar to stay thread save
        /// </summary>
        /// <param name="label"></param>
        private delegate void InitProgressBarDelegate(string label);

        /// <summary>
        /// This method sets the Progressbar to zero and sets the label to a defined value
        /// Requires an Invoke check to stay thread save
        /// </summary>
        /// <param name="label"></param>
        private void InitProgressBar(string label)
        {
            if (!this.InvokeRequired)
            {
	            this.statusLabel.Text = label;
	            this.statusProgressBar.Style = ProgressBarStyle.Blocks;
	            this.statusProgressBar.Value = 0;
            }
            else
	            this.Invoke(new InitProgressBarDelegate(this.InitProgressBar), label);
        }

        /// <summary>
        /// The delegate method for InitProgressBar to stay thread save
        /// </summary>
        /// <param name="label"></param>
        private delegate void InitProgressBarMarqueeDelegate(string label);

        /// <summary>
        /// This method sets the Progressbar to a Marquee effect (we don't know long the action takes) and sets the label to a defined value
        /// Requires an Invoke check to stay thread save
        /// </summary>
        /// <param name="label"></param>
        private void InitProgressBarMarquee(string label)
        {
            if (!this.InvokeRequired)
            {
	            this.statusLabel.Text = label;
	            this.statusProgressBar.Style = ProgressBarStyle.Marquee;
            }
            else
	            this.Invoke(new InitProgressBarMarqueeDelegate(this.InitProgressBarMarquee), label);
        }

        /// <summary>
        /// The delegate method for SetProgressBarValue to stay thread save
        /// </summary>
        /// <param name="value"></param>
        private delegate void SetProgressBarValueDelegate(int value);

        /// <summary>
        /// This method sets the status of the ProgressBar to the defined percentage value.
        /// Requires an Invoke check to stay thread save
        /// </summary>
        /// <param name="percentValue"></param>
        private void SetProgressBarValue(int percentValue)
        {
            if (!this.InvokeRequired)
            {
	            this.statusProgressBar.Value = percentValue;
            }
            else
	            this.Invoke(new SetProgressBarValueDelegate(this.SetProgressBarValue), percentValue);
        }

        /// <summary>
        /// The delegate method for ResetProgressBar to stay thread save
        /// </summary>
        private delegate void ResetProgressBarDelegate();

        /// <summary>
        /// This method resets the ProgressBar and sets the label to "Ready"
        /// Requires an Invoke check to stay thread save
        /// </summary>
        private void ResetProgressBar()
        {
            if (!this.InvokeRequired)
            {
	            this.statusLabel.Text = "Ready";
	            this.statusProgressBar.Style = ProgressBarStyle.Blocks;
	            this.statusProgressBar.Value = 0;
            }
            else
	            this.Invoke(new ResetProgressBarDelegate(this.ResetProgressBar));
        }

        public static void ProgressReset()
        {
            self.ResetProgressBar();
        }

        public static void ProgressStartMarquee(string label)
        {
            self.InitProgressBarMarquee(label);
        }

        public static void ProgressStart(string label)
        {
            self.InitProgressBar(label);
        }

        public static void ProgressUpdate(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            self.SetProgressBarValue(percent);
        }

        #endregion

        #region UI Actions

        private void preferencesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var form = new PreferencesForm();
            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
	            this.Initialize();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.batchMode)
            {
                Properties.Settings.Default.Reload();
                return;
            }

            Properties.Settings.Default.Save();
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private delegate void HandleRenderButtonDelegate(bool enabled);

        private void HandleRenderButton(bool enabled)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new HandleRenderButtonDelegate(this.HandleRenderButton), enabled);
                return;
            }

            this.renderButton.Enabled = enabled;
        }

        #region River Color

        private void riverColorSelectButton_Click(object sender, EventArgs e)
        {
            if (this.riversColorColorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
	            this.mapRiversColorTextBox.Text = string.Format("{0}{1}{2}", this.riversColorColorDialog.Color.R.ToString("X2"), this.riversColorColorDialog.Color.G.ToString("X2"), this.riversColorColorDialog.Color.B.ToString("X2"));
                Properties.Settings.Default.mapRiverColor = this.riversColorColorDialog.Color;
                Properties.Settings.Default.Save();
            }
        }

        private void riverUseColorDefault_CheckedChanged(object sender, EventArgs e)
        {
	        this.mapRiversColorTextBox.Enabled = !this.riversUseDefaultColorCheckBox.Checked;
	        this.riversColorSelectButton.Enabled = !this.riversUseDefaultColorCheckBox.Checked;
        }

        private void riversColorTextBox_TextChanged(object sender, EventArgs e)
        {
            if (this.mapRiversColorTextBox.Text.Length == 6)
            {
                try
                {
	                this.riversColorPreview.BackColor = ColorTranslator.FromHtml("#" + this.mapRiversColorTextBox.Text);
                    Properties.Settings.Default.mapRiverColor = ColorTranslator.FromHtml("#" + this.mapRiversColorTextBox.Text);
                    Properties.Settings.Default.Save();
                }
                catch { }
            }
        }

        private void riversColorTextBox_KeyDown(object sender, KeyEventArgs e)
        {
	        this.riversColorTextBox_TextChanged(null, null);
        }

        #endregion

        #region Bounds color

        private void boundsColorSelectButton_Click(object sender, EventArgs e)
        {
	        this.boundsColorDialog.Color = Properties.Settings.Default.mapBoundsColor;
            if (this.boundsColorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
	            this.mapBoundsColorTextBox.Text = string.Format("{0}{1}{2}", this.boundsColorDialog.Color.R.ToString("X2"), this.boundsColorDialog.Color.G.ToString("X2"), this.boundsColorDialog.Color.B.ToString("X2"));
                Properties.Settings.Default.mapBoundsColor = this.boundsColorDialog.Color;
                Properties.Settings.Default.Save();
            }
        }

        private void mapBoundsColorTextBox_TextChanged(object sender, EventArgs e)
        {
            if (this.mapRiversColorTextBox.Text.Length == 6)
            {
                try
                {
	                this.mapBoundsColorPreview.BackColor = ColorTranslator.FromHtml("#" + this.mapBoundsColorTextBox.Text);
                    Properties.Settings.Default.mapBoundsColor = ColorTranslator.FromHtml("#" + this.mapBoundsColorTextBox.Text);
                    Properties.Settings.Default.Save();
                }
                catch { }
            }
        }

        private void mapBoundsColorTextBox_KeyDown(object sender, KeyEventArgs e)
        {
	        this.mapBoundsColorTextBox_TextChanged(null, null);
        }

        #endregion

        #endregion

        /// <summary>
        /// Start rendering the zone(s)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void renderButton_Click(object sender, EventArgs e)
        {
            if (MpkWrapper.CheckGamePath())
            {
                Log("Game found...", LogLevel.Notice);

                if (this.SelectedZones.Count == 0)
                {
                    Log("Please select at least one Zone to render.", LogLevel.Error);
                }
                else
                {
	                this.HandleRenderButton(false);

                    var counter = 1;
                    foreach (var zone in this.SelectedZones)
                    {
                        Log(string.Format("Rendering {0} ({1})...", zone.Name, zone.Id), LogLevel.Notice);
                        this.currentMapLabel.Text = string.Format("| {0} ({1}) |", zone.Name, zone.Id);
                        this.queueProcessedLabel.Text = counter.ToString();
                        this.drawMapBackgroundWorker.RunWorkerAsync(zone);

                        while (this.drawMapBackgroundWorker.IsBusy)
                        {
                            Application.DoEvents();
                        }

                        counter++;
                    }

                    this.HandleRenderButton(true);
                }
            }
        }
        /// <summary>
        /// Load Image Delegate
        /// </summary>
        /// <param name="filename"></param>
        private delegate void LoadImageDelegate(string filename);

        /// <summary>
        /// Loads an image to the preview
        /// </summary>
        /// <param name="filename"></param>
        private void LoadImage(string filename)
        {
            if(!this.enableResultPreview.Checked)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.Invoke(new LoadImageDelegate(this.LoadImage), filename);
                return;
            }

            this.mapPreview.ImageLocation = filename;
        }

        /// <summary>
        /// Render selected Zones as Background Worker
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void drawMapBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            /*
            try
            {
            */
            var zone = (ZoneSelection)e.Argument;

            // Start BackgroundWorker
            Log(string.Format("Start creating map for zone {0} ...", zone.Id), LogLevel.Notice);

            // The filename
            var targetFileDirectory = this.directoryPatternTextBox.Text;
            if (string.IsNullOrEmpty(targetFileDirectory)) targetFileDirectory = "maps";

            targetFileDirectory = targetFileDirectory.Replace("{id}", zone.Id);
            targetFileDirectory = targetFileDirectory.Replace("{name}", zone.Name);
            targetFileDirectory = targetFileDirectory.Replace("{realm}", zone.Realm);
            targetFileDirectory = targetFileDirectory.Replace("{expansion}", zone.Expansion);
            targetFileDirectory = targetFileDirectory.Replace("{type}", zone.Type);
            targetFileDirectory = targetFileDirectory.Replace("{size}", this.TargetMapSize.ToString());
            targetFileDirectory = Tools.MakeValidDirectoryName(targetFileDirectory);

            var targetFileName = this.filePatternTextBox.Text;
            if (string.IsNullOrEmpty(targetFileName)) targetFileName = "zone{id}_{size}";

            // Replace some values
            targetFileName = targetFileName.Replace("{id}", zone.Id);
            targetFileName = targetFileName.Replace("{name}", zone.Name);
            targetFileName = targetFileName.Replace("{realm}", zone.Realm);
            targetFileName = targetFileName.Replace("{expansion}", zone.Expansion);
            targetFileName = targetFileName.Replace("{type}", zone.Type);
            targetFileName = targetFileName.Replace("{size}", this.TargetMapSize.ToString());
            targetFileName = Tools.MakeValidFileName(targetFileName);

            // File extension
            var fileExtension = "jpg";
            var selectedFileExtension = "JPEG";
            this.Invoke((MethodInvoker)delegate ()
            {
                selectedFileExtension = this.fileTypeComboBox.Text;
            });
            switch (selectedFileExtension)
            {
                case "PNG":
                    fileExtension = "png";
                    break;
                case "JPEG":
                default:
                    fileExtension = "jpg";
                    break;
            }

            // The Target File
            var targetFilePath = string.Format("{0}", (!string.IsNullOrEmpty(Properties.Settings.Default.targetMapPath)) ? Properties.Settings.Default.targetMapPath : Application.StartupPath);
            var mapFile = new FileInfo(string.Format("{0}\\{3}\\{1}.{2}", targetFilePath, targetFileName, fileExtension, targetFileDirectory));
            if (!Directory.Exists(mapFile.DirectoryName))
            {
                Directory.CreateDirectory(mapFile.DirectoryName);
            }

            if (this.skipIfFileExistsCheckbox.Checked && mapFile.Exists)
            {
                Log(string.Format("The target file \"{0}/{1}.{2}\" already exists. Skipping.", targetFileDirectory, targetFileName, fileExtension));
                return;
            }


            var lightmap = this.generateLightmapCheckBox.Checked;
            var lightmapZScale = Convert.ToDouble(this.heightmapZScaleTextBox.Value);
            var lightmapLightMin = Convert.ToDouble(this.heightmapLightMinTextBox.Value);
            var lightmapLightMax = Convert.ToDouble(this.heightmapLightMaxTextBox.Value);
            var lightmapZVector = new double[] { Convert.ToDouble(this.heightmapZVector1TextBox.Value), Convert.ToDouble(this.heightmapZVector2TextBox.Value), Convert.ToDouble(this.heightmapZVector3TextBox.Value) };

            var rivers = this.generateRiversCheckBox.Checked;
            var riversUseDefaultColor = this.riversUseDefaultColorCheckBox.Checked;
            var riversColor = Properties.Settings.Default.mapRiverColor;
            var riverOpacity = Convert.ToInt32(this.mapRiversOpacityTextBox.Value);

            var bounds = this.generateBoundsCheckBox.Checked;
            var boundsColor = Properties.Settings.Default.mapBoundsColor;
            var boundsOpacity = Convert.ToInt32(this.mapBoundsOpacityTextBox.Text);
            var excludeBoundsFromMap = this.excludeBoundsFromMapCheckbox.Checked;

            var drawFixtures = this.drawFixturesCheckBox.Checked;
            var drawFixturesBelowWater = this.drawFixturesBelowWaterCheckBox.Checked;
            var drawTrees = this.drawTreesCheckBox.Checked;

            // Generate the map
            using (var conf = new ZoneConfiguration(zone.Id, this.TargetMapSize))
            {
                // Create Background
                var background = new MapBackground(conf)
                                 {
		                                 DrawBackground = this.createBackgroundCheckBox.Checked
                                 };

                MainForm.Log("Rendering background ...", LogLevel.Notice);
                using (var map = background.Draw())
                {
                    if (map != null)
                    {
                        MainForm.Log("Finished background rendering!", LogLevel.Success);

                        // Create lightmap
                        if (lightmap)
                        {
                            MainForm.Log("Rendering lightmap ...", LogLevel.Notice);
                            var lightmapGenerator = new MapLightmap(conf)
                                                    {
		                                                    ZScale = lightmapZScale,
		                                                    LightMin = lightmapLightMin,
		                                                    LightMax = lightmapLightMax,
		                                                    ZVector = lightmapZVector
                                                    };
                            lightmapGenerator.RecalculateLights();
                            lightmapGenerator.Draw(map);
                            MainForm.Log("Finished lightmap rendering!", LogLevel.Success);
                        }

                        // We need this for fixtures
                        MainForm.Log("Loading water configurations ...", LogLevel.Notice);
                        var river = new MapWater(conf);
                        MainForm.Log("Finished loading water configurations!", LogLevel.Success);

                        MapFixtures fixturesGenerator = null;
                        if (drawFixtures || drawFixturesBelowWater || drawTrees)
                        {
                            MainForm.Log("Loading fixtures ...", LogLevel.Notice);
                            fixturesGenerator = new MapFixtures(conf, river.WaterAreas);
                            fixturesGenerator.DrawFixtures = this.drawFixturesCheckBox.Checked ||this.drawFixturesBelowWaterCheckBox.Checked;
                            fixturesGenerator.DrawTrees = this.drawTreesCheckBox.Checked;
                            fixturesGenerator.DrawTreesAsImages = this.treesAsImages.Checked;
                            fixturesGenerator.TreeTransparency = Convert.ToInt32(this.mapTreeTransparencyTextBox.Value);
                            fixturesGenerator.Start();
                            MainForm.Log("Finished loading fixtures!", LogLevel.Success);
                        }

                        // Draw Fixtures below water
                        if (drawFixturesBelowWater)
                        {
                            MainForm.Log("Rendering fixtures below water level ...", LogLevel.Notice);
                            fixturesGenerator.Draw(map, true);
                            MainForm.Log("Finished rendering fixtures below water level!", LogLevel.Success);
                        }

                        // Create Rivers
                        if (rivers)
                        {
                            MainForm.Log("Rendering water ...", LogLevel.Notice);
                            river.WaterColor = riversColor;
                            river.WaterTransparency = riverOpacity;
                            river.UseClientColors = riversUseDefaultColor;
                            river.Draw(map);
                            MainForm.Log("Finished water rendering!", LogLevel.Success);
                        }

                        // Draw Fixtures above water
                        if (drawFixtures || drawTrees)
                        {
                            MainForm.Log("Rendering fixtures above water level ...", LogLevel.Notice);
                            fixturesGenerator.Draw(map, false);
                            MainForm.Log("Finished rendering fixtures above water level!", LogLevel.Success);
                        }

                        if (fixturesGenerator != null)
                        {
                            fixturesGenerator.Dispose();
                        }

                        // Create bounds
                        if (bounds)
                        {
                            MainForm.Log("Adding zone bounds ...", LogLevel.Notice);
                            var mapBounds = new MapBounds(conf)
                                            {
		                                            BoundsColor = boundsColor,
		                                            Transparency = boundsOpacity,
		                                            ExcludeFromMap = excludeBoundsFromMap
                                            };
                            mapBounds.Draw(map);
                            MainForm.Log("Finished zone bunds!", LogLevel.Success);
                        }

                        MainForm.Log(string.Format("Writing map image {0} ...", mapFile.Name));
                        ProgressStartMarquee("Writing map image ...");
                        map.Quality = Convert.ToUInt32(this.mapQualityTextBox.Value);
                        map.Write(mapFile.FullName);
                    }
                }
            }

            if (File.Exists(mapFile.FullName))
            {
	            this.LoadImage(mapFile.FullName);
                ProgressReset();
            }
            else
            {
                Log("Errors during progress!", LogLevel.Error);
            }
            /*
            }
            catch (Exception ex)
            {
                MainForm.Log("Unhandled Exception thrown!", LogLevel.error);
                MainForm.Log(ex.Message, LogLevel.error);
                MainForm.Log(ex.StackTrace, LogLevel.error);
            }
            */
        }

        private void drawMapBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MainForm.Log("Unhandled Exception thrown!", LogLevel.Error);
                MainForm.Log(e.Error.Message, LogLevel.Error);
                MainForm.Log(e.Error.StackTrace, LogLevel.Error);    
            }
            else
            {
                Log("Finished without errors!", LogLevel.Success);
            }
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
	        this.splitContainer1.SplitterDistance = this.flowLayoutSizerPanel.Location.X + this.flowLayoutSizerPanel.Width;
        }

        private void treesAsShadedModel_CheckedChanged(object sender, EventArgs e)
        {
	        this.treesAsImages.Checked = !this.treesAsShadedModel.Checked;
        }

        private void treesAsImages_CheckedChanged(object sender, EventArgs e)
        {
	        this.treesAsShadedModel.Checked = !this.treesAsImages.Checked;
        }

        private void drawTreesCheckBox_CheckedChanged(object sender, EventArgs e)
        {
	        this.treesAsImages.Enabled = this.drawTreesCheckBox.Checked;
	        this.treesAsShadedModel.Enabled = this.drawTreesCheckBox.Checked;
        }

        private void dawnOfLightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenUrl(@"http://www.dolserver.net");
        }

        private static void OpenUrl(string url)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void aboutMapCreatorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            (new About()).ShowDialog();
        }

        private void reportABugToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenUrl(@"http://www.dolserver.net/viewtopic.php?f=69&t=21710");
        }

        private void createShapedNIFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            (new ShapedNifForm()).ShowDialog();
        }

        private void clearfixturesPolygonCacheToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(MessageBox.Show("Do you really want to delete the fixture polygon cache?", "Delete fixture polygons", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                var fixturesCache = new FileInfo(Application.StartupPath + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "polys.mpk");
                if (fixturesCache.Exists)
                {
                    fixturesCache.Delete();
                }
            }
        }

        private void clearheightmapsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Do you really want to delete all prerendered heightmaps?", "Delete heightmaps", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                var heightmapsCache = new DirectoryInfo(Application.StartupPath + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "heightmaps");
                if (heightmapsCache.Exists)
                {
                    heightmapsCache.Delete(true);
                }
            }
        }

        private void widthTextBox_ValueChanged(object sender, EventArgs e)
        {
            var steps = new int[] { 256, 512, 1024, 2048, 4096, 8192, 16384, 32768 };
            var newValue = 256;

            var currentValue = (int)this.widthTextBox.Value;
            if (currentValue < steps.First())
            {
                newValue = steps.First();
            }
            else if (currentValue > steps.Last())
            {
                newValue = steps.Last();
            }
            else
            {
                var closestValue = steps.Aggregate((current, next) => Math.Abs((long)current - this.widthTextBox.Value) < Math.Abs((long)next - this.widthTextBox.Value) ? current : next);
                var closestIndex = Array.IndexOf(steps, closestValue);

                if (currentValue > closestValue)
                {
                    newValue = steps[closestIndex + 1];
                }
                else if (currentValue < closestValue)
                {
                    newValue = steps[closestIndex - 1];
                }
                else
                {
                    newValue = closestValue;
                }
            }

            this.widthTextBox.Value = newValue;
        }
    }
}
