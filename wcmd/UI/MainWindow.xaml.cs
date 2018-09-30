using System;
using System.Diagnostics;
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

// ReSharper disable IdentifierTypo

namespace wcmd.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly TraceSource _trace;
        private Thread _outputHandler;

        private IntPtr HwndConsole => _hwndConsole.Value;
        private readonly Lazy<IntPtr> _hwndConsole;

        private DataFile DataFile => _session.Value;
        private readonly Lazy<DataFile> _session;

        private static DataFile CreateCurrentSession()
        {
            var config = Configuration.LoadDefault() ?? Configuration.CreateDefault();
            return new DataFile( config );
        }

        private Searcher Searcher => _searcher.Value;

        private readonly Lazy<Searcher> _searcher;

        private Searcher CreateCurrentSearcher()
        {
            return new Searcher( DataFile );
        }

        private readonly Thread _consoleWindowMonitor;
        private readonly DispatcherTimer _timer;

        private Process _process;
        private Thread _processMonitor;
        private DateTime _lastKeypress;
        private Findings _lastFindings;

        public MainWindow()
        {
            _trace = DiagnosticsCenter.GetTraceSource( this );
            _hwndConsole = new Lazy<IntPtr>( Kernel32.GetConsoleWindow );
            _session = new Lazy<DataFile>( CreateCurrentSession );
            _searcher = new Lazy<Searcher>( CreateCurrentSearcher );

            if ( !Kernel32.AllocConsole() )
            {
                _trace.TraceError( "Unable to allocate a new console. Is there a console already?" );
                Environment.Exit( 1 );
            }

            _consoleWindowMonitor = new Thread( ConsoleWindowMonitor );
            _consoleWindowMonitor.Start();

            _timer = new DispatcherTimer();
            _timer.Tick += Tick;
            _timer.Interval = new TimeSpan( 0, 0, 1 );
            _timer.Start();
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
        private void ConsoleWindowMonitor()
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

                    // Detect console position and iconic state.
                    if ( !User32.GetWindowRect( HwndConsole, out var rect ) )
                        _trace.TraceError( "Unable to determine console window pos." );

                    var isIconic = User32.IsIconic( HwndConsole );

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

        private void Window_Loaded( object sender, RoutedEventArgs e )
        {
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
            _processMonitor = new Thread( ProcessMonitor );
            _processMonitor.Start();
            //process.StandardInput.WriteLine( @"cd \repo\ad\ip\azkms" );
            //process.StandardInput.WriteLine( "git show HEAD" );
            RtCommand.Focus();
        }

        private void ProcessMonitor()
        {
            _process.WaitForExit();
            _consoleWindowMonitor.Abort();
            _consoleWindowMonitor.Join();
            User32.ShowWindow( HwndConsole, User32.ShowWindowCommands.SW_HIDE );
            Kernel32.FreeConsole();
            Environment.Exit( 0 );
        }

        private LogView _logWindow;

        private void Tick( object sender, EventArgs e )
        {
            if ( _logWindow == null )
            {
                _logWindow = new LogView();
                _logWindow.Show();
            }

            var sinceLastKey = DateTime.Now - _lastKeypress;
            if ( sinceLastKey < TimeSpan.FromSeconds( 1 ) )
                return;

            var findings = Searcher.GetFindings();
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
            User32.SetForegroundWindow( HwndConsole );
            var now = DateTime.Now;
            SendKeys.SendWait( text );
            Activate();
            document.Blocks.Clear();
            DataFile.Write( now, text );
        }

        private void RtCommand_KeyUp( object sender, System.Windows.Input.KeyEventArgs e )
        {
            if ( e.Key == Key.Enter )
                BtRun_Click( this, e );
        }

        private void TextBox_TextChanged( object sender, System.Windows.Controls.TextChangedEventArgs e )
        {
            Searcher.SetSearchText( TbSearch.Text );
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
}