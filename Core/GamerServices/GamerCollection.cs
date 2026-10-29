namespace Microsoft.Xna.Framework.GamerServices
{
	/// <summary>
	/// Collection of gamers.
	/// </summary>
	public class GamerCollection<T> : System.Collections.ObjectModel.ReadOnlyCollection<T>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GamerCollection{T}"/> class wrapping <paramref name="list"/>.
        /// </summary>
        public GamerCollection(IList<T> list) : base(list) { }
    }
}
