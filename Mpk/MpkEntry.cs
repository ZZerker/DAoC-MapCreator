namespace Mpk
{
    public sealed class MpkEntry
    {
        public string Name { get; }
        public uint Timestamp { get; }
        public uint Size { get; }
        public uint CompressedSize { get; }
        public uint Crc { get; }
        internal uint Unknown { get; }
        internal uint DataOffset { get; }

        internal MpkEntry(string name, uint timestamp, uint unknown, uint size, uint compressedSize, uint dataOffset, uint crc)
        {
            Name = name;
            Timestamp = timestamp;
            Unknown = unknown;
            Size = size;
            CompressedSize = compressedSize;
            DataOffset = dataOffset;
            Crc = crc;
        }
    }
}
