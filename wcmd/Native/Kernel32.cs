using System;
using System.Runtime.InteropServices;

namespace wcmd.Native
{
    static class Kernel32
    {
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr( -1L );

        public enum HRESULT : uint
        {
            S_FALSE = 0x0001,
            S_OK = 0x0000,
            E_INVALIDARG = 0x80070057,
            E_OUTOFMEMORY = 0x8007000E
        }

        public const int STD_INPUT_HANDLE = -10;
        public const int STD_OUTPUT_HANDLE = -11;
        public const int STD_ERROR_HANDLE = -12;

        [DllImport( "kernel32", SetLastError = true )]
        public static extern IntPtr GetStdHandle( int nStdHandle );

        [Flags]
        public enum FileAccess : uint
        {
            GENERIC_READ = 0x80000000,
            GENERIC_WRITE = 0x40000000,
            GENERIC_EXECUTE = 0x20000000,
            GENERIC_ALL = 0x10000000
        }

        public enum ShareMode : uint
        {
            FILE_SHARE_READ = 0x00000001,
            FILE_SHARE_WRITE = 0x00000002,
            FILE_SHARE_DELETE = 0x00000004
        }

        public enum CreationDisposition : uint
        {
            CREATE_NEW = 1,
            CREATE_ALWAYS = 2,
            OPEN_EXISTING = 3,
            OPEN_ALWAYS = 4,
            TRUNCATE_EXISTING = 5
        }

        /* TODO: Create num for:
         
private const uint FILE_ATTRIBUTE_READONLY        = 0x00000001;
private const uint FILE_ATTRIBUTE_HIDDEN          = 0x00000002;
private const uint FILE_ATTRIBUTE_SYSTEM          = 0x00000004;
private const uint FILE_ATTRIBUTE_DIRECTORY       = 0x00000010;
private const uint FILE_ATTRIBUTE_ARCHIVE         = 0x00000020;
private const uint FILE_ATTRIBUTE_DEVICE          = 0x00000040;
private const uint FILE_ATTRIBUTE_NORMAL          = 0x00000080;
private const uint FILE_ATTRIBUTE_TEMPORARY       = 0x00000100;
private const uint FILE_ATTRIBUTE_SPARSE_FILE     = 0x00000200;
private const uint FILE_ATTRIBUTE_REPARSE_POINT       = 0x00000400;
private const uint FILE_ATTRIBUTE_COMPRESSED      = 0x00000800;
private const uint FILE_ATTRIBUTE_OFFLINE         = 0x00001000;
private const uint FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x00002000;
private const uint FILE_ATTRIBUTE_ENCRYPTED       = 0x00004000;

             */

        [DllImport( "kernel32", SetLastError = true )]
        public static extern IntPtr CreateFile(
            string lpFileName,
            FileAccess dwDesiredAccess,
            ShareMode dwShareMode,
            IntPtr lpSecurityAttributes,
            CreationDisposition dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        [DllImport( "kernel32" )]
        public static extern bool CreatePipe(
            out IntPtr hReadPipe,
            out IntPtr hWritePipe,
            ref SECURITY_ATTRIBUTES lpPipeAttributes,
            uint nSize
        );

        [DllImport( "kernel32" )]
        public static extern bool CreatePipe(
            out IntPtr hReadPipe,
            out IntPtr hWritePipe,
            IntPtr lpPipeAttributes,
            uint nSize
        );

        [DllImport( "kernel32" )]
        public static extern bool ReadFile(
            IntPtr hFile,
            [Out] byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            ref uint lpNumberOfBytesRead,
            IntPtr lpOverlapped
        );

        [DllImport( "kernel32" )]
        public static extern bool ReadFileEx(
            IntPtr hFile,
            [Out] byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            [In] ref System.Threading.NativeOverlapped lpOverlapped,
            System.Threading.IOCompletionCallback lpCompletionRoutine
        );

        [DllImport( "kernel32" )]
        public static extern bool WriteFile(
            IntPtr hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            ref uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped
        );

        [DllImport( "kernel32" )]
        public static extern bool WriteFileEx(
            IntPtr hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            [In] ref System.Threading.NativeOverlapped lpOverlapped,
            System.Threading.IOCompletionCallback lpCompletionRoutine
        );

        [DllImport( "kernel32" )]
        public static extern bool AllocConsole();

        [DllImport( "kernel32" )]
        public static extern bool FreeConsole();

        [DllImport( "kernel32" )]
        public static extern IntPtr GetConsoleWindow();

        [DllImport( "kernel32" )]
        public static extern HRESULT CreatePseudoConsole(
            COORD size,
            IntPtr hInput,
            IntPtr hOutput,
            uint dwFlags,
            out IntPtr hPC
        );

        [DllImport( "kernel32", EntryPoint = "WriteConsoleInputW", CharSet = CharSet.Unicode, SetLastError = true )]
        public static extern bool WriteConsoleInput(
            IntPtr hConsoleInput,
            [MarshalAs( UnmanagedType.LPArray ), In]
            INPUT_RECORD[] lpBuffer,
            uint nLength,
            out uint lpNumberOfEventsWritten
        );
    }
}