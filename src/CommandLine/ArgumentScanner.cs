using System;
using System.Collections;
using System.Collections.Generic;

namespace fam.CommandLine
{
    internal sealed class ArgumentScanner : IEnumerator<string>
    {
        private string[] _args;
        private int _index;

        public ArgumentScanner( string[] args )
        {
            _args = args;
            _index = -1;
        }

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            if ( _index == _args.Length )
                throw new InvalidOperationException( "Cannot move to after end." );

            ++_index;
            return _index < _args.Length;
        }

        public bool MovePrevious()
        {
            if ( _index == -1 )
                throw new InvalidOperationException( "Cannot move to before begin." );

            --_index;
            return _index >= 0;
        }

        public void Reset()
        {
            _index = -1;
        }

        public string Current
        {
            get
            {
                if ( _index == -1 )
                    throw new InvalidOperationException( "Cannot read before first item." );
                if ( _index == _args.Length )
                    throw new InvalidOperationException( "Cannot read after last item." );
                return _args[_index];
            }
        }

        public bool HasNext
        {
            get
            {
                var result = MoveNext();
                MovePrevious();
                return result;
            }
        }

        public string Next
        {
            get
            {
                MoveNext();
                try
                {
                    return Current;
                }
                finally
                {
                    MovePrevious();
                }
            }
        }

        object IEnumerator.Current => Current;
        public int Index => _index;
    }
}