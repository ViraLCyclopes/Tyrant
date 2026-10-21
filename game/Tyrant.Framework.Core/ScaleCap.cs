using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// The game's only cap on an animal's final scale: AnimalNative.CurrentScale's getter compares the scale with 0.05 and 2,
    /// and returns (and logs) that constant when it is outside. Tyrant swaps those constants for its limit, all or none.
    /// </summary>
    public static class ScaleCap
    {
        public const string Type = "PrehistoricKingdom.AnimalsV2.AnimalNative", Property = "CurrentScale";

        /// <summary>
        /// The positions of the getter's cap constants: every ldc.r4 0.05 (Min) and ldc.r4 2 (Max). Null unless the code is as
        /// Tyrant expects: no other float constant, and each constant is first compared (a conditional branch or compare right
        /// after it) and last returned (a ret right after it).
        /// </summary>
        public static (int[] Min, int[] Max)? Find(IReadOnlyList<(OpCode Op, object? Operand)> code)
        {
            var min = new List<int>();
            var max = new List<int>();
            for (var i = 0; i < code.Count; i++)
            {
                if (code[i].Op != OpCodes.Ldc_R4) continue;
                if (!(code[i].Operand is float value)) return null;
                if (value == SizeMath.FinalMin) min.Add(i);
                else if (value == SizeMath.FinalMax) max.Add(i);
                else return null;
            }
            bool Compared(int i) => i + 1 < code.Count && IsCompare(code[i + 1].Op);
            bool Returned(int i) => i + 1 < code.Count && code[i + 1].Op == OpCodes.Ret;
            foreach (var found in new[] { min, max })
                if (found.Count < 2 || !Compared(found[0]) || !Returned(found.Last())) return null;
            return (min.ToArray(), max.ToArray());
        }

        private static bool IsCompare(OpCode op) =>
            op.FlowControl == FlowControl.Cond_Branch || op == OpCodes.Clt || op == OpCodes.Clt_Un || op == OpCodes.Cgt || op == OpCodes.Cgt_Un;
    }
}
