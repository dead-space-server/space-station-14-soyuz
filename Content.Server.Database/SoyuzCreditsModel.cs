// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Content.Server.Database.DeadSpace._Soyuz.Credits;

/// <summary>
/// Любая запись Кредитов принадлежит конкретному серверу
/// </summary>
public interface IServerScoped
{
    string ServerId { get; }
}

public sealed class CreditBalanceRow : IServerScoped
{
    public string ServerId { get; set; } = default!;
    public Guid UserId { get; set; }
    public long Balance { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CreditLogRow : IServerScoped
{
    public long Id { get; set; }
    public string ServerId { get; set; } = default!;
    public Guid UserId { get; set; }
    public Guid? ActorUserId { get; set; }
    public long Amount { get; set; }
    public int Type { get; set; }
    public string Reason { get; set; } = default!;
    public string? RefId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CreditPlaytimeRow : IServerScoped
{
    public string ServerId { get; set; } = default!;
    public Guid UserId { get; set; }
    public long Seconds { get; set; }
    /// <summary>
    /// Сколько полных часов игры уже оплачено наградой
    /// </summary>
    public long HoursRewarded { get; set; }
}

public sealed class CreditReferralCodeRow : IServerScoped
{
    public string ServerId { get; set; } = default!;
    public Guid UserId { get; set; }
    public string Code { get; set; } = default!;
}

public sealed class CreditReferralRow : IServerScoped
{
    public string ServerId { get; set; } = default!;
    public Guid InviteeId { get; set; }
    public Guid InviterId { get; set; }
    public string Code { get; set; } = default!;
    public DateTime AppliedAt { get; set; }
    public DateTime? RewardedAt { get; set; }
}

public sealed class CreditPurchaseRow : IServerScoped
{
    public long Id { get; set; }
    public string ServerId { get; set; } = default!;
    public Guid UserId { get; set; }
    public string ProductId { get; set; } = default!;
    public long Price { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CreditOwnedRow : IServerScoped
{
    public string ServerId { get; set; } = default!;
    public Guid UserId { get; set; }
    public string ProductId { get; set; } = default!;
    public bool Equipped { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public abstract class SoyuzCreditsDbContext : DbContext
{
    public string CurrentServerId { get; set; } = "design-time";

    protected SoyuzCreditsDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<CreditBalanceRow> Balances => Set<CreditBalanceRow>();
    public DbSet<CreditLogRow> Log => Set<CreditLogRow>();
    public DbSet<CreditPlaytimeRow> Playtimes => Set<CreditPlaytimeRow>();
    public DbSet<CreditReferralCodeRow> ReferralCodes => Set<CreditReferralCodeRow>();
    public DbSet<CreditReferralRow> Referrals => Set<CreditReferralRow>();
    public DbSet<CreditPurchaseRow> Purchases => Set<CreditPurchaseRow>();
    public DbSet<CreditOwnedRow> Owned => Set<CreditOwnedRow>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<CreditBalanceRow>(e =>
        {
            e.ToTable("soyuz_credit_balance", t => t.HasCheckConstraint("ck_soyuz_credit_balance_nonneg", "balance >= 0"));
            e.HasKey(x => new { x.ServerId, x.UserId });
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        mb.Entity<CreditLogRow>(e =>
        {
            e.ToTable("soyuz_credit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Reason).IsRequired();
            e.HasIndex(x => new { x.ServerId, x.UserId, x.Id });
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        mb.Entity<CreditPlaytimeRow>(e =>
        {
            e.ToTable("soyuz_credit_playtime");
            e.HasKey(x => new { x.ServerId, x.UserId });
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        mb.Entity<CreditReferralCodeRow>(e =>
        {
            e.ToTable("soyuz_credit_referral_code");
            e.HasKey(x => new { x.ServerId, x.UserId });
            e.HasIndex(x => new { x.ServerId, x.Code }).IsUnique();
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        mb.Entity<CreditReferralRow>(e =>
        {
            e.ToTable("soyuz_credit_referral");
            e.HasKey(x => new { x.ServerId, x.InviteeId });
            e.HasIndex(x => new { x.ServerId, x.InviterId });
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        mb.Entity<CreditPurchaseRow>(e =>
        {
            e.ToTable("soyuz_credit_purchase");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.ServerId, x.UserId });
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        mb.Entity<CreditOwnedRow>(e =>
        {
            e.ToTable("soyuz_credit_owned");
            e.HasKey(x => new { x.ServerId, x.UserId, x.ProductId });
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        foreach (var et in mb.Model.GetEntityTypes())
        {
            foreach (var p in et.GetProperties())
                p.SetColumnName(ToSnake(p.Name));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardServerId();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardServerId();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardServerId()
    {
        foreach (var entry in ChangeTracker.Entries<IServerScoped>())
        {
            if (entry.State == EntityState.Unchanged || entry.State == EntityState.Detached)
                continue;

            if (entry.Entity.ServerId != CurrentServerId)
                throw new InvalidOperationException($"Попытка записи данных чужого сервера: '{entry.Entity.ServerId}' != '{CurrentServerId}'.");
        }
    }

    private static string ToSnake(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

public sealed class SoyuzCreditsPostgresDbContext : SoyuzCreditsDbContext
{
    public SoyuzCreditsPostgresDbContext(DbContextOptions<SoyuzCreditsPostgresDbContext> options) : base(options)
    {
    }
}

public sealed class SoyuzCreditsSqliteDbContext : SoyuzCreditsDbContext
{
    public SoyuzCreditsSqliteDbContext(DbContextOptions<SoyuzCreditsSqliteDbContext> options) : base(options)
    {
    }
}

public static class SoyuzCreditsDbOptions
{
    public const string HistoryTable = "__soyuz_credits_migrations";

    public static DbContextOptions<SoyuzCreditsPostgresDbContext> Postgres(string connectionString)
    {
        return new DbContextOptionsBuilder<SoyuzCreditsPostgresDbContext>()
            .UseNpgsql(connectionString, o => o.MigrationsHistoryTable(HistoryTable))
            .Options;
    }

    public static DbContextOptions<SoyuzCreditsSqliteDbContext> Sqlite(string connectionString)
    {
        return new DbContextOptionsBuilder<SoyuzCreditsSqliteDbContext>()
            .UseSqlite(connectionString, o => o.MigrationsHistoryTable(HistoryTable))
            .Options;
    }
}

public sealed class SoyuzCreditsPostgresDesignFactory : IDesignTimeDbContextFactory<SoyuzCreditsPostgresDbContext>
{
    public SoyuzCreditsPostgresDbContext CreateDbContext(string[] args)
        => new(SoyuzCreditsDbOptions.Postgres("Host=localhost;Database=design;Username=design;Password=design"));
}

public sealed class SoyuzCreditsSqliteDesignFactory : IDesignTimeDbContextFactory<SoyuzCreditsSqliteDbContext>
{
    public SoyuzCreditsSqliteDbContext CreateDbContext(string[] args)
        => new(SoyuzCreditsDbOptions.Sqlite("Data Source=design.db"));
}