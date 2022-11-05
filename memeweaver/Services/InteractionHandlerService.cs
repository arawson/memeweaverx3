
using System;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace memeweaver;

public class InteractionHandlerService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _handler;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    public InteractionHandlerService(
        DiscordSocketClient client,
        InteractionService handler,
        IServiceProvider services,
        IConfiguration config) 
    {
            _client = client;
            _handler = handler;
            _services = services;
            _configuration = config;

            _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("InteractionHandlerService");
    }

    public async Task InitializeAsync()
    {
        _logger.LogTrace("InitializeAsync");
        _client.Ready += ReadyAsync;
        _handler.Log += LogAsync;

        await _handler.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

        _client.InteractionCreated += HandleInteraction;
    }

    private async Task ReadyAsync()
    {
        _logger.LogTrace("ReadyAsync");
        // context and slash commands can be auto registered, but only after the
        // client enters the READY state.
        // global commands take around 1 hour to register, so we should use
        // a test guild to instantly update and test our commands

        if (Program.IsDebug()) {
            _logger.LogInformation("Registering commands against the test guild (instant)");
            await _handler.RegisterCommandsToGuildAsync(_configuration.GetValue<ulong>("testGuild"));
        } else {
            _logger.LogInformation("Registering commands globally (up to 1 hour)");
            await _handler.RegisterCommandsGloballyAsync(true);
        }
    }

    // TODO: is there a way to not copy paste these 2 implementations?
    // a default logging implementation of some sort?
    private async Task LogAsync(LogMessage log)
    {
        var severity = DiscordHelper.GetLogLevel(log.Severity);

        if (!String.IsNullOrWhiteSpace(log.Message))
            _logger.Log(severity, $"Message {log.Message} (Source = {log.Source})");
        if (log.Exception != null)
            _logger.Log(severity, $"Exception {log.Exception}");

        // async nop to stop warning
        await Task.FromResult(0);
    }

    private async Task HandleInteraction(SocketInteraction interaction)
    {
        try
        {
            // create a context to match the generic type parameter of the
            // InteractionModuleBase<T> modules.
            var context = new SocketInteractionContext(_client, interaction);

            // execute the command
            var result = await _handler.ExecuteCommandAsync(context, _services);

            if (!result.IsSuccess)
            {
                _logger.LogError($"Interactionc command failed with error {result.ErrorReason}");
                switch (result.Error)
                {
                    // TODO: implement the other command error conditions
                    // for logging
                    case InteractionCommandError.UnmetPrecondition:
                        break;
                    default:
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            // if SlashCommand execution fails, it is most likely that the
            // original interaction ack will persist.
            // it is a good idea to delete the original response, or at least
            // let the user know that something went wrong in the command exec
            _logger.LogError("Exception in HandleInteraction:");
            _logger.LogError(ex.ToString());

            if (interaction.Type is InteractionType.ApplicationCommand)
            {
                await interaction
                    .GetOriginalResponseAsync()
                    .ContinueWith(async (msg) => await msg.Result.DeleteAsync());
            }
        }
    }
}
