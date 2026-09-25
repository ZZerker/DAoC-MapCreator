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

namespace MapCreator.Classes
{
    public struct ZoneSelection: IEquatable<ZoneSelection>
    {
	    public string Id { get; set; }

	    public string Name { get; set; }

	    public string Realm { get; set; }

	    public string Expansion { get; set; }

	    public string Type { get; set; }

	    internal ZoneSelection(string id, string name, string expansion, string realm, string type)
        {
            this.Id = id;
            this.Name = name;
            this.Realm = realm;
            this.Expansion = expansion;
            this.Type = type;
        }

        public override string ToString()
        {
            return this.Name + " (" + this.Id + ")";
        }

        #region Equality members
        public bool Equals(ZoneSelection other)
        {
	        return this.Id == other.Id&&this.Name == other.Name&&this.Realm == other.Realm&&this.Expansion == other.Expansion&&this.Type == other.Type;
        }

        public override bool Equals(object obj)
        {
	        return obj is ZoneSelection other&&this.Equals(other);
        }

        public override int GetHashCode()
        {
	        unchecked
	        {
		        var hashCode = (this.Id != null?this.Id.GetHashCode():0);
		        hashCode = (hashCode * 397)^(this.Name != null?this.Name.GetHashCode():0);
		        hashCode = (hashCode * 397)^(this.Realm != null?this.Realm.GetHashCode():0);
		        hashCode = (hashCode * 397)^(this.Expansion != null?this.Expansion.GetHashCode():0);
		        hashCode = (hashCode * 397)^(this.Type != null?this.Type.GetHashCode():0);
		        return hashCode;
	        }
        }

        public static bool operator ==(ZoneSelection left, ZoneSelection right)
        {
	        return left.Equals(right);
        }

        public static bool operator !=(ZoneSelection left, ZoneSelection right)
        {
	        return !left.Equals(right);
        }
        #endregion
    }
}
