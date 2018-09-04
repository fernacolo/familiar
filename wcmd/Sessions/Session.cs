using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Management.Instrumentation;
using System.Text;
using System.Windows.Forms;
using wcmd.Native;

namespace wcmd.Sessions
{
    class Session
    {
        private Configuration _config;

        public Session(Configuration config)
        {
            _config = config;
        }

        public void Write(DateTime whenExecuted, string command)
        {
            var record = new SessionRecord
            {
                Type = SessionRecord.CommandV1,
                WhenExecuted = whenExecuted,
                Command= command
            };

            WriteRecord(record);
        }

        private void WriteRecord(SessionRecord record)
        {
            return;
            var block = GetCurrentBlock();
            var size = record.StreamSize;
            if (size <= block.SizeLeft)
            {
                block.WriteRecord(record);
                if (block.SizeLeft < SessionRecord.MinSize)
                    CloseCurrentBlock();
                return;
            }

            var records = record.Split(block.Position + block.Size,  block.SizeLeft, block.MaxSize);
            var closeBlock = false;
            foreach (var item in records)
            {
                if (closeBlock)
                    block = CloseCurrentBlock();
                block.WriteRecord(item);
                closeBlock = true;
            }
        }

        private SessionDataBlock CloseCurrentBlock()
        {
            throw new NotImplementedException();
        }

        private SessionDataBlock GetCurrentBlock()
        {
            throw new NotImplementedException();
        }
    }

    internal class SessionDataBlock
    {
        public int SizeLeft { get; set; }
        public int Size { get; set; }
        public int MaxSize { get; set; }
        public int Position { get; set; }

        public void WriteRecord(SessionRecord record)
        {
            throw new NotImplementedException();
        }
    }

    internal class SessionRecord
    {
        public static BinaryReader CreateReader(Stream stream)
        {
            return new BinaryReader(stream, Encoding.UTF8, true);
        }

        public static BinaryWriter CreateWriter(Stream stream)
        {
            return new BinaryWriter(stream, Encoding.UTF8, true);
        }

        // The minimum record size is the size of a Padding record with zero bytes.
        public const int MinSize = 4;

        /// <summary>
        /// An empty record that stores the record type and a variable number of bytes.
        /// </summary>
        public const ushort Padding = 0x0001;

        public const ushort SplitHeader = 0x0010;
        public const ushort SplitFragment = 0x0011;

        public const ushort CommandV1 = 0x0020;

        public ushort Type;

        public DateTime WhenExecuted;
        public string Command;

        public ushort PaddingBytes;

        public int AssembledSize;
        public int FirstFragmentPos;
        public short FragmentCount;

        public int HeaderPos;
        public int NextFragmentPos;
        public ushort FragmentIndex;
        public ushort FragmentSize;

        public byte[] Binary;

        private int _binarySize;

        public int StreamSize
        {
            get
            {
                if (_binarySize != -1)
                    return _binarySize;

                var stream = new SizeOnlyStream();
                var writer = CreateWriter(stream);
                WriteTo(writer);
                _binarySize = (int) stream.Length;

                return _binarySize;
            }
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(Type);
            switch (Type)
            {
                case Padding:
                    writer.Write(PaddingBytes);
                    for (var i = 0; i < PaddingBytes; ++i)
                        writer.Write((byte) 0);
                    return;

                case SplitHeader:
                    writer.Write(AssembledSize);
                    writer.Write(FirstFragmentPos);
                    writer.Write(FragmentCount);
                    return;

                case SplitFragment:
                    writer.Write(HeaderPos);
                    writer.Write(FragmentIndex);
                    writer.Write(FragmentSize);
                    writer.Write(NextFragmentPos);
                    writer.Write(Binary, 0, FragmentSize);
                    return;

                case CommandV1:
                    WriteDateTime(writer, WhenExecuted);
                    writer.Write(Command);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown record type: 0x{Type:X4}");
            }
        }

        private void WriteDateTime(BinaryWriter writer, DateTime value)
        {
            writer.Write((byte) value.Kind);
            writer.Write(value.Ticks);
        }

        public IEnumerable<SessionRecord> Split(int offset, int sizeLeft, int blockSize)
        {
            var result = new List<SessionRecord>();

            // Initialize the split header record; for now we don't know the first fragment position.
            var header = new SessionRecord
            {
                Type = SplitHeader,
                AssembledSize = StreamSize,
                FirstFragmentPos = -1,
                FragmentCount = -1
            };

            if (sizeLeft < header.StreamSize)
            {
                // No space for the SplitHeader record; add a padding in order to start with an empty block.
                var padding = CreatePaddingRecord(sizeLeft);
                result.Add(padding);
                offset += sizeLeft;

                // Since we padded, the current block is empty. Check if still need to split.
                if (StreamSize <= blockSize)
                {
                    // No need to split.
                    result.Add(this);
                    return result;
                }

                // Because the current block is empty, we can use the entire block.
                sizeLeft = blockSize;
            }

            // We have space for the split header record.

            var headerPos = offset;
            result.Add(header);
            offset += header.StreamSize;
            sizeLeft -= header.StreamSize;

            // Check if we can add the first fragment in current block.

            var firstFragment = new SessionRecord
            {
                Type = SplitFragment,
                HeaderPos = headerPos,
                FragmentIndex = 0,
                FragmentSize = 1,
                NextFragmentPos = -1,
                Binary = new byte[1],
            };

            if (sizeLeft < firstFragment.StreamSize)
            {
                // No space for the first fragment.

                // Since we will use an entire block, check if this record fits without splitting.
                if (StreamSize <= blockSize)
                {
                    // No need to split.

                    // Replace the split header by a big padding.
                    result.Clear();
                    sizeLeft += header.StreamSize;

                    var padding = CreatePaddingRecord(sizeLeft);
                    result.Add(padding);

                    // Add this object as-is after the padding.
                    result.Add(this);
                    return result;
                }

                // We need to split, but first let's add a padding, if needed.
                if (sizeLeft >= MinSize)
                {
                    var padding = CreatePaddingRecord(sizeLeft);
                    result.Add(padding);
                }

                sizeLeft = blockSize;
            }

            throw new NotImplementedException();
        }

        private SessionRecord CreatePaddingRecord(int sizeLeft)
        {
            throw new NotImplementedException();
        }
    }
}