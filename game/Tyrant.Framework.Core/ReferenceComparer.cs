using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Tyrant.Framework.Core
{
    /// <summary>Compares objects by reference, whatever their own Equals does (the game's skin objects as dictionary keys).</summary>
    public sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object x, object y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
