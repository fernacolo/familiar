using System;
using System.Runtime.InteropServices;

namespace wcmd.Native
{
    internal static class Ntdll
    {
        /// <summary>
        /// Retrieves a pointer to a PEB structure that can be used to determine whether the specified process is being debugged, and a unique value used by the system to identify the specified process.
        /// Use the CheckRemoteDebuggerPresent and GetProcessId functions to obtain this information.
        /// </summary>
        public const int ProcessBasicInformation = 0;

        /// <summary>
        /// Retrieves a DWORD_PTR value that is the port number of the debugger for the process. A nonzero value indicates that the process is being run under the control of a ring 3 debugger.
        /// Use the CheckRemoteDebuggerPresent or IsDebuggerPresent function.
        /// </summary>
        public const int ProcessDebugPort = 7;

        /// <summary>
        /// Determines whether the process is running in the WOW64 environment (WOW64 is the x86 emulator that allows Win32-based applications to run on 64-bit Windows).
        /// Use the IsWow64Process2 function to obtain this information.
        /// </summary>
        public const int ProcessWow64Information = 26;

        /// <summary>
        /// Retrieves a UNICODE_STRING value containing the name of the image file for the process.
        /// Use the QueryFullProcessImageName or GetProcessImageFileName function to obtain this information.
        /// </summary>
        public const int ProcessImageFileName = 27;

        /// <summary>
        /// Retrieves a ULONG value indicating whether the process is considered critical.
        /// Note  This value can be used starting in Windows XP with SP3. Starting in Windows 8.1, IsProcessCritical should be used instead.
        /// </summary>
        public const int ProcessBreakOnTermination = 29;

        public const int ProcessSubsystemInformation = 75;

        [DllImport( "ntdll" )]
        public static extern int NtQueryInformationProcess( IntPtr processHandle, int processInformationClass, ref smPROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength );
    }

    [StructLayout( LayoutKind.Sequential )]
    internal struct smPROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}