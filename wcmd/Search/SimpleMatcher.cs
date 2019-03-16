using System;

namespace wcmd
{
    public sealed class SimpleMatcher: IMatcher
    {
        private readonly string _searchText;

        public SimpleMatcher( string searchText )
        {
            _searchText = searchText.ToLowerInvariant();
        }

        public override string ToString()
        {
            return _searchText;
        }

        public string Term => _searchText;

        public bool Contains( IMatcher other )
        {
            var simpleOther = other as SimpleMatcher;
            return simpleOther != null && _searchText.Contains( simpleOther._searchText );
        }

        public bool IsMatch( Command command )
        {
            if ( _searchText.Length == 0 )
                return true;

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

    public sealed class AndMatcher : IMatcher
    {
        private IMatcher _left;
        private IMatcher _right;

        public AndMatcher( IMatcher left, IMatcher right )
        {
            _left = left ?? throw new ArgumentNullException( nameof( left ) );
            _right = right ?? throw new ArgumentNullException( nameof( right ) );
        }

        public string Term => _left.Term + " " + _right.Term;

        public bool IsMatch( Command command )
        {
            return _left.IsMatch( command ) && _right.IsMatch( command );
        }

        public bool Contains( IMatcher matcher )
        {
            return false;
        }
    }

    public static class MatcherBuiler
    {
        public static IMatcher Build( string searchExpr )
        {
            var terms = searchExpr.Split(' ');
            if ( terms.Length == 0 )
                return new SimpleMatcher( "" );

            IMatcher result = new SimpleMatcher( terms[0] );
            for ( var i = 1; i < terms.Length; ++i )
                result = new AndMatcher(new SimpleMatcher( terms[i] ), result);

            return result;
        }
    }
}