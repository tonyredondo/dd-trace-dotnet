using System;
using System.IO;
using System.Text;
using System.Threading;
using Datadog.Logging.Emission;
using Datadog.Util;

namespace Datadog.Logging.Composition
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1308:Variable names must not be prefixed", Justification = "Should not apply to statics")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1311:Static readonly fields must begin with upper-case letter", Justification = "Should only apply to vars that are logically const.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1116:Split parameters must start on line after declaration", Justification = "Bad rule")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1310:Field names must not contain underscore", Justification = "Underscore aid visibility in long names")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0007:Use implicit type", Justification = "Worst piece of advise Style tools ever gave.")]
    internal sealed class FileLogSink : ILogSink, IDisposable
    {
        public const string FilenameSeparator_Timestamp = "-";
        public const string FilenameSeparator_Index = "_";
        public const string FilenameTimestampFormat = "yyyyMMdd";
        public const string FilenameExtension = "log";
        public const int FilenameFixedPartsLength = 20;

        public const int RotateFilesWhenLargerBytes = 1024 * 1024 * 128;  // 128 MB

        public static readonly Encoding LogTextEncoding = Encoding.UTF8;

        private readonly object _rotationLock = new object();
        private readonly Guid _logSessionId;
        private readonly LogGroupMutex _logGroupMutex;
        private readonly string _logFileDir;
        private readonly string _logFileNameBase;

        private FileStream _logStream;
        private StreamWriter _logWriter;
        private int _rotationIndex;

        private FileLogSink(LogGroupMutex logGroupMutex, string logFileDir, string logFileNameBase, FileStream logStream, int rotationIndex)
        {
            _logSessionId = Guid.NewGuid();
            _logGroupMutex = logGroupMutex;
            _logFileDir = logFileDir;
            _logFileNameBase = logFileNameBase;

            _logStream = logStream;
            _logWriter = new StreamWriter(logStream, LogTextEncoding);

            _rotationIndex = rotationIndex;
        }

        public Guid LogSessionId
        {
            get { return _logSessionId; }
        }

        public static bool TryCreateNew(string logFileDir, string logFileNameBase, Guid logGroupId, out FileLogSink newSink)
        {
            Validate.NotNullOrWhitespace(logFileNameBase, nameof(logFileNameBase));

            newSink = null;

            if (string.IsNullOrWhiteSpace(logFileDir))
            {
                return false;
            }

            // Normalize in respect to final dir separator:
            logFileDir = Path.GetDirectoryName(Path.Combine(logFileDir, "."));

            // Ensure the directory exists:
            if (!EnsureDirectoryExists(logFileDir, out DirectoryInfo logFileDirInfo))
            {
                return false;
            }

            var logGroupMutex = new LogGroupMutex(logGroupId);
            if (!logGroupMutex.TryAcquire(out Mutex mutex))
            {
                return false;
            }

            try
            {
                try
                {
                    DateTimeOffset now = DateTimeOffset.Now;
                    int rotationIndex = FindLatestRotationIndex(logFileDirInfo, logFileNameBase, now);

                    string logFileName = ConstructFilename(logFileNameBase, now, rotationIndex);
                    string logFilePath = Path.Combine(logFileDir, logFileName);
                    FileStream logStream = new FileStream(logFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

                    newSink = new FileLogSink(logGroupMutex, logFileDir, logFileNameBase, logStream, rotationIndex);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }

                newSink.Info(StringPair.Create(typeof(FileLogSink).FullName, null), "Logging session started", "LogGroupId", logGroupId, "LogSessionId", newSink.LogSessionId);
            }
            catch
            {
                try
                {
                    newSink.Dispose();
                }
                catch
                { }

                newSink = null;
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            if (_logStream != null && _logWriter != null)
            {
                this.Info(StringPair.Create(typeof(FileLogSink).FullName, null), "Finishing logging session", "LogSessionId", LogSessionId);

                lock (_rotationLock)
                {
                    bool hasMutex = _logGroupMutex.TryAcquire(out Mutex mutex);
                    try
                    {
                        StreamWriter logWriter = _logWriter;
                        if (logWriter != null)
                        {
                            _logWriter = null;
                            logWriter.Dispose();
                        }

                        FileStream logStream = _logStream;
                        if (logStream != null)
                        {
                            _logStream = null;
                            logStream.Dispose();
                        }
                    }
                    finally
                    {
                        if (hasMutex)
                        {
                            mutex.ReleaseMutex();
                        }
                    }

                    _logGroupMutex.Dispose();
                }
            }
        }

        public void Error(StringPair componentName, string message, Exception exception, params object[] dataNamesAndValues)
        {
            string errorMessage = DefaultFormat.ConstructErrorMessage(message, exception);
            string logLine = DefaultFormat.ConstructLogLine(DefaultFormat.LogLevelMoniker_Error,
                                                             componentName.Item1,
                                                             componentName.Item2,
                                                             useUtcTimestamp: false,
                                                             errorMessage,
                                                             dataNamesAndValues)
                                          .ToString();
            WriteToFile(logLine);
        }

        public void Info(StringPair componentName, string message, params object[] dataNamesAndValues)
        {
            string logLine = DefaultFormat.ConstructLogLine(DefaultFormat.LogLevelMoniker_Info,
                                                            componentName.Item1,
                                                            componentName.Item2,
                                                            useUtcTimestamp: false,
                                                            message,
                                                            dataNamesAndValues)
                                          .ToString();
            WriteToFile(logLine);
        }

        public void Debug(StringPair componentName, string message, params object[] dataNamesAndValues)
        {
            string logLine = DefaultFormat.ConstructLogLine(DefaultFormat.LogLevelMoniker_Debug,
                                                            componentName.Item1,
                                                            componentName.Item2,
                                                            useUtcTimestamp: false,
                                                            message,
                                                            dataNamesAndValues)
                                          .ToString();
            WriteToFile(logLine);
        }

        private static bool EnsureDirectoryExists(string dirName, out DirectoryInfo dirInfo)
        {
            try
            {
                dirInfo = Directory.CreateDirectory(dirName);
                if (dirInfo.Exists)
                {
                    return true;
                }
            }
            catch
            { }

            dirInfo = null;
            return false;
        }

        private static string ConstructFilename(string nameBase, DateTimeOffset timestamp, int index)
        {
            if (index < 0)
            {
                return ConstructFilename(nameBase, timestamp, null);
            }
            else
            {
                string indexStr = index.ToString("000");
                return ConstructFilename(nameBase, timestamp, indexStr);
            }
        }

        private static int FindLatestRotationIndex(DirectoryInfo logFileDirInfo, string logFileNameBase, DateTimeOffset timestamp)
        {
            string filenamePattern = ConstructFilename(logFileNameBase, timestamp, "*");

            FileInfo[] logFileInfos = logFileDirInfo.GetFiles("*", SearchOption.TopDirectoryOnly);
            if (logFileInfos == null || logFileInfos.Length == 0)
            {
                return 0;
            }

            Array.Sort(logFileInfos, (fi1, fi2) => -fi1.Name.CompareTo(fi2));
            string lastFile = logFileInfos[0].Name;
            lastFile = Path.GetFileNameWithoutExtension(lastFile);

            string rotationIndexStr = lastFile.Substring(lastFile.Length - 3);
            int rotationIndex = int.Parse(rotationIndexStr);
            return rotationIndex;
        }

        private static string ConstructFilename(string nameBase, DateTimeOffset timestamp, string index)
        {
            var filename = new StringBuilder(nameBase.Length + FilenameFixedPartsLength);
            filename.Append(nameBase);
            filename.Append(FilenameSeparator_Timestamp);
            filename.Append(timestamp.ToString(FilenameTimestampFormat));

            if (index != null)
            {
                filename.Append(FilenameSeparator_Index);
                filename.Append(index);
            }

            filename.Append(".");
            filename.Append(FilenameExtension);

            return filename.ToString();
        }

        private bool WriteToFile(string logLine)
        {
            bool logLineWritten = false;
            while (!logLineWritten)
            {
                if (!_logGroupMutex.TryAcquire(out Mutex mutex))
                {
                    return false;  // Disposed or error => give up.
                }

                try
                {
                    long pos = _logStream.Seek(0, SeekOrigin.End);
                    if (pos <= RotateFilesWhenLargerBytes)
                    {
                        _logWriter.WriteLine(logLine);

                        _logWriter.Flush();
                        _logStream.Flush(flushToDisk: true);

                        logLineWritten = true;
                    }
                }
                finally
                {
                    mutex.ReleaseMutex();
                }

                if (!logLineWritten)
                {
                    if (!RotateLogFile())
                    {
                        return false;  // Cannot rotate => give up.
                    }
                }
            }

            return true;
        }

        private bool RotateLogFile()
        {
            try
            {
                if (!EnsureDirectoryExists(_logFileDir, out DirectoryInfo logFileDirInfo))
                {
                    return false;
                }

                lock (_rotationLock)
                {
                    int nextRotationIndex;
                    FileStream logStream;
                    StreamWriter logWriter;

                    if (!_logGroupMutex.TryAcquire(out Mutex mutex))
                    {
                        return false;  // Disposed or error => give up.
                    }

                    try
                    {
                        DateTimeOffset now = DateTimeOffset.Now;
                        int lastRotationIndexOnDisk = FindLatestRotationIndex(logFileDirInfo, _logFileNameBase, now);

                        nextRotationIndex = (lastRotationIndexOnDisk > _rotationIndex)
                                                    ? lastRotationIndexOnDisk
                                                    : _rotationIndex + 1;

                        string logFileName = ConstructFilename(_logFileNameBase, now, nextRotationIndex);
                        string logFilePath = Path.Combine(_logFileDir, logFileName);
                        logStream = new FileStream(logFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                        logWriter = new StreamWriter(_logStream, LogTextEncoding);
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }

                    _rotationIndex = nextRotationIndex;
                    _logStream = logStream;
                    _logWriter = logWriter;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
