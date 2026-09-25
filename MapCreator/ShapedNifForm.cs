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
using System.Windows.Forms;

namespace MapCreator
{
    public partial class ShapedNifForm : Form
    {
        public ShapedNifForm()
        {
	        this.InitializeComponent();
        }

        private void selectFileButton_Click(object sender, EventArgs e)
        {
            var file = Properties.Settings.Default.shapedNifLastFile;
            if (!string.IsNullOrEmpty(this.selectedFileTextBox.Text))
            {
                file = this.selectedFileTextBox.Text;
            }

            //selectFileDialog.InitialDirectory = System.IO.Path.GetDirectoryName(file);
            this.selectFileDialog.FileName = System.IO.Path.GetFileName(file);

            if (this.selectFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
	            this.selectedFileTextBox.Text = this.selectFileDialog.FileName;
	            this.selectFileDialog.FileName = this.selectFileDialog.FileName;
                Properties.Settings.Default.Save();
            }
        }
    }
}
