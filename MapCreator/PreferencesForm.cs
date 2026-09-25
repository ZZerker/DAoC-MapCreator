//
// MapCreator
// Copyright(C) 2015 Stefan Schäfer <merec@merec.org>
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
using System.Windows.Forms;

namespace MapCreator
{
    public partial class PreferencesForm : Form
    {
        public PreferencesForm()
        {
	        this.InitializeComponent();
        }

        private void gamePathTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            Properties.Settings.Default.Save();
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();
            this.DialogResult = System.Windows.Forms.DialogResult.OK;
        }

        private void gamePathBrowseButton_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(this.gamePathTextBox.Text))
            {
	            this.gamePathFileBrowser.SelectedPath = this.gamePathTextBox.Text;
            }

            if (this.gamePathFileBrowser.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
	            this.gamePathTextBox.Text = this.gamePathFileBrowser.SelectedPath;
            }
        }

        private void targetMapBrowseButton_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(this.targetMapPathTextBox.Text))
            {
	            this.targetMapPathBrowser.SelectedPath = this.targetMapPathTextBox.Text;
            }

            if (this.targetMapPathBrowser.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
	            this.targetMapPathTextBox.Text = this.targetMapPathBrowser.SelectedPath;
            }
        }
    }
}
