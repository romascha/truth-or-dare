using Microsoft.EntityFrameworkCore;
using TruthOrDare.Api.Models;

namespace TruthOrDare.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();
    public DbSet<Turn> Turns => Set<Turn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasKey(x => x.Id);
        modelBuilder.Entity<User>().HasIndex(x => x.SessionToken).IsUnique();

        modelBuilder.Entity<Game>().HasKey(x => x.Id);
        modelBuilder.Entity<Game>().HasIndex(x => x.PublicCode).IsUnique();

        modelBuilder.Entity<GamePlayer>().HasKey(x => x.Id);
        modelBuilder.Entity<GamePlayer>().HasIndex(x => new { x.GameId, x.UserId }).IsUnique();

        modelBuilder.Entity<GamePlayer>()
            .HasOne(x => x.Game)
            .WithMany(x => x.Players)
            .HasForeignKey(x => x.GameId);

        modelBuilder.Entity<Turn>().HasKey(x => x.Id);
        modelBuilder.Entity<Turn>()
            .HasOne(x => x.Game)
            .WithMany(x => x.Turns)
            .HasForeignKey(x => x.GameId);
    }
}
