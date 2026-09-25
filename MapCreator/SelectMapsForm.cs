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
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Collections;
using System.ComponentModel;
using MapCreator.Classes;
using MapCreator.data;

namespace MapCreator
{
	internal partial class SelectMapsForm : Form
    {
	    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	    public List<ZoneSelection> SelectedZones { get; set; } = new List<ZoneSelection>();

	    private readonly List<TreeNode> AllNodes = new List<TreeNode>();

        public SelectMapsForm()
        {
	        this.InitializeComponent();
	        this.InitializeMapTreeView();

	        this.selectedMapsListBox.DataSource = this.SelectedZones.OrderBy(s => s.Name).ToList();
	        this.selectedMapsListBox.DisplayMember = "Name";
	        this.selectedMapsListBox.ValueMember = "Id";

            // Load Presets
            this.presetsComboBox.DataSource = DataWrapper.GetPresetRows();
            this.presetsComboBox.DisplayMember = "Name";
            //presetsComboBox.ValueMember = "Id";
        }

        public void Preselect(List<ZoneSelection> preselect)
        {
	        this.SelectedZones = preselect;
	        this.UpdateSelectedMapsListBox();
        }

        private void UpdateSelectedMapsListBox()
        {
	        this.selectedMapsListBox.DataSource = null;
	        this.selectedMapsListBox.DataSource = this.SelectedZones.OrderBy(s => s.Id).ToList();
        }

        private void UpdatePresets()
        {
	        this.presetsComboBox.DataSource = DataWrapper.GetPresetRows();
        }

        private void InitializeMapTreeView()
        {
            foreach (var realm in DataWrapper.GetRealms())
            {
                var realmNode = new TreeNode(realm);

                foreach (var expansion in DataWrapper.GetExpansionsByRealm(realm))
                {
                    var expansionNode = new TreeNode(expansion);

                    foreach (var mapType in DataWrapper.GetZoneTypesByRealmAndExpansion(realm, expansion).OrderBy(o => o.ToString()))
                    {
                        var mapTypeNode = new TreeNode(mapType);

                        foreach (var zone in DataWrapper.GetZonesByRealmAndExpansionAndType(realm, expansion, mapType).OrderBy(o => o.Key))
                        {
                            if (mapType == "Capitol" || mapType == "Indoor" || mapType == "Dungeons" || mapType == "Instances") continue;

                            var currentZone = new ZoneSelection(zone.Key, zone.Value, expansion, realm, mapType);
                            var zoneNode = new TreeNode(currentZone.ToString())
                                           {
		                                           Tag = currentZone
                                           };

                            this.AllNodes.Add(zoneNode);
                            mapTypeNode.Nodes.Add(zoneNode);
                        }

                        if (mapTypeNode.Nodes.Count > 0)
                        {
                            expansionNode.Nodes.Add(mapTypeNode);
                        }
                    }

                    if (expansionNode.Nodes.Count == 1)
                    {
                        foreach (TreeNode node in expansionNode.Nodes[0].Nodes)
                        {
                            expansionNode.Nodes.Add(node);
                        }
                        expansionNode.Nodes.RemoveAt(0);
                        realmNode.Nodes.Add(expansionNode);
                    }
                    else if (expansionNode.Nodes.Count > 1)
                    {
                        realmNode.Nodes.Add(expansionNode);
                    }
                }

                this.mapsTreeView.Nodes.Add(realmNode);
            }
        }

        private void AddZonesRecursive(TreeNode node)
        {
            if(node.Tag is ZoneSelection tag)
            {
                if (!this.SelectedZones.Contains(tag))
                {
	                this.SelectedZones.Add(tag);
                }
            }
            else
            {
                foreach(TreeNode childNode in node.Nodes)
                {
	                this.AddZonesRecursive(childNode);
                }
            }
        }

        private void addAllButton_Click(object sender, EventArgs e)
        {
            if(this.mapsTreeView.SelectedNode != null && !(this.mapsTreeView.SelectedNode.Tag is ZoneSelection))
            {
	            this.AddZonesRecursive(this.mapsTreeView.SelectedNode);
            }
            else if(this.mapsTreeView.SelNodes.Count > 0)
            {
                foreach(DictionaryEntry nodeEntry in this.mapsTreeView.SelNodes)
                {
	                this.AddZonesRecursive(((MWCommon.MWTreeNodeWrapper)nodeEntry.Value).Node);
                }
            }

            this.UpdateSelectedMapsListBox();
        }

        private void addButton_Click(object sender, EventArgs e)
        {
            foreach (DictionaryEntry entry in this.mapsTreeView.SelNodes)
            {
                var nodeWrapper = (MWCommon.MWTreeNodeWrapper)entry.Value;
                if (nodeWrapper.Node.Tag is ZoneSelection tag)
                {
                    if (!this.SelectedZones.Contains(tag))
                    {
	                    this.SelectedZones.Add(tag);
                    }
                }
            }

            this.UpdateSelectedMapsListBox();
        }

        private void removeButton_Click(object sender, EventArgs e)
        {
            foreach (ZoneSelection selectedZone in this.selectedMapsListBox.SelectedItems)
            {
	            this.SelectedZones.Remove(selectedZone);
            }

            this.UpdateSelectedMapsListBox();
        }

        private void removeAllButton_Click(object sender, EventArgs e)
        {
	        this.SelectedZones.Clear();
	        this.UpdateSelectedMapsListBox();
        }

        private void saveNewPresetButton_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(this.newPresetTextBox.Text))
            {
                var zoneIds = new List<string>();
                foreach (ZoneSelection selectedZone in this.selectedMapsListBox.Items) zoneIds.Add(selectedZone.Id);
                DataWrapper.AddPresetRow(this.newPresetTextBox.Text, zoneIds);

                this.UpdatePresets();
                this.newPresetTextBox.Text = "";
            }
            else
            {
                MessageBox.Show("Please enter a preset name.");
            }
        }

        private void loadPresetButton_Click(object sender, EventArgs e)
        {
            if (this.presetsComboBox.SelectedItem == null || !(this.presetsComboBox.SelectedItem is MapCreatorData.ZoneSelectionPresetsRow preset))
            {
                return;
            }

            this.SelectedZones.Clear();

            foreach (var zoneId in preset.Zones.Split(','))
            {
                var result = this.AllNodes.Where(n => ((ZoneSelection)n.Tag).Id == zoneId).Select(n => (ZoneSelection)n.Tag);
                if (result.Any())
	                this.SelectedZones.Add(result.First());
            }

            this.UpdateSelectedMapsListBox();
        }

        private void savePresetButton_Click(object sender, EventArgs e)
        {
            if (this.presetsComboBox.SelectedItem == null || !(this.presetsComboBox.SelectedItem is MapCreatorData.ZoneSelectionPresetsRow preset))
            {
                return;
            }

            var zoneIds = new List<string>();
            foreach (ZoneSelection selectedZone in this.selectedMapsListBox.Items) zoneIds.Add(selectedZone.Id);
            preset.Zones = string.Join(",", zoneIds);

            DataWrapper.SavePresets();
        }

        private void deletePresetButton_Click(object sender, EventArgs e)
        {
            if (this.presetsComboBox.SelectedItem == null || !(this.presetsComboBox.SelectedItem is MapCreatorData.ZoneSelectionPresetsRow preset))
            {
                return;
            }

            DataWrapper.RemovePreset(preset);
            this.UpdatePresets();
        }

        private void selectedMapsListBox_DataSourceChanged(object sender, EventArgs e)
        {
	        this.selectMapsCounterLabel.Text = this.SelectedZones.Count.ToString();
        }

        private void mapsTreeView_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Tag is ZoneSelection tag)
            {
	            this.SelectedZones.Add(tag);
	            this.UpdateSelectedMapsListBox();
            }
        }
    }
}
