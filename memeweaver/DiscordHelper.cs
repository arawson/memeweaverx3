using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging;

namespace memeweaver;

public static class DiscordHelper
{
    public static LogLevel GetLogLevel(LogSeverity severity)
    {
        LogLevel level = LogLevel.Error;
        switch (severity)
        {
            case LogSeverity.Critical: level = LogLevel.Critical; break;
            case LogSeverity.Debug: level = LogLevel.Debug; break;
            case LogSeverity.Error: level = LogLevel.Error; break;
            case LogSeverity.Info: level = LogLevel.Information; break;
            case LogSeverity.Verbose: level = LogLevel.Trace; break;
        }
        return level;
    }

    public static async Task ModifyOriginalResponseAsync(
        IInteractionContext context,
        string message
    ) {
        await context
            .Interaction
            .ModifyOriginalResponseAsync(
                x => x.Content = message
            );
    }
}
