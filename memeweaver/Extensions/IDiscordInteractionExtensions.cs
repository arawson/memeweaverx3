
using System.Threading.Tasks;
using Discord;

namespace memeweaver.Extensions;

public static class IDiscordInteractionExtensions
{
    /// <summary>
    /// Shortcut which updates the original response with new content.
    /// </summary>
    /// <param name="Interaction">The interaction to work upon.</param>
    /// <param name="Content">The new message content to send.</param>
    /// <returns></returns>
    public static async Task ModifyOriginalResponseAsync(
        this IDiscordInteraction Interaction,
        string Content
    ) {
        await Interaction
            .ModifyOriginalResponseAsync(
                x => x.Content = Content
            );
    }
}
