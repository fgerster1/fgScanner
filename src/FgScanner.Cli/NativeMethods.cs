using System.Runtime.InteropServices;

namespace FgScanner.Cli;

internal static class NativeMethods
{
    public const uint EsContinuous = 0x80000000;

    public const uint EsSystemRequired = 0x00000001;

    [DllImport("kernel32")]
    public static extern uint SetThreadExecutionState(uint flags);
}
