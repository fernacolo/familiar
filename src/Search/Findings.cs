using System;
using System.Collections.Generic;
using fam.DataFiles;

namespace fam
{
    public sealed class Findings
    {
        private readonly IMatcher _matcher;
        private readonly List<IStoredItem> _foundItems;

        public Findings( IMatcher matcher, IReadOnlyList<IStoredItem> foundItems )
        {
            if ( matcher == null )
                throw new ArgumentNullException( nameof( matcher ) );

            if ( foundItems == null )
                throw new ArgumentNullException( nameof( foundItems ) );

            _matcher = matcher;
            _foundItems = new List<IStoredItem>( foundItems );
        }

        public IMatcher Matcher => _matcher;
        public IReadOnlyList<IStoredItem> FoundItems => _foundItems;
    }
}