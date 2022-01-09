using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
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

        // TODO why on Earth do I have these underscores here?
        private IConfiguration _config;
        private DiscordSocketClient _client;
        private ILoggerFactory _loggerFactory;
        private ILogger _logger;

        public async Task MainAsync()
        {
            _client = new DiscordSocketClient();
            _config = BuildConfig();
            // create the logger statically because that DI is too complicated for the moment
            _loggerFactory = LoggerFactory.Create(builder => {
                    builder
                        .SetMinimumLevel(LogLevel.Debug)
                        .AddFile(_config.GetSection("logging"))
                        .AddConsole();
                });

            var services = ConfigureServices();

            _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
            _logger.LogInformation("DI is done. Starting Command Services and Discord Client.");

            await services.GetRequiredService<CommandHandlingService>().InitializeAsync();

            await _client.LoginAsync(TokenType.Bot, _config["token"]);
            await _client.StartAsync();
            await _client.SetActivityAsync(new Game(_config["playing"]));
            await Task.Delay(-1);
        }

        private ServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddDbContext<MemeMySqlContext>(options =>
                    options.UseMySQL(_config.GetConnectionString("MemeweaverDatabase")))
                // .AddSingleton<DbContextOptions<MemeMySqlContext>>()
                // .AddSingleton<MemeMySqlContext>()
                .AddSingleton(_client)
                // TODO I bet singletons are causing the duplicate error I saw
                .AddSingleton<CommandService>()
                .AddSingleton<CommandHandlingService>()
                .AddLogging()
                .AddSingleton(_config)
                .AddSingleton(_loggerFactory)
                //from original example
                .AddSingleton<HttpClient>()
                .AddSingleton<PlayableCachingService>()
                .AddSingleton<ServerSettingService>()
                // voice service needs to provide persistence so it has to be a singleton
                .AddSingleton<VoiceClientService>()
                .AddSingleton<NetQueryService>()
                .BuildServiceProvider();
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
