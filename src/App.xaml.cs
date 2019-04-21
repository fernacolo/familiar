using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using fam.CommandLine;
using fam.DataFiles;
using fam.Diagnostics;
using fam.Native;
using fam.Sessions;
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
                    ConsoleNeeded( true );
                    PerformFileTest( resolvedArgs.TestFile );
                    Current.Shutdown( ExitCodes.Success );
                    return;
                }

                if ( resolvedArgs.ShowInfo )
                {
                    ConsoleNeeded();
                    ShowInformation( resolvedArgs );
                    Current.Shutdown( ExitCodes.Success );
                    return;
                }

                if ( resolvedArgs.Connect != null )
                {
                    ConsoleNeeded( true );
                    ConnectToSharedDir( resolvedArgs );
                    Current.Shutdown( ExitCodes.Success );
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
                ConsoleNeeded( true );
                _trace.TraceError( "{0}", exception );
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
            Terminal.WriteLine( "Unable to start the familiar. Please check logs for details." );

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

            Terminal.WriteLine( "Forward read finished. Found {0} records.", count );

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

            Terminal.WriteLine( "Backward read finished. Found {0} records.", count );
        }

        private void ShowInformation( FamiliarCommandLineArguments args )
        {
            var config = Configuration.LoadDefault( args );
            if ( config == null )
            {
                Terminal.WriteLine( "There is no active configuration." );
                return;
            }

            Terminal.WriteLine( "Configuration file: {0}", config.ConfigFile.FullName );
            Terminal.WriteLine( "Local data directory: {0}", config.LocalDbDirectory?.FullName ?? "not set" );
            Terminal.WriteLine( "Shared data directory: {0}", config.SharedDirectory?.FullName ?? "not set" );
            Terminal.WriteLine( "Session id: {0}", config.SessionId );
            Terminal.WriteLine();
        }

        private void ConnectToSharedDir( FamiliarCommandLineArguments args )
        {
            var newSharedDir = new DirectoryInfo( Path.Combine( args.Connect, "familiar" ) );

            var config = Configuration.LoadDefault( args );
            if ( config == null )
                config = Configuration.CreateDefault( args );

            if ( config.SharedDirectory != null )
            {
                Terminal.WriteLine( "Will change: {0}", config.SharedDirectory.FullName );
                Terminal.WriteLine( "         To: {0}", newSharedDir.FullName );
            }
            else
            {
                Terminal.WriteLine( "New shared directory: {0}", newSharedDir.FullName );
            }

            if ( !newSharedDir.Exists )
            {
                Terminal.WriteLine( "Directory does not exist. Please create if you want to use that." );
                return;
            }

            var configFile = config.ConfigFile;

            ConfigurationData configData;
            using ( var stream = new FileStream( configFile.FullName, FileMode.Open ) )
            {
                configData = Serializer.LoadFromStream<ConfigurationData>( stream );
            }

            configData.SharedFolder = newSharedDir.FullName;

            using ( var stream = new FileStream( configFile.FullName, FileMode.Create ) )
            {
                Serializer.SaveToStream( stream, configData );
            }

            var localStore = new FileStore( config );
            var myDataFile = localStore.FileName;

            var replication = new InboundReplication(
                newSharedDir,
                config.LocalDbDirectory,
                TimeSpan.Zero,
                ( fileName ) => !string.Equals( myDataFile, fileName, StringComparison.OrdinalIgnoreCase )
            );

            replication.RunOnce();

            Terminal.WriteLine( "Done." );
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
                _trace.TraceError( "{0}", ex );
                ConsoleNeeded();
                Terminal.WriteLine( ex.Message );
                Terminal.WriteLine( $"For help, use {_helpOpt.Description}." );
                Current.Shutdown( ExitCodes.InvalidArguments );
                return null;
            }

            if ( resolvedArgs.Verbose )
                LogViewTraceListener.MaxLevel = TraceEventType.Verbose;

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

        private void ConsoleNeeded( bool redirectLogs = false )
        {
            if ( redirectLogs )
            {
                var listener = new OutputTraceListener();
                listener.Other = LogViewTraceListener.Actual;
                LogViewTraceListener.Actual = listener;
            }

            if ( Kernel32.GetConsoleWindow() == IntPtr.Zero )
            {
                var parentPid = GetParentProcessId( Process.GetCurrentProcess() );
                Kernel32.AttachConsole( (uint) parentPid );
                if ( Kernel32.GetConsoleWindow() == IntPtr.Zero )
                    Kernel32.AllocConsole();
            }

            Terminal.WriteLine();
        }

        private static void ShowHelp( IReadOnlyList<CommandLineOption> options )
        {
            Terminal.WriteLine( "The best companion for the command line adventurer." );
            Terminal.WriteLine();

            var flagColumnsSize = CommandLineOption.ComputeFlagsColumnSize( options );
            const string indent = "  ";
            const int lineLength = 120;
            foreach ( var option in options )
                option.Write( indent, flagColumnsSize, lineLength );

            Terminal.WriteLine();
        }

        private CommandLineOption _testFileOpt;
        private CommandLineOption _connectOpt;
        private CommandLineOption _infoOpt;
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
                Help = "Read the specified file, searching for corrupted regions.",
                ValueHelp = "file-to-test"
            } );

            _connectOpt = new CommandLineOption( new CommandLineOptionSpec
            {
                LongName = "connect",
                HasValue = true,
                Help = "Connect to specified shared directory, giving access to commands typed in multiple machines.",
                ValueHelp = "shared-directory"
            } );

            _infoOpt = new CommandLineOption( new CommandLineOptionSpec
            {
                LongName = "info",
                Help = "Prints some internal information.",
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
                Help = "Enable verbose logs. To view logs, type: eventvwr /c:familiar",
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
                _infoOpt,
                _verboseOpt,
                _helpOpt
            };
        }

        private FamiliarCommandLineArguments ResolveArguments( CommandLineArgument[] parsedArgs )
        {
            var result = new FamiliarCommandLineArguments();

            if ( parsedArgs.Length == 0 )
                return result;

            CommandLineOption.ValidateMutuallyExclusive( parsedArgs, _testFileOpt, _connectOpt, _infoOpt, _helpOpt );
            _testFileOpt.ValidateThisForbidsOthers( parsedArgs, _selectOpt, _databaseOpt );
            _connectOpt.ValidateThisForbidsOthers( parsedArgs, _selectOpt );
            _infoOpt.ValidateThisForbidsOthers( parsedArgs, _selectOpt );
            _helpOpt.ValidateThisForbidsOthers( parsedArgs, _selectOpt, _databaseOpt );

            result.Verbose = _verboseOpt.ExtractOrNull( parsedArgs ) != null;
            result.ShowHelp = _helpOpt.ExtractOrNull( parsedArgs ) != null;
            result.TestFile = _testFileOpt.ExtractOrNull( parsedArgs )?.Value;
            result.Connect = _connectOpt.ExtractOrNull( parsedArgs )?.Value;
            result.ShowInfo = _infoOpt.ExtractOrNull( parsedArgs ) != null;
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

    internal static class Terminal
    {
        public static void WriteLine()
        {
            Write( "\r\n" );
        }

        public static void WriteLine( string message, params object[] args )
        {
            if ( args == null || args.Length == 0 )
                Write( message );
            else
                Write( string.Format( CultureInfo.InvariantCulture, message, args ) );
            WriteLine();
        }

        public static void Write( string s )
        {
            var hConsoleOutput = Kernel32.GetStdHandle( Kernel32.STD_OUTPUT_HANDLE );
            uint written = 0;
            Kernel32.WriteConsole( hConsoleOutput, s, (uint) s.Length, ref written, IntPtr.Zero );
        }
    }
}