using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace Mpk
{
    public sealed class MpkArchive : IDisposable
    {
        private const int HEADER_SIZE = 21;
        private const int ENTRY_RECORD_SIZE = 284;
        private const int ENTRY_NAME_SIZE = 256;
        private const uint MAGIC = 0x4B41504D;

        private readonly Stream stream;
        private readonly bool leaveOpen;
        private readonly object readLock = new object();
        private readonly long dataStart;
        private readonly string sourceLabel;
        private bool disposed;

        public string Name { get; }
        public IReadOnlyList<MpkEntry> Entries { get; }

        private MpkArchive(Stream stream, bool leaveOpen, string name, IReadOnlyList<MpkEntry> entries, long dataStart, string sourceLabel)
        {
            this.stream = stream;
            this.leaveOpen = leaveOpen;
            Name = name;
            Entries = entries;
            this.dataStart = dataStart;
            this.sourceLabel = sourceLabel;
        }

        public static MpkArchive Open(string path)
        {
            var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            try
            {
                return Open(fileStream, leaveOpen: false, path);
            }
            catch
            {
                fileStream.Dispose();
                throw;
            }
        }

        public static MpkArchive Open(Stream stream, bool leaveOpen = false)
        {
            try
            {
                return Open(stream, leaveOpen, "<stream>");
            }
            catch
            {
                if (!leaveOpen)
                {
                    stream.Dispose();
                }
                throw;
            }
        }

        private static MpkArchive Open(Stream stream, bool leaveOpen, string sourceLabel)
        {
            var length = stream.Length;
            if (length < HEADER_SIZE)
            {
                throw new InvalidDataException($"{sourceLabel}: too short to be an MPK archive");
            }

            var header = new byte[HEADER_SIZE];
            stream.Position = 0;
            stream.ReadExactly(header, 0, HEADER_SIZE);

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (magic != MAGIC)
            {
                throw new InvalidDataException($"{sourceLabel}: bad MPAK magic");
            }

            var decodedHeader = new byte[16];
            for (var i = 0; i < 16; i++)
            {
                decodedHeader[i] = (byte)(header[5 + i] ^ (byte)i);
            }

            var directoryCrc = BinaryPrimitives.ReadUInt32LittleEndian(decodedHeader.AsSpan(0, 4));
            var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(decodedHeader.AsSpan(4, 4));
            var nameSize = BinaryPrimitives.ReadUInt32LittleEndian(decodedHeader.AsSpan(8, 4));
            var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(decodedHeader.AsSpan(12, 4));

            if (nameSize > int.MaxValue || directorySize > int.MaxValue)
            {
                throw new InvalidDataException($"{sourceLabel}: name or directory size out of range");
            }

            var nameRegionEnd = (long)HEADER_SIZE + nameSize;
            var directoryRegionEnd = nameRegionEnd + directorySize;
            if (nameRegionEnd > length || directoryRegionEnd > length)
            {
                throw new InvalidDataException($"{sourceLabel}: name or directory region beyond the stream end");
            }

            var compressedName = new byte[nameSize];
            stream.ReadExactly(compressedName, 0, (int)nameSize);
            var name = Encoding.UTF8.GetString(Inflate(compressedName));

            var compressedDirectory = new byte[directorySize];
            stream.ReadExactly(compressedDirectory, 0, (int)directorySize);

            var actualDirectoryCrc = Crc32.HashToUInt32(compressedDirectory);
            if (actualDirectoryCrc != directoryCrc)
            {
                throw new InvalidDataException($"{sourceLabel}: directory CRC mismatch");
            }

            var directoryBytes = Inflate(compressedDirectory);
            var expectedDirectoryLength = (long)entryCount * ENTRY_RECORD_SIZE;
            if (directoryBytes.Length != expectedDirectoryLength)
            {
                throw new InvalidDataException($"{sourceLabel}: inflated directory length does not match entry count");
            }

            var dataStart = directoryRegionEnd;
            var dataLength = length - dataStart;
            var entries = new List<MpkEntry>((int)entryCount);
            for (var i = 0; i < entryCount; i++)
            {
                var record = directoryBytes.AsSpan(i * ENTRY_RECORD_SIZE, ENTRY_RECORD_SIZE);
                var nameBytes = record.Slice(0, ENTRY_NAME_SIZE);
                var nulIndex = nameBytes.IndexOf((byte)0);
                var entryName = nulIndex < 0
                    ? Encoding.UTF8.GetString(nameBytes)
                    : Encoding.UTF8.GetString(nameBytes.Slice(0, nulIndex));

                var fields = record.Slice(ENTRY_NAME_SIZE);
                var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(fields.Slice(0, 4));
                var unknown = BinaryPrimitives.ReadUInt32LittleEndian(fields.Slice(4, 4));
                var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(fields.Slice(12, 4));
                var compressedOffset = BinaryPrimitives.ReadUInt32LittleEndian(fields.Slice(16, 4));
                var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(fields.Slice(20, 4));
                var crc = BinaryPrimitives.ReadUInt32LittleEndian(fields.Slice(24, 4));

                var entryRegionEnd = (long)compressedOffset + compressedSize;
                if (entryRegionEnd > dataLength)
                {
                    throw new InvalidDataException($"{sourceLabel}: entry {entryName} data region beyond the stream end");
                }

                entries.Add(new MpkEntry(entryName, timestamp, unknown, rawSize, compressedSize, compressedOffset, crc));
            }

            return new MpkArchive(stream, leaveOpen, name, entries, dataStart, sourceLabel);
        }

        public MpkEntry Find(string name)
        {
            foreach (var entry in Entries)
            {
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        public bool Contains(string name)
        {
            return Find(name) != null;
        }

        public byte[] ReadBytes(string name)
        {
            var entry = Find(name);
            if (entry == null)
            {
                throw new FileNotFoundException($"{name} not found in {sourceLabel}");
            }
            return ReadBytes(entry);
        }

        public byte[] ReadBytes(MpkEntry entry)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (entry.Size > int.MaxValue)
            {
                throw new InvalidDataException($"{sourceLabel}: entry {entry.Name} size out of range");
            }

            byte[] compressed;
            lock (readLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                compressed = new byte[entry.CompressedSize];
                stream.Position = dataStart + entry.DataOffset;
                stream.ReadExactly(compressed, 0, (int)entry.CompressedSize);
            }

            var actualCrc = Crc32.HashToUInt32(compressed);
            if (actualCrc != entry.Crc)
            {
                throw new InvalidDataException($"{sourceLabel}: entry {entry.Name} CRC mismatch");
            }

            var data = Inflate(compressed);
            if (data.Length != entry.Size)
            {
                throw new InvalidDataException($"{sourceLabel}: entry {entry.Name} inflated length does not match Size");
            }

            return data;
        }

        private static byte[] Inflate(byte[] compressed)
        {
            using var source = new MemoryStream(compressed);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        public void Dispose()
        {
            lock (readLock)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                if (!leaveOpen)
                {
                    stream.Dispose();
                }
            }
        }
    }
}
