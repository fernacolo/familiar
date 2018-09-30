using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using wcmd.Diagnostics;

namespace wcmd.UI
{
    /// <summary>
    /// Interaction logic for LogView.xaml
    /// </summary>
    public partial class LogView : Window
    {
        public LogView()
        {
            InitializeComponent();
            LogViewTraceListener.Actual = new UpdateLogViewListener( Dispatcher, RbLogContent );
        }
    }

    public class UpdateLogViewListener : TraceListener
    {
        private readonly Dispatcher _dispatcher;
        private readonly RichTextBox _textBox;

        public UpdateLogViewListener( Dispatcher dispatcher, RichTextBox textBox )
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException( nameof( dispatcher ) );
            _textBox = textBox ?? throw new ArgumentNullException( nameof( textBox ) );
        }

        public override void Write( string message )
        {
            throw new InvalidOperationException();
        }

        public override void WriteLine( string message )
        {
            throw new InvalidOperationException();
        }

        public override void TraceData( TraceEventCache eventCache, string source, TraceEventType eventType, int id, object data )
        {
            throw new InvalidOperationException();
        }

        public override void TraceData( TraceEventCache eventCache, string source, TraceEventType eventType, int id, params object[] data )
        {
            throw new InvalidOperationException();
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id )
        {
            throw new InvalidOperationException();
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message )
        {
            message = $"[{source} - {eventType}] {message}\r";
            if ( _dispatcher.CheckAccess() )
                Append( message );
            else
                _dispatcher.Invoke( () => { Append( message ); } );
        }

        private void Append( string message )
        {
            _textBox.AppendText( message );
            _textBox.ScrollToEnd();
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args )
        {
            var message = args?.Length > 0 ? string.Format( CultureInfo.InvariantCulture, format, args ) : format;
            TraceEvent( eventCache, source, eventType, id, message );
        }

        public override void TraceTransfer( TraceEventCache eventCache, string source, int id, string message, Guid relatedActivityId )
        {
            throw new InvalidOperationException();
        }
    }
}