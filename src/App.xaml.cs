using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using fam.CommandLine;
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

            try
            {
                var resolvedArgs = ParseCommandLine( args );
                if ( resolvedArgs == null )
                    return;

                if ( resolvedArgs.TestFile != null )
                {
                    ConsoleNeeded();
                    PerformFileTest( resolvedArgs.TestFile );
                    Current.Shutdown( ExitCodes.TestFinished );
                    return;
                }

                if ( !SelectWindow( resolvedArgs, currentProcess, out var parentPid, out var targetWindow ) )
                    return;

                var mutexName = $"fam-{parentPid}";
                _trace.TraceVerbose( "Creating mutex named {0}...", mutexName );

                _mutex = new Mutex( false, mutexName, out var createdNew );
                if ( !createdNew )
                {
                    _mutex.Dispose();
                    _mutex = null;

                    _trace.TraceWarning( "Unable to create mutex {0}. This process ({1}) will attempt to find a previous one attached to PID {2} and activate.", mutexName, currentPid, parentPid );

                    BringCurrentToFront( parentPid, currentPid );
                    Current.Shutdown( ExitCodes.PreviousInstanceDetected );
                    return;
                }

                Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var mainWindow = new MainWindow( resolvedArgs, parentPid, targetWindow );
                //Current.MainWindow = mainWindow;
                _trace.TraceInformation( "Showing main window." );
                mainWindow.Show();
            }
            catch ( Exception exception )
            {
                _trace.TraceError( "{0}", exception );
                ConsoleNeeded();
                Console.WriteLine( "{0}", exception );
            }
        }

        private void BringCurrentToFront( int parentPid, int currentPid )
        {
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

                return;
            }

            ConsoleNeeded();
            Console.Error.WriteLine( "Unable to start the familiar. Please check logs for details." );

            _trace.TraceError( "Unable to find previous process." );
        }

        private bool SelectWindow( FamiliarCommandLineArguments resolvedArgs, Process currentProcess, out int parentPid, out IntPtr targetWindow )
        {
            if ( resolvedArgs.SelectWindow )
            {
                var attachSelector = new AttachSelector();
                attachSelector.ShowDialog();

                parentPid = attachSelector.ParentPid;
                targetWindow = attachSelector.TargetWindow;

                if ( parentPid == 0 )
                {
                    Current.Shutdown( ExitCodes.UserCanceledAttach );
                    return false;
                }
            }
            else
            {
                parentPid = GetParentProcessId( currentProcess );

                var attached = Kernel32.AttachConsole( (uint) parentPid );
                _trace.TraceVerbose( "{0} returned {1}", nameof( Kernel32.AttachConsole ), attached );

                targetWindow = Kernel32.GetConsoleWindow();
                if ( targetWindow == IntPtr.Zero )
                {
                    _trace.TraceWarning( "No console window detected." );
                    MessageBox.Show( "Console not detected.\r\nPlease, start the familiar from Command Prompt.", "Familiar Notification", MessageBoxButton.OK, MessageBoxImage.Information );
                    Current.Shutdown( ExitCodes.ConsoleNotDetected );
                    return false;
                }
            }

            return true;
        }

        private static void PerformFileTest( string fileToTest )
        {
            var fileToRead = new FileInfo( fileToTest );
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

            Console.WriteLine( "Forward read finished. Found {0} records.", count );

            file = new FileStore( fileToRead );
            current = file.Eof;
            count = 0;
            for ( ;; )
            {
                current = file.GetPrevious( current );
                if ( current == file.Bof )
                    break;
                ++count;
            }

            Console.WriteLine( "Backward read finished. Found {0} records.", count );
        }

        private FamiliarCommandLineArguments ParseCommandLine( string[] args )
        {
            var options = CreateCommandLineOptions();
            var commandLineParser = new CommandLineParser( options );
            FamiliarCommandLineArguments resolvedArgs;
            try
            {
                var parsedArgs = commandLineParser.Parse( args );
                resolvedArgs = ResolveArguments( parsedArgs );
            }
            catch ( InvalidArgumentsException ex )
            {
                ConsoleNeeded();
                _trace.TraceError( "{0}", ex );
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine( ex.Message );
                Console.ResetColor();
                Console.WriteLine( $"For help, use {_helpOpt.Description}." );
                Current.Shutdown( ExitCodes.InvalidArguments );
                return null;
            }

            _trace.TraceInformation( "Parameters parsed." );

            if ( resolvedArgs.ShowHelp )
            {
                ConsoleNeeded();
                ShowHelp( options );
                Current.Shutdown( ExitCodes.Success );
                return null;
            }

            return resolvedArgs;
        }

        private void ConsoleNeeded()
        {
            if ( Kernel32.GetConsoleWindow() != IntPtr.Zero )
                return;

            var parentPid = GetParentProcessId( Process.GetCurrentProcess() );
            Kernel32.AttachConsole( (uint) parentPid );
            if ( Kernel32.GetConsoleWindow() == IntPtr.Zero )
                Kernel32.AllocConsole();
        }

        private static void ShowHelp( IReadOnlyList<CommandLineOption> options )
        {
            Console.WriteLine();
            Console.WriteLine( "The best companion for the command line adventurer." );
            Console.WriteLine();

            var flagColumnsSize = CommandLineOption.ComputeFlagsColumnSize( options );
            const string indent = "  ";
            const int lineLength = 120;
            foreach ( var option in options )
                option.Write( Console.Out, indent, flagColumnsSize, lineLength );

            Console.WriteLine();
        }

        private CommandLineOption _testFileOpt;
        private CommandLineOption _connectOpt;
        private CommandLineOption _helpOpt;
        private CommandLineOption _verboseOpt;
        private CommandLineOption _selectOpt;
        private CommandLineOption _databaseOpt;

        private IReadOnlyList<CommandLineOption> CreateCommandLineOptions()
        {
            _testFileOpt = new CommandLineOption( new CommandLineOptionSpec
            {
                LongName = "test-file",
                HasValue = true,
                Help = "Reads the specified file, searching for corrupted regions.",
                ValueHelp = "file-to-test"
            } );

            _connectOpt = new CommandLineOption( new CommandLineOptionSpec
            {
                LongName = "connect",
                HasValue = true,
                Help = "Connect to specified shared directory, giving access to commands typed in multiple machines.",
                ValueHelp = "shared-directory"
            } );

            _helpOpt = new CommandLineOption( new CommandLineOptionSpec
            {
                LongName = "help",
                ShortName = 'h',
                Help = "Shows command line help.",
            } );

            _verboseOpt = new CommandLineOption( new CommandLineOptionSpec
            {
                LongName = "verbose",
                ShortName = 'v',
                Help = "Enable verbose logs.",
            } );

            _selectOpt = new CommandLineOption( new CommandLineOptionSpec
            {
                LongName = "select",
                Help = "Instead of connecting to current prompt, allows selecting a window (alpha).",
            } );

            _databaseOpt = new CommandLineOption( new CommandLineOptionSpec
            {
                LongName = "database",
                HasValue = true,
                Help = "Use the specified database directory instead of default.",
                ValueHelp = "data-dir"
            } );

            return new[]
            {
                _testFileOpt,
                _selectOpt,
                _databaseOpt,
                _connectOpt,
                _verboseOpt,
                _helpOpt
            };
        }

        private FamiliarCommandLineArguments ResolveArguments( CommandLineArgument[] parsedArgs )
        {
            var result = new FamiliarCommandLineArguments();

            if ( parsedArgs.Length == 0 )
                return result;

            CommandLineOption.ValidateMutuallyExclusive( parsedArgs, _testFileOpt, _connectOpt, _helpOpt );
            _testFileOpt.ValidateThisForbidsOthers( parsedArgs, _selectOpt, _databaseOpt );
            _connectOpt.ValidateThisForbidsOthers( parsedArgs, _selectOpt, _databaseOpt );
            _helpOpt.ValidateThisForbidsOthers( parsedArgs, _selectOpt, _databaseOpt );

            result.Verbose = _verboseOpt.ExtractOrNull( parsedArgs ) != null;
            result.ShowHelp = _helpOpt.ExtractOrNull( parsedArgs ) != null;
            result.TestFile = _testFileOpt.ExtractOrNull( parsedArgs )?.Value;
            result.Connect = _connectOpt.ExtractOrNull( parsedArgs )?.Value;
            result.SelectWindow = _selectOpt.ExtractOrNull( parsedArgs ) != null;
            result.Database = _databaseOpt.ExtractOrNull( parsedArgs )?.Value;

            return result;
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