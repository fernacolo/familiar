﻿using System;
using System.Diagnostics;
using System.Threading;

namespace wcmd.Diagnostics
{
    public static class DiagnosticsCenter
    {
        private static readonly LogViewTraceListener _logViewListener = new LogViewTraceListener();

        public static TraceSource GetTraceSource( object owner )
        {
            var result = new TraceSource( owner.GetType().Name, SourceLevels.All );
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
    }

    public sealed class LogViewTraceListener : TraceListener
    {
        private static TraceListener _actual = new DefaultTraceListener();

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
            Actual.TraceEvent( eventCache, source, eventType, id, message );
        }

        public override void TraceEvent( TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args )
        {
            Actual.TraceEvent( eventCache, source, eventType, id, format, args );
        }

        public override void TraceTransfer( TraceEventCache eventCache, string source, int id, string message, Guid relatedActivityId )
        {
            throw new InvalidOperationException();
        }
    }
}