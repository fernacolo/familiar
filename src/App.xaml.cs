using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using fam.DataFiles;
using fam.Diagnostics;
using fam.Native;
using fam.UI;

namespace fam
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
            LogViewTraceListener.Actual = new WindowsApplicationEventTraceListener();
            _trace = DiagnosticsCenter.GetTraceSource( nameof( App ) );

            var currentProcess = Process.GetCurrentProcess();
            var currentPid = currentProcess.Id;

            var message = new StringBuilder();
            message.AppendLine( "Starting the familiar." );
            message.AppendLine( $"PID: {currentPid}" );
            message.AppendLine( $"Image: {currentProcess.MainModule.FileName}" );
            var args = Environment.GetCommandLineArgs();
            for ( var i = 0; i < args.Length; ++i )
                message.AppendLine( $"args[{i}]: {args[i]}" );

            _trace.TraceInformation( "{0}", message );

            int parentPid;
            var targetWindow = IntPtr.Zero;

            if ( args.Length > 1 && args[1] == "--select" )
            {
                var attachSelector = new AttachSelector();
                attachSelector.ShowDialog();

                parentPid = attachSelector.ParentPid;
                targetWindow = attachSelector.TargetWindow;

                if ( parentPid == 0 )
                {
                    Current.Shutdown( ExitCodes.UserCanceledAttach );
                    return;
                }
            }
            else
            {
                //var logFileName = Path.Combine( config.LocalDbDirectory.FullName, $"log-{DateTime.Now:yyyy-MM-dd,HHmm}.txt" );
                //_trace.TraceInformation( "Log file: {0}", logFileName );
                //var stream = new FileStream( logFileName, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete, 1, false );
                //LogViewTraceListener.Actual = new TextWriterTraceListener( stream );

                parentPid = GetParentProcessId( currentProcess );

                var attached = Kernel32.AttachConsole( (uint) parentPid );
                _trace.TraceVerbose( "{0} returned {1}", nameof( Kernel32.AttachConsole ), attached );

                targetWindow = Kernel32.GetConsoleWindow();
                if ( targetWindow == IntPtr.Zero )
                {
                    _trace.TraceWarning( "No console window detected." );
                    MessageBox.Show( "Console not detected.\r\nPlease, start the familiar from Command Prompt.", "Familiar Notification", MessageBoxButton.OK, MessageBoxImage.Information );
                    Current.Shutdown( ExitCodes.ConsoleNotDetected );
                    return;
                }

                if ( args.Length > 2 && args[1] == "--readforward" )
                {
                    var fileToRead = new FileInfo( args[2] );
                    var file = new FileStore( fileToRead );
                    var current = file.Bof;
                    var count = 0;
                    for ( ;; )
                    {
                        current = file.GetNext( current );
                        if ( current == file.Eof )
                            break;
                        ++count;
                    }

                    Console.WriteLine( "Read finished. Found {0} records.", count );
                    Current.Shutdown( ExitCodes.TestFinished );
                    return;
                }

                if ( args.Length > 2 && args[1] == "--readbackward" )
                {
                    var fileToRead = new FileInfo( args[2] );
                    var file = new FileStore( fileToRead );
                    var current = file.Eof;
                    var count = 0;
                    for ( ;; )
                    {
                        current = file.GetPrevious( current );
                        if ( current == file.Bof )
                            break;
                        ++count;
                    }

                    Console.WriteLine( "Read finished. Found {0} records.", count );
                    Current.Shutdown( ExitCodes.TestFinished );
                    return;
                }
            }

            var mutexName = $"fam-{parentPid}";
            _trace.TraceVerbose( "Creating mutex named {0}...", mutexName );

            _mutex = new Mutex( false, mutexName, out var createdNew );
            if ( !createdNew )
            {
                _mutex.Dispose();
                _mutex = null;

                _trace.TraceWarning( "Unable to create mutex {0}. This process ({1}) will attempt to find a previous one attached to PID {2} and activate.", mutexName, currentPid, parentPid );

                foreach ( var process in Process.GetProcessesByName( "fam" ) )
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
            var mainWindow = new MainWindow( parentPid, targetWindow );
            //Current.MainWindow = mainWindow;
            _trace.TraceInformation( "Showing main window." );
            mainWindow.Show();
        }

        private int GetParentProcessId( Process process )
        {
            var processBasicInformation = new smPROCESS_BASIC_INFORMATION();
            var result = Ntdll.NtQueryInformationProcess( process.Handle, Ntdll.ProcessBasicInformation, ref processBasicInformation, Marshal.SizeOf( processBasicInformation ), out var returnLength );
            _trace.TraceVerbose( "{0} returned {1}", nameof( Ntdll.NtQueryInformationProcess ), result );

            var parentPid = processBasicInformation.InheritedFromUniqueProcessId.ToInt32();
            _trace.TraceVerbose( "Parent PID: {0}", parentPid );
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