using Microsoft.Extensions.Logging;

namespace ScaleBlazor.Server.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly long _maxFileSizeBytes;
    private readonly object _lock = new();

    public FileLoggerProvider(string path, long maxFileSizeBytes)
    {
        _path = path;
        _maxFileSizeBytes = maxFileSizeBytes;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_path, _maxFileSizeBytes, _lock, categoryName);

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _path;
        private readonly long _maxFileSizeBytes;
        private readonly object _lock;
        private readonly string _categoryName;

        public FileLogger(string path, long maxFileSizeBytes, object lockObject, string categoryName)
        {
            _path = path;
            _maxFileSizeBytes = maxFileSizeBytes;
            _lock = lockObject;
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception == null)
            {
                return;
            }

            var logLine = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] {_categoryName} - {message}";
            if (exception != null)
            {
                logLine = $"{logLine}{Environment.NewLine}{exception}";
            }

            lock (_lock)
            {
                RotateIfNeeded();
                File.AppendAllText(_path, logLine + Environment.NewLine);
            }
        }

        private void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                var info = new FileInfo(_path);
                if (info.Length < _maxFileSizeBytes)
                {
                    return;
                }

                var directory = Path.GetDirectoryName(_path) ?? string.Empty;
                var fileName = Path.GetFileNameWithoutExtension(_path);
                var extension = Path.GetExtension(_path);
                var archivePath = Path.Combine(directory, $"{fileName}-{DateTimeOffset.Now:yyyyMMddHHmmss}{extension}");
                File.Move(_path, archivePath, true);
            }
            catch
            {
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
