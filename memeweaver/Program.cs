using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace memeweaver
{
    class Program
    {
        static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

        private ILogger _logger;
        private ILogger _discordClientLogger;

        public async Task MainAsync()
        {

            var services = ConfigureServices();

            _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
            _discordClientLogger  = services.GetRequiredService<ILoggerFactory>().CreateLogger("Discord Client");
            _logger.LogInformation("DI is done. Starting Command Services and Discord Client.");

            var _client = services.GetRequiredService<DiscordSocketClient>();
            var _config = services.GetRequiredService<IConfiguration>();

            _client.Log += DiscordClientLogAsync;

            await _client.LoginAsync(TokenType.Bot, _config["token"]);
            await _client.StartAsync();
            await _client.SetActivityAsync(new Game(_config["playing"]));

            await services.GetRequiredService<InteractionHandlerService>().InitializeAsync();

            _logger.LogInformation("MainAsync completed, awaiting service kill signal");
            await Task.Delay(Timeout.Infinite);
        }

        
        // TODO: is there a way to not copy paste these 2 implementations?
        // a default logging implementation of some sort?
        private async Task DiscordClientLogAsync(LogMessage log)
        {
            var severity = DiscordHelper.GetLogLevel(log.Severity);

            if (!String.IsNullOrWhiteSpace(log.Message))
                _discordClientLogger.Log(severity, $"Message {log.Message} (Source = {log.Source})");
            if (log.Exception != null)
                _discordClientLogger.Log(severity, $"Exception {log.Exception}");

            // async nop to stop warning
            await Task.FromResult(0);
        }

        private ServiceProvider ConfigureServices()
        {
            var _config = BuildConfig();

            return new ServiceCollection()
                .AddSingleton(_config)

                .AddLogging()
                .AddSingleton(LoggerFactory.Create(builder => {
                    builder
                        .SetMinimumLevel(LogLevel.Debug)
                        .AddFile(_config.GetSection("logging"))
                        .AddConsole();
                }))

                .AddDbContext<MemeMySqlContext>(options =>
                    options.UseMySQL(_config.GetConnectionString("MemeweaverDatabase")))

                .AddSingleton(new DiscordSocketConfig() {
                    GatewayIntents = GatewayIntents.AllUnprivileged,
                    AlwaysDownloadUsers = true
                })
                .AddSingleton<DiscordSocketClient>()
                
                .AddSingleton<InteractionService>(
                    x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
                .AddSingleton<InteractionHandlerService>()
                
                // TODO: figure out why the HttpClient is a singleton
                .AddSingleton<HttpClient>()
                .AddSingleton<PlayableCachingService>()
                .AddSingleton<ServerSettingService>()

                // voice service needs to provide persistence so it has to be a singleton
                .AddSingleton<VoiceClientService>()
                .AddSingleton<NetQueryService>()
                .BuildServiceProvider();
        }

        public static bool IsDebug()
        {
            #if DEBUG
                return true;
            #else
                return false;
            #endif
        }

        private IConfiguration BuildConfig()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json")
                .Build();
        }
    }
}
