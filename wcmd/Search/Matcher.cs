namespace wcmd
{
    public class Matcher
    {
        private readonly string _searchText;

        public Matcher( string searchText )
        {
            _searchText = searchText.ToLowerInvariant();
        }

        public string Term => _searchText;

        public bool Contains( Matcher other )
        {
            return _searchText.Contains( other._searchText );
        }

        public bool IsMatch( Command command )
        {
            // If we have the command in all-lowers, immediately use it.
            if ( command.AllLowers != null )
                return command.AllLowers.Contains( _searchText );

            // Otherwise, check if the original command contains the search text. Most commands use all-lowers format.
            var isMatch = command.Original.Contains( _searchText );
            if ( isMatch )
                return true;

            // Otherwise, compute all-lowers and check if there is a match.
            command.ComputeAllLowers();
            return command.AllLowers.Contains( _searchText );
        }
    }
}