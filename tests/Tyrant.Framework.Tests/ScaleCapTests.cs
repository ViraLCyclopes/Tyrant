using System.Reflection.Emit;
using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class ScaleCapTests
{
    // AnimalNative.get_CurrentScale as the game's Assembly-CSharp has it (operands that do not matter here are null).
    private static List<(OpCode, object?)> Getter() =>
    [
        (OpCodes.Ldarg_0, null), (OpCodes.Call, null), (OpCodes.Ldfld, null), (OpCodes.Ldarg_0, null), (OpCodes.Call, null),
        (OpCodes.Ldarg_0, null), (OpCodes.Call, null), (OpCodes.Ldarg_0, null), (OpCodes.Ldfld, null), (OpCodes.Call, null),
        (OpCodes.Stloc_0, null),
        (OpCodes.Ldloc_0, null), (OpCodes.Ldc_R4, 0.05f), (OpCodes.Bge_Un_S, null),                                   // 11-13
        (OpCodes.Ldstr, "Animal {0} has invalid scale {1}! Clamping to {2}"), (OpCodes.Ldarg_0, null), (OpCodes.Call, null),
        (OpCodes.Box, null), (OpCodes.Ldloc_0, null), (OpCodes.Box, null), (OpCodes.Ldc_R4, 0.05f), (OpCodes.Box, null), // 20
        (OpCodes.Call, null), (OpCodes.Call, null), (OpCodes.Ldc_R4, 0.05f), (OpCodes.Ret, null),                     // 24
        (OpCodes.Ldloc_0, null), (OpCodes.Ldc_R4, 2f), (OpCodes.Ble_Un_S, null),                                      // 27
        (OpCodes.Ldstr, "Animal {0} has invalid scale {1}! Clamping to {2}"), (OpCodes.Ldarg_0, null), (OpCodes.Call, null),
        (OpCodes.Box, null), (OpCodes.Ldloc_0, null), (OpCodes.Box, null), (OpCodes.Ldc_R4, 2f), (OpCodes.Box, null),   // 35
        (OpCodes.Call, null), (OpCodes.Call, null), (OpCodes.Ldc_R4, 2f), (OpCodes.Ret, null),                         // 39
        (OpCodes.Ldloc_0, null), (OpCodes.Ret, null),
    ];

    [Fact]
    public void Finds_every_cap_constant_of_the_games_getter()
    {
        var found = ScaleCap.Find(Getter());

        Assert.NotNull(found);
        Assert.Equal([12, 20, 24], found.Value.Min);
        Assert.Equal([27, 35, 39], found.Value.Max);
    }

    [Fact]
    public void Another_float_constant_matches_nothing()
    {
        var code = Getter();
        code[35] = (OpCodes.Ldc_R4, 3f);

        Assert.Null(ScaleCap.Find(code));
    }

    [Fact]
    public void A_cap_that_is_not_compared_and_returned_matches_nothing()
    {
        var noCompare = Getter();
        noCompare[13] = (OpCodes.Pop, null);
        Assert.Null(ScaleCap.Find(noCompare));

        var noReturn = Getter();
        noReturn[40] = (OpCodes.Stloc_1, null); // the last 2 no longer returned
        Assert.Null(ScaleCap.Find(noReturn));

        var noMax = Getter().Take(26).Concat(new (OpCode, object?)[] { (OpCodes.Ldloc_0, null), (OpCodes.Ret, null) }).ToList();
        Assert.Null(ScaleCap.Find(noMax));
    }
}
