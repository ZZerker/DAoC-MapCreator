using System;
using System.IO;
using System.Security.Cryptography;
using MPKLib;
using Xunit;

namespace Mpk.Tests
{
    public class MpklibCharacterizationTests
    {
        private const string CSV163_PATH = "frontiers\\zones\\zone163\\csv163.mpk";

        private static readonly string[] Csv163EntryOrder =
        {
            "borders.csv",
            "bound.csv",
            "fixtures.csv",
            "lights.csv",
            "nifs.csv",
            "SECTOR.DAT",
            "zonejump.csv"
        };

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

                var mpak = new MPAK();
                eMPAKError loadResult;
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    loadResult = mpak.Load(stream);
                }
                Assert.True(eMPAKError.None == loadResult, $"{archive.Path}: Load did not return None");

                Assert.Equal(archive.ArchiveName, mpak.ArchiveName);
                Assert.Equal(archive.Entries.Count, mpak.Files.Length);

                for (var i = 0; i < archive.Entries.Count; i++)
                {
                    var expected = archive.Entries[i];
                    var actual = mpak.Files[i];
                    Assert.True(expected.Name == actual.Name, $"{archive.Path}: entry {i} name mismatch");
                    Assert.True(expected.Timestamp == actual.Timestamp, $"{archive.Path}: entry {expected.Name} timestamp mismatch");
                    Assert.True(expected.Size == actual.FileSize, $"{archive.Path}: entry {expected.Name} FileSize mismatch");
                    Assert.True(expected.DataLength == actual.Data.Length, $"{archive.Path}: entry {expected.Name} Data.Length mismatch");

                    var actualHash = Convert.ToHexString(SHA256.HashData(actual.Data)).ToLowerInvariant();
                    Assert.True(expected.Sha256 == actualHash, $"{archive.Path}: entry {expected.Name} sha256 mismatch");
                }
            }
        }

        [Fact]
        public void Csv163EntryOrderIsPinned()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var mpak = MpkFixtures.OpenArchive(CSV163_PATH);
            Assert.Equal(Csv163EntryOrder.Length, mpak.Files.Length);
            for (var i = 0; i < Csv163EntryOrder.Length; i++)
            {
                Assert.Equal(Csv163EntryOrder[i], mpak.Files[i].Name);
            }
        }

        [Fact]
        public void GetFileIgnoresCaseAndKeepsStoredName()
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var mpak = MpkFixtures.OpenArchive(CSV163_PATH);

            var nifs = mpak.GetFile("NIFS.CSV");
            Assert.NotNull(nifs);
            Assert.Equal("nifs.csv", nifs.Name);

            var sector = mpak.GetFile("sector.dat");
            Assert.NotNull(sector);
            Assert.Equal("SECTOR.DAT", sector.Name);

            Assert.Null(mpak.GetFile("nope.csv"));
        }

        [Theory]
        [InlineData(DamageOffset.Magic, eMPAKError.InvalidMPAK)]
        [InlineData(DamageOffset.Version, eMPAKError.None)]
        [InlineData(DamageOffset.DirectoryStart, eMPAKError.EntryCRCMismatch)]
        [InlineData(DamageOffset.BeforeDataStart, eMPAKError.EntryCRCMismatch)]
        [InlineData(DamageOffset.DataStart, eMPAKError.DataCRCMismatch)]
        [InlineData(DamageOffset.LastByte, eMPAKError.DataCRCMismatch)]
        public void SingleByteDamageGivesPinnedError(DamageOffset offsetKind, eMPAKError expected)
        {
            var manifest = MpkFixtures.LoadManifest();
            MpkFixtures.SkipUnlessAvailable(manifest, CSV163_PATH);

            var bytes = MpkFixtures.ReadArchiveBytes(CSV163_PATH);
            var offsets = MpkFixtures.ComputeOffsets(bytes);

            var offset = offsetKind switch
            {
                DamageOffset.Magic => 0,
                DamageOffset.Version => 4,
                DamageOffset.DirectoryStart => offsets.DirectoryStart,
                DamageOffset.BeforeDataStart => offsets.DataStart - 1,
                DamageOffset.DataStart => offsets.DataStart,
                DamageOffset.LastByte => bytes.Length - 1,
                _ => throw new ArgumentOutOfRangeException(nameof(offsetKind))
            };

            var damaged = (byte[])bytes.Clone();
            damaged[offset] ^= 0xFF;

            var mpak = new MPAK();
            eMPAKError result;
            using (var stream = new MemoryStream(damaged))
            {
                result = mpak.Load(stream);
            }

            Assert.Equal(expected, result);
        }

        [Fact]
        public void EmptyStreamGivesUnknownError()
        {
            var mpak = new MPAK();
            eMPAKError result;
            using (var stream = new MemoryStream(Array.Empty<byte>()))
            {
                result = mpak.Load(stream);
            }

            Assert.Equal(eMPAKError.UnknownError, result);
        }
    }
}
