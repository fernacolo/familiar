using System.Runtime.InteropServices;

namespace fam.Native
{
    [StructLayout( LayoutKind.Sequential )]
    public struct COORD
    {
        public short X;
        public short Y;

        public COORD( short X, short Y )
        {
            this.X = X;
            this.Y = Y;
        }
    };
}