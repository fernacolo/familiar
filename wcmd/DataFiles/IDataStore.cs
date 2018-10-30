using System;

namespace wcmd.DataFiles
{
    public interface IDataStore
    {
        /// <summary>
        /// A string that represents the current state of this store.
        /// This value is automatically modified when the store changes.
        /// </summary>
        string StateTag { get; }

        /// <summary>
        /// The absolute path of data file name.
        /// </summary>
        string FileName { get; }

        /// <summary>
        /// A pseudo-record that points to before the last one, suitable for calling <see cref="GetNext"/>.
        /// </summary>
        IStoredItem Bof { get; }

        /// <summary>
        /// A pseudo-record that points to after the last one, suitable for calling <see cref="GetPrevious"/>.
        /// </summary>
        IStoredItem Eof { get; }

        /// <summary>
        /// Stores an executed command, and returns an object that represents it.
        /// </summary>
        /// <remarks>
        /// If <see cref="stateTag"/> is null, then the write is always attempted and this method never returns null.
        /// If <see cref="stateTag"/> is not null, then the write is only attempted when <see cref="StateTag"/> matches the specified value (conditional operation).
        /// If the condition is not met, this method returns null.
        /// </remarks>
        IStoredItem Write( DateTime whenExecuted, string command, ref string stateTag );

        /// <summary>
        /// Returns the command that appears before the specified one, or null if the specified one is the first.
        /// </summary>
        IStoredItem GetPrevious( IStoredItem item );

        /// <summary>
        /// Returns the command that appears after the specified one, or null if the specified one is the last.
        /// </summary>
        IStoredItem GetNext( IStoredItem item );

        /// <summary>
        /// Creates a link for the specified item. The item can be recovered with <see cref="ResolveLink"/>.
        /// </summary>
        byte[] CreateLink( IStoredItem item );

        /// <summary>
        /// Resolves a link into an item.
        /// </summary>
        /// <param name="link">An array of bytes obtained with a call to <see cref="CreateLink"/></param>
        IStoredItem ResolveLink( byte[] link );
    }

    public interface IStoredItem
    {
        /// <summary>
        /// The store state when this record was read.
        /// </summary>
        string StateTag { get; }

        /// <summary>
        /// When the command was executed.
        /// </summary>
        DateTime WhenExecuted { get; }

        /// <summary>
        /// The exact command that was executed.
        /// </summary>
        string Command { get; }
    }
}