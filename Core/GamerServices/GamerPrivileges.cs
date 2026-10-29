using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.GamerServices
{
	/// <summary>
	/// Describes what a signed-in gamer is allowed to do.
	/// </summary>
	public class GamerPrivileges
	{
		/// <summary>
		/// Gets or sets whether the gamer may take part in online sessions. <see cref="SignedInGamer"/> keeps it equal to <see cref="SignedInGamer.IsSignedInToLive"/>.
		/// </summary>
		public bool AllowOnlineSessions { get; set; }
	}
}
