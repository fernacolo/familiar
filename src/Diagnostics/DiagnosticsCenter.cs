using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

// ReSharper disable InconsistentNaming

namespace fam.Diagnostics
{
    public static class DiagnosticsCenter
    {
        private static readonly LogViewTraceListener _logViewListener = new LogViewTraceListener();

        public static TraceSource GetTraceSource( string name )
        {
            if ( name == null )
                throw new ArgumentNullException( nameof( name ) );
            var result = new TraceSource( name, SourceLevels.All );
            result.Listeners.Clear();
            result.Listeners.Add( _logViewListener );
            return result;
        }

        public static void TraceError( this TraceSource source, string message, params object[] args )
        {
            source.TraceEvent( TraceEventType.Error, 0, message, args );
        }

        public static void TraceWarning( this TraceSource source, string message, params object[] args )
        {
            source.TraceEvent( TraceEventType.Warning, 0, message, args );
        }

        public static void TraceVerbose( this TraceSource source, string message, params object[] args )
        {
            source.TraceEvent( TraceEventType.Verbose, 0, message, args );
        }
    }

    public sealed class LogViewTraceListener : TraceListener
    {
        private static TraceListener _actual = new DebugTraceListener();

        public static TraceListener Actual
        {
            get => Interlocked.CompareExchange( ref _actual, null, null );

            set
            {
                if ( value == null )
                    throw new ArgumentNullException();
                Interlocked.Exchange( ref _actual, value );
            }
        }

        private static int _maxLevel = (int) TraceEventType.Information;

        public static TraceEventType MaxLevel
        {
            get => (TraceEventType) Interlocked.CompareExchange( ref _maxLevel, 0, 0 );

            set => Interlocked.Exchange( ref _maxLevel, (int) value );
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
            try
            {
                if ( eventType > MaxLevel )
                    return;
                Actual.TraceEvent( eventCache, source, eventType, id, message );
                Actual.Flush();
            }
            catch ( Exception ex )
            {
                Debug.WriteLine( ex );
                throw;
            }
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args )
        {
            try
            {
                if ( eventType > MaxLevel )
                    return;
                Actual.TraceEvent( eventCache, source, eventType, id, format, args );
                Actual.Flush();
            }
            catch ( Exception ex )
            {
                Debug.WriteLine( ex );
                throw;
            }
        }

        public override void TraceTransfer( TraceEventCache eventCache, string source, int id, string message, Guid relatedActivityId )
        {
            throw new InvalidOperationException();
        }
    }

    internal class DebugTraceListener : TraceListener
    {
        public override void Write( string message )
        {
            throw new InvalidOperationException();
        }

        public override void WriteLine( string message )
        {
            throw new InvalidOperationException();
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message )
        {
            Debug.WriteLine( "[{0}] {1}: {2}", source, eventType, message );
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args )
        {
            var message = format;
            if ( args != null && args.Length > 0 )
                message = string.Format( CultureInfo.InvariantCulture, format, args );
            TraceEvent( eventCache, source, eventType, id, message );
        }
    }

    internal sealed class OutputTraceListener : TraceListener
    {
        public TraceListener Other { get; set; }

        public override void Write( string message )
        {
            Other?.Write( message );
        }

        public override void WriteLine( string message )
        {
            Other?.WriteLine( message );
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message )
        {
            Terminal.WriteLine( "[{0}] {1}: {2}", source, eventType, message );
            Other?.TraceEvent( eventCache, source, eventType, id, message );
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args )
        {
            var message = format;
            if ( args != null && args.Length > 0 )
                message = string.Format( CultureInfo.InvariantCulture, format, args );
            TraceEvent( eventCache, source, eventType, id, message );
        }
    }

    internal sealed class WindowsApplicationEventTraceListener : TraceListener
    {
        private readonly ConcurrentDictionary<string, EventLog> _cache = new ConcurrentDictionary<string, EventLog>();

        public override void Write( string message )
        {
            throw new InvalidOperationException();
        }

        public override void WriteLine( string message )
        {
            throw new InvalidOperationException();
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message )
        {
            var eventLog = _cache.GetOrAdd( source, CreateEventLog );
            eventLog.WriteEntry( message ?? "(no message)", ToEntryType( eventType ), id );
        }

        private static EventLog CreateEventLog( string source )
        {
            const string logName = "Familiar";

            var sourceName = $"{logName}-{source}";
            if ( EventLog.SourceExists( sourceName ) )
            {
                // Find the log associated with this source.    
                var currentLogName = EventLog.LogNameFromSourceName( sourceName, "." );
                // Make sure the source is in the log we believe it to be in.
                if ( currentLogName != logName )
                    EventLog.DeleteEventSource( sourceName );
            }
            else
            {
                EventLog.CreateEventSource( sourceName, logName );
            }

            var eventLog = new EventLog( logName, ".", sourceName );
            return eventLog;
        }

        private static EventLogEntryType ToEntryType( TraceEventType eventType )
        {
            switch ( eventType )
            {
                case TraceEventType.Critical:
                case TraceEventType.Error:
                    return EventLogEntryType.Error;

                case TraceEventType.Information:
                case TraceEventType.Start:
                case TraceEventType.Stop:
                case TraceEventType.Suspend:
                case TraceEventType.Resume:
                case TraceEventType.Transfer:
                case TraceEventType.Verbose:
                    return EventLogEntryType.Information;

                case TraceEventType.Warning:
                    return EventLogEntryType.Warning;

                default:
                    throw new InvalidOperationException( $"Unexpected trace event type: {eventType}" );
            }
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args )
        {
            var message = format;
            if ( args != null && args.Length > 0 )
                message = string.Format( CultureInfo.InvariantCulture, format, args );
            TraceEvent( eventCache, source, eventType, id, message );
        }
    }
}