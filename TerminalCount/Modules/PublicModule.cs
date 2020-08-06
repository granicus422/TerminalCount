
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Commands;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.ComponentModel;
using System.Xml;

namespace TerminalCount.Modules
{
    // for commands to be available, and have the Context passed to them, we must inherit ModuleBase
    public class PublicModule : ModuleBase
    {

        // setup fields to be set later in the constructor
        private readonly IConfiguration _config;
        private readonly CommandService _commands;
        private readonly DiscordSocketClient _client;
        private readonly IServiceProvider _services;
        private readonly string _connStr;

        public PublicModule(IServiceProvider services)
        {
            _config = services.GetRequiredService<IConfiguration>();
            _commands = services.GetRequiredService<CommandService>();
            _client = services.GetRequiredService<DiscordSocketClient>();
            _services = services;

            //In secrets.json
            _connStr = _config["ConnStr"];
        }

        [Command("help"), Summary("help [cmd]"),Remarks("Receive more info on command")]
        [Alias("helpme")]
        public async Task Help(string args=null)
        {
            var cmdList = await _commands.GetExecutableCommandsAsync(Context, _services);
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();
            embed.WithColor(new Color(255, 140, 0));
            if (args == null)
            {
                embed.Title = "Available commands are:";

                foreach (var cmd in cmdList)
                {
                    sb.AppendLine($"{cmd.Summary}");
                }
            } else
            {
                foreach (var cmd in cmdList)
                {
                    if (cmd.Name==args.ToLower())
                    {
                        embed.Title = cmd.Summary;
                        sb.AppendLine($"{cmd.Remarks}");
                    }
                }
            }
            embed.Description = Format.Code(sb.ToString());
            var msg = await ReplyAsync(null, false, embed.Build());
        }

        [Command("new"), Summary("new [description of event]"),Remarks("Create a new event")]
        [Alias("create")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        public async Task Create([Remainder] string args = null)
        {
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();

            embed.WithColor(new Color(255, 140, 0));
            
            long id = 0;
            if (Context.Guild == null)
            {
                //private DM
                embed.Title = "Ruh roh!";
                sb.AppendLine("Sorry, can't add an event from a DM.  Who would be able to notify you?");
            }
            else
            {
                if (args == null)
                {
                    embed.Title = "Ruh roh!";
                    sb.AppendLine("Sorry, can't add an empty event!");
                }
                else
                {
                    embed.Title = "New Event Added to Terminal Count:";

                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        cmd.CommandText = $"INSERT INTO `events` (`desc`,serverId) VALUES (@desc,@serverId)";
                        cmd.Parameters.AddWithValue("@desc", args);
                        cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                        cmd.ExecuteNonQuery();
                        id = cmd.LastInsertedId;
                        cmd.Parameters.Clear();
                    }

                    sb.AppendLine($"**{args}**");
                    sb.AppendLine($"ID # {id}");
                    sb.AppendLine();
                    sb.AppendLine($"You can subscribe to this event by clicking the \u2705 or using the command !tc sub {id}");
                }
            }

            embed.Description = sb.ToString();
            var msg = await ReplyAsync(null, false, embed.Build());
            if (id > 0)
            {
                var emoji = new Emoji("\u2705");
                await msg.AddReactionAsync(emoji);

                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    cmd.CommandText = $"UPDATE `events` SET discordId = @discordId WHERE id=@id";
                    cmd.Parameters.AddWithValue("@discordId", msg.Id);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                    id = cmd.LastInsertedId;
                    cmd.Parameters.Clear();
                }
            }
        }

        [Command("list"), Summary("list"),Remarks("Lists all active events")]
        [Alias("ls")]
        public async Task List([Remainder] string args = null)
        {
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();

            embed.WithColor(new Color(255, 140, 0));
            embed.Title = "Unretired Events:";

            using MySqlConnection cn = new MySqlConnection(_connStr);
            using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
            {
                cmd.CommandText = $"SELECT id,`desc`,serverId FROM `events` WHERE";
                if (Context.Guild!=null)
                {
                    //not a private DM
                    cmd.CommandText += " serverId = @serverId AND";
                    cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                }
                cmd.CommandText+= " retireDate IS NULL ORDER BY eventDateTime, id";
                using (MySqlDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        var serverId = dr.GetString("serverId");
                        var server = _client.GetGuild(Convert.ToUInt64(serverId));
                        if (Context.Guild == null && server!=null)
                        {
                            if (server.GetUser(Context.User.Id)!=null)
                            {
                                sb.AppendLine($"ID {dr.GetInt64("id")}, **{dr.GetString("desc")}** on {server.Name}");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"ID {dr.GetInt64("id")}, **{dr.GetString("desc")}**");
                        }
                    }
                }
                cmd.Parameters.Clear();
            }

            embed.Description = sb.ToString();
            await ReplyAsync(null, false, embed.Build());
        }

        [Command("event"), Summary("event [#]"), Remarks("Displays event and lists all subscribers")]
        [Alias("details")]
        public async Task Event([Remainder] int eventId)
        {
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();

            embed.WithColor(new Color(255, 140, 0));

            using MySqlConnection cn = new MySqlConnection(_connStr);
            using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
            {
                bool found = false;
                if (Context.Guild != null)
                {
                    cmd.CommandText = $"SELECT `desc`,retireDate FROM `events` WHERE serverId = @serverId AND id = @eventId";
                    cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                }
                else
                {
                    cmd.CommandText = $"SELECT e.`desc` as `desc`,e.retireDate as retireDate, e.serverId as serverId FROM `events` e JOIN subscriptions s ON e.id=s.eventId AND s.userId = @userId WHERE e.id = @eventId";
                    cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                }
                cmd.Parameters.AddWithValue("@eventId", eventId);
                using (MySqlDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        found = true;
                        embed.Title = $"Event #{eventId}, {dr.GetString("desc")}";
                        if (Context.Guild==null)
                        {
                            var serverId = Convert.ToUInt64(dr.GetString("serverId"));
                            var server = _client.GetGuild(serverId);
                            embed.Title += $" on {server.Name}";
                        }
                        if (dr["retireDate"]!=DBNull.Value)
                        {
                            embed.Title += $" (retired on {dr.GetDateTime("retireDate")})";
                        }
                    }
                }
                cmd.Parameters.Clear();

                if (found)
                {
                    bool found2 = false;
                    sb.AppendLine("Subscribed users:");
                    sb.AppendLine();
                    var userList = new List<string>();
                    cmd.CommandText = $"SELECT userId FROM subscriptions WHERE eventId = @eventId";
                    cmd.Parameters.AddWithValue("@eventId", eventId);
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            found2 = true;
                            var userId = dr.GetUInt64("userId");
                            var user = _client.GetUser(userId);
                            userList.Add(user.Username);
                        }
                    }
                    cmd.Parameters.Clear();
                    if (found2)
                    {
                        userList.Sort();
                        foreach (var user in userList)
                        {
                            sb.AppendLine(user);
                        }
                    } else
                    {
                        sb.AppendLine("*no subscribers yet*");
                    }
                } else
                {
                    if (Context.Guild != null)
                    {
                        embed.Title = "Ruh Roh";
                        sb.AppendLine($"Event #{eventId} cannot be found...");
                    } else
                    {
                        embed.Title = "Sorry!";
                        sb.AppendLine($"I can only show event details in a private DM channel for events you are already subscribed to.");
                    }
                }
            }

            embed.Description = sb.ToString();
            await ReplyAsync(null, false, embed.Build());
        }

        [Command("mysubs"), Summary("mysubs"),Remarks("List all events the caller is subscribed to")]
        [Alias("mine")]
        public async Task MySubscriptions()
        {
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();

            embed.WithColor(new Color(255, 140, 0));
            embed.Title = $"Subscriptions for user {Context.User.Username}:";

            using MySqlConnection cn = new MySqlConnection(_connStr);
            using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
            {
                var eventDict = new Dictionary<int,string>();

                cmd.CommandText = $"SELECT s.eventId as eventId,e.serverId as serverId FROM subscriptions s,`events` e WHERE e.Id = s.eventId";
                if (Context.Guild!=null)
                {
                    cmd.CommandText += " AND e.serverId = @serverId";
                    cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                }
                cmd.CommandText += " AND s.userId = @userId";
                cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                using (MySqlDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        eventDict.Add(dr.GetInt32("eventId"), dr.GetString("serverId"));
                    }
                }
                cmd.Parameters.Clear();

                foreach (var ev in eventDict)
                {
                    cmd.CommandText = $"SELECT `desc` FROM `events` WHERE id = @id AND retireDate IS NULL;";
                    cmd.Parameters.AddWithValue("@id", ev.Key);
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            if (Context.Guild != null)
                            {
                                sb.AppendLine($"ID # {ev.Key}, {dr.GetString("desc")}");
                            } else
                            {
                                var server = _client.GetGuild(Convert.ToUInt64(ev.Value));
                                sb.AppendLine($"ID # {ev.Key}, {dr.GetString("desc")} on {server.Name}");
                            }
                        }
                    }
                    cmd.Parameters.Clear();
                }
            }

            embed.Description = sb.ToString();
            await ReplyAsync(null, false, embed.Build());
        }

        [Command("subscribe"), Summary("sub [#]"),Remarks("Subscribe caller to event #")]
        [Alias("sub")]
        public async Task Subscribe([Remainder] int args = 0)
        {
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();

            embed.WithColor(new Color(255, 140, 0));
            embed.Title = $"Subscription to event:";

            int id = args;

            if (id == 0)
            {
                sb.AppendLine($"Sorry, event id {id} does not exist...");
            }
            else
            {
                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    string desc = "";
                    string serverName = "";
                    DateTime retireDate = DateTime.MaxValue;
                    bool found = false;
                    cmd.CommandText = $"SELECT `desc`,retireDate,serverId FROM `events` WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            var serverId = Convert.ToUInt64(dr.GetString("serverId"));
                            if (Context.Guild != null)
                            {
                                if (Context.Guild.Id == serverId)
                                {
                                    cmd.CommandText += " and serverId=@serverId;";
                                    cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                                    found = true;
                                }
                            } else
                            {
                                var server = _client.GetGuild(serverId);
                                if (server.GetUser(Context.User.Id) != null)
                                {
                                    serverName = server.Name;
                                    found = true;
                                }
                            }
                            if (found)
                            {
                                desc = dr.GetString("desc");
                                if (dr["retireDate"] != DBNull.Value)
                                {
                                    retireDate = dr.GetDateTime("retireDate");
                                }
                            }
                        }
                    }
                    cmd.Parameters.Clear();

                    if (!found)
                    {
                        sb.AppendLine($"Sorry, event id {id} does not exist...");
                    }
                    else if (retireDate < DateTime.MaxValue)
                    {
                        sb.AppendLine($"Sorry, event {id}, **{desc}** has already been retired.");
                    }
                    else
                    {
                        cmd.CommandText = $"INSERT INTO subscriptions (eventId,userId) VALUES (@eventId,@userId) ON DUPLICATE KEY UPDATE userId=userId;";
                        cmd.Parameters.AddWithValue("@eventId", id);
                        cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                        cmd.ExecuteNonQuery();
                        cmd.Parameters.Clear();

                        if (serverName == "")
                        {
                            sb.AppendLine($"Subscribed to event {id}, **{desc}**!");
                        } else
                        {
                            sb.AppendLine($"Subscribed to event {id}, **{desc}** on {serverName}!");
                        }
                    }
                }

            }

            embed.Description = sb.ToString();
            await ReplyAsync(null, false, embed.Build());
        }

        [Command("unsubscribe"), Summary("unsub [#]"),Remarks("Unsubscribe caller to event #")]
        [Alias("unsub")]
        public async Task Unsubscribe([Remainder] int args = 0)
        {
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();

            embed.WithColor(new Color(255, 140, 0));
            embed.Title = $"Unsubscribe from event:";

            int id = args;

            if (id == 0)
            {
                sb.AppendLine($"Sorry, event id {id} does not exist...");
            }
            else
            {
                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    string desc = "";
                    UInt64 serverId = 0;
                    DateTime retireDate = DateTime.MaxValue;
                    bool found = false;
                    if (Context.Guild != null)
                    {
                        cmd.CommandText = $"SELECT `desc`,retireDate FROM `events` WHERE id = @id AND serverId=@serverId;";
                        cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                    } else
                    {
                        cmd.CommandText = $"SELECT e.`desc`,e.retireDate,e.serverId FROM `events` e JOIN subscriptions s ON e.id = s.eventId WHERE e.id = @id AND s.userId = @userId;";
                        cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                    }
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            found = true;
                            desc = dr.GetString("desc");
                            if (dr["retireDate"] != DBNull.Value)
                            {
                                retireDate = dr.GetDateTime("retireDate");
                            }
                            if (Context.Guild==null)
                            {
                                serverId = Convert.ToUInt64(dr.GetString("serverId"));
                            }
                        }
                    }
                    cmd.Parameters.Clear();

                    if (!found)
                    {
                        sb.AppendLine($"Sorry, event id {id} does not exist...");
                    }
                    else if (retireDate < DateTime.MaxValue)
                    {
                        sb.AppendLine($"Sorry, event {id}, **{desc}** has already been retired.");
                    }
                    else
                    {
                        cmd.CommandText = $"DELETE FROM subscriptions WHERE eventId=@eventId AND userId=@userId";
                        cmd.Parameters.AddWithValue("@eventId", id);
                        cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                        cmd.ExecuteNonQuery();
                        cmd.Parameters.Clear();

                        if (serverId == 0)
                        {
                            sb.AppendLine($"Unsubscribed from event {id}, **{desc}**!");
                        } else
                        {
                            var server = _client.GetGuild(serverId);
                            sb.AppendLine($"Unsubscribed from event {id}, **{desc}** on {server.Name}!");
                        }
                    }
                }

            }

            embed.Description = sb.ToString();
            await ReplyAsync(null, false, embed.Build());
        }

        [Command("unsubscribe"), Summary("unsub all"),Remarks("Unsubscribe caller to all events they are currently subscribed to")]
        [Alias("unsub")]
        public async Task Unsubscribe([Remainder] string args = null)
        {
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();

            embed.WithColor(new Color(255, 140, 0));
            embed.Title = $"Unsubscribe from event:";

            if (args.ToLower() != "all")
            {
                if (args == null)
                {
                    sb.AppendLine($"Sorry, I don't know what you are trying to unsubscribe from without you telling me...");
                }
                else
                {
                    sb.AppendLine($"Sorry, {args} is not an event ID.  To unsubscribe please provide an event ID #, or 'all' to be unsubscribed from all active events.");
                }
            }
            else
            {
                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    var eventList = new List<int>();

                    cmd.CommandText = $"SELECT eventId FROM subscriptions WHERE userId = @userId";
                    if (Context.Guild!=null)
                    {
                        cmd.CommandText+=" AND serverId=@serverId";
                        cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                    }
                    cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            eventList.Add(dr.GetInt32("eventId"));
                        }
                    }
                    cmd.Parameters.Clear();

                    sb.AppendLine($"You have unsubscribed from the following events:");
                    var serverDict = new Dictionary<UInt64, string>();
                    foreach (int eventId in eventList)
                    {
                        cmd.CommandText = $"SELECT `desc`,serverId FROM `events` WHERE id = @id AND retireDate IS NULL;";
                        cmd.Parameters.AddWithValue("@id", eventId);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                if (Context.Guild != null)
                                {
                                    sb.AppendLine($"**{dr.GetString("desc")}**");
                                } else
                                {
                                    var serverId = Convert.ToUInt64(dr.GetString("serverId"));
                                    if (!serverDict.ContainsKey(serverId))
                                    {
                                        var server = _client.GetGuild(serverId);
                                        serverDict.Add(serverId, server.Name);
                                    }
                                    sb.AppendLine($"**{dr.GetString("desc")}** on {serverDict[serverId]}");
                                }
                            }
                        }
                        cmd.Parameters.Clear();

                        cmd.CommandText = $"DELETE FROM subscriptions WHERE eventId=@eventId AND userId=@userId";
                        cmd.Parameters.AddWithValue("@eventId", eventId);
                        cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                        cmd.ExecuteNonQuery();
                        cmd.Parameters.Clear();

                    }
                }

            }

            embed.Description = sb.ToString();
            await ReplyAsync(null, false, embed.Build());
        }

        [Command("retire"), Summary("retire [#]"),Remarks("Retires event #, removing it from events that can be notified on")]
        public async Task Retire([Remainder] int args = 0)
        {
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();

            embed.WithColor(new Color(255, 140, 0));
            embed.Title = $"Retiring event:";

            int id = args;

            if (Context.Guild != null)
            {
                if (id == 0)
                {
                    sb.AppendLine($"Sorry, event id {id} does not exist...");
                }
                else
                {
                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        string desc = "";
                        DateTime retireDate = DateTime.MaxValue;
                        bool found = false;
                        cmd.CommandText = $"SELECT `desc`,retireDate FROM `events` WHERE id = @id and serverId=@serverId;";
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                found = true;
                                desc = dr.GetString("desc");
                                if (dr["retireDate"] != DBNull.Value)
                                {
                                    retireDate = dr.GetDateTime("retireDate");
                                }
                            }
                        }
                        cmd.Parameters.Clear();

                        if (!found)
                        {
                            sb.AppendLine($"Sorry, event id {id} does not exist...");
                        }
                        else if (retireDate < DateTime.MaxValue)
                        {
                            sb.AppendLine($"Sorry, event {id}, **{desc}** has already been retired.");
                        }
                        else
                        {
                            cmd.CommandText = $"UPDATE `events` SET retireDate = @retireDate WHERE id = @eventId";
                            cmd.Parameters.AddWithValue("@eventId", id);
                            cmd.Parameters.AddWithValue("@retireDate", DateTime.UtcNow);
                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();

                            cmd.CommandText = $"DELETE FROM subscriptions WHERE eventId = @eventId";
                            cmd.Parameters.AddWithValue("@eventId", id);
                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();

                            sb.AppendLine($"Event {id}, **{desc}** retired!");
                        }
                    }

                }
            } else
            {
                sb.AppendLine("Sorry, events can only be retired from the server they were created on.");
            }

            embed.Description = sb.ToString();
            await ReplyAsync(null, false, embed.Build());
        }

        [Command("notify"), Summary("notify [#] [optional message]"), Remarks("Notifies all subscribers of event #.  Optional message is appended on end of standard notification.")]
        public async Task Notify(int eventId = 0, [Remainder] string msg=null)
        {
            var sb = new StringBuilder();
            var embed = new EmbedBuilder();

            embed.WithColor(new Color(255, 140, 0));
            embed.Title = $"Event notification:";

            int id = eventId;

            if (id == 0)
            {
                sb.AppendLine($"Sorry, event id {id} does not exist...");
            }
            else
            {
                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    string desc = "";
                    string serverName = "";
                    UInt64 serverId = 0;
                    DateTime retireDate = DateTime.MaxValue;
                    bool found = false;
                    cmd.CommandText = $"SELECT `desc`,retireDate,serverId FROM `events` WHERE id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            found = true;
                            desc = dr.GetString("desc");
                            serverId = Convert.ToUInt64(dr.GetString("serverId"));
                            if (dr["retireDate"] != DBNull.Value)
                            {
                                retireDate = dr.GetDateTime("retireDate");
                            }
                            else
                            {
                                retireDate = DateTime.MaxValue;
                            }
                        }
                    }
                    cmd.Parameters.Clear();

                    bool memberOfServer = false;
                    if (Context.Guild!=null)
                    {
                        memberOfServer = true;
                    } else
                    {
                        var server = _client.GetGuild(serverId);
                        if (server.GetUser(Context.User.Id) != null)
                        {
                            memberOfServer = true;
                            serverName = server.Name;
                        }
                    }

                    if (!found)
                    {
                        sb.AppendLine($"Sorry, event id {id} does not exist...");
                    }
                    else if (!memberOfServer)
                    {
                        sb.AppendLine($"Sorry, you are not a member of the Discord server this event originated on and may not notify on it.");
                    }
                    else if (retireDate < DateTime.MaxValue)
                    {
                        sb.AppendLine($"Sorry, event {id}, **{desc}** has already been retired.");
                    }
                    else
                    {
                        cmd.CommandText = $"SELECT userId FROM subscriptions WHERE eventId = @eventId;";
                        cmd.Parameters.AddWithValue("@eventId", id);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                ulong userId = dr.GetUInt64("userId");
                                var u = _client.GetUser((ulong)userId);
                                StringBuilder message = new StringBuilder();
                                message.AppendLine($"{Context.User.Username} is alerting that event **{desc}** is about to occur!");
                                if (msg!=null)
                                {
                                    message.AppendLine(msg);
                                }
                                await UserExtensions.SendMessageAsync(u, message.ToString());
                            }
                        }
                        cmd.Parameters.Clear();

                        if (serverName == "")
                        {
                            sb.AppendLine($"Notified subscribers for event {id}, **{desc}**!");
                        } else
                        {
                            sb.AppendLine($"Notified subscribers for event {id}, **{desc}** on {serverName}!");
                        }
                    }
                }

            }

            embed.Description = sb.ToString();
            await ReplyAsync(null, false, embed.Build());
        }
    }
}