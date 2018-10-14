using System;

namespace wcmd.DataFiles
{
    public interface IDataFile
    {
        string FileName { get; }

        CommandPage ReadCommandsFromEnd( CommandPage previous, int maxResults, TimeSpan maxDuration );

        void Write( DateTime whenExecuted, string command );
    }
}