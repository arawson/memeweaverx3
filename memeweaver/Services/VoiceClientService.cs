
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

#nullable enable

namespace memeweaver
{
    public sealed class VoiceClientService
    {
        private class VoiceControlRegistration {
            // a semaphore is used because locks are not allowed in async code
            internal readonly SemaphoreSlim Control;
            internal IVoiceChannel? Channel;
            internal IAudioClient? Client;
            internal CancellationTokenSource? TokenSource;
            internal Queue<Uri> playQueue = new Queue<Uri>();

            internal VoiceControlRegistration() {
                Control = new SemaphoreSlim(1, 1);
            }
        }

        private readonly ILogger logger;
        private readonly ConcurrentDictionary<ulong, VoiceControlRegistration> voices = new ConcurrentDictionary<ulong, VoiceControlRegistration>();
        private readonly CancellationTokenSource tokenSource = new CancellationTokenSource();

        public VoiceClientService(ILoggerFactory loggerFactory, DiscordSocketClient _client) {
            logger = loggerFactory.CreateLogger("VoiceClientService");
            logger.LogInformation("Service initialized.");
        }

        public void EnqueueUri(ulong guildID, Uri uri) {
            VoiceControlRegistration vcr = voices.GetOrAdd(guildID, new VoiceControlRegistration());            
            lock(vcr.playQueue) {
                vcr.playQueue.Enqueue(uri);
            }
        }

        public bool IsPlayingAndEnqueue(ulong guildID, Uri uri) {
            VoiceControlRegistration vcr = voices.GetOrAdd(guildID, new VoiceControlRegistration());
            bool result = false;
            lock(vcr.playQueue) {
                result = vcr.Control.CurrentCount == 0;
                vcr.playQueue.Enqueue(uri);
            }
            return result;
        }

        public Uri? Dequeue(ulong guildID) {
            VoiceControlRegistration vcr = voices.GetOrAdd(guildID, new VoiceControlRegistration());
            Uri? result = null;
            lock(vcr.playQueue) {
                vcr.playQueue.TryDequeue(out result);
            }
            return result;
        }

        public async Task<Tuple<AudioOutStream, CancellationToken>> AcquireStreamFor(ulong guildID, IVoiceChannel channel) {
            SemaphoreSlim? slim = null;

            try {
                VoiceControlRegistration vcr = voices.GetOrAdd(guildID, new VoiceControlRegistration());
                slim = vcr.Control;

                logger.LogDebug($"Waiting on semaphore to acquire for {guildID}");
                await vcr.Control.WaitAsync();
                if (vcr.Channel != null) {
                    // cleanup the last stream
                    logger.LogDebug($"Changing voice channel for {guildID}");
                    await vcr.Channel.DisconnectAsync();
                }
                vcr.Channel = channel;
                vcr.TokenSource = new CancellationTokenSource();
                vcr.Client = await channel.ConnectAsync();

                return Tuple.Create(vcr.Client.CreatePCMStream(AudioApplication.Mixed, null, 200), vcr.TokenSource.Token);
            }
            catch(Exception ex) {
                logger.LogCritical($"Failure on setting audio client for guild {guildID}");
                logger.LogCritical(ex.ToString());
                logger.LogCritical(ex.StackTrace);
                throw;
            }
        }

        public async Task ReleaseStreamFor(ulong guildID) {
            SemaphoreSlim? slim = null;

            try {
                VoiceControlRegistration vcr = voices.GetOrAdd(guildID, new VoiceControlRegistration());
                slim = vcr.Control;

                logger.LogDebug($"Waiting on semaphore to release for {guildID}");
                // await vcr.Control.WaitAsync();
                if (vcr.Channel != null) await vcr.Channel.DisconnectAsync();
                vcr.Channel = null;
                vcr.Client = null;
                vcr.TokenSource = null;
            }
            catch(Exception ex) {
                logger.LogCritical($"Failure on setting audio client for guild {guildID}");
                logger.LogCritical(ex.ToString());
                logger.LogCritical(ex.StackTrace);
                throw;
            }
            finally {
                logger.LogDebug($"Releasing semaphore for {guildID}");
                slim?.Release();
            }
        }

        public bool CanAcquireStream(ulong guildID) {
            VoiceControlRegistration vcr = voices[guildID];
            return vcr.Control.CurrentCount > 0;
        }

        public void SkipPlay(ulong guildID) {
            try {
                var vcr = voices.GetOrAdd(guildID, new VoiceControlRegistration());
                logger.LogDebug($"skipping for guild {guildID}");
                vcr.TokenSource?.Cancel();
            }
            catch(Exception ex) {
                logger.LogCritical($"Failure on setting audio client for guild {guildID}");
                logger.LogCritical(ex.ToString());
                logger.LogCritical(ex.StackTrace);
                throw;
            }
        }

        public async Task CancelStream(ulong guildID) {
            try {
                var vcr = voices.GetOrAdd(guildID, new VoiceControlRegistration());
                logger.LogDebug($"forcing exit of voice for guild {guildID}");
                vcr.TokenSource?.Cancel();
                if (vcr.Channel != null) await vcr.Channel.DisconnectAsync();
                // TODO play's finally is never called so this is a workaround for that
                vcr.Control.Release();
            }
            catch(Exception ex) {
                logger.LogCritical($"Failure on setting audio client for guild {guildID}");
                logger.LogCritical(ex.ToString());
                logger.LogCritical(ex.StackTrace);
                throw;
            }
        }
    }
}
