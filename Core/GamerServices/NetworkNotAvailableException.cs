using Microsoft.Xna.Framework.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.GamerServices
{
	/// <summary>
	/// Exception thrown when a network operation is attempted but no network is available.
	/// </summary>
	public class NetworkNotAvailableException : NetworkException
	{
	}
}
