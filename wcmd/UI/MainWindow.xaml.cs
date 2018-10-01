using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using wcmd.DataFiles;
using wcmd.Diagnostics;
using wcmd.Native;
using wcmd.Sessions;
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
        private readonly DispatcherTimer _timer;

        private LogView _logWindow;
        private Process _parentProcess;

        private DataFile _dataFile;
        private Searcher _searcher;

        private DateTime _lastKeypress;
        private Findings _lastFindings;

        public MainWindow()
        {
            // Safe initializations. Do not put anything here that can throw an exception.
            // Defer to loaded.

            _trace = DiagnosticsCenter.GetTraceSource( this );

            _parentProcessObserver = new Thread( ParentProcessObserver );

            _consoleWindowObserver = new Thread( ConsoleWindowObserver );

            _timer = new DispatcherTimer();
            _timer.Tick += Tick;
            _timer.Interval = new TimeSpan( 0, 0, 0, 1 );
        }

        private void Window_Loaded( object sender, RoutedEventArgs e )
        {
            _logWindow = new LogView();
            _logWindow.Show();

            var config = Configuration.LoadDefault() ?? Configuration.CreateDefault();

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

            _dataFile = new DataFile( config );
            _searcher = new Searcher( _dataFile );

            _parentProcessObserver.Start();
            _consoleWindowObserver.Start();
            _timer.Start();

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

                            // Put the tool window below the console.
                            Left = rect.Left;
                            Top = rect.Bottom;
                            Width = rect.Width;

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

        private void Tick( object sender, EventArgs e )
        {
            var sinceLastKey = DateTime.Now - _lastKeypress;
            if ( sinceLastKey < TimeSpan.FromSeconds( 1 ) )
                return;

            var findings = _searcher.GetFindings();
            if ( findings == _lastFindings )
                return;

            _trace.TraceInformation( "Detected new findings" );
            LbSearchResults.Items.Clear();
            if ( findings != null )
                foreach ( var item in findings.FoundItems )
                    LbSearchResults.Items.Add( new ListBoxItem() {Content = item.Original, Height = 20} );

            _lastFindings = findings;
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

        private void RtCommand_KeyUp( object sender, System.Windows.Input.KeyEventArgs e )
        {
            if ( e.Key == Key.Enter )
                BtRun_Click( this, e );
        }

        private void TextBox_TextChanged( object sender, System.Windows.Controls.TextChangedEventArgs e )
        {
            _searcher.SetSearchText( TbSearch.Text );
            _lastKeypress = DateTime.Now;
        }

        private void TbSearch_PreviewKeyDown( object sender, System.Windows.Input.KeyEventArgs e )
        {
            switch ( e.Key )
            {
                case Key.Up:
                    MoveSelected( -1 );
                    e.Handled = true;
                    return;

                case Key.Down:
                    MoveSelected( 1 );
                    e.Handled = true;
                    return;

                case Key.Enter:
                    var item = GetSelected();
                    if ( item != null )
                    {
                        RtCommand.Document.Blocks.Clear();
                        RtCommand.Document.Blocks.Add( new Paragraph( new Run( item ) ) );
                        BtRun_Click( sender, e );
                        e.Handled = true;
                    }

                    return;
            }
        }

        private string GetSelected()
        {
            var idx = LbSearchResults.SelectedIndex;
            var count = LbSearchResults.Items.Count;
            if ( idx < 0 || idx >= count )
                return null;
            return (string) ((ListBoxItem) LbSearchResults.Items[idx]).Content;
        }

        private void MoveSelected( int move )
        {
            var idx = LbSearchResults.SelectedIndex + move;
            var count = LbSearchResults.Items.Count;
            if ( idx < 0 || idx >= count )
                return;

            LbSearchResults.SelectedIndex = idx;
            LbSearchResults.ScrollIntoView( LbSearchResults.Items[idx] );
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