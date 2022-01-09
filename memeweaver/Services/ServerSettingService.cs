
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

#nullable enable

namespace memeweaver
{
    public class ServerSettingService
    {
        private ILogger Logger { get; init; }

        private MemeMySqlContext Context { get; init; }

        private NetQueryService Queries { get; init; }

        private Random Random { get; init; } = new Random();

        public ServerSettingService(IServiceProvider services) {
            Logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PlayableCache");
            Context = services.GetRequiredService<MemeMySqlContext>();
            Queries = services.GetRequiredService<NetQueryService>();
        }

        private async Task<ServerSetting> GetOrCreateSettings(ulong guildId) {
            ServerSetting? ss = await
                Context.ServerSettings
                .Include(s => s.Playables)
                .FirstOrDefaultAsync<ServerSetting>(
                    s => s.GuildId == guildId
                );
            
            if (ss == null) {
                ss = new ServerSetting();
                ss.GuildId = guildId;
                Context.ServerSettings.Add(ss);
            }

            return ss;
        }

        public async void PutPlayableURI(ulong guildId, Uri uri) {
            var ss = await GetOrCreateSettings(guildId);

            Playable playable = await Context.GetOrCreatePlayable(uri);

            if (!ss.Playables.Contains(playable))
                ss.Playables.Add(playable);
            if (!playable.ServerSettings.Contains(ss))
                playable.ServerSettings.Add(ss);

            await Context.SaveChangesAsync();
        }

        public async Task<Uri?> RandomPlayableForGuild(ulong guildID)
        {
            ServerSetting? ssetting = await GetOrCreateSettings(guildID);

            var c = ssetting.Playables.Count;

            if (c == 0) {
                Logger.LogInformation($"Random on server {guildID} but no playables yet.");
                return null;
            }
            var rid = Random.Next(0, c);

            return ssetting.Playables.ToList()[rid].Location;
        }
    }
}
