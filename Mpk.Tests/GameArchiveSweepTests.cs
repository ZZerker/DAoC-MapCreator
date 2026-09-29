using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MPKLib;
using Mpk;
using Xunit;

namespace Mpk.Tests
{
    public class GameArchiveSweepTests(ITestOutputHelper output)
    {
        private const string GAME_PATH = "C:\\Spiele\\Eden DAoC";

        [Fact(Explicit = true)]
        [Trait("Category", "GameFiles")]
        public void MpkArchiveMatchesMpklibAcrossAllGameArchives()
        {
            if (!Directory.Exists(GAME_PATH))
            {
                Assert.Skip($"Game folder missing: {GAME_PATH}");
            }

            var differences = new List<string>();
            var unknownCounts = new Dictionary<uint, int>();
            var duplicateNameArchives = new List<string>();
            var nonNoneResults = new List<string>();
            var orderDifferingArchives = new List<string>();
            var archivesChecked = 0;
            var entriesChecked = 0;
            long bytesChecked = 0;

            var mpkFiles = Directory.EnumerateFiles(GAME_PATH, "*.mpk", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(GAME_PATH, "*.npk", SearchOption.AllDirectories));
            foreach (var path in mpkFiles)
            {
                archivesChecked++;
                var relativePath = Path.GetRelativePath(GAME_PATH, path);

                var mpak = new MPAK();
                eMPAKError loadResult;
                using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    loadResult = mpak.Load(fileStream);
                }

                if (loadResult != eMPAKError.None)
                {
                    nonNoneResults.Add($"{relativePath}: {loadResult}");
                    if (!MpkArchiveFailsSomewhere(path))
                    {
                        differences.Add($"{relativePath}: MPKLib returned {loadResult} but MpkArchive did not fail");
                    }
                    continue;
                }

                MpkArchive archive;
                try
                {
                    archive = MpkArchive.Open(path);
                }
                catch (Exception ex)
                {
                    differences.Add($"{relativePath}: MPKLib returned None but MpkArchive.Open threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                using (archive)
                {
                    if (mpak.ArchiveName != archive.Name)
                    {
                        differences.Add($"{relativePath}: archive name mismatch, mpklib '{mpak.ArchiveName}' vs mpk '{archive.Name}'");
                    }
                    if (mpak.Files.Length != archive.Entries.Count)
                    {
                        differences.Add($"{relativePath}: entry count mismatch, mpklib {mpak.Files.Length} vs mpk {archive.Entries.Count}");
                    }

                    var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var hasDuplicate = false;
                    foreach (var actual in archive.Entries)
                    {
                        if (!seenNames.Add(actual.Name))
                        {
                            hasDuplicate = true;
                        }
                        unknownCounts[actual.Unknown] = unknownCounts.TryGetValue(actual.Unknown, out var existing) ? existing + 1 : 1;
                    }
                    if (hasDuplicate)
                    {
                        duplicateNameArchives.Add(relativePath);
                    }

                    // MPKLib sorts its entries after loading, so pair by name (case insensitive) rather than by index
                    var byName = new Dictionary<string, Queue<MpkEntry>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var actual in archive.Entries)
                    {
                        if (!byName.TryGetValue(actual.Name, out var queue))
                        {
                            queue = new Queue<MpkEntry>();
                            byName[actual.Name] = queue;
                        }
                        queue.Enqueue(actual);
                    }

                    var mpklibOrder = mpak.Files.Select(f => f.Name).ToList();
                    var mpkOrder = archive.Entries.Select(e => e.Name).ToList();
                    // name set mismatches are already differences above, this only reports MPKLib's re-sorting
                    if (!mpklibOrder.SequenceEqual(mpkOrder, StringComparer.Ordinal))
                    {
                        orderDifferingArchives.Add(relativePath);
                    }

                    foreach (var expected in mpak.Files)
                    {
                        entriesChecked++;

                        if (!byName.TryGetValue(expected.Name, out var queue) || queue.Count == 0)
                        {
                            differences.Add($"{relativePath}: entry '{expected.Name}' present in mpklib but not in mpk");
                            continue;
                        }

                        var actual = queue.Dequeue();

                        if (expected.Name != actual.Name)
                        {
                            differences.Add($"{relativePath}: entry '{expected.Name}' stored name mismatch, mpklib '{expected.Name}' vs mpk '{actual.Name}'");
                        }
                        if (expected.Timestamp != actual.Timestamp)
                        {
                            differences.Add($"{relativePath}: entry {actual.Name} timestamp mismatch");
                        }
                        if (expected.FileSize != actual.Size)
                        {
                            differences.Add($"{relativePath}: entry {actual.Name} size mismatch");
                        }

                        try
                        {
                            var data = archive.ReadBytes(actual);
                            bytesChecked += data.Length;
                            if (!expected.Data.SequenceEqual(data))
                            {
                                differences.Add($"{relativePath}: entry {actual.Name} data mismatch");
                            }
                        }
                        catch (Exception ex)
                        {
                            differences.Add($"{relativePath}: entry {actual.Name} ReadBytes threw {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    foreach (var leftover in byName.Values.SelectMany(queue => queue))
                    {
                        differences.Add($"{relativePath}: entry '{leftover.Name}' present in mpk but not in mpklib");
                    }
                }
            }

            output.WriteLine($"Archives checked: {archivesChecked}");
            output.WriteLine($"Entries checked: {entriesChecked}");
            output.WriteLine($"Total bytes read: {bytesChecked}");
            output.WriteLine("Unknown field distribution:");
            foreach (var pair in unknownCounts.OrderByDescending(p => p.Value))
            {
                output.WriteLine($"  {pair.Key}: {pair.Value}");
            }
            output.WriteLine($"Archives with entry order differing from MPKLib: {orderDifferingArchives.Count}");
            foreach (var archivePath in orderDifferingArchives.Take(5))
            {
                output.WriteLine($"  {archivePath}");
            }
            output.WriteLine($"Archives with duplicate entry names: {duplicateNameArchives.Count}");
            foreach (var archivePath in duplicateNameArchives)
            {
                output.WriteLine($"  {archivePath}");
            }
            output.WriteLine($"MPKLib non-None results: {nonNoneResults.Count}");
            foreach (var result in nonNoneResults)
            {
                output.WriteLine($"  {result}");
            }

            Assert.Empty(differences);
        }

        private static bool MpkArchiveFailsSomewhere(string path)
        {
            try
            {
                using var archive = MpkArchive.Open(path);
                foreach (var entry in archive.Entries)
                {
                    archive.ReadBytes(entry);
                }
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }
    }
}
