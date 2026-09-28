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
using System.Threading.Tasks;
using MapCreator.Classes;
using MapCreator.Classes.MapCreation;
using MapCreator.Classes.Rendering;

namespace MapCreator
{

    public partial class MainForm : Form, IRenderReporter
    {
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
            AppLog.Reporter = this;
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
        /// Logs somthing
        /// </summary>
        /// <param name="text"></param>
        /// <param name="logLevel"></param>
        public void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
            this.LogText(text, logLevel);
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
            // Queued, not blocking: render threads must not wait for the UI
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new LogDelegate(this.LogText), text, logLevel);
                return;
            }

            if (!this.enableLogCheckBox.Checked)
            {
                return;
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

        public void ProgressReset()
        {
            this.ResetProgressBar();
        }

        public void ProgressStartMarquee(string label)
        {
            this.InitProgressBarMarquee(label);
        }

        public void ProgressStart(string label)
        {
            this.InitProgressBar(label);
        }

        public void ProgressUpdate(int percent)
        {
            this.SetProgressBarValue(Math.Clamp(percent, 0, 100));
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
            if (this.mapBoundsColorTextBox.Text.Length == 6)
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
        private async void renderButton_Click(object sender, EventArgs e)
        {
            await this.RenderSelectedZonesAsync();
        }

        private async Task RenderSelectedZonesAsync()
        {
            if (!MpkWrapper.CheckGamePath())
            {
                return;
            }

            this.Log("Game found...", LogLevel.Notice);

            if (this.SelectedZones.Count == 0)
            {
                this.Log("Please select at least one Zone to render.", LogLevel.Error);
                return;
            }

            this.HandleRenderButton(false);

            var settings = this.CaptureRenderSettings();
            var zones = this.SelectedZones.ToList();
            var batch = new ZoneBatch(settings, this) { ZoneStarted = this.ShowCurrentZone, MapWritten = mapFile => this.LoadImage(mapFile.FullName) };
            try
            {
                await Task.Run(() => batch.Run(zones));
            }
            finally
            {
                this.HandleRenderButton(true);
            }
        }

        private void ShowCurrentZone(ZoneSelection zone, int number)
        {
            this.BeginInvoke(() =>
            {
                this.currentMapLabel.Text = string.Format("| {0} ({1}) |", zone.Name, zone.Id);
                this.queueProcessedLabel.Text = number.ToString();
            });
        }

        private RenderSettings CaptureRenderSettings()
        {
            var settings = Properties.Settings.Default;
            return new RenderSettings
            {
                MapSize = this.TargetMapSize,
                Parallel = Convert.ToInt32(this.parallelZonesUpDown.Value),
                TargetPath = !string.IsNullOrEmpty(settings.targetMapPath) ? settings.targetMapPath : Application.StartupPath,
                DirectoryPattern = this.directoryPatternTextBox.Text,
                FilePattern = this.filePatternTextBox.Text,
                FileType = this.fileTypeComboBox.Text,
                Quality = Convert.ToUInt32(this.mapQualityTextBox.Value),
                SkipIfFileExists = this.skipIfFileExistsCheckbox.Checked,
                LabelsOnly = this.labelsOnlyCheckBox.Checked,
                DrawBackground = this.createBackgroundCheckBox.Checked,
                Lightmap = this.generateLightmapCheckBox.Checked,
                LightmapZScale = Convert.ToDouble(this.heightmapZScaleTextBox.Value),
                LightmapLightMin = Convert.ToDouble(this.heightmapLightMinTextBox.Value),
                LightmapLightMax = Convert.ToDouble(this.heightmapLightMaxTextBox.Value),
                LightmapZVector = new[] { Convert.ToDouble(this.heightmapZVector1TextBox.Value), Convert.ToDouble(this.heightmapZVector2TextBox.Value), Convert.ToDouble(this.heightmapZVector3TextBox.Value) },
                Rivers = this.generateRiversCheckBox.Checked,
                RiversUseDefaultColor = this.riversUseDefaultColorCheckBox.Checked,
                RiversColor = settings.mapRiverColor,
                RiverOpacity = Convert.ToInt32(this.mapRiversOpacityTextBox.Value),
                DepthShadedWater = this.depthShadedWaterCheckBox.Checked,
                Bounds = this.generateBoundsCheckBox.Checked,
                BoundsColor = settings.mapBoundsColor,
                BoundsOpacity = Convert.ToInt32(this.mapBoundsOpacityTextBox.Text),
                ExcludeBoundsFromMap = this.excludeBoundsFromMapCheckbox.Checked,
                DrawFixtures = this.drawFixturesCheckBox.Checked,
                DrawFixturesBelowWater = this.drawFixturesBelowWaterCheckBox.Checked,
                DrawKeeps = this.drawKeepsCheckBox.Checked,
                DrawTrees = this.drawTreesCheckBox.Checked,
                TreeTransparency = Convert.ToInt32(this.mapTreeTransparencyTextBox.Value)
            };
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
            if (this.InvokeRequired)
            {
                this.Invoke(new LoadImageDelegate(this.LoadImage), filename);
                return;
            }

            if(!this.enableResultPreview.Checked)
            {
                return;
            }

            this.mapPreview.ImageLocation = filename;
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            // A minimized window has no room for the splitter
            if (this.WindowState == FormWindowState.Minimized)
            {
                return;
            }

	        this.splitContainer1.SplitterDistance =this.flowLayoutSizerPanel.Location.X + this.flowLayoutSizerPanel.Width;
        }

        private void drawTreesCheckBox_CheckedChanged(object sender, EventArgs e)
        {
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
                Classes.MapCreation.Fixtures.FixtureCache.Clear();
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
