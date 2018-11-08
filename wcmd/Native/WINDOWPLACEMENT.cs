using System;
using System.Runtime.InteropServices;

namespace wcmd.Native
{
    [StructLayout( LayoutKind.Sequential )]
    internal struct WINDOWPLACEMENT
    {
        public uint length;
        public WindowPlacementFlags flags;
        public ShowWindowValue showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [Flags]
    internal enum WindowPlacementFlags : uint
    {
        WPF_SETMINPOSITION = 0x0001,
        WPF_RESTORETOMAXIMIZED = 0x0002,
        WPF_ASYNCWINDOWPLACEMENT = 0x0004
    }
}