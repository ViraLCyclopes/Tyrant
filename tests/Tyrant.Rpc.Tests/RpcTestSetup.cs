using System.Runtime.CompilerServices;
using Tyrant.Core.Tests;

namespace Tyrant.Rpc.Tests;

internal static class RpcTestSetup
{
#pragma warning disable CA2255 // a module initializer in a test assembly is the point here
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseTestRecycleBin() => TestRecycleBin.Install();
}
