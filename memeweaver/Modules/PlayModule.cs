
using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Microsoft.Extensions.Logging;
using Discord.Interactions;
using memeweaver.Extensions;
using System.Linq;

#nullable enable

namespace memeweaver.modules
{
    public class PlayModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly ILogger logger;
        private readonly VoiceClientService voices;
        private readonly NetQueryService queries;
        private readonly PlayableCachingService cache;
        private readonly ServerSettingService settings;

        public PlayModule(ILoggerFactory loggerFactory, VoiceClientService voices,
            NetQueryService queries, PlayableCachingService cache,
            ServerSettingService settings) {
            logger = loggerFactory.CreateLogger("PlayModule");

            logger.LogInformation("Module initialized.");
            this.voices = voices;
            this.queries = queries;
            this.cache = cache;
            this.settings = settings;
        }

        [SlashCommand("check", "Get information on a URL to play.", false, RunMode.Async)]
        public async Task CheckAsync(string url) {
            // defer because this could be a long operation
            await Context.Interaction.DeferAsync();

            Uri uriTarget;
            try {
                uriTarget = new Uri(url);
            
            
                if (await queries.GetVideoInformation(uriTarget) == null) {
                    await Context.Interaction.ModifyOriginalResponseAsync("I probably can't play that.");
                } else {
                    await Context.Interaction.ModifyOriginalResponseAsync("I should be able to play that.");
                }
            }
            catch (UriFormatException)
            {
                await Context.Interaction.ModifyOriginalResponseAsync("That is not a valid URL.");
            }
        }

        [SlashCommand("stop", "Stop the currently playing audio in your voice channel.", false, RunMode.Async)]
        public async Task StopAsync() {
            logger.LogTrace($"StopAsync");
            await Context.Interaction.DeferAsync();

            var audiocheck = await GetAudioJoinInfo();

            if (audiocheck == null || audiocheck.Item1 == 0) {
                logger.LogInformation($"Got message from non guild user: {Context.User.Username}");
                await Context.Interaction.ModifyOriginalResponseAsync("I couldn't get the server you were on. Please contact your server admin.");
                return;
            }

            await voices.CancelStream(audiocheck.Item1);
            await Context.Interaction.ModifyOriginalResponseAsync("Done.");
        }

        [SlashCommand("skip", "Skip the currently playing URL.", false, RunMode.Async)]
        public async Task SkipAsync() {
            var audioCheck = await GetAudioJoinInfo();
            if (audioCheck == null) return;
            voices.SkipPlay(audioCheck.Item1);
            await Context.Interaction.RespondAsync("Done.");
        }

        [SlashCommand("play", "Queue up a URL to play in voice chat.", false, RunMode.Async)]
        public async Task PlayAsync(
            [Summary(description: "Either a URL to play, a set of search tearms, or nothing for pick a registered thing at random.")]
            string? urlOrSearch = null
        ) {
            logger.LogTrace("PlayAsync");
            await Context.Interaction.DeferAsync();

            Uri? targetUri = null;
            string? searchTerms = null;
            string currentMessage = "";

            var UpdateStatus = async (string addContent) => {
                currentMessage += addContent + " ";
                await Context.Interaction.ModifyOriginalResponseAsync(currentMessage);
            };

            //TODO wrap all this in a transaction
            
            if (!String.IsNullOrEmpty(urlOrSearch)) {
                try {
                    targetUri = new Uri(urlOrSearch);
                    currentMessage += "That is a valid URL. ";
                    logger.LogTrace("Playing from a valid URI");
                }
                catch (UriFormatException) {
                    logger.LogTrace("Not a valid URI");
                }

                if (targetUri == null) {
                    searchTerms = urlOrSearch;
                    logger.LogTrace("Playing from search terms");
                }
            } else {
                // else nothing, just random play mode
                logger.LogTrace("Playing from the guild random list");
            }

            var audioCheck = await GetAudioJoinInfo();
            if (audioCheck == null) {
                logger.LogError($"Unable to join user audio for unknown reason.");
                await UpdateStatus("You must be in a voice channel for that to work!");
                return;
            }
            var guildID = audioCheck.Item1;
            var voiceChannel = audioCheck.Item2;

            // pull in a random one for the server
            if (targetUri == null && searchTerms == null)
            {
                targetUri = await settings.RandomPlayableForGuild(guildID);

                if (targetUri == null) {
                    await UpdateStatus("There aren't any memes added for your server yet. Try my `add` command to get started!");
                    return;
                }
            }

            if (targetUri == null && searchTerms != null)
            {
                try {
                    await UpdateStatus("Searching YouTube for that.");
                    // fallback to YT search for URI
                    targetUri = await queries.SearchYoutube(searchTerms);
                } catch (Exception ex) {
                    logger.LogError(ex, "Failure querying youtube.");
                    await UpdateStatus("I couldn't search YouTube for that. Please contact the bot admin.");
                    return;
                }
            }

            // targetUri still null means no search results, somehow
            if (targetUri == null && searchTerms != null)
            {
                logger.LogError($"No YT search results for {searchTerms}");
                await UpdateStatus("I didn't find any results for that on YouTube, traveller. You better find someone that sells weaker search terms!");
                return;
            } else {
                await UpdateStatus($"I found {targetUri} to play.");
            }

            // TODO message about already playing
            if (voices.IsPlayingAndEnqueue(guildID, targetUri!)) {
                await UpdateStatus("That has been queued for playback.");
                return;
            };

            AudioOutStream? stream = null;
            NetQueryService.VidFormat? vidInfo = null;
            CancellationToken? skipTok = null;

            // TODO: are we OK to keep doing stuff in this thread or do we need a worker?
            // Is responding to the interaction sufficient for Discord to be happy?
            try{

                Uri? uri = null;
                Uri? orig = null;
                while ((uri = voices.Dequeue(guildID)) != null) {
                    logger.LogInformation("Trying to play {uri}", uri);
                    // the only problem here is the extreme nesting
                    try {
                        var cacheEntry = await cache.GetCacheFor(uri);
                        vidInfo = cacheEntry.Item2;
                        orig = uri;
                        uri = cacheEntry.Item1;
                        if (vidInfo == null) {
                            logger.LogError("No video info for {uri}", uri);
                            break;
                        }
                    } catch (InvalidOperationException ex) {
                        logger.LogError(ex, "youtube-dl couldn't analyze {uri}", uri);
                        logger.LogError(ex, "Output");
                        break;
                    }

                    try {
                        if (stream == null) {
                            var result = await voices.AcquireStreamFor(guildID, voiceChannel);
                            stream = result.Item1;
                            skipTok = result.Item2;
                        }

                        if (uri != orig) {
                            string fpath = uri.AbsolutePath;
                            using (var ffmpeg = queries.GetFFMPEGFileInputProcess(fpath)) {
                                ffmpeg.Start();
                                using (var out_stage_2 = ffmpeg.StandardOutput.BaseStream) {
                                    // TODO register the cancellation token so we can skip
                                    skipTok?.Register(() => {
                                        logger.LogInformation($"Skip requested");
                                        ffmpeg.CloseMainWindow();
                                    });

                                    ffmpeg.Exited += (_,_) => {
                                        out_stage_2.Flush();
                                    };

                                    try {
                                        await out_stage_2.CopyToAsync(stream);
                                    }
                                    catch (Exception ex) {
                                        logger.LogCritical($"Stage 2 Audio Failure for guild {guildID}");
                                        logger.LogCritical(ex.ToString());
                                        logger.LogCritical(ex.StackTrace);
                                    }
                                    finally {
                                        await stream.FlushAsync();
                                    }
                                    await cache.CachedPlayFinished(orig);
                                }
                            }
                        } else {
                            using (var ytdl = queries.GetYTDLProcess(uri, vidInfo!))
                            using (var ffmpeg = queries.GetFFMPEGProcess())
                            {
                                //TODO: capture stderr from these 2 and log it
                                ytdl.Start();
                                ffmpeg.Start();
                                
                                using (var out_stage_1 = ytdl.StandardOutput.BaseStream)
                                using (var in_stage_2 = ffmpeg.StandardInput.BaseStream)
                                using (var out_stage_2 = ffmpeg.StandardOutput.BaseStream)
                                {

                                    // TODO register the cancellation token so we can skip
                                    skipTok?.Register(() => {
                                        logger.LogInformation($"Skip requested");
                                        ytdl.CloseMainWindow(); // on skip (on cached video) no process is associated with the handle
                                        ffmpeg.CloseMainWindow();
                                        in_stage_2.Flush();
                                        in_stage_2.Dispose();
                                    });

                                    // when youtube-dl exits, close the stream, which tells
                                    // ffmpeg to finish up
                                    ytdl.Exited += (_,_) => {
                                        // the next 2 lines appeared to make no difference on
                                        // the closed file exception
                                        // out_stage_1.Flush();
                                        // out_stage_1.Dispose();
                                        in_stage_2.Flush(); // 'Cannot access a closed file is happening here
                                        in_stage_2.Dispose();
                                    };
                                    try {
                                        // TODO determine if there's a better way to handle closure
                                        // currently we ignore the error caused by the ffmpeg process closing
                                        // but our copyto's still writing
                                        // normally, there's no problem with that
                                        Func<Task> stage_1 = async () => {
                                            try {
                                                await out_stage_1.CopyToAsync(in_stage_2);
                                            } catch (Exception ex) {
                                                logger.LogCritical($"Stage 1 Audio Failure for guild {guildID}");
                                                logger.LogCritical(ex.ToString());
                                                logger.LogCritical(ex.StackTrace);
                                                throw;
                                            }
                                        };
                                        Func<Task> stage_2 = async () => {
                                            try {
                                                await out_stage_2.CopyToAsync(stream);
                                            } catch (Exception ex) {
                                                logger.LogCritical($"Stage 2 Audio Failure for guild {guildID}");
                                                logger.LogCritical(ex.ToString());
                                                logger.LogCritical(ex.StackTrace);
                                                throw;
                                            }
                                        };
                                        Task.WaitAll(
                                            stage_1.Invoke(),
                                            stage_2.Invoke()
                                        );
                                        ffmpeg.CloseMainWindow();
                                        await cache.CachedPlayFinished(orig);
                                    }
                                    finally {
                                        await stream.FlushAsync();
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException ex) {
                        logger.LogCritical($"Skipped playing audio for guild {guildID}");
                        logger.LogCritical(ex.ToString());
                        logger.LogCritical(ex.StackTrace);
                    }
                    catch (Exception ex) {
                        logger.LogCritical($"Failure on playing audio for guild {guildID}");
                        logger.LogCritical(ex.ToString());
                        logger.LogCritical(ex.StackTrace);
                        throw;
                    }
                }
            }
            finally {
                // TODO finally is not run on !stop
                logger.LogCritical($"Stream is null? {stream == null}");
                if (stream != null) await voices.ReleaseStreamFor(guildID);
            }
        }

        private async Task<Tuple<ulong, IVoiceChannel>?> GetAudioJoinInfo() {
            logger.LogTrace("CheckAudioJoin");

            var guildID = Context.Interaction.GuildId;
            var user = Context.Interaction.User as IGuildUser;// Context.Guild.GetUser(Context.User.Id) as IGuildUser;
            var textChannel = await Context.Interaction.GetChannelAsync();

            if (guildID == null)
            {
                logger.LogInformation($"Got message from non guild user: {Context.User.Username}");
                return null;
            }
            

            if (user == null || guildID == null) {
                logger.LogInformation($"Got message from non guild user: {Context.User.Username}");
                return null;
            }

            logger.LogDebug($"Checking join voice channel of user {user.Username} in guild {guildID}");

            var voiceChannel = user.VoiceChannel;

            // can I get the current user's voice channel?
            var me = (Context.Client.CurrentUser as ISelfUser);
            if (me == null) logger.LogInformation("Self is not self user.");
            
            if (voiceChannel == null) {
                return null;
            }

            return Tuple.Create(guildID ?? 0, voiceChannel);
        }
    }
}
