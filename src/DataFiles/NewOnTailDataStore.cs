using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace fam.DataFiles
{
    /// <summary>
    /// A cached store that keeps all items written to it in the tail (i.e. towards eof).
    /// Old items in the inner store appear before the items written in this store.
    /// New items in the inner store are suppressed, unless they were written by this store.
    /// </summary>
    /// <remarks>
    /// This store implements the ordering of items found with arrow keys (up and down).
    /// The user don't want other fam instances to interfere with this ordering, therefore any
    /// new item written by those instances is suppressed. Those items are still visible in the
    /// search window.
    /// </remarks>
    internal sealed class NewOnTailDataStore : IDataStore
    {
        private readonly IDataStore _inner;
        private readonly NewAtTailEntry _bof;
        private readonly NewAtTailEntry _eof;
        private readonly List<WeakReference<NewAtTailEntry>> _lastEntries = new List<WeakReference<NewAtTailEntry>>();

        private NewAtTailEntry _first;
        private NewAtTailEntry _last;

        public string StateTag => _inner.StateTag;

        public string FileName => _inner.FileName;

        public NewOnTailDataStore( IDataStore inner )
        {
            _inner = inner ?? throw new ArgumentNullException( nameof( inner ) );
            _bof = new NewAtTailEntry( inner.Bof, null, null );
            _eof = new NewAtTailEntry( inner.Eof, null, null );
        }

        public IStoredItem Bof => _bof;

        public IStoredItem Eof => _eof;

        public IStoredItem Write( ref string stateTag, ItemPayload payload )
        {
            if ( stateTag != null )
                throw new NotImplementedException();

            // Before the first write, we read the last item in order to immediately create a link.
            // This way when the user iterate to the previous, it will always show the same item.
            if ( _last == null )
            {
                GetPrevious( _eof );
                Debug.Assert( _last != null );
            }

            var inner = _inner.Write( ref stateTag, payload );
            Debug.Assert( inner != null );

            // If the last command is identical, do not create a new entry.
            if ( SameCommand( inner, _last ) )
                return _last;

            // The new last record is the one we just wrote.
            var newLast = new NewAtTailEntry( inner, _last, _eof );

            // Set the last and return the record we just wrote.
            SetLast( newLast );
            return newLast;
        }

        private static bool SameCommand( IStoredItem a, IStoredItem b )
        {
            if ( !(a.Payload is CommandPayload) )
                return false;
            if ( !(b.Payload is CommandPayload) )
                return false;
            return a.Command == b.Command;
        }

        public IStoredItem GetPrevious( IStoredItem item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _bof )
                throw new ArgumentException( "Cannot read before BOF." );

            if ( item == _eof )
            {
                if ( _last == null )
                {
                    // Read the last from inner store, and initialize the cached last.
                    var fromInner = _inner.GetPrevious( _inner.Eof );
                    var newLast = fromInner == _inner.Bof ? _bof : new NewAtTailEntry( fromInner, null, _eof );
                    SetLast( newLast );

                    if ( newLast == _bof )
                        // Optimize for the empty case.
                        _first = _eof;
                }

                return _last;
            }

            var bm = (NewAtTailEntry) item;

            // If we don't have the previous, get it from the inner store.
            if ( bm._previous == null )
            {
                var fromInner = _inner.GetPrevious( bm._inner );

                // Set the previous to either BOF or a wrapped entry.
                bm._previous = fromInner == _inner.Bof ? _bof : new NewAtTailEntry( fromInner, null, bm );

                if ( bm._previous == _bof )
                {
                    // We have reach EOF. Cache the first.
                    _first = bm;
                }
            }

            return bm._previous;
        }

        public IStoredItem GetNext( IStoredItem item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _eof )
                throw new ArgumentException( "Cannot read before BOF." );

            if ( item == _bof )
            {
                if ( _first == null )
                {
                    // Read the first from inner store, and initialize the cached first.
                    var fromInner = _inner.GetNext( _inner.Bof );
                    _first = fromInner == _inner.Eof ? _eof : new NewAtTailEntry( fromInner, _bof, null );

                    if ( _first == _eof )
                        // Optimize for the empty case.
                        _last = _bof;
                }

                return _first;
            }

            var bm = (NewAtTailEntry) item;

            // If we don't have the next, get it from the inner store.
            if ( bm._next == null )
            {
                var fromInner = _inner.GetNext( bm._next );

                // Set the next to either EOF or a wrapped entry.
                bm._next = fromInner == _inner.Eof ? _eof : new NewAtTailEntry( fromInner, bm, null );

                if ( bm._next == _eof )
                    // We have reached EOF. Cache the last.
                    SetLast( bm );
            }

            return bm._next;
        }

        private void SetLast( NewAtTailEntry newLast )
        {
            Debug.Assert( newLast != null );

            // Update all previous last entries so they point to the new last.
            for ( var i = _lastEntries.Count - 1; i >= 0; --i )
            {
                _lastEntries[i].TryGetTarget( out var previousLast );
                if ( previousLast != null )
                {
                    Debug.Assert( previousLast._next == _eof );
                    Debug.Assert( newLast != _bof );
                    previousLast._next = newLast;
                }

                // No entry is last anymore; therefore we can stop tracking it.
                _lastEntries.RemoveAt( i );
            }

            // The loop above must have updated the old last.
            if ( _last != null && _last != _bof )
                Debug.Assert( _last._next == newLast );

            _last = newLast;

            if ( newLast != _bof )
                TrackLast( newLast );
        }

        /// <summary>
        /// Adds the cache entry to a list of instances that represent the last entry in the file.
        /// </summary>
        /// <remarks>
        /// When a record is appended, any cache entry that represents the last record (i.e. any entry where next is EOF),
        /// must be updated. This method is to remember such entries.
        /// </remarks>
        private void TrackLast( NewAtTailEntry entry )
        {
            Debug.Assert( entry != null, "entry is null" );
            Debug.Assert( entry != _bof, "entry is bof" );
            Debug.Assert( entry != _eof, "entry is eof" );
            Debug.Assert( entry._next == _eof, "entry.next is not eof" );

            for ( var i = _lastEntries.Count - 1; i >= 0; --i )
            {
                _lastEntries[i].TryGetTarget( out var previousEntry );
                if ( previousEntry == entry )
                    return;

                if ( previousEntry == null )
                    _lastEntries.RemoveAt( i );
            }

            _lastEntries.Add( new WeakReference<NewAtTailEntry>( entry ) );
        }

        public byte[] CreateLink( IStoredItem item )
        {
            throw new NotImplementedException();
        }

        public IStoredItem ResolveLink( byte[] link )
        {
            throw new NotImplementedException();
        }

        private class NewAtTailEntry : IStoredItem
        {
            public readonly IStoredItem _inner;
            public NewAtTailEntry _previous;
            public NewAtTailEntry _next;

            public NewAtTailEntry( IStoredItem inner, NewAtTailEntry previous, NewAtTailEntry next )
            {
                _inner = inner ?? throw new ArgumentNullException( nameof( inner ) );
                _previous = previous;
                _next = next;
            }

            public string StateTag => _inner.StateTag;

            public int SizeInStore => _inner.SizeInStore;

            public ItemPayload Payload => _inner.Payload;

            public DateTime WhenExecuted => _inner.WhenExecuted;

            public string Command => _inner.Command;
        }
    }
}