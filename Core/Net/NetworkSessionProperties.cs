using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Properties used when creating or searching for network sessions: eight integer slots, as in XNA.
    /// A slot that is null is not set when creating a session and is not compared when searching for one.
    /// </summary>
    public class NetworkSessionProperties : IList<int?>
    {
        /// <summary>The number of slots.</summary>
        public const int SlotCount = 8;

        private readonly int?[] values = new int?[SlotCount];

        /// <summary>
        /// Gets or sets a slot.
        /// </summary>
        public int? this[int index]
        {
            get => values[CheckIndex(index)];
            set => values[CheckIndex(index)] = value;
        }

        /// <summary>The number of slots, always 8.</summary>
        public int Count => SlotCount;

        public bool IsReadOnly => false;

        /// <summary>Sets every slot back to null.</summary>
        public void Clear() => Array.Clear(values, 0, values.Length);

        public bool Contains(int? item) => IndexOf(item) >= 0;

        public int IndexOf(int? item) => Array.IndexOf(values, item);

        public void CopyTo(int?[] array, int arrayIndex) => values.CopyTo(array, arrayIndex);

        public IEnumerator<int?> GetEnumerator() => ((IEnumerable<int?>)values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => values.GetEnumerator();

        // the number of slots is fixed
        public void Add(int? item) => throw new NotSupportedException();
        public void Insert(int index, int? item) => throw new NotSupportedException();
        public bool Remove(int? item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();

        private static int CheckIndex(int index)
        {
            if (index < 0 || index >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(index), "A slot is 0 to 7.");
            return index;
        }

        /// <summary>
        /// The slots that are set, keyed by slot number, which is how sessions carry them.
        /// </summary>
        internal IDictionary<string, object> ToDictionary()
        {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < SlotCount; i++)
                if (values[i] is { } slotValue)
                    result[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = slotValue;
            return result;
        }
    }
}
