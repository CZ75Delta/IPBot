using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Timers;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace IPBot
{
    class Program
    {
        static DiscordClient _discord;
        private static string _currentIp = new WebClient().DownloadString("http://icanhazip.com");
        private static Timer _listTimer;

        static void Main(string[] args)
        {
            using (var db = new BotDbContext())
            {
                db.Database.Migrate();
                db.SaveChanges();
            }

            MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            string token = "";
            await using (var db = new BotDbContext())
            {
                if (db.Tokens.Any())
                {
                    Console.WriteLine("Use saved token? yes/no");
                    string answer;
                    if (args.Length == 0)
                    {
                        answer = Console.ReadLine();
                    }
                    else
                        answer = args[0] switch
                        {
                            "yes" => "yes",
                            "no" => "no",
                            _ => null
                        };
                    switch (answer)
                    {
                        case "yes":
                            token = db.Tokens.First().TokenString;
                            if (token == null) { Console.WriteLine("No saved token."); Environment.Exit(22); }
                            break;
                        case "no":
                            Console.WriteLine("Please input token.");
                            token = Console.ReadLine();
                            Console.WriteLine("Save this token? yes/no");
                            string changeToken = Console.ReadLine();
                            if (changeToken != null && changeToken.Equals("yes"))
                            {
                                if (db.Tokens.Any())
                                {
                                    Token tempToken = db.Tokens.First();
                                    tempToken.TokenString = token;
                                    db.Tokens.Update(tempToken);
                                }
                                else
                                {
                                    await db.Tokens.AddAsync(new Token { TokenString = token });
                                }
                            }
                            break;
                        default:
                            Console.WriteLine("Answer with the given options next time...");
                            Console.ReadLine();
                            Environment.Exit(33);
                            break;
                    }
                }
                else
                {
                    Console.WriteLine("Input token from Discord Dev.");
                    token = Console.ReadLine();
                    Console.WriteLine("Save this token? yes/no");
                    string changeToken = Console.ReadLine();
                    if (changeToken != null && changeToken.Equals("yes"))
                    {
                        if (db.Tokens.Any())
                        {
                            Token tempToken = db.Tokens.First();
                            tempToken.TokenString = token;
                            db.Tokens.Update(tempToken);
                        }
                        else
                        {
                            await db.Tokens.AddAsync(new Token { TokenString = token });
                        }
                    }
                }

                await db.SaveChangesAsync();
            }

            _discord = new DiscordClient(new DiscordConfiguration
            {
                Token = token,
                TokenType = TokenType.Bot,
                LogLevel = LogLevel.Info
            });

            _discord.MessageCreated += async e =>
            {
                if (e.Message.Content.ToLower().Equals("ip!get"))
                {
                    string externalIp = new WebClient().DownloadString("http://icanhazip.com");
                    await e.Message.RespondAsync("The server IP is: " + externalIp);
                }
                else if (e.Message.Content.ToLower().Equals("ip!bind"))
                {
                    await using (var db = new BotDbContext())
                    {
                        await e.Channel.SendMessageAsync("Server's IP is: " + _currentIp);
                        List<DiscordMessage> messages = e.Channel.GetMessagesAsync(after: e.Channel.LastMessageId).Result.ToList();
                        DiscordMessage editable = messages.First(bot => bot.Author.Id == _discord.CurrentUser.Id);
                        await db.Messages.AddAsync(new Message { ChannelId = editable.ChannelId, MessageId = editable.Id });
                        await db.SaveChangesAsync();
                    }
                    await e.Message.RespondAsync("Bound to channel. You can delete this message.");
                }
                else if (e.Message.Content.ToLower().Equals("ip!unbind"))
                {
                    await using var db = new BotDbContext();
                    if (db.Messages.Any(d => d.ChannelId == e.Channel.Id))
                    {
                        Message toRemove = db.Messages.First(d => d.ChannelId == e.Channel.Id);
                        db.Messages.Remove(toRemove);
                        await e.Channel.SendMessageAsync("Unbound from channel.");
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync("No message exists for this channel. According to the db at least...");
                    }
                    await db.SaveChangesAsync();
                }
                else if (e.Message.Content.ToLower().Equals("ip!help"))
                {
                    await e.Channel.SendMessageAsync(Formatter.InlineCode("ip!get") + " - get current IP; " + Formatter.InlineCode("ip!bind") + " - bind to this channel and keep updating a message; " + Formatter.InlineCode("ip!get") + " - unbind from this channel");
                }
            };

            _discord.Ready += OnDiscordReady;
            _discord.MessageDeleted += OnMessageDeletion;
            CheckMessages();

            await _discord.ConnectAsync();
            await Task.Delay(-1);
        }

        private static void SetTimer()
        {
            _listTimer = new Timer(30000);
            _listTimer.Elapsed += OnTimedEvent;
            _listTimer.AutoReset = true;
            _listTimer.Enabled = true;
        }

        private static async void UpdateMessages()
        {
            await using var db = new BotDbContext();
            foreach (var dbMessage in db.Messages)
            {
                DiscordMessage message = await _discord.GetChannelAsync(dbMessage.ChannelId).Result.GetMessageAsync(dbMessage.MessageId);
                await message.ModifyAsync("Server's IP is: " + _currentIp);
            }
            await db.SaveChangesAsync();
        }

        private static async void CheckMessages()
        {
            await using var db = new BotDbContext();
            List<Message> messagesToRemove = new List<Message>();
            foreach (var dbMessage in db.Messages)
            {
                try
                {
                    DiscordMessage message = await _discord.GetChannelAsync(dbMessage.ChannelId).Result.GetMessageAsync(dbMessage.MessageId);
                }
                catch
                {
                    messagesToRemove.Add(dbMessage);
                    DiscordChannel channel = await _discord.GetChannelAsync(dbMessage.ChannelId); 
                    await channel.SendMessageAsync("Unbound from channel because of message deletion while bot was offline.");
                }
            }
            if (messagesToRemove.Count > 0)
            {
                db.RemoveRange(messagesToRemove);
            }
            await db.SaveChangesAsync();
        }

        private static void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            string externalIp = _currentIp = new WebClient().DownloadString("http://icanhazip.com");
            if (_currentIp == null || _currentIp.Equals("")) _currentIp = externalIp;
            if (!_currentIp.Equals(externalIp))
            {
                _currentIp = externalIp;
                UpdateMessages();
            }
        }

        private static Task OnMessageDeletion(MessageDeleteEventArgs e)
        {
            using var db = new BotDbContext();
            if (db.Messages.Any(d => d.ChannelId == e.Channel.Id && d.MessageId == e.Message.Id))
            {
                Message toRemove = db.Messages.First(d => d.ChannelId == e.Channel.Id);
                db.Messages.Remove(toRemove);
                e.Channel.SendMessageAsync("Unbound from channel because of message deletion.");
            }
            db.SaveChanges();
            return Task.CompletedTask;
        }

        private static Task OnDiscordReady(ReadyEventArgs e)
        {
            Console.WriteLine("Connected to Discord as: " + e.Client.CurrentUser.Username + "#" + e.Client.CurrentUser.Discriminator);
            return Task.CompletedTask;
        }
    }
}
