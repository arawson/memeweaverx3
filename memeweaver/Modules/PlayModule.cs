
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Discord.Commands;
using Microsoft.Extensions.Logging;
using System.Linq;

#nullable enable

namespace memeweaver.modules
{
    public class PlayModule : ModuleBase<SocketCommandContext>
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

        [Command("check", RunMode = RunMode.Async)]
        public async Task CheckAsync(string target) {
            Uri uriTarget = new Uri(target);

            await queries.GetVideoInformation(uriTarget);
        }

        [Command("stop", RunMode = RunMode.Async)]
        public async Task StopAsync() {
            logger.LogDebug($"Attempting to leave voice channel");

            var user = (Context.User as IGuildUser);

            if (user == null) {
                logger.LogInformation($"Got message from non guild user: {Context.User.Username}");
                return;
            }

            var guildID = user.GuildId;

            await voices.CancelStream(guildID);
        }

        [Command("skip", RunMode = RunMode.Async)]
        public async Task SkipAsync() {
            var audioCheck = await CheckAudioJoin();
            if (audioCheck == null) return;
            voices.SkipPlay(audioCheck.Item1);
        }

        // [Command("play", RunMode = RunMode.Async)]
        // public async Task PlayAsync() {
        //     // TODO rando play
        //     await Context.Message.ReplyAsync("hep");
        // }

        [Command("play", RunMode = RunMode.Async)]
        public async Task PlayAsync(params string[] args) {
            //TODO wrap all this in a transaction
            string target = string.Join(' ', args);

            var audioCheck = await CheckAudioJoin();
            if (audioCheck == null) throw new InvalidOperationException("NOPE!");
            var guildID = audioCheck.Item1;
            var voiceChannel = audioCheck.Item2;

            Uri? targetUri = null;

            // pull in a random one for the server
            if (String.IsNullOrWhiteSpace(target))
            {
                targetUri = await settings.RandomPlayableForGuild(guildID);
            }

            if (targetUri == null && !Uri.TryCreate(target, UriKind.Absolute, out targetUri))
            {
                try {
                // fallback to YT search for URI
                    targetUri = await queries.SearchYoutube(target);
                } catch (Exception ex) {
                    logger.LogError(ex, "Failure querying youtube.");
                    return;
                }
            }

            if (targetUri == null)
            {
                // TODO should I send a message back?
                logger.LogError($"No YT search for and no URI possible for {target}");
                return;
            }

            // TODO message about already playing
            if (voices.IsPlayingAndEnqueue(guildID, targetUri)) return;

            AudioOutStream? stream = null;
            NetQueryService.VidFormat? vidInfo = null;
            CancellationToken? skipTok = null;

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
                                        ytdl.CloseMainWindow();
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
                                        in_stage_2.Flush();
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
                                                // throw;
                                            }
                                        };
                                        Func<Task> stage_2 = async () => {
                                            try {
                                                await out_stage_2.CopyToAsync(stream);
                                            } catch (Exception ex) {
                                                logger.LogCritical($"Stage 2 Audio Failure for guild {guildID}");
                                                logger.LogCritical(ex.ToString());
                                                logger.LogCritical(ex.StackTrace);
                                                // throw;
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

        private async Task<Tuple<ulong, IVoiceChannel>?> CheckAudioJoin() {
            var user = (Context.User as IGuildUser);

            if (user == null) {
                logger.LogInformation($"Got message from non guild user: {Context.User.Username}");
                return null;
            }

            logger.LogDebug($"Checking join voice channel of user {user.Username} in guild {user.GuildId}");

            var guildID = user.GuildId;
            var voiceChannel = user.VoiceChannel;

            // can I get the current user's voice channel?
            var me = (Context.Client.CurrentUser as ISelfUser);
            if (me == null) logger.LogInformation("Self is not self user.");
            
            if (voiceChannel == null) {
                //TODO: figure out how to make error messages replies
                await Context.Channel.SendMessageAsync("You must be in a voice channel for that to work!");
                return null;
            }

            return Tuple.Create(guildID, voiceChannel);
        }
    }
}
