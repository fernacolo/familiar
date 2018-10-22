﻿using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using wcmd.DataFiles;
using wcmd.Diagnostics;
using wcmd.Native;
using wcmd.Sessions;
using Application = System.Windows.Application;
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
        private readonly Process _parentProcess;
        private readonly Thread _parentProcessObserver;
        private readonly Thread _consoleWindowObserver;

        //private CachedDataFile _dataFile;
        private IDataFile _dataFile;
        private IStoredCommand _storedCommand;
        private Searcher _searcher;
        private ReplicationJob _inboundMonitor;
        private ReplicationJob _outboundMonitor;

        public MainWindow( int parentPid )
        {
            // Safe initializations. Do not put anything here that can throw an exception.
            // Defer to loaded.

            _trace = DiagnosticsCenter.GetTraceSource( this );

            var culture = Thread.CurrentThread.CurrentCulture;
            _trace.TraceInformation( "Culture {0}: {1}", nameof( culture.DisplayName ), culture.DisplayName );
            _trace.TraceInformation( "Culture {0}: {1}", nameof( culture.EnglishName ), culture.EnglishName );
            _trace.TraceInformation( "Culture {0}: {1}", nameof( culture.IsNeutralCulture ), culture.IsNeutralCulture );
            _trace.TraceInformation( "Culture {0}: {1}", nameof( culture.Name ), culture.Name );
            _trace.TraceInformation( "Culture {0}: {1}", nameof( culture.KeyboardLayoutId ), culture.KeyboardLayoutId );
            _trace.TraceInformation( "Culture {0}: {1}", nameof( culture.NativeName ), culture.NativeName );
            _trace.TraceInformation( "Culture {0}: {1}", nameof( culture.ThreeLetterISOLanguageName ), culture.ThreeLetterISOLanguageName );
            _trace.TraceInformation( "Culture {0}: {1}", nameof( culture.ThreeLetterWindowsLanguageName ), culture.ThreeLetterWindowsLanguageName );
            _trace.TraceInformation( "Culture {0}: {1}", nameof( culture.TwoLetterISOLanguageName ), culture.TwoLetterISOLanguageName );

            var textInfo = culture.TextInfo;
            _trace.TraceInformation( "Culture text info {0}: {1}", nameof( textInfo.ANSICodePage ), textInfo.ANSICodePage );
            _trace.TraceInformation( "Culture text info {0}: {1}", nameof( textInfo.CultureName ), textInfo.CultureName );
            _trace.TraceInformation( "Culture text info {0}: {1}", nameof( textInfo.EBCDICCodePage ), textInfo.EBCDICCodePage );
            _trace.TraceInformation( "Culture text info {0}: {1}", nameof( textInfo.MacCodePage ), textInfo.MacCodePage );
            _trace.TraceInformation( "Culture text info {0}: {1}", nameof( textInfo.OEMCodePage ), textInfo.OEMCodePage );

            _parentProcess = Process.GetProcessById( parentPid );
            _parentProcessObserver = new Thread( ParentProcessObserver );
            _parentProcessObserver.IsBackground = true;

            _consoleWindowObserver = new Thread( ConsoleWindowObserver );
            // Set priority a bit higher than normal so this thread get cycles when our app is not active.
            _consoleWindowObserver.Priority = ThreadPriority.AboveNormal;
            _consoleWindowObserver.IsBackground = true;

            InitializeComponent();

            _trace.TraceInformation( "Main window object created." );
        }

        private void Window_Loaded( object sender, RoutedEventArgs e )
        {
            _trace.TraceInformation( "Reading configuration..." );
            var config = Configuration.LoadDefault() ?? Configuration.CreateDefault();

            //_dataFile = new CachedDataFile( new DataFile( config ) );
            //_dataFile.DumpRecords();
            _dataFile = new OnDemandCachedDataFile( new DataFile( config ) );
            _storedCommand = _dataFile.Eof;
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
            Dispatcher.Invoke( () => { Application.Current.Shutdown( 0 ); } );
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
                // Last time we detected a change in console position.
                // Initialize with now to update immediately.
                var lastChange = DateTimeOffset.UtcNow.Ticks;

                // If there is no change in this time, we consider idle. Use large value to avoid burning user's CPU and battery.
                var idleTime = TimeSpan.FromSeconds( 1 );

                // If there is no change in this time, we consider "possibly idle". Use smaller than idle time to be responsive.
                var possiblyIdleTime = TimeSpan.FromMilliseconds( 250 );

                // A flag that tells we need to activate ourselves when the console gets activated.
                var activateWithConsole = false;

                while ( true )
                {
                    var now = DateTimeOffset.UtcNow.Ticks;
                    var timeSinceLastChange = TimeSpan.FromTicks( now - Interlocked.Add( ref lastChange, 0 ) );

                    if ( timeSinceLastChange >= idleTime )
                    {
                        //_trace.TraceInformation( "Idle detected." );
                        Thread.Sleep( idleTime );
                    }
                    else if ( timeSinceLastChange >= possiblyIdleTime )
                    {
                        //_trace.TraceInformation( "Possibly idle detected." );
                        Thread.Sleep( possiblyIdleTime );
                    }
                    else
                    {
                        // Since we are not possibly idle, just yield.
                        // This causes immediate redraws as the user resizes, making a responsive UI.
                        //_trace.TraceInformation( "Not idle detected." );
                        Thread.Yield();
                    }

                    //_trace.TraceInformation( "Updating tool window position..." );

                    Dispatcher.Invoke(
                        () =>
                        {
                            var changeTime = DateTimeOffset.UtcNow.Ticks;

                            var hwndConsole = Kernel32.GetConsoleWindow();
                            if ( hwndConsole == IntPtr.Zero )
                            {
                                _trace.TraceError( "Unable to get console window." );
                                return;
                            }

                            // If the console is iconic, iconize ourselves.
                            if ( User32.IsIconic( hwndConsole ) )
                            {
                                WindowState = WindowState.Minimized;
                                Interlocked.Exchange( ref lastChange, changeTime );
                                return;
                            }

                            // Otherwise get out of iconic state.
                            if ( WindowState != WindowState.Normal )
                            {
                                WindowState = WindowState.Normal;
                                Interlocked.Exchange( ref lastChange, changeTime );
                            }

                            if ( !User32.GetWindowRect( hwndConsole, out var rect ) )
                            {
                                _trace.TraceError( "Unable to determine console window pos." );
                                return;
                            }

                            var topLeft = new Point( rect.Left, rect.Top );
                            var size = new Point( rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1 );

                            //_trace.TraceInformation( "Console Position: ({0},{1}) Size: {2}x{3}", topLeft.X, topLeft.Y, size.X, size.Y );

                            var compositionTarget = PresentationSource.FromVisual( this )?.CompositionTarget;
                            if ( compositionTarget != null )
                            {
                                // Transform device coords to window coords.
                                topLeft = compositionTarget.TransformFromDevice.Transform( topLeft );
                                size = compositionTarget.TransformFromDevice.Transform( size );
                                //_trace.TraceInformation( "After transform, console Position: ({0},{1}) Size: {2}x{3}", topLeft.X, topLeft.Y, size.X, size.Y );
                            }

                            var newLeft = topLeft.X;
                            var newTop = topLeft.Y + size.Y;
                            var newWidth = size.X;

                            if ( Left != newLeft || Top != newTop || Width != newWidth )
                            {
                                Left = newLeft;
                                Top = newTop;
                                Width = newWidth;
                                Interlocked.Exchange( ref lastChange, changeTime );
                            }

                            // If we are active, we know that the console is not. This is a final state.
                            if ( IsActive )
                            {
                                // Just make sure the console is visible.
                                //User32.SetWindowPos( hwndConsole, new IntPtr(-2), 0, 0, 0, 0, User32.SetWindowPosFlags.SWP_NOACTIVATE );
                                return;
                            }

                            var hwndTop = User32.GetForegroundWindow();
                            if ( hwndTop != hwndConsole )
                            {
                                // The console is not the foreground window. We need to activate ourselves when the console becomes the foreground window.
                                activateWithConsole = true;
                                // For now there is nothing to do. The focus is on another application.
                                return;
                            }

                            // The console is the foreground window. Check if we need to activate ourselves.
                            if ( activateWithConsole )
                            {
                                Activate();
                                activateWithConsole = false;
                                Interlocked.Exchange( ref lastChange, changeTime );
                            }
                        }, DispatcherPriority.Send
                    );
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

        private void RtCommand_KeyDown( object sender, KeyEventArgs e )
        {
            if ( e.Key == Key.Enter )
            {
                e.Handled = true;
                ExecuteCommand();
                return;
            }
        }

        private void RtCommand_PreviewKeyDown( object sender, KeyEventArgs e )
        {
            if ( e.Key == Key.Up && _storedCommand != _dataFile.Bof )
            {
                var previous = _dataFile.GetPrevious( _storedCommand );
                SetCommand( previous );
                e.Handled = true;
            }

            if ( e.Key == Key.Down && _storedCommand != _dataFile.Eof )
            {
                var next = _dataFile.GetNext( _storedCommand );
                SetCommand( next );
                e.Handled = true;
            }
        }

        private void MiQuit_Click( object sender, RoutedEventArgs e )
        {
            Close();
        }

        private void MiSearch_Click( object sender, RoutedEventArgs e )
        {
            var dialog = new SearchWindow( _searcher );
            dialog.Owner = this;
            dialog.ShowDialog();
            var selectedCommand = dialog.SelectedCommand;
            if ( selectedCommand != null )
                SetCommand( dialog.SelectedCommand );
        }

        private void SetCommand( IStoredCommand item )
        {
            if ( item != _dataFile.Bof && item != _dataFile.Eof )
            {
                RtCommand.Document.Blocks.Clear();
                var text = item.Command.TrimEnd( '\r', '\n', ' ', '\t' );
                RtCommand.Document.Blocks.Add( new Paragraph( new Run( text ) ) );
                RtCommand.CaretPosition = RtCommand.CaretPosition.DocumentStart;
            }

            _storedCommand = item;
        }

        private void ExecuteCommand()
        {
            var document = RtCommand.Document;
            var textRange = new TextRange( document.ContentStart, document.ContentEnd );
            var text = textRange.Text;
            text = text.TrimEnd( '\r', '\n', ' ', '\t' );
            _trace.TraceInformation( "Writing \"{0}\"...", text );
            User32.SetForegroundWindow( Kernel32.GetConsoleWindow() );
            var now = DateTime.Now;

            text = AdjustForAccentSymbols( text );

            if ( !text.Contains( "~" ) )
            {
                SendKeys.SendWait( text + "\r" );
                Activate();
                document.Blocks.Clear();
            }
            else
                MessageBox.Show(
                    "Your command contains '~'. This familiar don't support that because there is a bug in .NET: https://referencesource.microsoft.com/#system.windows.forms/winforms/Managed/System/WinForms/SendKeys.cs,547."
                );

            _storedCommand = _dataFile.Write( now, text );
        }

        private string AdjustForAccentSymbols( string text )
        {
            var keyboardLayout = Thread.CurrentThread.CurrentCulture.KeyboardLayoutId;

            // TODO: Make this configurable.
            if ( keyboardLayout != 1033 )
            {
                _trace.TraceInformation( "Keyboard layout ({0}) doesn't require adjust for accent letters.", keyboardLayout );
                return text;
            }

            for ( var i = 0; i < text.Length; ++i )
            {
                if ( !IsAccentSymbol( text[i] ) )
                    continue;

                var result = new StringBuilder( text.Length + 32, int.MaxValue );
                result.Append( text.Substring( 0, i + 1 ) );
                result.Append( ' ' );

                for ( i = i + 1; i < text.Length; ++i )
                {
                    var ch = text[i];
                    result.Append( ch );
                    if ( IsAccentSymbol( text[i] ) )
                        result.Append( ' ' );
                }

                text = result.ToString();
                _trace.TraceInformation( "Because of keyboard layout ({0}), text was adjusted to \"{1}\".", keyboardLayout, text );
                return text;
            }

            _trace.TraceInformation( "Keyboard layout is {0}, but no accent symbol was found.", keyboardLayout );
            return text;
        }

        /// <summary>
        /// Returns true if the character is used for building an accent symbol.
        /// </summary>
        private static bool IsAccentSymbol( char ch )
        {
            switch ( ch )
            {
                case '~': // Used for ã, õ.
                case '`': // Used for à, è, ì, ò, ù.
                case '\'': // Used for á, é, í, ó, ú, ç.
                case '"': // Used for ä, ë, ï, ö, ü.
                case '^': // Used for â, ê, î, ô, û. 
                    return true;

                default:
                    return false;
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
        public static readonly int PreviousInstanceDetected = 2;
    }
}