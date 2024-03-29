﻿using System;
using Discord;
using Discord.Net;
using Discord.Commands;
using Discord.WebSocket;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using TerminalCount.Services;
using System.Threading;
using Serilog;
using Discord.Rest;

namespace TerminalCount
{
    class Program
    {
        // setup our fields we assign later
        private readonly IConfiguration _config;
        private DiscordSocketClient _client;
        private DiscordRestClient _restClient;

        static void Main(string[] args)
        {
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public Program()
        {
            // create the configuration
            var _builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(path: "appsettings.json")
#if DEBUG || ServerTest
                .AddJsonFile(path: "appsettings.Development.json");
            _builder.AddUserSecrets<Program>();
#else
            //https://github.com/rajanadar/VaultSharp
                        .AddJsonFile("secrets.json");
#endif

            // build the configuration and assign to _config          
            _config = _builder.Build();
        }

        public async Task MainAsync()
        {
            // call ConfigureServices to create the ServiceCollection/Provider for passing around the services
            using (var services = ConfigureServices())
            {
                // get the client and assign to client 
                // you get the services via GetRequiredService<T>
                var client = services.GetRequiredService<DiscordSocketClient>();
                _client = client;
                var restClient = services.GetRequiredService<DiscordRestClient>();
                _restClient = restClient;

                // setup logging and the ready event
                client.Log += LogAsync;
                client.Ready += ReadyAsync;
                services.GetRequiredService<CommandService>().Log += LogAsync;

                // this is where we get the Token value from the configuration file, and start the bot
                //In secrets.json
#if DEBUG || ServerTest
                await client.LoginAsync(TokenType.Bot, _config["TokenDev"]);
                await restClient.LoginAsync(TokenType.Bot, _config["TokenDev"]);
#else
                await client.LoginAsync(TokenType.Bot, _config["Token"]);
                await restClient.LoginAsync(TokenType.Bot, _config["Token"]);
#endif
                await client.StartAsync();

                // we get the CommandHandler class here and call the InitializeAsync method to start things up for the CommandHandler service
                await services.GetRequiredService<CommandHandler>().InitializeAsync();

                await Task.Delay(Timeout.Infinite);
            }
        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        }

        private Task ReadyAsync()
        {
            Console.WriteLine($"Connected as -> [{_client.CurrentUser}] :)");
            return Task.CompletedTask;
        }

        // this method handles the ServiceCollection creation/configuration, and builds out the service provider we can call on later
        private ServiceProvider ConfigureServices()
        {
            var serilogLogger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration: _config)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();
            Serilog.Debugging.SelfLog.Enable(Console.Error);
            Log.Logger = serilogLogger;
            var dscc = new DiscordSocketConfig();
            dscc.AlwaysDownloadUsers = true;
            var dc = new DiscordSocketClient(dscc);
            // this returns a ServiceProvider that is used later to call for those services
            // we can add types we have access to here, hence adding the new using statement:
            // using csharpi.Services;
            // the config we build is also added, which comes in handy for setting the command prefix!
            return new ServiceCollection()
                .AddSingleton(_config)
                .AddLogging(configure => configure.AddSerilog(logger: serilogLogger))
                .AddSingleton<DiscordSocketClient>(dc)
                .AddSingleton<DiscordRestClient>()
                .AddSingleton<CommandService>()
                .AddSingleton<CommandHandler>()
                .BuildServiceProvider();
        }
    }
}