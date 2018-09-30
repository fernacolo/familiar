using System;
using wcmd.DataFiles;

namespace wcmd
{
    internal sealed class Command
    {
        private readonly string _original;
        private readonly DateTime _whenExecuted;
        private string _allLowers;

        public Command( DataFileRecord record )
        {
            _original = record.Command;
            _whenExecuted = record.WhenExecuted;
        }

        public void ComputeAllLowers()
        {
            _allLowers = _original.ToLowerInvariant();
        }

        public string Original => _original;
        public string AllLowers => _allLowers;
        public DateTime WhenExecuted => _whenExecuted;
    }
}