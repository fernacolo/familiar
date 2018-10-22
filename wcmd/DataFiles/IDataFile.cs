using System;

namespace wcmd.DataFiles
{
    public interface IDataFile
    {
        /// <summary>
        /// The absolute path of data file name.
        /// </summary>
        string FileName { get; }

        /// <summary>
        /// A pseudo-record that points to before the last one, suitable for calling <see cref="GetNext"/>.
        /// </summary>
        IStoredCommand Bof { get; }

        /// <summary>
        /// A pseudo-record that points to after the last one, suitable for calling <see cref="GetPrevious"/>.
        /// </summary>
        IStoredCommand Eof { get; }

        /// <summary>
        /// Stores an executed command, and returns an object that represents it.
        /// </summary>
        IStoredCommand Write( DateTime whenExecuted, string command );

        /// <summary>
        /// Returns the command that appears before the specified one, or null if the specified one is the first.
        /// </summary>
        IStoredCommand GetPrevious( IStoredCommand item );

        /// <summary>
        /// Returns the command that appears after the specified one, or null if the specified one is the last.
        /// </summary>
        IStoredCommand GetNext( IStoredCommand item );
    }

    public interface IStoredCommand
    {
        DateTime WhenExecuted { get; }
        string Command { get; }
    }
}