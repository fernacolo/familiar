using System;
using wcmd.DataFiles;

namespace wcmd
{
    public sealed class Command
    {
        private readonly IStoredCommand _stored;
        private string _allLowers;

        public Command( IStoredCommand stored )
        {
            _stored = stored ?? throw new ArgumentNullException( nameof( stored ) );
        }

        public void ComputeAllLowers()
        {
            _allLowers = Original.ToLowerInvariant();
        }

        public IStoredCommand Stored => _stored;
        public string Original => _stored.Command;
        public string AllLowers => _allLowers;
    }
}