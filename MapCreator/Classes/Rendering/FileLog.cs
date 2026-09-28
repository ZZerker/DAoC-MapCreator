using System;
using System.IO;
using System.Text;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Log file written directly by the render threads. Every line is flushed at once, so a crashed run keeps its log up to the crash.
    /// </summary>
    internal sealed class FileLog : IRenderReporter, IDisposable
    {
        private readonly object writeLock = new();
        private readonly StreamWriter writer;

        public FileLog(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            this.writer = new StreamWriter(new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        }

        public void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
            var line = string.Format("{0:HH:mm:ss} {1,-7} {2}", DateTime.Now, logLevel, text);
            lock (this.writeLock)
            {
                this.writer.WriteLine(line);
            }
        }

        public void ProgressStart(string label)
        {
        }

        public void ProgressStartMarquee(string label)
        {
        }

        public void ProgressUpdate(int percent)
        {
        }

        public void ProgressReset()
        {
        }

        public void Dispose()
        {
            lock (this.writeLock)
            {
                this.writer.Dispose();
            }
        }
    }
}
