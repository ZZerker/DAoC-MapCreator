using System;
using System.IO;
using MapCreator.Classes;
using MapCreator.Ui.ViewModels;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class SettingsViewModelTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "SettingsViewModelTests_" + Guid.NewGuid().ToString("N"));

        public SettingsViewModelTests()
        {
            Directory.CreateDirectory(this.directory);
        }

        public void Dispose()
        {
            Directory.Delete(this.directory, true);
        }

        [Fact]
        public void DeleteFolderRemovesTheTree()
        {
            var folder = Path.Combine(this.directory, "cache");
            Directory.CreateDirectory(Path.Combine(folder, "nested", "deeper"));
            File.WriteAllText(Path.Combine(folder, "a.png"), "a");
            File.WriteAllText(Path.Combine(folder, "nested", "deeper", "b.png"), "b");

            var (outcome, error) = SettingsViewModel.DeleteFolder(folder);

            Assert.Equal(DeleteOutcome.Deleted, outcome);
            Assert.Null(error);
            Assert.False(Directory.Exists(folder));
        }

        [Fact]
        public void DeleteFolderOfMissingFolderDeletesNothing()
        {
            var (outcome, error) = SettingsViewModel.DeleteFolder(Path.Combine(this.directory, "missing"));

            Assert.Equal(DeleteOutcome.NothingToDelete, outcome);
            Assert.Null(error);
        }

        [Fact]
        public void GamePathWithCamelotIsApplied()
        {
            File.WriteAllText(Path.Combine(this.directory, "camelot.exe"), "");
            var settings = new AppSettings();

            Assert.True(SettingsViewModel.TryApplyGamePath(settings, this.directory));
            Assert.Equal(this.directory, settings.GamePath);
        }

        [Fact]
        public void GamePathWithoutCamelotIsRefused()
        {
            var settings = new AppSettings { GamePath = "before" };

            Assert.False(SettingsViewModel.TryApplyGamePath(settings, this.directory));
            Assert.Equal("before", settings.GamePath);
        }
    }
}
