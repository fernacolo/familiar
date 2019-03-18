using System;
using System.Runtime.InteropServices;

namespace fam.Native
{
    [StructLayout( LayoutKind.Explicit )]
    internal struct INPUT_RECORD
    {
        [FieldOffset( 0 )] public InputEventType EventType;
        [FieldOffset( 4 )] public KEY_EVENT_RECORD KeyEvent;
        [FieldOffset( 4 )] public MOUSE_EVENT_RECORD MouseEvent;
        [FieldOffset( 4 )] public WINDOW_BUFFER_SIZE_RECORD WindowBufferSizeEvent;
        [FieldOffset( 4 )] public MENU_EVENT_RECORD MenuEvent;
        [FieldOffset( 4 )] public FOCUS_EVENT_RECORD FocusEvent;
    };

    internal enum InputEventType : ushort
    {
        /// <summary>
        /// The Event member contains a FOCUS_EVENT_RECORD structure.These events are used internally and should be ignored.
        /// </summary>
        FOCUS_EVENT = 0x0010,

        /// <summary>
        /// The Event member contains a KEY_EVENT_RECORD structure with information about a keyboard event.
        /// </summary>
        KEY_EVENT = 0x0001,

        /// <summary>
        /// The Event member contains a MENU_EVENT_RECORD structure. These events are used internally and should be ignored.
        /// </summary>
        MENU_EVENT = 0x0008,

        /// <summary>
        /// The Event member contains a MOUSE_EVENT_RECORD structure with information about a mouse movement or button press event.
        /// </summary>
        MOUSE_EVENT = 0x0002,

        /// <summary>
        /// The Event member contains a WINDOW_BUFFER_SIZE_RECORD structure with information about the new size of the console screen buffer.
        /// </summary>
        WINDOW_BUFFER_SIZE_EVENT = 0x0004
    }

    [StructLayout( LayoutKind.Sequential )]
    internal struct KEY_EVENT_RECORD
    {
        public bool bKeyDown;
        public ushort wRepeatCount;
        public VirtualKeyCode wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public ControlKeyState dwControlKeyState;
    }

    [Flags]
    internal enum ControlKeyState : uint
    {
        /// <summary>
        /// The CAPS LOCK light is on.
        /// </summary>
        CAPSLOCK_ON = 0x0080,

        /// <summary>
        ///  The key is enhanced.
        /// </summary>
        ENHANCED_KEY = 0x0100,


        /// <summary>
        /// The left ALT key is pressed.
        /// </summary>
        LEFT_ALT_PRESSED = 0x0002,


        /// <summary>
        /// The left CTRL key is pressed.
        /// </summary>
        LEFT_CTRL_PRESSED = 0x0008,

        /// <summary>
        /// The NUM LOCK light is on.
        /// </summary>
        NUMLOCK_ON = 0x0020,

        /// <summary>
        /// The right ALT key is pressed.
        /// </summary>
        RIGHT_ALT_PRESSED = 0x0001,


        /// <summary>
        /// The right CTRL key is pressed.
        /// </summary>
        RIGHT_CTRL_PRESSED = 0x0004,

        /// <summary>
        /// The SCROLL LOCK light is on.
        /// </summary>
        SCROLLLOCK_ON = 0x0040,

        /// <summary>
        /// The SHIFT key is pressed.
        /// </summary>
        SHIFT_PRESSED = 0x0010
    }

    [StructLayout( LayoutKind.Sequential )]
    internal struct MOUSE_EVENT_RECORD
    {
        public COORD dwMousePosition;
        public uint dwButtonState;
        public uint dwControlKeyState;
        public uint dwEventFlags;
    }

    [StructLayout( LayoutKind.Sequential )]
    internal struct WINDOW_BUFFER_SIZE_RECORD
    {
        public COORD dwSize;
    }

    [StructLayout( LayoutKind.Sequential )]
    internal struct MENU_EVENT_RECORD
    {
        public uint dwCommandId;
    }

    [StructLayout( LayoutKind.Sequential )]
    internal struct FOCUS_EVENT_RECORD
    {
        public uint bSetFocus;
    }
}