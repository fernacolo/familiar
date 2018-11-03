using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using wcmd.Diagnostics;
using wcmd.Native;
using wcmd.UI;

namespace wcmd
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        private TraceSource _trace;
        private Mutex _mutex;

        private void OnStartup( object sender, StartupEventArgs e )
        {
            _trace = DiagnosticsCenter.GetTraceSource( nameof( App ) );

            var args = Environment.GetCommandLineArgs();
            for ( var i = 0; i < args.Length; ++i )
                _trace.TraceInformation( "args[{0}]: {1}", i, args[i] );

            //var logFileName = Path.Combine( config.LocalDbDirectory.FullName, $"log-{DateTime.Now:yyyy-MM-dd,HHmm}.txt" );
            //_trace.TraceInformation( "Log file: {0}", logFileName );
            //var stream = new FileStream( logFileName, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete, 1, false );
            //LogViewTraceListener.Actual = new TextWriterTraceListener( stream );

            var parentPid = GetParentProcessId( Process.GetCurrentProcess() );

            var attached = Kernel32.AttachConsole( (uint) parentPid );
            _trace.TraceInformation( "{0} returned {1}", nameof( Kernel32.AttachConsole ), attached );

            var hwndConsole = Kernel32.GetConsoleWindow();
            if ( hwndConsole == IntPtr.Zero )
            {
                _trace.TraceWarning( "No console window detected." );
                MessageBox.Show( "Console not detected.\r\nPlease, start the familiar from Command Prompt.", "Familiar Notification", MessageBoxButton.OK, MessageBoxImage.Information );
                Current.Shutdown( ExitCodes.ConsoleNotDetected );
                return;
            }

            var mutexName = $"fam-{parentPid}";
            _trace.TraceInformation( "Creating mutex named {0}...", mutexName );

            _mutex = new Mutex( false, mutexName, out var createdNew );
            if ( !createdNew )
            {
                _mutex.Dispose();
                _mutex = null;

                var currentPid = Process.GetCurrentProcess().Id;

                _trace.TraceWarning( "Unable to create mutex {0}. This process ({1}) will attempt to find a previous one attached to PID {2} and activate.", mutexName, currentPid, parentPid );

                foreach ( var process in Process.GetProcessesByName( "wcmd" ) )
                {
                    if ( process.Id == currentPid )
                        continue;

                    var processParentPid = GetParentProcessId( process );
                    if ( processParentPid != parentPid )
                    {
                        _trace.TraceInformation( "Found a previous process with PID {0}, but it does not appear to be attached to PID {1}. Ignoring...", process.Id, parentPid );
                        continue;
                    }

                    _trace.TraceInformation( "Found a previous process with PID {0}. Getting windows...", process.Id );

                    var windows = GetProcessWindows( process.Id );
                    foreach ( var window in windows )
                    {
                        _trace.TraceInformation( "Calling BringWindowToTop with HWND {0}.", window );
                        User32.BringWindowToTop( window );
                    }

                    Current.Shutdown( ExitCodes.PreviousInstanceDetected );
                    return;
                }

                Console.Error.WriteLine( "Unable to start the familiar. Please check logs for details." );

                _trace.TraceError( "Unable to find previous process." );
                Current.Shutdown( ExitCodes.PreviousInstanceDetected );
                return;
            }

            Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var mainWindow = new MainWindow( parentPid );
            //Current.MainWindow = mainWindow;
            _trace.TraceInformation( "Showing main window." );
            mainWindow.Show();
        }

        private int GetParentProcessId( Process process )
        {
            var processBasicInformation = new smPROCESS_BASIC_INFORMATION();
            var result = Ntdll.NtQueryInformationProcess( process.Handle, Ntdll.ProcessBasicInformation, ref processBasicInformation, Marshal.SizeOf( processBasicInformation ), out var returnLength );
            _trace.TraceInformation( "{0} returned {1}", nameof( Ntdll.NtQueryInformationProcess ), result );

            var parentPid = processBasicInformation.InheritedFromUniqueProcessId.ToInt32();
            _trace.TraceInformation( "Parent PID: {0}", parentPid );
            return parentPid;
        }

        private static IReadOnlyList<IntPtr> GetProcessWindows( int processId )
        {
            var handles = new List<IntPtr>();

            foreach ( ProcessThread thread in Process.GetProcessById( processId ).Threads )
                User32.EnumThreadWindows(
                    thread.Id,
                    ( hWnd, lParam ) =>
                    {
                        handles.Add( hWnd );
                        return true;
                    },
                    IntPtr.Zero
                );

            return handles;
        }

        private void OnExit( object sender, ExitEventArgs e )
        {
            _trace?.TraceInformation( "Exiting with code {0}.", e.ApplicationExitCode );
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}