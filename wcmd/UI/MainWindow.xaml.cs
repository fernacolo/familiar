using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using wcmd.DataFiles;
using wcmd.Diagnostics;
using wcmd.Native;
using wcmd.Sessions;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

// ReSharper disable IdentifierTypo

namespace wcmd.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly TraceSource _trace;
        private readonly Thread _parentProcessObserver;
        private readonly Thread _consoleWindowObserver;

        private Process _parentProcess;

        private CachedDataFile _dataFile;
        private Searcher _searcher;
        private ReplicationJob _inboundMonitor;
        private ReplicationJob _outboundMonitor;

        public MainWindow()
        {
            // Safe initializations. Do not put anything here that can throw an exception.
            // Defer to loaded.

            _trace = DiagnosticsCenter.GetTraceSource( this );

            _parentProcessObserver = new Thread( ParentProcessObserver );

            _consoleWindowObserver = new Thread( ConsoleWindowObserver );
        }

        private void Window_Loaded( object sender, RoutedEventArgs e )
        {
            _trace.TraceInformation( "Reading configuration..." );
            var config = Configuration.LoadDefault() ?? Configuration.CreateDefault();

            //var logFileName = Path.Combine( config.LocalDbDirectory.FullName, $"log-{DateTime.Now:yyyy-MM-dd,HHmm}.txt" );
            //_trace.TraceInformation( "Log file: {0}", logFileName );
            //var stream = new FileStream( logFileName, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete, 1, false );
            //LogViewTraceListener.Actual = new TextWriterTraceListener( stream );

            smPROCESS_BASIC_INFORMATION processBasicInformation = new smPROCESS_BASIC_INFORMATION();
            var result = Ntdll.NtQueryInformationProcess( Process.GetCurrentProcess().Handle, Ntdll.ProcessBasicInformation, ref processBasicInformation, Marshal.SizeOf( processBasicInformation ), out var returnLength );
            _trace.TraceInformation( "{0} returned {1}", nameof( Ntdll.NtQueryInformationProcess ), result );

            var parentPid = processBasicInformation.InheritedFromUniqueProcessId.ToInt32();
            _trace.TraceInformation( "Parent PID: {0}", parentPid );

            _parentProcess = Process.GetProcessById( parentPid );

            var args = Environment.GetCommandLineArgs();
            for ( var i = 0; i < args.Length; ++i )
                _trace.TraceInformation( "args[{0}]: {1}", i, args[i] );

            var attached = Kernel32.AttachConsole( (uint) parentPid );
            _trace.TraceInformation( "{0} returned {1}", nameof( Kernel32.AttachConsole ), attached );

            var hwndConsole = Kernel32.GetConsoleWindow();
            if ( hwndConsole == IntPtr.Zero )
            {
                _trace.TraceWarning( "No console window detected." );
                MessageBox.Show( "Console not detected.\r\nPlease, start the familiar from Command Prompt or Powershell.", "Familiar Notification", MessageBoxButton.OK, MessageBoxImage.Information );
                Environment.Exit( ExitCodes.ConsoleNotDetected );
            }

            _dataFile = new CachedDataFile( new DataFile( config ) );
            //_dataFile.DumpRecords();
            _searcher = new Searcher( _dataFile );

            if ( config.SharedDirectory != null )
            {
                _trace.TraceInformation( "Using shared folder: {0}", config.SharedDirectory.FullName );
                _inboundMonitor = new ReplicationJob( "InboundReplication", config.SharedDirectory, config.LocalDbDirectory, TimeSpan.FromSeconds( 10 ), IsNotMyDataFile );
                _inboundMonitor.Start();
                _outboundMonitor = new ReplicationJob( "OutboundReplication", config.LocalDbDirectory, config.SharedDirectory, TimeSpan.FromSeconds( 30 ), IsMyDataFile );
                _outboundMonitor.Start();
            }
            else
            {
                _trace.TraceWarning( "Shared folder not found: {0}.", config.SharedDirectory?.FullName );
                _trace.TraceWarning( "Replication is disabled." );
            }

            _parentProcessObserver.Start();
            _consoleWindowObserver.Start();

            /*
            var startInfo = new ProcessStartInfo();
            //startInfo.FileName = "cmd.exe";
            startInfo.FileName = "powershell.exe";
            //startInfo.Arguments = string.Format(CultureInfo.InvariantCulture, "/d /c {0}", commandLine);
            startInfo.UseShellExecute = false;
            //startInfo.RedirectStandardInput = true;
            //startInfo.RedirectStandardOutput = true;
            //startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = false;
            _process = Process.Start( startInfo );
            _parentProcessObserver = new Thread( ParentProcessObserver );
            _parentProcessObserver.Start();
            //process.StandardInput.WriteLine( @"cd \repo\ad\ip\azkms" );
            //process.StandardInput.WriteLine( "git show HEAD" );
            RtCommand.Focus();
            */

            _trace.TraceInformation( "Initialization finished." );
        }

        private bool IsMyDataFile( string fileName )
        {
            return string.Equals( _dataFile.FileName, fileName, StringComparison.OrdinalIgnoreCase );
        }

        private bool IsNotMyDataFile( string fileName )
        {
            return !IsMyDataFile( fileName );
        }

        private void ParentProcessObserver()
        {
            _parentProcess.WaitForExit();
            _consoleWindowObserver.Abort();
            _consoleWindowObserver.Join();
            Environment.Exit( 0 );
        }

        /// <summary>
        /// A method that monitors the console window.
        /// </summary>
        /// <remarks>
        /// In order to be least intrusive, we don't subclass or change parent of the console window.
        /// But this requires a background thread that keeps looking at the console window position, so we automatically
        /// move ourselves when that changes.
        /// Precautions are taken to avoid burning CPU and battery (see implementation).
        /// </remarks>
        private void ConsoleWindowObserver()
        {
            try
            {
                // Last detected console position and companion lock object.
                var lastPosition = new RECT();
                var lastIsIconic = false;
                var lastPositionLock = new object();

                // Last time we detected a change in console position.
                // Initialize with now to update immediately.
                var lastChange = DateTimeOffset.UtcNow;

                // If there is no change in this time, we consider idle. Use large value to avoid burning user's CPU and battery.
                var idleTime = TimeSpan.FromSeconds( 1 );

                // If there is no change in this time, we consider "possibly idle". Use smaller than idle time to be responsive.
                var possiblyIdleTime = TimeSpan.FromMilliseconds( 250 );

                while ( true )
                {
                    var timeSinceLastChange = DateTimeOffset.UtcNow - lastChange;

                    if ( timeSinceLastChange >= idleTime )
                        Thread.Sleep( idleTime );
                    else if ( timeSinceLastChange >= possiblyIdleTime )
                        Thread.Sleep( possiblyIdleTime );
                    else
                        // Since we are not possibly idle, just yield.
                        // This causes immediate redraws as the user resizes, making a responsive UI.
                        Thread.Yield();

                    var hwndConsole = Kernel32.GetConsoleWindow();
                    if ( hwndConsole == IntPtr.Zero )
                        continue;

                    // Detect console position and iconic state.
                    if ( !User32.GetWindowRect( hwndConsole, out var rect ) )
                        _trace.TraceError( "Unable to determine console window pos." );

                    var isIconic = User32.IsIconic( hwndConsole );

                    // Update position, if needed.
                    lock ( lastPositionLock )
                    {
                        if ( rect == lastPosition && isIconic == lastIsIconic )
                        {
                            // No change means wakeup was not needed. Continue (go back to sleep).
                            //_trace.TraceInformation( "Console window position didn't change." );
                            continue;
                        }

                        // Record what we are seeing now.
                        lastPosition = rect;
                        lastIsIconic = isIconic;
                        lastChange = DateTimeOffset.UtcNow;
                    }

                    Dispatcher.Invoke( () =>
                    {
                        // Make sure the rect still represent up-to-date position.
                        lock ( lastPositionLock )
                        {
                            // ReSharper disable AccessToModifiedClosure
                            if ( rect != lastPosition || isIconic != lastIsIconic )
                            {
                                // Position changed again after this event got triggered.
                                // This means another event was generated. Ignore this one to avoid flickering.
                                _trace.TraceInformation( "Ignored position change notification." );
                                return;
                            }
                            // ReSharper restore AccessToModifiedClosure
                        }

                        if ( isIconic )
                        {
                            WindowState = WindowState.Minimized;
                            _trace.TraceInformation( "Minimized." );
                        }
                        else
                        {
                            // If the user shakes the console window, OS will minimize everything (including the tool window).
                            // Therefore, we must restore ourselves.
                            if ( WindowState == WindowState.Minimized )
                                WindowState = WindowState.Normal;

                            var topLeft = new Point( rect.Left, rect.Top );
                            var size = new Point( rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1 );

                            _trace.TraceInformation( "Console Position: ({0},{1}) Size: {2}x{3}", topLeft.X, topLeft.Y, size.X, size.Y );

                            var compositionTarget = PresentationSource.FromVisual( this )?.CompositionTarget;
                            if ( compositionTarget != null )
                            {
                                // Transform device coords to window coords.
                                topLeft = compositionTarget.TransformFromDevice.Transform( topLeft );
                                size = compositionTarget.TransformFromDevice.Transform( size );
                                _trace.TraceInformation( "After transform, console Position: ({0},{1}) Size: {2}x{3}", topLeft.X, topLeft.Y, size.X, size.Y );
                            }

                            // Put the tool window below the console.
                            Left = topLeft.X;
                            Top = topLeft.Y + size.Y;
                            Width = size.X;

                            _trace.TraceInformation( "Position: ({0},{1}) Size: {2}x{3}", rect.Left, rect.Top, rect.Width, rect.Height );
                        }
                    } );
                }
            }
            catch ( ThreadAbortException )
            {
            }
            catch ( Exception ex )
            {
                _trace.TraceError( ex.ToString() );
            }
        }

        private void BtRun_Click( object sender, RoutedEventArgs e )
        {
            var document = RtCommand.Document;
            var textRange = new TextRange( document.ContentStart, document.ContentEnd );
            var text = textRange.Text;

            var iCr = text.IndexOf( '\r' );
            if ( iCr < 0 ) iCr = text.Length;

            var iLf = text.IndexOf( '\n' );
            if ( iLf < 0 ) iLf = text.Length;

            var len = Math.Min( iCr, iLf );
            text = text.Substring( 0, len ) + "\r";

            _trace.TraceInformation( "Writing \"{0}\"...", text );
            User32.SetForegroundWindow( Kernel32.GetConsoleWindow() );
            var now = DateTime.Now;
            SendKeys.SendWait( text );
            Activate();
            document.Blocks.Clear();
            _dataFile.Write( now, text );
        }

        private void RtCommand_KeyUp( object sender, KeyEventArgs e )
        {
            if ( e.Key == Key.Enter )
                BtRun_Click( this, e );
        }

        private void MenuItem_Click( object sender, RoutedEventArgs e )
        {
            var dialog = new SearchWindow( _searcher );
            dialog.Owner = this;
            dialog.ShowDialog();
            var selectedCommand = dialog.SelectedCommand;
            if ( selectedCommand != null )
            {
                RtCommand.Document.Blocks.Clear();
                RtCommand.Document.Blocks.Add( new Paragraph( new Run( dialog.SelectedCommand ) ) );
            }
        }

        /*
        private VirtualKeyCode ToVirtualKeyCode( char ch )
        {
            if ( ch >= '0' && ch <= '9' )
                return VirtualKeyCode.VK_0 + (ushort) (ch - '0');
            if ( ch >= 'a' && ch <= 'z' )
                return VirtualKeyCode.VK_A + (ushort) (ch - 'a');
            if ( ch >= 'A' && ch <= 'Z' )
                return VirtualKeyCode.VK_A + (ushort) (ch - 'A');

            switch ( ch )
            {
                ' ': return VirtualKeyCode.VK_SPACE;
                '=': return VirtualKeyCode.VK_OEM_NEC_EQUAL;
                ',': return VirtualKeyCode.VK_OEM_COMMA;
                '.': return VirtualKeyCode.VK_OEM_PERIOD;
            }
        }
        */
    }

    internal class ExitCodes
    {
        public static readonly int ConsoleNotDetected = 1;
    }
}