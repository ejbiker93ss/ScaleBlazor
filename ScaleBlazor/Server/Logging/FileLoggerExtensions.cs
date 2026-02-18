using Microsoft.Extensions.Logging;

namespace ScaleBlazor.Server.Logging;

public static class FileLoggerExtensions
{
    private const long DefaultMaxFileSizeBytes = 5 * 1024 * 1024;

    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string path, long maxFileSizeBytes = DefaultMaxFileSizeBytes)
    {
        builder.AddProvider(new FileLoggerProvider(path, maxFileSizeBytes));
        return builder;
    }
}
