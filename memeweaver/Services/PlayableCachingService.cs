
using System;
using System.Linq;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

#nullable enable

namespace memeweaver
{
    public class PlayableCachingService
    {
        private ILogger Logger { get; init; }

        private MemeMySqlContext Context { get; init; }

        private NetQueryService Queries { get; init; }

        private string MemeStoragePath { get; init; }

        private int MemeCacheLimit { get; init; }

        public PlayableCachingService(IServiceProvider services) {
            Logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PlayableCache");
            Context = services.GetRequiredService<MemeMySqlContext>();
            Queries = services.GetRequiredService<NetQueryService>();
            var config = services.GetRequiredService<IConfiguration>();
            MemeCacheLimit = config.GetValue<int>("memeCacheLimit");
            MemeStoragePath = config["memeStoragePath"];
            // TODO check MemeStoragePath is a directory and writable
        }

        // the interface the play module needs is simple:
        // put in a uri
        // get back either the uri of the cached output or null if it hasn't been cached
        // downloading it directly if it is going to be cached

        public async Task<Tuple<Uri,NetQueryService.VidFormat?>> GetCacheFor(Uri source)
        {
            Uri resultUri = source;
            NetQueryService.VidFormat? resultFormat = null;

            Playable cacheEntry = await Context.GetOrCreatePlayable(source);

            try {
                resultFormat = await Queries.GetVideoInformation(source);
            } catch (InvalidOperationException ex) {
                Logger.LogError(ex, "youtube-dl couldn't analyze {uri}", source);
            }

            if (cacheEntry.StoragePath != null) {
                resultUri = new Uri(cacheEntry.StoragePath);
            }
            else if (cacheEntry.PlayCount >= MemeCacheLimit) {
                // for now, do nothing, eventually, replace the uri after downloading
                Logger.LogInformation($"Attempting to download {source}");
                string? resultString = await Queries.DownloadOpus(source, MemeStoragePath);
                if (resultString == null) {
                    throw new InvalidOperationException("Unable to download URI: " + source);
                }
                var p1 = Path.Combine(MemeStoragePath, resultString);
                var p2 = Path.GetFullPath(p1);
                Logger.LogInformation($"Saved as {p2}");
                resultUri = new Uri("file://" + p2);
                cacheEntry.StoragePath = resultUri.ToString();
            }

            await Context.SaveChangesAsync();

            return Tuple.Create(resultUri, resultFormat);
        }

        public async Task CachedPlayFinished(Uri source) {
            var cacheEntry =
                await Context
                .Playables
                .AsAsyncEnumerable()
                .Where<Playable>(p => p.Location == source)
                .FirstAsync();
            
            if (cacheEntry == null) {
                Logger.LogError($"CachedPlayFinished: Missing cache entry for {source}");
                return;
            }

            cacheEntry.PlayCount++;

            await Context.SaveChangesAsync();
        }
    }
}
