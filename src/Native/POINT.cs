using System.Runtime.InteropServices;

namespace fam.Native
{
    [StructLayout( LayoutKind.Sequential )]
    internal struct POINT
    {
        int x;
        int y;
    }
}