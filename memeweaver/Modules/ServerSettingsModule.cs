
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#nullable enable

namespace memeweaver.modules
{
    public class SettingsModule : ModuleBase<SocketCommandContext>
    {
        private ILogger Logger { get; init; }

        private ServerSettingService Settings { get; init; }

        private NetQueryService Queries { get; init; }

        public SettingsModule(IServiceProvider services) {
            Logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PlayableCache");
            Settings = services.GetRequiredService<ServerSettingService>();
            Queries = services.GetRequiredService<NetQueryService>();
        }

        public enum MWXType {
            play
        }

        [Command("add", RunMode = RunMode.Async)]
        public async Task AddAsync(MWXType type, string target)
        {
            switch (type)
            {
                case MWXType.play:
                await AddPlayable(target);
                break;
            }
        }

        private async Task AddPlayable(string target)
        {
            ulong guildId = (Context.User as IGuildUser)?.GuildId ?? 0;
            if (guildId == 0) {
                Logger.LogInformation($"Received message from non-server user {Context.User}");
                return;
            }

            Uri? uri;
            try {
                uri = new Uri(target);
            } catch (InvalidOperationException ex) {
                Logger.LogError(ex, "Bad URL");
                await Context.Message.ReplyAsync("Something's borked with that URL.");
                return;
            }

            NetQueryService.VidFormat? vf;
            try {
                vf = await Queries.GetVideoInformation(uri);
            } catch (Exception ex) {
                Logger.LogError(ex, "Bad YTDL Lookup");
                await Context.Message.ReplyAsync("I couldn't read that URL.");
                return;
            }

            // Playable? p = Settings.GetPlayable(uri);

            Settings.PutPlayableURI(guildId, uri);
        }
    }
}
