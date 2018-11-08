using System.Runtime.InteropServices;

namespace wcmd.Native
{
    [StructLayout( LayoutKind.Sequential )]
    internal struct POINT
    {
        int x;
        int y;
    }
}