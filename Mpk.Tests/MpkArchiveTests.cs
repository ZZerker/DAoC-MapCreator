using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Mpk;
using Xunit;

namespace Mpk.Tests
{
    public class MpkArchiveTests
    {
        private const string CSV163_PATH = "frontiers\\zones\\zone163\\csv163.mpk";
        private const string TEX163_PATH = "frontiers\\zones\\zone163\\tex163.mpk";

        public enum DamageOffset
        {
            Magic,
            Version,
            DirectoryStart,
            BeforeDataStart,
            DataStart,
            LastByte
        }

        [Fact]
        public void AllManifestArchivesMatchPinnedContent()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest);
            Assert.NotEmpty(manifest.Archives);

            foreach (var archive in manifest.Archives)
            {
                var fullPath = Path.Combine(MpkFixtures.TestDataDirectory, archive.Path);
                Assert.True(File.Exists(fullPath), $"{archive.Path} listed in manifest.json but missing: rerun the copy script");

                using var mpkArchive = MpkArchive.Open(fullPath);

                Assert.Equal(archive.ArchiveName, mpkArchive.Name);
                Assert.Equal(archive.Entries.Count, mpkArchive.Entries.Count);

                for (var i = 0; i < archive.Entries.Count; i++)
                {
                    var expected = archive.Entries[i];
                    var actual = mpkArchive.Entries[i];
                    Assert.True(expected.Name == actual.Name, $"{archive.Path}: entry {i} name mismatch");
                    Assert.True(expected.Timestamp == actual.Timestamp, $"{archive.Path}: entry {expected.Name} timestamp mismatch");
                    Assert.True(expected.Size == actual.Size, $"{archive.Path}: entry {expected.Name} Size mismatch");

                    var data = mpkArchive.ReadBytes(actual);
                    Assert.True(expected.DataLength == data.Length, $"{archive.Path}: entry {expected.Name} data length mismatch");

                    var actualHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                    Assert.True(expected.Sha256 == actualHash, $"{archive.Path}: entry {expected.Name} sha256 mismatch");
                }
            }
        }

        [Fact]
        public void MatchesMpklibForEveryFixture()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest);

            foreach (var archive in manifest.Archives)
            {
                var mpak = MpkFixtures.OpenArchive(archive.Path);
                using var mpkArchive = MpkArchive.Open(Path.Combine(MpkFixtures.TestDataDirectory, archive.Path));

                Assert.True(mpak.ArchiveName == mpkArchive.Name, $"{archive.Path}: archive name mismatch");
                Assert.True(mpak.Files.Length == mpkArchive.Entries.Count, $"{archive.Path}: entry count mismatch");

                for (var i = 0; i < mpak.Files.Length; i++)
                {
                    var expected = mpak.Files[i];
                    var actual = mpkArchive.Entries[i];
                    Assert.True(expected.Name == actual.Name, $"{archive.Path}: entry {i} name mismatch");
                    Assert.True(expected.Timestamp == actual.Timestamp, $"{archive.Path}: entry {expected.Name} timestamp mismatch");
                    Assert.True(expected.FileSize == actual.Size, $"{archive.Path}: entry {expected.Name} size mismatch");

                    var data = mpkArchive.ReadBytes(actual);
                    Assert.True(expected.Data.SequenceEqual(data), $"{archive.Path}: entry {expected.Name} data mismatch");
                }
            }
        }

        [Fact]
        public void FindIsCaseInsensitiveAndKeepsStoredName()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            using var archive = MpkArchive.Open(Path.Combine(MpkFixtures.TestDataDirectory, CSV163_PATH));

            var nifs = archive.Find("NIFS.CSV");
            Assert.NotNull(nifs);
            Assert.Equal("nifs.csv", nifs.Name);

            var sector = archive.Find("sector.dat");
            Assert.NotNull(sector);
            Assert.Equal("SECTOR.DAT", sector.Name);

            Assert.Null(archive.Find("nope.csv"));
            Assert.True(archive.Contains("nifs.csv"));
            Assert.False(archive.Contains("nope.csv"));

            Assert.Throws<FileNotFoundException>(() => archive.ReadBytes("nope.csv"));
        }

        [Theory]
        [InlineData(DamageOffset.Magic)]
        [InlineData(DamageOffset.DirectoryStart)]
        [InlineData(DamageOffset.BeforeDataStart)]
        public void DamageBeforeDataStartFailsOpen(DamageOffset offsetKind)
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var bytes = MpkFixtures.ReadArchiveBytes(CSV163_PATH);
            var offsets = MpkFixtures.ComputeOffsets(bytes);

            var offset = offsetKind switch
            {
                DamageOffset.Magic => 0,
                DamageOffset.DirectoryStart => offsets.DirectoryStart,
                DamageOffset.BeforeDataStart => offsets.DataStart - 1,
                _ => throw new ArgumentOutOfRangeException(nameof(offsetKind))
            };

            var damaged = (byte[])bytes.Clone();
            damaged[offset] ^= 0xFF;

            using var stream = new MemoryStream(damaged);
            Assert.Throws<InvalidDataException>(() => MpkArchive.Open(stream, leaveOpen: true));
        }

        [Fact]
        public void DamageOnVersionByteStillOpensAndReadsAllEntries()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var bytes = MpkFixtures.ReadArchiveBytes(CSV163_PATH);
            var damaged = (byte[])bytes.Clone();
            damaged[4] ^= 0xFF;

            using var stream = new MemoryStream(damaged);
            using var archive = MpkArchive.Open(stream, leaveOpen: true);

            foreach (var entry in archive.Entries)
            {
                archive.ReadBytes(entry);
            }
        }

        [Fact]
        public void FailedOpenWithoutLeaveOpenDisposesTheStream()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var bytes = MpkFixtures.ReadArchiveBytes(CSV163_PATH);
            var damaged = (byte[])bytes.Clone();
            damaged[0] ^= 0xFF;

            var stream = new MemoryStream(damaged);
            Assert.Throws<InvalidDataException>(() => MpkArchive.Open(stream, leaveOpen: false));
            Assert.False(stream.CanRead);
        }

        [Fact]
        public void DamageAtDataStartOpensButFirstEntryFails()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var bytes = MpkFixtures.ReadArchiveBytes(CSV163_PATH);
            var offsets = MpkFixtures.ComputeOffsets(bytes);
            var damaged = (byte[])bytes.Clone();
            damaged[offsets.DataStart] ^= 0xFF;

            using var stream = new MemoryStream(damaged);
            using var archive = MpkArchive.Open(stream, leaveOpen: true);

            Assert.Throws<InvalidDataException>(() => archive.ReadBytes(archive.Entries[0]));
        }

        [Fact]
        public void DamageOnLastByteFailsLastEntry()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var bytes = MpkFixtures.ReadArchiveBytes(CSV163_PATH);
            var damaged = (byte[])bytes.Clone();
            damaged[damaged.Length - 1] ^= 0xFF;

            using var stream = new MemoryStream(damaged);
            using var archive = MpkArchive.Open(stream, leaveOpen: true);

            Assert.Throws<InvalidDataException>(() => archive.ReadBytes(archive.Entries[archive.Entries.Count - 1]));
        }

        [Fact]
        public void EmptyStreamFailsOpen()
        {
            using var stream = new MemoryStream(Array.Empty<byte>());
            Assert.Throws<InvalidDataException>(() => MpkArchive.Open(stream, leaveOpen: true));
        }

        [Fact]
        public void TruncatedStreamFailsOpen()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var bytes = MpkFixtures.ReadArchiveBytes(CSV163_PATH);
            var truncated = bytes.Take(30).ToArray();

            using var stream = new MemoryStream(truncated);
            Assert.Throws<InvalidDataException>(() => MpkArchive.Open(stream, leaveOpen: true));
        }

        [Fact]
        public void ParallelReadsMatchManifest()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, TEX163_PATH);

            var archiveManifest = manifest.Archives.First(a => a.Path == TEX163_PATH);
            using var archive = MpkArchive.Open(Path.Combine(MpkFixtures.TestDataDirectory, TEX163_PATH));

            Parallel.For(0, archive.Entries.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            {
                var entry = archive.Entries[i];
                var expected = archiveManifest.Entries[i];
                var data = archive.ReadBytes(entry);
                var actualHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                Assert.True(expected.Sha256 == actualHash, $"entry {entry.Name} sha256 mismatch under parallel reads");
            });
        }

        [Fact]
        public void DisposedArchiveThrowsOnReadBytes()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var archive = MpkArchive.Open(Path.Combine(MpkFixtures.TestDataDirectory, CSV163_PATH));
            var entry = archive.Entries[0];
            archive.Dispose();

            Assert.Throws<ObjectDisposedException>(() => archive.ReadBytes(entry));
        }
    }
}
