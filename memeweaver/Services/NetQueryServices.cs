
using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

using System.Collections.Generic;
using YoutubeExplode;
using YoutubeExplode.Search;

#nullable enable

namespace memeweaver
{
    public class NetQueryService
    {
        public class VidFormat {
            public string ACodec = "";
            public string Container = "";
            public int YTDLFormatID;
            public int SampleRate;
            public int BitRate;
            public string Ext = "";
            public string? Id = null;
        }

        private ILogger Logger { get; init; }

        public NetQueryService(IServiceProvider services) {
            Logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("NetQuery");

            var config = services.GetRequiredService<IConfiguration>();
        }

        public async Task<Uri?> SearchYoutube(string keywords)
        {
            var youtube = new YoutubeClient();
            List<Uri?> videos = new List<Uri?>();

            await foreach (var result in youtube.Search.GetVideosAsync(keywords))
            {
                Uri? u = null;
                Uri.TryCreate(
                    result.Url,
                    UriKind.Absolute,
                    out u);
                return u;
            }
            return null;
        }

        public async Task<string?> DownloadOpus(Uri target, string directory) {
            
            try {
                // TODO factor out this common pattern
                var tcs = new TaskCompletionSource<string?>();
                var p = new Process();
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.FileName = "youtube-dl";
                p.StartInfo.ArgumentList.Add("-x");
                p.StartInfo.ArgumentList.Add("--id");
                p.StartInfo.ArgumentList.Add("--audio-quality");
                p.StartInfo.ArgumentList.Add("1");
                p.StartInfo.ArgumentList.Add(target.ToString());
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.WorkingDirectory = directory;
                p.EnableRaisingEvents = true;

                var stdout = new StringBuilder();
                // var stderr = new StringBuilder();

                p.OutputDataReceived += (sender, e) => {
                    if (!String.IsNullOrEmpty(e.Data)) {
                        stdout.Append(e.Data);
                        stdout.Append('\n');
                    }
                    Logger.LogInformation(e.Data);
                };

                // p.ErrorDataReceived += (sender, e) => {
                //     if (!String.IsNullOrEmpty(e.Data)) stderr.Append(e.Data);
                // };

                p.Exited += (sender, args) => {
                    // the target line looks like this:
                    //[download] Destination: tZejhuSGCpE.m4a
                    //regex something like:
                    //"\[download\] Destination: (\w.*\.\w+)"
                    var matches = Regex.Matches(
                        stdout.ToString(),
                        @"Destination: (\w+\.\w+)"
                    );
                    string? filename = matches.Last().Groups[1].Value;

                    p.Dispose();

                    tcs.SetResult(filename);
                };

                p.Start();
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();

                return await tcs.Task;

            } catch (Exception ex) {
                Logger.LogError(ex, $"Failed to get download URL: {target.ToString()}");
                throw;
            }

        }

        public Task<Tuple<int, string, string>> QueryYTDL(Uri target) {
            try {
                var tcs = new TaskCompletionSource<Tuple<int, string, string>>();
                var p = new Process();
                p.StartInfo.RedirectStandardError = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.FileName = "youtube-dl";
                p.StartInfo.ArgumentList.Add("-J");
                p.StartInfo.ArgumentList.Add(target.ToString());
                p.EnableRaisingEvents = true;

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();

                // i sure hope there's no data races here
                // because .Net seems dead set on only reading the first few
                // lines
                p.OutputDataReceived += (sender, e) => {
                    if (!String.IsNullOrEmpty(e.Data)) stdout.Append(e.Data);
                };

                // p.ErrorDataReceived += (sender, e) => {
                //     if (!String.IsNullOrEmpty(e.Data)) stderr.Append(e.Data);
                // };

                p.Exited += (sender, args) => {
                    tcs.SetResult(
                        Tuple.Create(p.ExitCode,
                            stdout.ToString(),
                            stderr.ToString()
                        )
                    );
                    p.Dispose();
                };

                p.Start();
                p.BeginOutputReadLine();
                // p.BeginErrorReadLine();

                // p.WaitForExit();

                return tcs.Task;
            } catch (Exception ex){
                Logger.LogError(ex, $"Failed to get information for URL: {target.ToString()}");
                throw;
            }
        }

        public Process GetYTDLProcess(Uri target, VidFormat info) {
            var p = new Process();
            p.StartInfo.RedirectStandardError = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.EnableRaisingEvents = true;

            p.Exited += (sender, e) => {
                Logger.LogInformation("YTDL Process Finished");
            };

            // Now for the magic. We're gonna make it happen!
            // Looks a bit like this:
            // youtube-dl -f 251 https://www.youtube.com/watch\?v\=rds6pdACeLE --output -
            // | ffmpeg -i pipe:0 -f wav - > test2.wav

            p.StartInfo.FileName = "youtube-dl";
            p.StartInfo.ArgumentList.Add("-f");
            p.StartInfo.ArgumentList.Add($"{info.YTDLFormatID}");
            p.StartInfo.ArgumentList.Add(target.ToString());
            p.StartInfo.ArgumentList.Add("--output");
            p.StartInfo.ArgumentList.Add("-");

            Console.WriteLine("cmd = " + p.StartInfo.ToString());

            return p;
        }

        public Process GetFFMPEGFileInputProcess(string path) {
            var p = new Process();
            
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.EnableRaisingEvents = true;

            p.Exited += (sender, e) => {
                Logger.LogInformation("FFMPEG Process Finished");
            };

            p.StartInfo.FileName = "ffmpeg";

            p.StartInfo.ArgumentList.Add("-i");
            p.StartInfo.ArgumentList.Add(path);

            p.StartInfo.ArgumentList.Add("-f");
            p.StartInfo.ArgumentList.Add("s16le");
            p.StartInfo.ArgumentList.Add("-ar");
            p.StartInfo.ArgumentList.Add("48000");

            p.StartInfo.ArgumentList.Add("-"); // - is stdout (so is -o pipe:1)

            Console.WriteLine("cmd = " + p.StartInfo.ToString());

            return p;
        }

        public Process GetFFMPEGProcess() {
            var p = new Process();
            p.StartInfo.RedirectStandardError = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.EnableRaisingEvents = true;

            p.Exited += (sender, e) => {
                Logger.LogInformation("FFMPEG Process Finished");
            };

            p.StartInfo.FileName = "ffmpeg";

            p.StartInfo.ArgumentList.Add("-i");
            p.StartInfo.ArgumentList.Add("pipe:0"); // pipe:0 is stdin

            p.StartInfo.ArgumentList.Add("-f");
            p.StartInfo.ArgumentList.Add("s16le");
            p.StartInfo.ArgumentList.Add("-ar");
            p.StartInfo.ArgumentList.Add("48000");

            p.StartInfo.ArgumentList.Add("-"); // - is stdout (so is -o pipe:1)

            Console.WriteLine("cmd = " + p.StartInfo.ToString());

            return p;
        }

        public async Task<VidFormat?> GetVideoInformation(Uri target) {
            var ytdl = await QueryYTDL(target);

            JObject ytdlOutput = JObject.Parse(ytdl.Item2);

            return GetBestAudioFormat(ytdlOutput);            
        }

        public VidFormat? GetBestAudioFormat(JObject ytdlOutput) {
            VidFormat? vf = null;

            string? id = ytdlOutput["id"]?.ToString();
            if (id == null) {
                throw new InvalidOperationException("Could not find id for video");
            }

            var f = (
                from format in ytdlOutput["formats"]
                select new {
                    FormatID = format["format_id"],
                    Ext = format["ext"],
                    ACodec = format["acodec"],
                    VCodec = format["vcodec"],
                    SampleRate = format["asr"],
                    BitRate = format["abr"],
                    Container = format["container"]
                }
                into format2
                where format2.VCodec.ToString() == "none"
                orderby format2.BitRate descending
                select format2
            ).First();

            if (f != null) {
                vf = new VidFormat {
                    YTDLFormatID = int.Parse(f.FormatID.ToString()),
                    ACodec = f.ACodec.ToString(),
                    BitRate = (int)double.Parse(f.BitRate.ToString()),
                    Container = f.Container?.ToString() ?? "",
                    Ext = f.Ext.ToString(),
                    SampleRate = int.Parse(f.SampleRate.ToString()),
                    Id = id
                };
                Logger.LogInformation($"Using FormatID from format info {f.ToString()}");
            }

            if (vf == null) {
                throw new InvalidOperationException($"No audio formats found.");
            } else {
                return vf;
            }
        }
    }
}
