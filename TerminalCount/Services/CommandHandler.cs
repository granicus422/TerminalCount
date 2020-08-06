using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using TerminalCount.Modules;
using MySql.Data.MySqlClient;
using System.Data;
using System.Linq;

namespace TerminalCount.Services
{
    public class CommandHandler
    {
        // setup fields to be set later in the constructor
        private readonly IConfiguration _config;
        private readonly CommandService _commands;
        private readonly DiscordSocketClient _client;
        private readonly IServiceProvider _services;
        private readonly string _connStr;

        public CommandHandler(IServiceProvider services)
        {
            // juice up the fields with these services
            // since we passed the services in, we can use GetRequiredService to pass them into the fields set earlier
            _config = services.GetRequiredService<IConfiguration>();
            _commands = services.GetRequiredService<CommandService>();
            _client = services.GetRequiredService<DiscordSocketClient>();
            _services = services;

            // take action when we execute a command
            _commands.CommandExecuted += CommandExecutedAsync;

            // take action when we receive a message (so we can process it, and see if it is a valid command)
            _client.MessageReceived += MessageReceivedAsync;
            _client.ReactionAdded += ReactionAddedAsync;
            _client.ReactionRemoved += ReactionRemovedAsync;

            //In secrets.json
            _connStr = _config["ConnStr"];
        }

        public async Task InitializeAsync()
        {
            // register modules that are public and inherit ModuleBase<T>.
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        }

        // this class is where the magic starts, and takes actions upon receiving messages
        public async Task MessageReceivedAsync(SocketMessage rawMessage)
        {
            // ensures we don't process system/other bot messages
            if (!(rawMessage is SocketUserMessage message))
            {
                return;
            }

            if (message.Source != MessageSource.User)
            {
                return;
            }

            // sets the argument position away from the prefix we set
            var argPos = 0;

            // get prefix from the configuration file
            string prefix = _config["Prefix"];

            // determine if the message has a valid prefix, and adjust argPos based on prefix
            if (!(message.HasMentionPrefix(_client.CurrentUser, ref argPos) || message.HasStringPrefix(prefix, ref argPos)))
            {
                return;
            }

            var context = new SocketCommandContext(_client, message);

            // execute command if one is found that matches
            var result = await _commands.ExecuteAsync(context, argPos, _services);
            if (!result.IsSuccess && result.Error==CommandError.UnknownCommand)
            {
                //var cmd = _commands.Commands.First(obj => obj.Module.Name == "PublicModule" && obj.Name == "help");
                //var parse = new ParseResult();
                //parse.
                //await cmd.ExecuteAsync(context, new ParseResult(), _services);
            }
        }

        public async Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> msg, ISocketMessageChannel channel, SocketReaction react)
        {
            var em = new Emoji("\u2705");
            //In secrets.json
#if DEBUG
            var botUserId = Convert.ToUInt64(_config["DiscordBotUserIdDev"]);
#else
            var botUserId = Convert.ToUInt64(_config["DiscordBotUserId"]);
#endif
            if (react.UserId != botUserId)
            {
                if (react.Emote.Name == em.Name)
                {
                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        int eventId = 0;
                        cmd.CommandText = "SELECT id FROM `events` WHERE discordId = @discordId AND retireDate IS NULL;";
                        cmd.Parameters.AddWithValue("@discordId", msg.Id);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                eventId = dr.GetInt32("id");
                            }
                        }
                        cmd.Parameters.Clear();

                        if (eventId > 0)
                        {
                            cmd.CommandText = $"INSERT INTO subscriptions (eventId,userId) VALUES (@eventId,@userId) ON DUPLICATE KEY UPDATE userId=userId;";
                            cmd.Parameters.AddWithValue("@eventId", eventId);
                            cmd.Parameters.AddWithValue("@userId", react.UserId);
                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();
                        }
                    }
                }
            }
            return;
        }

        public async Task ReactionRemovedAsync(Cacheable<IUserMessage, ulong> msg, ISocketMessageChannel channel, SocketReaction react)
        {
            var em = new Emoji("\u2705");
            //In secrets.json
#if DEBUG
            var botUserId = Convert.ToUInt64(_config["DiscordBotUserIdDev"]);
#else
            var botUserId = Convert.ToUInt64(_config["DiscordBotUserId"]);
#endif
            if (react.UserId != botUserId)
            {
                if (react.Emote.Name == em.Name)
                {
                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        int eventId = 0;
                        cmd.CommandText = "SELECT id FROM `events` WHERE discordId = @discordId AND retireDate IS NULL;";
                        cmd.Parameters.AddWithValue("@discordId", msg.Id);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                eventId = dr.GetInt32("id");
                            }
                        }
                        cmd.Parameters.Clear();

                        if (eventId > 0)
                        {
                            cmd.CommandText = $"DELETE FROM subscriptions WHERE eventId=@eventId AND userId=@userId";
                            cmd.Parameters.AddWithValue("@eventId", eventId);
                            cmd.Parameters.AddWithValue("@userId", react.UserId);
                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();
                        }
                    }
                }
            }
            return;
        }

        public async Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            // if a command isn't found, log that info to console and exit this method
            if (!command.IsSpecified)
            {
                System.Console.WriteLine($"Command failed to execute for [{context.User.Username}] <-> [{result.ErrorReason}]!");
                return;
            }


            // log success to the console and exit this method
            if (result.IsSuccess)
            {
                System.Console.WriteLine($"Command [{command.Value.Name}] executed for -> [{context.User.Username}]");
                return;
            }


            // failure scenario, let's let the user know
            await context.Channel.SendMessageAsync($"Sorry, {context.User.Username}... something went wrong...  use **!tc help** to see commands and options");
        }
    }
}