using System;

namespace wcmd.DataFiles
{
    internal sealed class FilteredDataStore : IDataStore
    {
        private readonly IDataStore _inner;
        private readonly Func<ItemPayload, bool> _filter;

        public string StateTag => _inner.StateTag;

        public string FileName => _inner.FileName;

        public FilteredDataStore( IDataStore inner, Func<ItemPayload, bool> filter )
        {
            _inner = inner ?? throw new ArgumentNullException( nameof( inner ) );
            _filter = filter ?? throw new ArgumentNullException( nameof( filter ) );
        }

        public IStoredItem Bof => _inner.Bof;

        public IStoredItem Eof => _inner.Eof;

        public IStoredItem Write( ref string stateTag, ItemPayload payload )
        {
            if ( !Accept( payload ) )
                throw new NotImplementedException();

            return _inner.Write( ref stateTag, payload );
        }

        public IStoredItem GetPrevious( IStoredItem item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == Bof )
                throw new ArgumentException( "Cannot read before BOF." );

            for ( ;; )
            {
                item = _inner.GetPrevious( item );
                if ( item == Bof || Accept( item.Payload ) )
                    return item;
            }
        }

        public IStoredItem GetNext( IStoredItem item )
        {
            if ( item == null )
                throw new ArgumentNullException( nameof( item ) );
            if ( item == Eof )
                throw new ArgumentException( "Cannot read before BOF." );

            for ( ;; )
            {
                item = _inner.GetNext( item );
                if ( item == Bof || Accept( item.Payload ) )
                    return item;
            }
        }

        public byte[] CreateLink( IStoredItem item )
        {
            throw new NotImplementedException();
        }

        public IStoredItem ResolveLink( byte[] link )
        {
            throw new NotImplementedException();
        }

        private bool Accept( ItemPayload payload )
        {
            if ( payload == null )
                throw new InvalidOperationException();
            return _filter( payload );
        }
    }
}