
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
using Microsoft.Extensions.Logging;
using Serilog;
using Newtonsoft.Json;
using Serilog.Exceptions.Core;
using MySqlX.XDevAPI.Relational;
using System.Globalization;
using TerminalCount.Services;
using Discord.Rest;

namespace TerminalCount.Modules
{
    // for commands to be available, and have the Context passed to them, we must inherit ModuleBase
    public class PublicModule : ModuleBase
    {

        // setup fields to be set later in the constructor
        private readonly IConfiguration _config;
        private readonly CommandService _commands;
        private readonly DiscordSocketClient _client;
        private readonly DiscordRestClient _restClient;
        private readonly IServiceProvider _services;
        private readonly string _connStr;
        //private readonly ILogger _logger;

        public PublicModule(IServiceProvider services)
        {
            _config = services.GetRequiredService<IConfiguration>();
            _commands = services.GetRequiredService<CommandService>();
            _client = services.GetRequiredService<DiscordSocketClient>();
            _restClient = services.GetRequiredService<DiscordRestClient>();
            //_logger = services.GetRequiredService<ILogger>();
            _services = services;

            //In secrets.json
            _connStr = _config["ConnStr"];
        }

        [Command("help"), Summary("help [cmd]"), Remarks("Receive more info on command")]
        [Alias("helpme")]
        public async Task Help(string args = null)
        {
            try
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
                }
                else
                {
                    foreach (var cmd in cmdList)
                    {
                        if (cmd.Name == args.ToLower())
                        {
                            embed.Title = cmd.Summary;
                            sb.AppendLine($"{cmd.Remarks}");
                        }
                    }
                }
                embed.Description = Format.Code(sb.ToString());

                bool ok = false;
                var count = 0;
                Exception savedEx = new Exception();
                while (!ok && count < 3)
                {
                    try
                    {
                        var msg = await ReplyAsync(null, false, embed.Build());
                        ok = true;
                    }
                    catch (Exception ex)
                    {
                        savedEx = ex;
                        count++;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                if (count == 3)
                {
                    throw savedEx;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Command("new"), Summary("new [description of event]"), Remarks("Create a new event")]
        [Alias("create")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        public async Task Create([Remainder] string args = null)
        {
            try
            {
                var sb = new StringBuilder();
                var embed = new EmbedBuilder();

                embed.WithColor(new Color(255, 140, 0));
                long id = 0;
                string byteString = Utils.ConvertStringToHex(args);
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
                            cmd.Parameters.AddWithValue("@desc", byteString);
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

                bool ok = false;
                var count = 0;
                Exception savedEx = new Exception();
                while (!ok && count < 3)
                {
                    try
                    {
                        var msg = await ReplyAsync(null, false, embed.Build());
                        ok = true;
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
                    catch (Exception ex)
                    {
                        savedEx = ex;
                        count++;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                if (count == 3)
                {
                    throw savedEx;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Command("list"), Summary("list"), Remarks("Lists all active events")]
        [Alias("ls")]
        public async Task List([Remainder] string args = null)
        {
            try
            {
                var sb = new StringBuilder();
                var embed = new EmbedBuilder();

                embed.WithColor(new Color(255, 140, 0));
                embed.Title = "Unretired Events:";

                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    cmd.CommandText = $"SELECT id,`desc`,serverId,url FROM `events` WHERE";
                    if (Context.Guild != null)
                    {
                        //not a private DM
                        cmd.CommandText += " serverId = @serverId AND";
                        cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                    }
                    cmd.CommandText += " (retireDate IS NULL or retireDate > @retireDate) ORDER BY eventDateTime, id";
                    cmd.Parameters.AddWithValue("@retireDate", DateTime.UtcNow);
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            var serverId = dr.GetString("serverId");

                            bool ok = false;
                            var cnt = 0;
                            Exception svEx = new Exception();
                            while (!ok && cnt < 3)
                            {
                                try
                                {
                                    var server = _client.GetGuild(Convert.ToUInt64(serverId));
                                    ok = true;
                                    if (Context.Guild == null && server != null)
                                    {
                                        if (server.GetUser(Context.User.Id) != null)
                                        {
                                            sb.AppendLine($"ID {dr.GetInt64("id")}, **{Utils.ConvertHexToString(dr.GetString("desc"))}** on {server.Name}");
                                        }
                                    }
                                    else
                                    {
                                        sb.AppendLine($"ID {dr.GetInt64("id")}, **{Utils.ConvertHexToString(dr.GetString("desc"))}**");
                                    }
                                    if (dr.GetString("url") != "")
                                    {
                                        sb.AppendLine(dr.GetString("url"));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    svEx = ex;
                                    cnt++;
                                    System.Threading.Thread.Sleep(1000);
                                }
                            }
                            if (cnt == 3)
                            {
                                throw svEx;
                            }
                        }
                    }
                    cmd.Parameters.Clear();
                }

                embed.Description = sb.ToString();
                bool Ok = false;
                var count = 0;
                Exception savedEx = new Exception();
                while (!Ok && count < 3)
                {
                    try
                    {
                        await ReplyAsync(null, false, embed.Build());
                        Ok = true;
                    }
                    catch (Exception ex)
                    {
                        savedEx = ex;
                        count++;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                if (count == 3)
                {
                    throw savedEx;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Command("event"), Summary("event [#]"), Remarks("Displays event and lists all subscribers")]
        [Alias("details")]
        public async Task Event([Remainder] string args)
        {
            try
            {
                var sb = new StringBuilder();
                var embed = new EmbedBuilder();
                ulong serverId = 0;

                int eventId;
                bool isNumeric = int.TryParse(args, out eventId);

                embed.WithColor(new Color(255, 140, 0));

                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    bool found = false;
                    string eventIdStr;
                    if (isNumeric)
                    {
                        eventIdStr = "id = @eventId";
                    }
                    else
                    {
                        eventIdStr = "launchSlug = @launchSlug";
                    }
                    if (Context.Guild != null)
                    {
                        serverId = Context.Guild.Id;
                        cmd.CommandText = $"SELECT `desc`,retireDate,url,id as eventId FROM `events` WHERE serverId = @serverId AND {eventIdStr}";
                        cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                    }
                    else
                    {
                        cmd.CommandText = $"SELECT e.`desc` as `desc`,e.retireDate as retireDate, e.serverId as serverId,e.url as url,e.id as eventId FROM `events` e JOIN subscriptions s ON e.id=s.eventId AND s.userId = @userId WHERE e.{eventIdStr}";
                        cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                    }
                    cmd.Parameters.AddWithValue("@eventId", eventId);
                    cmd.Parameters.AddWithValue("@launchSlug", args);
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            found = true;
                            eventId = dr.GetInt32("eventId");
                            embed.Title = $"Event #{eventId}, {Utils.ConvertHexToString(dr.GetString("desc"))}";
                            if (Context.Guild == null)
                            {
                                serverId = Convert.ToUInt64(dr.GetString("serverId"));
                                var server = _client.GetGuild(serverId);
                                embed.Title += $" on {server.Name}";
                            }
                            if (dr["retireDate"] != DBNull.Value && dr.GetDateTime("retireDate") < DateTime.UtcNow)
                            {
                                TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

                                embed.Title += $" (retired on {TimeZoneInfo.ConvertTimeFromUtc(dr.GetDateTime("retireDate"), estZone)} TOTTZ)";
                            }
                            if (dr.GetString("url") != "")
                            {
                                sb.AppendLine($"URL: {dr.GetString("url")}");
                                sb.AppendLine();
                            }
                        }
                    }
                    cmd.Parameters.Clear();

                    if (found)
                    {
                        bool parentsFound = false;
                        sb.AppendLine("__Parent Events__");
                        cmd.CommandText = $"SELECT e.`desc` as description,e.serverId as serverId,e.id as id FROM `events` e, eventparents ep WHERE ep.parentId = e.id AND ep.eventId = @eventId AND (e.retireDate IS NULL OR e.retireDate > @retireDate)";
                        cmd.Parameters.AddWithValue("@eventId", eventId);
                        cmd.Parameters.AddWithValue("@retireDate", DateTime.UtcNow);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                parentsFound = true;
                                var server = _client.GetGuild(Convert.ToUInt64(dr.GetString("serverId")));
                                if (Context.Guild == null && server != null)
                                {
                                    if (server.GetUser(Context.User.Id) != null)
                                    {
                                        sb.AppendLine($"ID {dr.GetInt64("id")}, **{Utils.ConvertHexToString(dr.GetString("description"))}** on {server.Name}");
                                    }
                                }
                                else
                                {
                                    sb.AppendLine($"ID {dr.GetInt64("id")}, **{Utils.ConvertHexToString(dr.GetString("description"))}**");
                                }
                            }
                        }
                        cmd.Parameters.Clear();
                        if (!parentsFound)
                        {
                            sb.AppendLine("*No parent events defined*");
                        }
                        sb.AppendLine();

                        bool childFound = false;
                        sb.AppendLine("__Child Events__");
                        cmd.CommandText = $"SELECT e.`desc` as description,e.serverId as serverId,e.id as id FROM `events` e, eventparents ep WHERE ep.eventId = e.id AND ep.parentId = @eventId AND (e.retireDate IS NULL OR e.retireDate > @retireDate)";
                        cmd.Parameters.AddWithValue("@eventId", eventId);
                        cmd.Parameters.AddWithValue("@retireDate", DateTime.UtcNow);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                childFound = true;
                                var server = _client.GetGuild(Convert.ToUInt64(dr.GetString("serverId")));
                                if (Context.Guild == null && server != null)
                                {
                                    if (server.GetUser(Context.User.Id) != null)
                                    {
                                        sb.AppendLine($"ID {dr.GetInt64("id")}, **{Utils.ConvertHexToString(dr.GetString("description"))}** on {server.Name}");
                                    }
                                }
                                else
                                {
                                    sb.AppendLine($"ID {dr.GetInt64("id")}, **{Utils.ConvertHexToString(dr.GetString("description"))}**");
                                }
                            }
                        }
                        cmd.Parameters.Clear();
                        if (!childFound)
                        {
                            sb.AppendLine("*No child events defined*");
                        }
                        sb.AppendLine();

                        bool found2 = false;
                        sb.AppendLine("__Subscribed users:__");
                        var userList = new List<string>();
                        cmd.CommandText = $"SELECT userId FROM subscriptions WHERE eventId = @eventId";
                        cmd.Parameters.AddWithValue("@eventId", eventId);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                found2 = true;
                                var userId = dr.GetUInt64("userId");
                                try
                                {
                                    var user = _client.GetUser(userId);
                                    userList.Add(user.Username);
                                } catch (Exception ex)
                                {
                                    try
                                    {
                                        var user = _restClient.GetUserAsync(userId).Result;
                                        userList.Add(user.Username);
                                    }
                                    catch (Exception ex2)
                                    {
                                        userList.Add("Unavailable User");
                                        Log.Logger.Error(ex2,"Cannot list user " + userId);
                                    }
                                }
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
                        }
                        else
                        {
                            sb.AppendLine("*no subscribers yet*");
                        }

                        sb.AppendLine();
                        bool found3 = false;
                        sb.AppendLine("__Previous notifications:__");
                        cmd.CommandText = $"SELECT userId,notifyDateTime,message,channelId,messageId FROM notifications WHERE eventId = @eventId ORDER BY notifyDateTime";
                        cmd.Parameters.AddWithValue("@eventId", eventId);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                found3 = true;
                                var userId = dr.GetUInt64("userId");
                                string userName;
                                try
                                {
                                    var user = _client.GetUser(userId);
                                    userName = user.Username;
                                } catch (Exception ex)
                                {
                                    try
                                    {
                                        var user = _restClient.GetUserAsync(userId).Result;
                                        userName=user.Username;
                                    }
                                    catch (Exception ex2)
                                    {
                                        Log.Logger.Error("Cannot show notification for user " + userId);
                                        userName = "Unavailable User";
                                    }
                                }
                                var notifyDateTime = dr.GetDateTime("notifyDateTime");
                                var message = Utils.ConvertHexToString(dr.GetString("message"));
                                string channelId = dr.GetString("channelId");
                                string messageId = dr.GetString("messageId");
                                TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

                                sb.AppendLine($"{TimeZoneInfo.ConvertTimeFromUtc(notifyDateTime, estZone)} TOTTZ, {userName} alerted {message}");
                                sb.AppendLine($"https://discordapp.com/channels/{serverId}/{channelId}/{messageId}");
                            }
                        }
                        cmd.Parameters.Clear();
                        if (!found3)
                        {
                            sb.AppendLine("*No notifications yet*");
                        }
                    }
                    else
                    {
                        if (Context.Guild != null)
                        {
                            embed.Title = "Ruh Roh";
                            sb.AppendLine($"Event #{eventId} cannot be found...");
                        }
                        else
                        {
                            embed.Title = "Sorry!";
                            sb.AppendLine($"I can only show event details in a private DM channel for events you are already subscribed to.");
                        }
                    }
                }

                embed.Description = sb.ToString();

                bool ok = false;
                var count = 0;
                Exception savedEx = new Exception();
                while (!ok && count < 3)
                {
                    try
                    {
                        await ReplyAsync(null, false, embed.Build());
                        ok = true;
                    }
                    catch (Exception ex)
                    {
                        savedEx = ex;
                        count++;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                if (count == 3)
                {
                    throw savedEx;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Command("mysubs"), Summary("mysubs"), Remarks("List all events the caller is subscribed to")]
        [Alias("mine")]
        public async Task MySubscriptions()
        {
            try
            {
                var sb = new StringBuilder();
                var embed = new EmbedBuilder();

                embed.WithColor(new Color(255, 140, 0));
                embed.Title = $"Subscriptions for user {Context.User.Username}:";

                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    var eventDict = new Dictionary<int, string>();

                    cmd.CommandText = $"SELECT s.eventId as eventId,e.serverId as serverId FROM subscriptions s,`events` e WHERE e.Id = s.eventId";
                    if (Context.Guild != null)
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
                        cmd.CommandText = $"SELECT `desc` FROM `events` WHERE id = @id AND (retireDate IS NULL or retireDate > @retireDate);";
                        cmd.Parameters.AddWithValue("@id", ev.Key);
                        cmd.Parameters.AddWithValue("@retireDate", DateTime.UtcNow);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                if (Context.Guild != null)
                                {
                                    sb.AppendLine($"ID # {ev.Key}, {Utils.ConvertHexToString(dr.GetString("desc"))}");
                                }
                                else
                                {
                                    var server = _client.GetGuild(Convert.ToUInt64(ev.Value));
                                    sb.AppendLine($"ID # {ev.Key}, {Utils.ConvertHexToString(dr.GetString("desc"))} on {server.Name}");
                                }
                            }
                        }
                        cmd.Parameters.Clear();
                    }
                }

                embed.Description = sb.ToString();

                bool ok = false;
                var count = 0;
                Exception savedEx = new Exception();
                while (!ok && count < 3)
                {
                    try
                    {
                        await ReplyAsync(null, false, embed.Build());
                        ok = true;
                    }
                    catch (Exception ex)
                    {
                        savedEx = ex;
                        count++;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                if (count == 3)
                {
                    throw savedEx;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Command("subscribe"), Summary("sub [#/\"all\"]"), Remarks("Subscribe caller to event # / all events")]
        [Alias("sub")]
        public async Task Subscribe([Remainder] string args = "")
        {
            try
            {
                var sb = new StringBuilder();
                var embed = new EmbedBuilder();

                embed.WithColor(new Color(255, 140, 0));
                embed.Title = $"Subscription to event:";

                int id;
                bool isNumeric = int.TryParse(args, out id);

                if (args.ToLower() == "all")
                {
                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        cmd.CommandText = $"INSERT INTO subscriptions (eventId,userId) VALUES (@eventId,@userId) ON DUPLICATE KEY UPDATE userId=userId;";
                        cmd.Parameters.AddWithValue("@eventId", 0);
                        cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                        cmd.ExecuteNonQuery();
                        cmd.Parameters.Clear();

                        sb.AppendLine($"Subscribed to all events");
                    }
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
                        string eventIdStr;
                        if (isNumeric)
                        {
                            eventIdStr = "id = @id";
                        }
                        else
                        {
                            eventIdStr = "launchSlug = @launchSlug";
                        }

                        cmd.CommandText = $"SELECT `desc`,retireDate,serverId,id FROM `events` WHERE {eventIdStr};";
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@launchSlug", args.ToLower()); ;
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                var serverId = Convert.ToUInt64(dr.GetString("serverId"));
                                id = dr.GetInt32("id");
                                if (Context.Guild != null)
                                {
                                    if (Context.Guild.Id == serverId)
                                    {
                                        cmd.CommandText += " and serverId=@serverId;";
                                        cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                                        found = true;
                                    }
                                }
                                else
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
                                    desc = Utils.ConvertHexToString(dr.GetString("desc"));
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
                        else if (retireDate < DateTime.UtcNow)
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
                            }
                            else
                            {
                                sb.AppendLine($"Subscribed to event {id}, **{desc}** on {serverName}!");
                            }
                        }
                    }
                }

                embed.Description = sb.ToString();

                bool ok = false;
                var count = 0;
                Exception savedEx = new Exception();
                while (!ok && count < 3)
                {
                    try
                    {
                        await ReplyAsync(null, false, embed.Build());
                        ok = true;
                    }
                    catch (Exception ex)
                    {
                        savedEx = ex;
                        count++;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                if (count == 3)
                {
                    throw savedEx;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Command("unsubscribe"), Summary("unsub [#/\"all\"]"), Remarks("Unsubscribe caller to event # / all events")]
        [Alias("unsub")]
        public async Task Unsubscribe([Remainder] string args)
        {
            try
            {
                var sb = new StringBuilder();
                var embed = new EmbedBuilder();

                embed.WithColor(new Color(255, 140, 0));
                embed.Title = $"Unsubscribe from event:";

                int id;
                bool isNumeric = int.TryParse(args, out id);

                if (args.ToLower() == "all")
                {
                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        var eventList = new List<int>();

                        cmd.CommandText = $"SELECT eventId FROM subscriptions WHERE userId = @userId";
                        if (Context.Guild != null)
                        {
                            cmd.CommandText += " AND serverId=@serverId";
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
                            if (eventId == 0)
                            {
                                sb.Append("All events.");
                            }
                            else
                            {
                                cmd.CommandText = $"SELECT `desc`,serverId FROM `events` WHERE id = @id AND (retireDate IS NULL OR retireDate > @retireDate);";
                                cmd.Parameters.AddWithValue("@id", eventId);
                                cmd.Parameters.AddWithValue("@retireDate", DateTime.UtcNow);
                                using (MySqlDataReader dr = cmd.ExecuteReader())
                                {
                                    while (dr.Read())
                                    {
                                        if (Context.Guild != null)
                                        {
                                            sb.AppendLine($"**{Utils.ConvertHexToString(dr.GetString("desc"))}**");
                                        }
                                        else
                                        {
                                            var serverId = Convert.ToUInt64(dr.GetString("serverId"));
                                            if (!serverDict.ContainsKey(serverId))
                                            {
                                                var server = _client.GetGuild(serverId);
                                                serverDict.Add(serverId, server.Name);
                                            }
                                            sb.AppendLine($"**{Utils.ConvertHexToString(dr.GetString("desc"))}** on {serverDict[serverId]}");
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
                }
                else
                {
                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        string eventIdStr;
                        if (isNumeric)
                        {
                            eventIdStr = "id = @id";
                        }
                        else
                        {
                            eventIdStr = "launchSlug = @launchSlug";
                        }

                        string desc = "";
                        UInt64 serverId = 0;
                        DateTime retireDate = DateTime.MaxValue;
                        bool found = false;
                        if (Context.Guild != null)
                        {
                            cmd.CommandText = $"SELECT `desc`,retireDate FROM `events` WHERE {eventIdStr} AND serverId=@serverId;";
                            cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                        }
                        else
                        {
                            cmd.CommandText = $"SELECT e.`desc`,e.retireDate,e.serverId FROM `events` e JOIN subscriptions s ON e.id = s.eventId WHERE e.{eventIdStr} AND s.userId = @userId;";
                            cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                        }
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@launchSlug", args.ToLower());
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                found = true;
                                desc = Utils.ConvertHexToString(dr.GetString("desc"));
                                if (dr["retireDate"] != DBNull.Value)
                                {
                                    retireDate = dr.GetDateTime("retireDate");
                                }
                                if (Context.Guild == null)
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
                        else if (retireDate < DateTime.UtcNow)
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
                            }
                            else
                            {
                                var server = _client.GetGuild(serverId);
                                sb.AppendLine($"Unsubscribed from event {id}, **{desc}** on {server.Name}!");
                            }
                        }
                    }
                }

                embed.Description = sb.ToString();

                bool ok = false;
                var count = 0;
                Exception savedEx = new Exception();
                while (!ok && count < 3)
                {
                    try
                    {
                        await ReplyAsync(null, false, embed.Build());
                        ok = true;
                    }
                    catch (Exception ex)
                    {
                        savedEx = ex;
                        count++;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                if (count == 3)
                {
                    throw savedEx;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }


        [Command("retire"), Summary("retire [#]"), Remarks("Retires event #, removing it from events that can be notified on")]
        public async Task Retire([Remainder] int args = 0)
        {
            try
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
                                    desc = Utils.ConvertHexToString(dr.GetString("desc"));
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
                            else if (retireDate < DateTime.UtcNow)
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
                }
                else
                {
                    sb.AppendLine("Sorry, events can only be retired from the server they were created on.");
                }

                embed.Description = sb.ToString();

                bool ok = false;
                var count = 0;
                Exception savedEx = new Exception();
                while (!ok && count < 3)
                {
                    try
                    {
                        await ReplyAsync(null, false, embed.Build());
                        ok = true;
                    }
                    catch (Exception ex)
                    {
                        savedEx = ex;
                        count++;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                if (count == 3)
                {
                    throw savedEx;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Command("notify"), Summary("notify [#] [optional message]"), Remarks("Notifies all subscribers of event #.  Optional message is appended on end of standard notification.")]
        public async Task Notify(string eventId, [Remainder] string msg = "")
        {
            try
            {
                var sb = new StringBuilder();
                var embed = new EmbedBuilder();

                embed.WithColor(new Color(255, 140, 0));
                embed.Title = $"Event notification:";

                int id;
                bool isNumeric = int.TryParse(eventId, out id);

                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    string eventIdStr;
                    if (isNumeric)
                    {
                        eventIdStr = "id = @id";
                    }
                    else
                    {
                        eventIdStr = "launchSlug = @launchSlug";
                    }

                    string desc = "";
                    string serverName = "";
                    string url = "";
                    UInt64 serverId = 0;
                    DateTime retireDate = DateTime.MaxValue;
                    bool found = false;
                    cmd.CommandText = $"SELECT `desc`,retireDate,serverId,url,id FROM `events` WHERE {eventIdStr}";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@launchSlug", eventId.ToLower());
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            found = true;
                            id = dr.GetInt32("id");
                            desc = Utils.ConvertHexToString(dr.GetString("desc"));
                            serverId = Convert.ToUInt64(dr.GetString("serverId"));
                            url = dr.GetString("url");
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

                    //bool memberOfServer = false;
                    //if (Context.Guild != null)
                    //{
                    //    memberOfServer = true;
                    //}
                    //else
                    //{
                    //    var server = _client.GetGuild(serverId);
                    //    if (server.GetUser(Context.User.Id) != null)
                    //    {
                    //        memberOfServer = true;
                    //        serverName = server.Name;
                    //    }
                    //}

                    if (!found)
                    {
                        sb.AppendLine($"Sorry, event id {id} does not exist...");
                    }
                    //else if (!memberOfServer)
                    //{
                    //    sb.AppendLine($"Sorry, you are not a member of the Discord server this event originated on and may not notify on it.");
                    //}
                    else if (Context.Guild == null || Context.Guild.Id != serverId)
                    {
                        sb.AppendLine($"Sorry, notifications must occur from the server they belong to.");
                    }
                    else if (retireDate < DateTime.UtcNow)
                    {
                        sb.AppendLine($"Sorry, event {id}, **{desc}** has already been retired.");
                    }
                    else
                    {
                        var parentList = new List<int>();
                        parentList.Add(0);
                        cmd.CommandText = $"SELECT ep.parentId as parentId FROM `eventparents` ep JOIN `events` e ON ep.parentId=e.id WHERE ep.eventId = @id AND (e.retireDate IS NULL OR e.retireDate > @retireDate)";
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@retireDate", DateTime.UtcNow);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                parentList.Add(dr.GetInt32("parentId"));
                            }
                        }
                        cmd.Parameters.Clear();

                        ulong userId = 0;
                        var channelId = Context.Message.Channel.Id;
                        var messageId = Context.Message.Id;
                        var eventString = new StringBuilder();
                        eventString.Append(id);
                        if (parentList.Count > 0)
                        {
                            foreach (var parentId in parentList)
                            {
                                eventString.Append($",{parentId}");
                            }
                        }

                        cmd.CommandText = $"SELECT DISTINCT userId FROM subscriptions WHERE eventId IN ({eventString.ToString()})";//= @eventId;";
                                                                                                                                   //cmd.Parameters.AddWithValue("@eventId", id);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                userId = dr.GetUInt64("userId");
                                IUser u = _client.GetUser(userId);
                                if (u == null)
                                {
                                    u = await _restClient.GetUserAsync(userId);
                                }
                                StringBuilder message = new StringBuilder();
                                message.AppendLine($"{Context.User.Username} is alerting that event **{desc}** is happening!");
                                if (msg != "")
                                {
                                    message.AppendLine(msg);
                                }
                                if (url != "")
                                {
                                    message.AppendLine(url);
                                }
                                message.AppendLine($"https://discordapp.com/channels/{serverId}/{channelId}/{messageId}");

                                bool ok = false;
                                var cnt = 0;
                                Exception svEx = new Exception();
                                while (!ok && cnt < 3)
                                {
                                    try
                                    {
                                        await UserExtensions.SendMessageAsync(u, message.ToString());
                                        ok = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        svEx = ex;
                                        cnt++;
                                        System.Threading.Thread.Sleep(1000);
                                    }
                                }
                                if (cnt == 3)
                                {
                                    //throw svEx;
                                    Log.Logger.Error("Cannot send message to " + u.Username);
                                }
                            }
                        }
                        cmd.Parameters.Clear();

                        cmd.CommandText = "INSERT INTO notifications (eventId,userId,notifyDateTime,message,channelId,messageId) VALUES (@eventId,@userId,@notifyDateTime,@message,@channelId,@messageId);";
                        cmd.Parameters.AddWithValue("@eventId", id);
                        cmd.Parameters.AddWithValue("@userId", Context.User.Id);
                        cmd.Parameters.AddWithValue("@notifyDateTime", DateTime.UtcNow);
                        cmd.Parameters.AddWithValue("@message", Utils.ConvertStringToHex(msg));
                        cmd.Parameters.AddWithValue("@channelId", channelId);
                        cmd.Parameters.AddWithValue("@messageId", messageId);
                        cmd.ExecuteNonQuery();
                        cmd.Parameters.Clear();

                        if (serverName == "")
                        {
                            sb.AppendLine($"Notified subscribers for event {id}, **{desc}**!");
                        }
                        else
                        {
                            sb.AppendLine($"Notified subscribers for event {id}, **{desc}** on {serverName}!");
                        }
                    }
                }


                embed.Description = sb.ToString();

                bool Ok = false;
                var count = 0;
                Exception savedEx = new Exception();
                while (!Ok && count < 3)
                {
                    try
                    {
                        await ReplyAsync(null, false, embed.Build());
                        Ok = true;
                    }
                    catch (Exception ex)
                    {
                        savedEx = ex;
                        count++;
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                if (count == 3)
                {
                    throw savedEx;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Group("update")]
        public class UpdateModule : ModuleBase
        {
            private readonly IConfiguration _config;
            private readonly CommandService _commands;
            private readonly DiscordSocketClient _client;
            private readonly IServiceProvider _services;
            //private readonly ILogger _logger;
            private readonly string _connStr;
            public UpdateModule(IServiceProvider services)
            {
                _config = services.GetRequiredService<IConfiguration>();
                _commands = services.GetRequiredService<CommandService>();
                _client = services.GetRequiredService<DiscordSocketClient>();
                //_logger = services.GetRequiredService<ILogger>();
                _services = services;

                //In secrets.json
                _connStr = _config["ConnStr"];
            }

            [Command("desc"), Summary("update desc [#] [new desc]"), Remarks("Updates description for event #")]
            public async Task Desc(string eventId, [Remainder] string desc)
            {
                try
                {
                    var sb = new StringBuilder();
                    var embed = new EmbedBuilder();

                    embed.WithColor(new Color(255, 140, 0));
                    embed.Title = $"Updating description for event:";

                    int id;
                    bool isNumeric = int.TryParse(eventId, out id);

                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        string eventIdStr;
                        if (isNumeric)
                        {
                            eventIdStr = "id = @id";
                        }
                        else
                        {
                            eventIdStr = "launchSlug = @launchSlug";
                        }

                        DateTime retireDate = DateTime.MaxValue;
                        bool found = false;
                        string serverId = "";
                        cmd.CommandText = $"SELECT retireDate,serverId FROM `events` WHERE {eventIdStr};";
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@launchSlug", eventId.ToLower());
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                serverId = dr.GetString("serverId");
                                var server = _client.GetGuild(Convert.ToUInt64(serverId));
                                if (Context.Guild == null && server != null)
                                {
                                    if (server.GetUser(Context.User.Id) != null)
                                    {
                                        found = true;
                                    }

                                }
                                else
                                {
                                    if (Context.Guild.Id == Convert.ToUInt64(serverId))
                                    {
                                        found = true;
                                    }
                                }
                                if (found)
                                {
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
                        else if (retireDate < DateTime.UtcNow)
                        {
                            sb.AppendLine($"Sorry, event {id}, **{desc}** has already been retired.");
                        }
                        else
                        {
                            string byteString = Utils.ConvertStringToHex(desc);
                            cmd.CommandText = $"UPDATE `events` SET `desc` = @desc WHERE id = @eventId";
                            cmd.Parameters.AddWithValue("@eventId", id);
                            cmd.Parameters.AddWithValue("@desc", byteString);
                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();

                            sb.AppendLine($"Event {id}, **{desc}** updated!");
                        }
                    }


                    embed.Description = sb.ToString();

                    bool ok = false;
                    var count = 0;
                    Exception savedEx = new Exception();
                    while (!ok && count < 3)
                    {
                        try
                        {
                            await ReplyAsync(null, false, embed.Build());
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            savedEx = ex;
                            count++;
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                    if (count == 3)
                    {
                        throw savedEx;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                    throw ex;
                }
            }

            [Command("url"), Summary("update url [#] [url]"), Remarks("Updates URL for event #")]
            public async Task Url(string eventId, [Remainder] string url)
            {
                try
                {
                    var sb = new StringBuilder();
                    var embed = new EmbedBuilder();

                    embed.WithColor(new Color(255, 140, 0));
                    embed.Title = $"Updating URL for event:";

                    int id;
                    bool isNumeric = int.TryParse(eventId, out id);

                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        string eventIdStr;
                        if (isNumeric)
                        {
                            eventIdStr = "id = @id";
                        }
                        else
                        {
                            eventIdStr = "launchSlug = @launchSlug";
                        }

                        DateTime retireDate = DateTime.MaxValue;
                        bool found = false;
                        string desc = "";
                        string serverId = "";
                        cmd.CommandText = $"SELECT retireDate,serverId,`desc` FROM `events` WHERE {eventIdStr};";
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@launchSlug", eventId.ToLower());
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                serverId = dr.GetString("serverId");
                                var server = _client.GetGuild(Convert.ToUInt64(serverId));
                                if (Context.Guild == null && server != null)
                                {
                                    if (server.GetUser(Context.User.Id) != null)
                                    {
                                        found = true;
                                    }

                                }
                                else
                                {
                                    if (Context.Guild.Id == Convert.ToUInt64(serverId))
                                    {
                                        found = true;
                                    }
                                }
                                if (found)
                                {
                                    desc = Utils.ConvertHexToString(dr.GetString("desc"));
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
                        else if (retireDate < DateTime.UtcNow)
                        {
                            sb.AppendLine($"Sorry, event {id}, **{desc}** has already been retired.");
                        }
                        else
                        {
                            cmd.CommandText = $"UPDATE `events` SET url = @url WHERE id = @eventId";
                            cmd.Parameters.AddWithValue("@eventId", id);
                            cmd.Parameters.AddWithValue("@url", url);
                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();

                            sb.AppendLine($"Event {id}, **{desc}** URL updated!");
                        }
                    }

                    embed.Description = sb.ToString();

                    bool ok = false;
                    var count = 0;
                    Exception savedEx = new Exception();
                    while (!ok && count < 3)
                    {
                        try
                        {
                            await ReplyAsync(null, false, embed.Build());
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            savedEx = ex;
                            count++;
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                    if (count == 3)
                    {
                        throw savedEx;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                    throw ex;
                }
            }

            [Command("parent"), Summary("update parent [#] [parent #] [\"remove\"]"), Remarks("Adds or removes parent for event #")]
            public async Task Parent(string eventId, int parentId, [Remainder] string remove = "")
            {
                try
                {
                    var sb = new StringBuilder();
                    var embed = new EmbedBuilder();

                    embed.WithColor(new Color(255, 140, 0));
                    embed.Title = $"Updating parent for event:";

                    int id;
                    bool isNumeric = int.TryParse(eventId, out id);

                    using MySqlConnection cn = new MySqlConnection(_connStr);
                    using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                    {
                        string eventIdStr;
                        if (isNumeric)
                        {
                            eventIdStr = "id = @id";
                        }
                        else
                        {
                            eventIdStr = "launchSlug = @launchSlug";
                        }

                        DateTime retireDate = DateTime.MaxValue;
                        bool found = false;
                        string desc = "";
                        string serverId = "";
                        cmd.CommandText = $"SELECT retireDate,serverId,`desc` FROM `events` WHERE {eventIdStr};";
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@launchSlug", eventId.ToLower());
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                serverId = dr.GetString("serverId");
                                var server = _client.GetGuild(Convert.ToUInt64(serverId));
                                if (Context.Guild == null && server != null)
                                {
                                    if (server.GetUser(Context.User.Id) != null)
                                    {
                                        found = true;
                                    }

                                }
                                else
                                {
                                    if (Context.Guild.Id == Convert.ToUInt64(serverId))
                                    {
                                        found = true;
                                    }
                                }
                                if (found)
                                {
                                    desc = Utils.ConvertHexToString(dr.GetString("desc"));
                                    if (dr["retireDate"] != DBNull.Value)
                                    {
                                        retireDate = dr.GetDateTime("retireDate");
                                    }
                                }
                            }
                        }
                        cmd.Parameters.Clear();

                        //check if parent event part of same server
                        bool parentFound = false;
                        string parentDesc = "";
                        cmd.CommandText = $"SELECT `desc`,retireDate FROM `events` WHERE id = @id and serverId=@serverId and (retireDate IS NULL OR retireDate > @retireDate);";
                        cmd.Parameters.AddWithValue("@id", parentId);
                        cmd.Parameters.AddWithValue("@serverId", serverId);
                        cmd.Parameters.AddWithValue("@retireDate", DateTime.UtcNow);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            while (dr.Read())
                            {
                                parentFound = true;
                                parentDesc = Utils.ConvertHexToString(dr.GetString("desc"));
                            }
                        }
                        cmd.Parameters.Clear();

                        if (!found)
                        {
                            sb.AppendLine($"Sorry, event id {id} does not exist...");
                        }
                        else if (retireDate < DateTime.UtcNow)
                        {
                            sb.AppendLine($"Sorry, event {id}, **{desc}** has already been retired.");
                        }
                        else
                        {
                            if (remove.ToLower() == "remove")
                            {
                                cmd.CommandText = $"DELETE FROM `eventparents` WHERE eventId = @eventId And parentId=@parentId";
                                cmd.Parameters.AddWithValue("@eventId", id);
                                cmd.Parameters.AddWithValue("@parentId", parentId);
                                cmd.ExecuteNonQuery();
                                cmd.Parameters.Clear();

                                sb.AppendLine($"Event {id}, **{desc}** parent removed!");
                            }
                            else
                            {
                                if (parentFound)
                                {
                                    cmd.CommandText = $"INSERT INTO `eventparents` (eventId,parentId) VALUES (@eventId,@parentId) ON DUPLICATE KEY UPDATE parentId=parentId;";
                                    cmd.Parameters.AddWithValue("@eventId", id);
                                    cmd.Parameters.AddWithValue("@parentId", parentId);
                                    cmd.ExecuteNonQuery();
                                    cmd.Parameters.Clear();

                                    sb.AppendLine($"Event {id}, **{desc}** added as child of {parentId}, **{parentDesc}** ");
                                }
                                else
                                {
                                    sb.AppendLine($"Parent event {parentId}, **{parentDesc}** not found or already retired.");
                                }
                            }
                        }
                    }

                    embed.Description = sb.ToString();

                    bool ok = false;
                    var count = 0;
                    Exception savedEx = new Exception();
                    while (!ok && count < 3)
                    {
                        try
                        {
                            await ReplyAsync(null, false, embed.Build());
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            savedEx = ex;
                            count++;
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                    if (count == 3)
                    {
                        throw savedEx;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                    throw ex;
                }
            }
        }

        [Command("botsub")]
        public async Task BotSub(string serverId, string eventId, int parentId, string url, string expiryDateTimeUtcString, string userId, [Remainder] string desc)
        {
            try
            {
                bool ok = false;
                if (Context.User.IsBot||Context.User.Username.ToLower()=="granicus")
                {
                    ok = true;
                } else
                {
                    var user = await Context.Guild.GetUserAsync(Context.User.Id);
                    if (user.GuildPermissions.ManageMessages)
                    {
                        ok = true;
                    }
                }
                if (ok)
                {
                    var sb = new StringBuilder();
                    int id;
                    bool isNumeric = int.TryParse(eventId, out id);

                    DateTime expiryDateTimeUtc;
                    bool isDateTime = DateTime.TryParse(expiryDateTimeUtcString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out expiryDateTimeUtc);

                    if (expiryDateTimeUtcString != "" && !isDateTime)
                    {
                        await SendTextReply("Invalid DateTime Format");
                    }
                    else
                    {

                        using MySqlConnection cn = new MySqlConnection(_connStr);
                        using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                        {
                            string eventIdStr;
                            if (isNumeric)
                            {
                                eventIdStr = "id = @id";
                            }
                            else
                            {
                                eventIdStr = "launchSlug = @launchSlug";
                            }

                            DateTime retireDate = DateTime.MaxValue;
                            bool found = false;
                            cmd.CommandText = $"SELECT id,retireDate,serverId FROM `events` WHERE serverId=@serverId AND {eventIdStr};";
                            cmd.Parameters.AddWithValue("@serverId", serverId);
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.Parameters.AddWithValue("@launchSlug", eventId.ToLower());
                            using (MySqlDataReader dr = cmd.ExecuteReader())
                            {
                                while (dr.Read())
                                {
                                    serverId = dr.GetString("serverId");
                                    var server = _client.GetGuild(Convert.ToUInt64(serverId));
                                    if (Context.Guild == null && server != null)
                                    {
                                        if (server.GetUser(Context.User.Id) != null)
                                        {
                                            found = true;
                                        }

                                    }
                                    else
                                    {
                                        if (Context.Guild.Id == Convert.ToUInt64(serverId))
                                        {
                                            found = true;
                                        }
                                    }
                                    if (found)
                                    {
                                        id = dr.GetInt32("id");
                                        if (dr["retireDate"] != DBNull.Value)
                                        {
                                            retireDate = dr.GetDateTime("retireDate");
                                        }
                                    }
                                }
                            }
                            cmd.Parameters.Clear();

                            //check if parent event part of same server
                            bool parentFound = false;
                            string parentDesc = "";
                            cmd.CommandText = $"SELECT `desc`,retireDate FROM `events` WHERE id = @id and serverId=@serverId and (retireDate IS NULL OR retireDate > @retireDate);";
                            cmd.Parameters.AddWithValue("@id", parentId);
                            cmd.Parameters.AddWithValue("@serverId", serverId);
                            cmd.Parameters.AddWithValue("@retireDate", DateTime.UtcNow);
                            using (MySqlDataReader dr = cmd.ExecuteReader())
                            {
                                while (dr.Read())
                                {
                                    parentFound = true;
                                    parentDesc = Utils.ConvertHexToString(dr.GetString("desc").Trim('"'));
                                }
                            }
                            cmd.Parameters.Clear();

                            bool updFound = false;
                            if (found)
                            {
                                cmd.CommandText = $"UPDATE events SET";
                                if (desc != "")
                                {
                                    cmd.CommandText += "`desc`=@desc";
                                    updFound = true;
                                }
                                if (expiryDateTimeUtcString != "")
                                {
                                    if (updFound)
                                    {
                                        cmd.CommandText += ",";
                                    }
                                    cmd.CommandText += "eventDateTime=@eventDateTime, retireDate=@retireDate";
                                    updFound = true;
                                }
                                if (url != "")
                                {
                                    if (updFound)
                                    {
                                        cmd.CommandText += ",";
                                    }
                                    cmd.CommandText += "url=@url";
                                    updFound = true;
                                }
                                cmd.CommandText += $" WHERE {eventIdStr};";
                            }
                            else
                            {
                                cmd.CommandText = "INSERT INTO events (`desc`,eventDateTime,retireDate,serverId,url,launchSlug) VALUES (@desc,@eventDateTime,@retireDate,@serverId,@url,@launchSlug);";
                            }
                            if (!found || updFound)
                            {
                                cmd.Parameters.AddWithValue("@serverId", serverId);
                                cmd.Parameters.AddWithValue("@id", id);
                                cmd.Parameters.AddWithValue("@launchSlug", eventId.ToLower());
                                cmd.Parameters.AddWithValue("@retireDate", expiryDateTimeUtc);
                                cmd.Parameters.AddWithValue("@eventDateTime", expiryDateTimeUtc);
                                cmd.Parameters.AddWithValue("@url", url);
                                cmd.Parameters.AddWithValue("@desc", Utils.ConvertStringToHex(desc));
                                cmd.ExecuteNonQuery();
                                if (!found)
                                {
                                    id = (int)cmd.LastInsertedId;
                                }
                            }

                            if (parentId > 0)
                            {
                                cmd.CommandText = $"INSERT INTO `eventparents` (eventId,parentId) VALUES (@eventId,@parentId) ON DUPLICATE KEY UPDATE parentId=parentId;";
                                cmd.Parameters.AddWithValue("@eventId", id);
                                cmd.Parameters.AddWithValue("@parentId", parentId);
                                cmd.ExecuteNonQuery();
                                cmd.Parameters.Clear();
                            }

                            if (userId != "")
                            {
                                cmd.CommandText = $"INSERT INTO subscriptions (eventId,userId) VALUES (@eventId,@userId) ON DUPLICATE KEY UPDATE userId=userId;";
                                cmd.Parameters.AddWithValue("@eventId", id);
                                cmd.Parameters.AddWithValue("@userId", userId);
                                cmd.ExecuteNonQuery();
                                cmd.Parameters.Clear();
                            }

                            await SendTextReply(id.ToString());
                        }
                    }
                } else
                {
                    await SendTextReply("You do not have sufficient permission to call this command.");
                }
            }
            catch (Exception ex)
            {
                await SendTextReply(ex.Message);
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Command("botunsub")]
        public async Task BotUnsub(string serverId, string eventId, string userId)
        {
            try
            {
                bool ok = false;
                if (Context.User.IsBot || Context.User.Username.ToLower() == "granicus")
                {
                    ok = true;
                }
                else
                {
                    var user = await Context.Guild.GetUserAsync(Context.User.Id);
                    if (user.GuildPermissions.ManageMessages)
                    {
                        ok = true;
                    }
                }
                if (ok)
                {
                    var sb = new StringBuilder();
                int id;
                bool isNumeric = int.TryParse(eventId, out id);


                using MySqlConnection cn = new MySqlConnection(_connStr);
                using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                {
                    string eventIdStr;
                    if (isNumeric)
                    {
                        eventIdStr = "id = @id";
                    }
                    else
                    {
                        eventIdStr = "launchSlug = @launchSlug";
                    }

                    DateTime retireDate = DateTime.MaxValue;
                    bool found = false;
                    string desc = "";
                    cmd.CommandText = $"SELECT id,retireDate,`desc`,serverId FROM `events` WHERE serverId=@serverId AND {eventIdStr};";
                    cmd.Parameters.AddWithValue("@serverId", serverId);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@launchSlug", eventId.ToLower());
                    using (MySqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            serverId = dr.GetString("serverId");
                            var server = _client.GetGuild(Convert.ToUInt64(serverId));
                            if (Context.Guild == null && server != null)
                            {
                                if (server.GetUser(Context.User.Id) != null)
                                {
                                    found = true;
                                }

                            }
                            else
                            {
                                if (Context.Guild.Id == Convert.ToUInt64(serverId))
                                {
                                    found = true;
                                }
                            }
                            if (found)
                            {
                                id = dr.GetInt32("id");
                                desc = dr.GetString("desc");
                                if (dr["retireDate"] != DBNull.Value)
                                {
                                    retireDate = dr.GetDateTime("retireDate");
                                }
                            }
                        }
                    }
                    cmd.Parameters.Clear();

                        if (userId != "")
                        {
                            cmd.CommandText = $"DELETE FROM subscriptions WHERE eventId=@eventId AND userId=@userId";
                            cmd.Parameters.AddWithValue("@serverId", serverId);
                            cmd.Parameters.AddWithValue("@eventId", id);
                            cmd.Parameters.AddWithValue("@userId", userId);
                            cmd.ExecuteNonQuery();
                            cmd.Parameters.Clear();
                        } else
                        {
                            await SendTextReply("Cannot unsubscribe if no user specified");
                        }

                }
            } else
            {
                await SendTextReply("You do not have sufficient permission to call this command.");
            }
        }
            catch (Exception ex)
            {
                await SendTextReply(ex.Message);
                Log.Error(ex, ex.Message);
                throw ex;
            }
        }

        [Group("topic")]
        public class TopicModule : ModuleBase
        {
            private readonly IConfiguration _config;
            private readonly CommandService _commands;
            private readonly DiscordSocketClient _client;
            private readonly IServiceProvider _services;
            //private readonly ILogger _logger;
            private readonly string _connStr;
            public TopicModule(IServiceProvider services)
            {
                _config = services.GetRequiredService<IConfiguration>();
                _commands = services.GetRequiredService<CommandService>();
                _client = services.GetRequiredService<DiscordSocketClient>();
                //_logger = services.GetRequiredService<ILogger>();
                _services = services;

                //In secrets.json
                _connStr = _config["ConnStr"];
            }

            [Command("help"), Summary("topic help [cmd]"), Remarks("Receive more info on command")]
            [Alias("helpme")]
            public async Task Help(string args = null)
            {
                try
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
                            if (!string.IsNullOrWhiteSpace(cmd.Summary))
                            {
                                sb.AppendLine($"{cmd.Summary}");
                            }
                        }
                    }
                    else
                    {
                        foreach (var cmd in cmdList)
                        {
                            if (cmd.Name == args.ToLower())
                            {
                                embed.Title = cmd.Summary;
                                sb.AppendLine($"{cmd.Remarks}");
                            }
                        }
                    }
                    embed.Description = Format.Code(sb.ToString());

                    bool ok = false;
                    var count = 0;
                    Exception savedEx = new Exception();
                    while (!ok && count < 3)
                    {
                        try
                        {
                            var msg = await ReplyAsync(null, false, embed.Build());
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            savedEx = ex;
                            count++;
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                    if (count == 3)
                    {
                        throw savedEx;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                    throw ex;
                }
            }

            [Command("add"), Summary("topic add [name of topic]"), Remarks("Add a topic for future discussion")]
            [Alias("new")]
            [RequireUserPermission(GuildPermission.KickMembers)]
            public async Task Add([Remainder] string args = null)
            {
                try
                {
                    var sb = new StringBuilder();
                    var embed = new EmbedBuilder();

                    embed.WithColor(new Color(255, 140, 0));

                    long id = 0;
                    if (Context.Guild == null)
                    {
                        //private DM
                        embed.Title = "Ruh roh!";
                        sb.AppendLine("Sorry, can't add a topic through a DM.");
                    }
                    else
                    {
                        if (args == null)
                        {
                            embed.Title = "Ruh roh!";
                            sb.AppendLine("Sorry, can't add a topic if you don't name one!");
                        }
                        else
                        {

                            embed.Title = $"New Topic Added by {Context.User.Username}:";

                            using MySqlConnection cn = new MySqlConnection(_connStr);
                            using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                            {

                                cmd.CommandText = $"INSERT INTO `topic` (serverId,channelId,name,enterBy,enterDateTime) VALUES (@serverId,@channelId,@name,@enterBy,@enterDate)";
                                cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                                cmd.Parameters.AddWithValue("@channelId", Context.Channel.Id);
                                cmd.Parameters.AddWithValue("@name", Utils.ConvertStringToHex(args));
                                cmd.Parameters.AddWithValue("@enterBy", Context.User.Id);
                                cmd.Parameters.AddWithValue("@enterDateTime", DateTime.UtcNow);
                                cmd.ExecuteNonQuery();
                                id = cmd.LastInsertedId;
                                cmd.Parameters.Clear();
                            }

                            sb.AppendLine($"{args}");
                        }
                    }

                    embed.Description = sb.ToString();

                    bool ok = false;
                    var count = 0;
                    Exception savedEx = new Exception();
                    while (!ok && count < 3)
                    {
                        try
                        {
                            await ReplyAsync(null, false, embed.Build());
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            savedEx = ex;
                            count++;
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                    if (count == 3)
                    {
                        throw savedEx;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                    throw ex;
                }
            }

            [Command("remove"), Summary("topic remove [topic ID]"), Remarks("Remove a topic for future discussion")]
            [Alias("delete")]
            [RequireUserPermission(GuildPermission.KickMembers)]
            public async Task Remove([Remainder] int args = 0)
            {
                try
                {
                    var sb = new StringBuilder();
                    var embed = new EmbedBuilder();

                    embed.WithColor(new Color(255, 140, 0));

                    long id = 0;
                    if (Context.Guild == null)
                    {
                        //private DM
                        embed.Title = "Ruh roh!";
                        sb.AppendLine("Sorry, can't remove a topic through a DM.");
                    }
                    else
                    {
                        if (args == 0)
                        {
                            embed.Title = "Ruh roh!";
                            sb.AppendLine("Sorry, can't remove a topic if you don't ID one!");
                        }
                        else
                        {

                            embed.Title = $"Topic Removed by {Context.User.Username}:";

                            using MySqlConnection cn = new MySqlConnection(_connStr);
                            using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                            {

                                cmd.CommandText = $"DELETE FROM `topic` WHERE topicId = @topicId";
                                cmd.Parameters.AddWithValue("@topicId", args);
                                cmd.ExecuteNonQuery();
                                id = cmd.LastInsertedId;
                                cmd.Parameters.Clear();
                            }

                            sb.AppendLine($"{args}");
                        }
                    }

                    embed.Description = sb.ToString();

                    bool ok = false;
                    var count = 0;
                    Exception savedEx = new Exception();
                    while (!ok && count < 3)
                    {
                        try
                        {
                            await ReplyAsync(null, false, embed.Build());
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            savedEx = ex;
                            count++;
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                    if (count == 3)
                    {
                        throw savedEx;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                    throw ex;
                }
            }

            [Command("list"), Summary("topic list"), Remarks("Lists all active topics")]
            [Alias("ls")]
            public async Task List([Remainder] string args = null)
            {
                try
                {
                    if (Context.Guild != null)
                    {
                        var sb = new StringBuilder();
                        var embed = new EmbedBuilder();

                        embed.WithColor(new Color(255, 140, 0));
                        embed.Title = $"Available Topics in #{Context.Channel.Name}:";

                        using MySqlConnection cn = new MySqlConnection(_connStr);
                        using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                        {
                            cmd.CommandText = $"SELECT topicId,name,useCount,lastUsedDateTime FROM `topic` WHERE serverId=@serverId AND channelId = @channelId ORDER BY useCount,lastUsedDateTime, topicId";
                            cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                            cmd.Parameters.AddWithValue("@channelId", Context.Channel.Id);
                            using (MySqlDataReader dr = cmd.ExecuteReader())
                            {
                                while (dr.Read())
                                {
                                    var topicId = dr.GetInt64("topicId");
                                    var name = dr.GetString("name");
                                    var useCount = dr.GetInt32("useCount");
                                    var lastUsedDateTime = dr.GetDateTime("LastUsedDateTime");

                                    TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");


                                    var output = $"ID {topicId}, **{Utils.ConvertHexToString(name)}**, Picked {useCount} times";
                                    if (lastUsedDateTime.Year != 1800)
                                    {
                                        output += $", Last Picked { TimeZoneInfo.ConvertTimeFromUtc(lastUsedDateTime, estZone)} TOTTZ";
                                    }
                                    sb.AppendLine(output);

                                }
                            }
                            cmd.Parameters.Clear();
                        }

                        embed.Description = sb.ToString();
                        bool Ok = false;
                        var count = 0;
                        Exception savedEx = new Exception();
                        while (!Ok && count < 3)
                        {
                            try
                            {
                                await ReplyAsync(null, false, embed.Build());
                                Ok = true;
                            }
                            catch (Exception ex)
                            {
                                savedEx = ex;
                                count++;
                                System.Threading.Thread.Sleep(1000);
                            }
                        }
                        if (count == 3)
                        {
                            throw savedEx;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                    throw ex;
                }
            }

            [Command("spin"), Summary("topic spin"), Remarks("Spin for a random topic to discuss!")]
            [Alias("pick")]
            public async Task Spin([Remainder] string args = null)
            {
                try
                {
                    if (Context.Guild != null)
                    {
                        var sb = new StringBuilder();
                        var embed = new EmbedBuilder();

                        embed.WithColor(new Color(255, 140, 0));
                        embed.Title = "Big money, big money, no whammies!";

                        var topicDict = new Dictionary<int, string>();
                        var topicChance = new Dictionary<int, int>();
                        int totalChance = 0;

                        using MySqlConnection cn = new MySqlConnection(_connStr);
                        using (MySqlCommand cmd = Utils.GetDbCmd(cn, CommandType.Text, ""))
                        {
                            cmd.CommandText = $"SELECT topicId,name,useCount,lastUsedDateTime FROM `topic` WHERE serverId=@serverId AND channelId = @channelId ORDER BY useCount,lastUsedDateTime, topicId";
                            cmd.Parameters.AddWithValue("@serverId", Context.Guild.Id);
                            cmd.Parameters.AddWithValue("@channelId", Context.Channel.Id);
                            using (MySqlDataReader dr = cmd.ExecuteReader())
                            {
                                while (dr.Read())
                                {
                                    int topicId = dr.GetInt32("topicId");
                                    var name = Utils.ConvertHexToString(dr.GetString("name"));
                                    var useCount = dr.GetInt32("useCount");
                                    var lastUsedDateTime = dr.GetDateTime("LastUsedDateTime");

                                    //1 points for every day not picked
                                    //-1 points for every time used
                                    //minimum 1 chance

                                    int chance = Convert.ToInt32((DateTime.UtcNow - lastUsedDateTime).TotalDays) - useCount;
                                    if (chance < 1)
                                    {
                                        chance = 1;
                                    }

                                    topicDict.Add(topicId, name);
                                    topicChance.Add(topicId, chance);
                                    totalChance += chance;

                                }
                            }
                            cmd.Parameters.Clear();
                        }

                        int random = RandomNumbers.NextNumber(totalChance);

                        int holdTot = 0;
                        int chosenTopicId = 0;
                        foreach (var kvp in topicChance)
                        {
                            if (chosenTopicId == 0 && kvp.Value + holdTot > random)
                            {
                                chosenTopicId = kvp.Key;
                            }
                            holdTot += kvp.Value;
                        }

                        sb.AppendLine($"Let's talk about {topicDict[chosenTopicId]}.  What's up with that??");

                        using MySqlConnection cn2 = new MySqlConnection(_connStr);
                        using (MySqlCommand cmd2 = Utils.GetDbCmd(cn2, CommandType.Text, ""))
                        {
                            cmd2.CommandText = $"UPDATE topic SET useCount=useCount+1,lastUsedDateTime=@lastUsedDateTime,lastUsedBy=@lastUsedBy WHERE topicId=@topicId";
                            cmd2.Parameters.AddWithValue("@lastUsedDateTime", DateTime.UtcNow);
                            cmd2.Parameters.AddWithValue("@lastUSedBy", Context.User.Id);
                            cmd2.Parameters.AddWithValue("@topicId", chosenTopicId);
                            cmd2.ExecuteNonQuery();
                            cmd2.Parameters.Clear();
                        }


                        embed.Description = sb.ToString();
                        bool Ok = false;
                        var count = 0;
                        Exception savedEx = new Exception();
                        while (!Ok && count < 3)
                        {
                            try
                            {
                                await ReplyAsync(null, false, embed.Build());
                                Ok = true;
                            }
                            catch (Exception ex)
                            {
                                savedEx = ex;
                                count++;
                                System.Threading.Thread.Sleep(1000);
                            }
                        }
                        if (count == 3)
                        {
                            throw savedEx;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                    throw ex;
                }
            }
        }

        private async Task SendTextReply(string text)
        {
            bool ok = false;
            var count = 0;
            Exception savedEx = new Exception();
            while (!ok && count < 3)
            {
                try
                {
                    await ReplyAsync(text);
                    ok = true;
                }
                catch (Exception ex)
                {
                    savedEx = ex;
                    count++;
                    System.Threading.Thread.Sleep(1000);
                }
            }
            if (count == 3)
            {
                throw savedEx;
            }
        }
    }
}