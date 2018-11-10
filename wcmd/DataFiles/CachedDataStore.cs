using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace wcmd.DataFiles
{
    internal class CachedDataStore : IDataStore
    {
        private readonly IDataStore _inner;
        private readonly CacheEntry _bof;
        private readonly CacheEntry _eof;
        private readonly List<WeakReference<CacheEntry>> _lastEntries = new List<WeakReference<CacheEntry>>();

        private CacheEntry _first;
        private CacheEntry _last;

        public string StateTag => _inner.StateTag;

        public string FileName => _inner.FileName;

        public CachedDataStore( IDataStore inner )
        {
            _inner = inner ?? throw new ArgumentNullException( nameof( inner ) );
            _bof = new CacheEntry( inner.Bof, null, null );
            _eof = new CacheEntry( inner.Eof, null, null );
        }

        public IStoredItem Bof => _bof;

        public IStoredItem Eof => _eof;

        public IStoredItem Write( ref string stateTag, ItemPayload payload )
        {
            if ( stateTag != null )
                throw new NotImplementedException();

            if ( _last != null )
                stateTag = _last.StateTag;

            var inner = _inner.Write( ref stateTag, payload );
            if ( inner == null )
            {
                // If the conditional write failed, some records might have appeared after what knew for last record.
                // This means what we have for last record is not actually the last.
                SetLast( null );

                // Write again, this time unconditionally.
                stateTag = null;
                inner = _inner.Write( ref stateTag, payload );
            }

            Debug.Assert( inner != null );

            // The new last record is the one we just wrote.
            var newLast = new CacheEntry( inner, _last, _eof );

            // Set the last and return the record we just wrote.
            SetLast( newLast );
            return newLast;
        }

        public IStoredItem GetPrevious( IStoredItem item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == _bof )
                throw new ArgumentException( "Cannot read before BOF." );

            if ( item == _eof )
            {
                if ( _last != null && _inner.StateTag != _last.StateTag )
                {
                    // Inner store have changed. We must clear the last.
                    SetLast( null );
                    Debug.Assert( _last == null );
                }

                if ( _last == null )
                {
                    // Read the last from inner store, and initialize the cached last.
                    var fromInner = _inner.GetPrevious( _inner.Eof );
                    var newLast = fromInner == _inner.Bof ? _bof : new CacheEntry( fromInner, null, _eof );
                    SetLast( newLast );

                    if ( newLast == _bof )
                        // Optimize for the empty case.
                        _first = _eof;
                }

                return _last;
            }

            var bm = (CacheEntry) item;

            // If we don't have the previous, get it from the inner store.
            if ( bm._previous == null )
            {
                var fromInner = _inner.GetPrevious( bm._inner );

                // Set the previous to either BOF or a wrapped entry.
                bm._previous = fromInner == _inner.Bof ? _bof : new CacheEntry( fromInner, null, bm );

                // If we have both _previous and _next, we don't need _inner any more; set to null to allow garbage collection.
                //if ( bm._next != null )
                //    bm._inner = null;

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
                    _first = fromInner == _inner.Eof ? _eof : new CacheEntry( fromInner, _bof, null );

                    if ( _first == _eof )
                        // Optimize for the empty case.
                        _last = _bof;
                }

                return _first;
            }

            var bm = (CacheEntry) item;

            if ( bm._next == _eof && _inner.StateTag != bm.StateTag )
            {
                // Inner store have changed. We must clear the last.
                SetLast( null );
                Debug.Assert( bm._next == null );
            }

            // If we don't have the next, get it from the inner store.
            if ( bm._next == null )
            {
                var fromInner = _inner.GetNext( bm._next );

                // Set the next to either EOF or a wrapped entry.
                bm._next = fromInner == _inner.Eof ? _eof : new CacheEntry( fromInner, bm, null );

                // If we have both _previous and _next, we don't need _inner anymore; set to null to allow garbage collection.
                //if ( bm._previous != null )
                //    bm._inner = null;

                if ( bm._next == _eof )
                    // We have reached EOF. Cache the last.
                    SetLast( bm );
            }

            return bm._next;
        }

        private void SetLast( CacheEntry newLast )
        {
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
            if ( _last != null )
                Debug.Assert( _last._next == newLast );

            _last = newLast;

            if ( newLast != null && newLast != _bof )
                TrackLast( newLast );
        }

        /// <summary>
        /// Adds the cache entry to a list of instances that represent the last entry in the file.
        /// </summary>
        /// <remarks>
        /// When a record is appended, any cache entry that represents the last record (i.e. any entry where next is EOF),
        /// must be updated. This method is to remember such entries.
        /// </remarks>
        private void TrackLast( CacheEntry entry )
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

            _lastEntries.Add( new WeakReference<CacheEntry>( entry ) );
        }

        public byte[] CreateLink( IStoredItem item )
        {
            throw new NotImplementedException();
        }

        public IStoredItem ResolveLink( byte[] link )
        {
            throw new NotImplementedException();
        }

        private class CacheEntry : IStoredItem
        {
            public IStoredItem _inner;
            public CacheEntry _previous;
            public CacheEntry _next;

            public CacheEntry( IStoredItem inner, CacheEntry previous, CacheEntry next )
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