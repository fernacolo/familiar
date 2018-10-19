using System;
using System.Collections.Generic;
using wcmd.DataFiles;

namespace wcmd
{
    public sealed class Findings
    {
        private readonly Matcher _matcher;
        private readonly List<IStoredCommand> _foundItems;

        public Findings( Matcher matcher, IReadOnlyList<IStoredCommand> foundItems )
        {
            if ( matcher == null )
                throw new ArgumentNullException( nameof( matcher ) );

            if ( foundItems == null )
                throw new ArgumentNullException( nameof( foundItems ) );

            _matcher = matcher;
            _foundItems = new List<IStoredCommand>( foundItems );
        }

        public Matcher Matcher => _matcher;
        public IReadOnlyList<IStoredCommand> FoundItems => _foundItems;
    }
}