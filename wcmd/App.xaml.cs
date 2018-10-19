using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using wcmd.Diagnostics;
using wcmd.Native;
using wcmd.Sessions;
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
            _trace = DiagnosticsCenter.GetTraceSource( this );

            var args = Environment.GetCommandLineArgs();
            for ( var i = 0; i < args.Length; ++i )
                _trace.TraceInformation( "args[{0}]: {1}", i, args[i] );

            //var logFileName = Path.Combine( config.LocalDbDirectory.FullName, $"log-{DateTime.Now:yyyy-MM-dd,HHmm}.txt" );
            //_trace.TraceInformation( "Log file: {0}", logFileName );
            //var stream = new FileStream( logFileName, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete, 1, false );
            //LogViewTraceListener.Actual = new TextWriterTraceListener( stream );

            var processBasicInformation = new smPROCESS_BASIC_INFORMATION();
            var result = Ntdll.NtQueryInformationProcess( Process.GetCurrentProcess().Handle, Ntdll.ProcessBasicInformation, ref processBasicInformation, Marshal.SizeOf( processBasicInformation ), out var returnLength );
            _trace.TraceInformation( "{0} returned {1}", nameof( Ntdll.NtQueryInformationProcess ), result );

            var parentPid = processBasicInformation.InheritedFromUniqueProcessId.ToInt32();
            _trace.TraceInformation( "Parent PID: {0}", parentPid );

            var attached = Kernel32.AttachConsole( (uint) parentPid );
            _trace.TraceInformation( "{0} returned {1}", nameof( Kernel32.AttachConsole ), attached );

            var hwndConsole = Kernel32.GetConsoleWindow();
            if ( hwndConsole == IntPtr.Zero )
            {
                _trace.TraceWarning( "No console window detected." );
                MessageBox.Show( "Console not detected.\r\nPlease, start the familiar from Command Prompt or Powershell.", "Familiar Notification", MessageBoxButton.OK, MessageBoxImage.Information );
                Current.Shutdown( ExitCodes.ConsoleNotDetected );
                return;
            }

            var mutexName = $"fam-{parentPid}";
            _trace.TraceInformation( "Creating mutex named {0}.", mutexName );

            _mutex = new Mutex( false, mutexName, out var createdNew );
            if ( !createdNew )
            {
                _mutex.Dispose();
                _mutex = null;
                _trace.TraceWarning( "Unable to create new mutex." );
                Console.WriteLine( "The familiar is already here. If you can't see it, please open a bug." );
                Current.Shutdown( ExitCodes.PreviousInstanceDetected );
                return;
            }

            Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var mainWindow = new MainWindow( parentPid );
            //Current.MainWindow = mainWindow;
            _trace.TraceInformation( "Showing main window." );
            mainWindow.Show();
        }

        private void OnExit( object sender, ExitEventArgs e )
        {
            _trace?.TraceInformation( "Exiting with code {0}.", e.ApplicationExitCode );
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}