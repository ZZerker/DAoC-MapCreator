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

namespace MapCreator.Classes.MapCreation.Fixtures.Objects
{
	internal class FixtureRow
    {
	    #region Getter/Setter

        public int Id { get; set; }

        public int NifId { get; set; }


        public string TextualName { get; set; }


        public double X { get; set; }


        public double Y { get; set; }


        public double Z { get; set; }


        public double A { get; set; }


        public double Scale { get; set; }


        public bool OnGround { get; set; }


        public bool Flip { get; set; }

        public double Angle3D { get; set; } = 0;

        public double AxisX3D { get; set; } = 0;


        public double AxisY3D { get; set; } = 0;

        public double AxisZ3D { get; set; } = 0;
        #endregion

        public FixtureRow()
        {
        }

        public override string ToString()
        {
            return "id: " + this.Id.ToString() + "nifId: " + this.NifId.ToString() + "; " + this.TextualName;
        }
    }
}
