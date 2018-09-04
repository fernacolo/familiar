using System;

namespace wcmd.DataFiles
{
    [Serializable]
    internal sealed class DataRecord
    {
        /// <summary>
        /// Command that was submitted.
        /// </summary>
        public string Command;

        /// <summary>
        /// Point in time of when the command was submitted.
        /// </summary>
        public DateTimeOffset SubmitTime;
    }
}
