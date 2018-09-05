using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using wcmd.Native;
using wcmd.Sessions;

// ReSharper disable IdentifierTypo

namespace wcmd
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private Thread _outputHandler;

        private IntPtr HwndConsole => _consoleWrapper.Value;

        private readonly Lazy<IntPtr> _consoleWrapper = new Lazy<IntPtr>(
            Kernel32.GetConsoleWindow
        );


        private Session CurrentSession => _session.Value;

        private readonly Lazy<Session> _session = new Lazy<Session>(
            CreateCurrentSession
        );

        private static Session CreateCurrentSession()
        {
            var config = Configuration.LoadDefault();
            if ( config == null )
                config = Configuration.CreateDefault();

            return new Session( config );
        }

        private readonly Thread _consoleWindowMonitor;
        private Process _process;
        private Thread _processMonitor;

        public MainWindow()
        {
            if ( !Kernel32.AllocConsole() )
            {
                Trace.TraceError( "Unable to allocate a new console. Is there a console already?" );
                Environment.Exit( 1 );
            }

            _consoleWindowMonitor = new Thread( ConsoleWindowMonitor );
            _consoleWindowMonitor.Start();
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
                        Trace.TraceError( "Unable to determine console window pos." );

                    var isIconic = User32.IsIconic( HwndConsole );

                    // Update position, if needed.
                    lock ( lastPositionLock )
                    {
                        if ( rect == lastPosition && isIconic == lastIsIconic )
                        {
                            // No change means wakeup was not needed. Continue (go back to sleep).
                            //Trace.TraceInformation( "Console window position didn't change." );
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
                                Trace.TraceInformation( "Ignored position change notification." );
                                return;
                            }
                            // ReSharper restore AccessToModifiedClosure
                        }

                        if ( isIconic )
                        {
                            WindowState = WindowState.Minimized;
                            Trace.TraceInformation( "Minimized." );
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

                            Trace.TraceInformation( "Position: ({0},{1}) Size: {2}x{3}", rect.Left, rect.Top, rect.Width, rect.Height );
                        }
                    } );
                }
            }
            catch ( ThreadAbortException )
            {
            }
            catch ( Exception ex )
            {
                Trace.TraceError( "{0}", ex );
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

            Trace.TraceInformation( "Writing \"{0}\"...", text );
            User32.SetForegroundWindow( HwndConsole );
            var now = DateTime.Now;
            SendKeys.SendWait( text );
            Activate();
            document.Blocks.Clear();
            CurrentSession.Write( now, text );
        }

        private void RtCommand_KeyUp( object sender, System.Windows.Input.KeyEventArgs e )
        {
            if ( e.Key == Key.Enter )
                BtRun_Click( this, e );
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