using System;
using System.Collections.Generic;

namespace wcmd
{
    internal sealed class Findings
    {
        private readonly Matcher _matcher;
        private readonly List<Command> _foundItems;

        public Findings( Matcher matcher, IReadOnlyList<Command> foundItems )
        {
            if ( matcher == null )
                throw new ArgumentNullException( nameof( matcher ) );

            if ( foundItems == null )
                throw new ArgumentNullException( nameof( foundItems ) );

            _matcher = matcher;
            _foundItems = new List<Command>( foundItems );
        }

        public Matcher Matcher => _matcher;
        public IReadOnlyList<Command> FoundItems => _foundItems;
    }
}