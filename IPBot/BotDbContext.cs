using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace IPBot
{
    public class BotDbContext : DbContext
    {
        public DbSet<Token> Tokens { get; set; }
        public DbSet<Message> Messages { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite("Data Source=botDb.db");
    }

    public class Token
    {
        public int Id { get; set; }
        public string TokenString { get; set; }
    }

    public class Message
    {
        public int Id { get; set; }
        public ulong MessageId { get; set; }
        public ulong ChannelId { get; set; }
    }
}
