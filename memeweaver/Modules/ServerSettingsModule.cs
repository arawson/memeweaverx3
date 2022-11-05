
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#nullable enable

namespace memeweaver.modules
{
    public class SettingsModule : InteractionModuleBase<SocketInteractionContext>
    {
        private ILogger Logger { get; init; }

        private ServerSettingService Settings { get; init; }

        private NetQueryService Queries { get; init; }

        public SettingsModule(IServiceProvider services)
        {
            Logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PlayableCache");
            Settings = services.GetRequiredService<ServerSettingService>();
            Queries = services.GetRequiredService<NetQueryService>();
        }

        public override void OnModuleBuilding(InteractionService commandService, ModuleInfo module)
        {
            base.OnModuleBuilding(commandService, module);
            Logger.LogInformation("Settings Module building.");
        }

        public enum MWXType {
            play
        }

        [SlashCommand("add", "Add a meme to the random play pool", false, RunMode.Async)]
        public async Task AddAsync(MWXType type, string target)
        {
            await Context.Interaction.DeferAsync(true);
            switch (type)
            {
                case MWXType.play:
                await AddPlayable(target);
                break;
            }
        }

        private async Task AddPlayable(string target)
        {
            
            ulong guildId = (Context.User as IGuildUser)?.GuildId
                ?? Context.Interaction.GuildId
                ?? 0;
            if (guildId == 0) {
                Logger.LogInformation($"Received message from non-server user {Context.User}");
                await DiscordHelper.ModifyOriginalResponseAsync(Context, "I can't tell what server you ar on. Please contact your server admin.");
                return;
            }

            Uri? uri;
            try {
                uri = new Uri(target);
            } catch (InvalidOperationException ex) {
                Logger.LogError(ex, "Bad URL");
                await DiscordHelper.ModifyOriginalResponseAsync(Context, "Something's borked with that URL.");
                return;
            }

            NetQueryService.VidFormat? vf;
            try {
                vf = await Queries.GetVideoInformation(uri);
            } catch (Exception ex) {
                Logger.LogError(ex, "Bad YTDL Lookup");
                await DiscordHelper.ModifyOriginalResponseAsync(Context, "I couldn't read that URL.");
                return;
            }

            // Playable? p = Settings.GetPlayable(uri);

            Settings.PutPlayableURI(guildId, uri);
            await DiscordHelper.ModifyOriginalResponseAsync(Context, "Done.");
        }
    }
}
