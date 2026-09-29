using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MPKLib;
using Xunit;

namespace Mpk.Tests
{
    public sealed class Manifest
    {
        public List<ArchiveManifest> Archives { get; set; }
    }

    public sealed class ArchiveManifest
    {
        public string Path { get; set; }
        public string ArchiveName { get; set; }
        public List<EntryManifest> Entries { get; set; }
    }

    public sealed class EntryManifest
    {
        public string Name { get; set; }
        public uint Timestamp { get; set; }
        public uint Size { get; set; }
        public int DataLength { get; set; }
        public string Sha256 { get; set; }
    }

    // shared helpers for characterization tests, reused later against a replacement reader
    public static class MpkFixtures
    {
        public static readonly string TestDataDirectory = ResolveTestDataDirectory();

        private static readonly string CopyScriptPath = Path.GetFullPath(Path.Combine(TestDataDirectory, "..", "..", "tools", "copy_mpk_fixtures.ps1"));

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static string ResolveTestDataDirectory()
        {
            var attribute = typeof(MpkFixtures).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "TestDataDirectory");
            return attribute?.Value;
        }

        public static Manifest LoadManifest()
        {
            if (TestDataDirectory == null)
            {
                return null;
            }

            var manifestPath = Path.Combine(TestDataDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<Manifest>(json, JsonOptions);
        }

        // skips (not fails) the test when the manifest or any of the given archives is missing
        public static void SkipUnlessAvailable(Manifest manifest, params string[] relativeArchivePaths)
        {
            if (manifest == null)
            {
                Assert.Skip($"Fixtures missing: run {CopyScriptPath}");
            }

            foreach (var relativePath in relativeArchivePaths)
            {
                var fullPath = Path.Combine(TestDataDirectory, relativePath);
                if (!File.Exists(fullPath))
                {
                    Assert.Skip($"Fixtures missing: run {CopyScriptPath}");
                }
            }
        }

        // read only load, mirrors MapCreator.Classes.MpkWrapper.Open
        public static MPAK OpenArchive(string relativePath)
        {
            var fullPath = Path.Combine(TestDataDirectory, relativePath);
            var mpak = new MPAK();
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                mpak.Load(stream);
            }
            return mpak;
        }

        public static byte[] ReadArchiveBytes(string relativePath)
        {
            return File.ReadAllBytes(Path.Combine(TestDataDirectory, relativePath));
        }

        // header bytes 5..20 are four little endian uint32, each byte xored with its index 0..15
        public static (int DirectoryStart, int DataStart) ComputeOffsets(byte[] archiveBytes)
        {
            var header = new uint[4];
            for (var i = 0; i < 16; i++)
            {
                var decoded = (byte)(archiveBytes[5 + i] ^ (byte)i);
                header[i / 4] |= (uint)decoded << ((i % 4) * 8);
            }

            var directorySize = (int)header[1];
            var nameSize = (int)header[2];
            var directoryStart = 21 + nameSize;
            var dataStart = directoryStart + directorySize;
            return (directoryStart, dataStart);
        }
    }
}
