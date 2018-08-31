using System;
using System.Runtime.InteropServices;

namespace wcmd.Native
{
    [StructLayout( LayoutKind.Sequential )]
    internal struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }
}