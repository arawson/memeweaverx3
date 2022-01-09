
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using MySql.EntityFrameworkCore.Extensions;

#nullable enable

namespace memeweaver
{
    public class MemeMySqlContext : DbContext
    {
        public DbSet<Playable> Playables => Set<Playable>();

        public DbSet<ServerSetting> ServerSettings => Set<ServerSetting>();

        public MemeMySqlContext(DbContextOptions<MemeMySqlContext> options) : base(options) {}

        protected override void OnModelCreating(ModelBuilder mb) {
            mb.Entity<Playable>().HasKey(p => p.PlayableId);
            mb.Entity<Playable>().HasIndex(p => p.Location).IsUnique();

            mb.Entity<ServerSetting>().HasKey(p => p.ServerSettingId);
            mb.Entity<ServerSetting>().HasIndex(p => p.GuildId).IsUnique();

            // mb.Entity<ServerSetting>()
            //     .HasMany(s => s.Playables)
            //     .WithMany(p => p.ServerSettings)
            //     .UsingEntity(x => x.ToTable("serversetting_playable"));

            // mb.Entity<Playable>()
            //     .HasMany(p => p.ServerSettings)
            //     .With
        }

        public async Task<Playable> GetOrCreatePlayable(Uri source) {
            Playable? cacheEntry =
                await Playables
                .AsAsyncEnumerable()
                .Where<Playable>(p => p.Location == source)
                .FirstOrDefaultAsync();
            
            if (cacheEntry == null) {
                cacheEntry = new Playable(source);
                Playables.Add(cacheEntry);
            }
            return cacheEntry;
        }
    }

    public class MemeMySqlDesignTimeFactory : IDesignTimeDbContextFactory<MemeMySqlContext>
    {
        public MemeMySqlContext CreateDbContext(string[] args)
        {
            var config =
                new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json")
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<MemeMySqlContext>();
            optionsBuilder.UseMySQL(config.GetConnectionString("MemeweaverDatabase"));

            return new MemeMySqlContext(optionsBuilder.Options);
        }
    }

    public class Playable
    {
        public long PlayableId { get; set; }

        public Uri Location { get; set; }

        public int PlayCount { get; set; }

        // TODO: server stats for quarterly meme report

        public string? StoragePath { get; set; }

        // TODO what if I don't care about this side of the navigation?
        public ICollection<ServerSetting> ServerSettings { get; set; }

        public Playable(Uri location) {
            Location = location;
            ServerSettings = new List<ServerSetting>();
        }
    }

    public class ServerSetting
    {
        public long ServerSettingId { get; set; }

        [Column(TypeName = "DECIMAL(20)")]
        public ulong GuildId { get; set; }

        public ICollection<Playable> Playables { get; set; }

        public ServerSetting()
        {
            Playables = new List<Playable>();
        }
    }
}
