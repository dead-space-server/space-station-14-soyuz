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

/// <summary>Любая запись Кредитов принадлежит конкретному серверу.</summary>
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
    /// <summary>Сколько полных часов игры уже оплачено наградой.</summary>
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
    /// <summary>Реферал отклонён (совпали IP/HWID): награда не выдаётся.</summary>
    public DateTime? RejectedAt { get; set; }
}

public sealed class CreditPurchaseRow : IServerScoped
{
    public long Id { get; set; }
    public string ServerId { get; set; } = default!;
    public Guid UserId { get; set; }
    public string ProductId { get; set; } = default!;
    public long Price { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Когда покупка была возвращена. Сама запись и запись журнала остаются неизменными.</summary>
    public DateTime? RefundedAt { get; set; }
}

public sealed class CreditOwnedRow : IServerScoped
{
    public string ServerId { get; set; } = default!;
    public Guid UserId { get; set; }
    public string ProductId { get; set; } = default!;
    public bool Equipped { get; set; }
    /// <summary>null = навсегда.</summary>
    public DateTime? ExpiresAt { get; set; }
}

public sealed class CreditPromoRow : IServerScoped
{
    public string ServerId { get; set; } = default!;
    public string Code { get; set; } = default!;
    /// <summary>Сколько кредитов даёт код (0, если код только на товар).</summary>
    public long Credits { get; set; }
    /// <summary>Товар, который выдаёт код (null, если код только на кредиты).</summary>
    public string? ProductId { get; set; }
    /// <summary>null = без ограничения числа использований.</summary>
    public int? MaxUses { get; set; }
    public int Uses { get; set; }
    /// <summary>null = бессрочно.</summary>
    public DateTime? ExpiresAt { get; set; }
    public bool Disabled { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CreditPromoUseRow : IServerScoped
{
    public string ServerId { get; set; } = default!;
    public string Code { get; set; } = default!;
    public Guid UserId { get; set; }
    public DateTime UsedAt { get; set; }
}

/// <summary>Привязка Discord-аккаунта к игроку. Один игрок — один Discord, один Discord — один игрок (на сервер).</summary>
public sealed class CreditDiscordLinkRow : IServerScoped
{
    public string ServerId { get; set; } = default!;
    public Guid UserId { get; set; }
    /// <summary>Discord snowflake строкой: так не теряется точность и не мешает ulong в SQLite.</summary>
    public string DiscordId { get; set; } = default!;
    public DateTime LinkedAt { get; set; }
}

public abstract class SoyuzCreditsDbContext : DbContext
{
    /// <summary>Выставляется фабрикой. Используется глобальными фильтрами запросов.</summary>
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
    public DbSet<CreditPromoRow> Promos => Set<CreditPromoRow>();
    public DbSet<CreditPromoUseRow> PromoUses => Set<CreditPromoUseRow>();
    public DbSet<CreditDiscordLinkRow> DiscordLinks => Set<CreditDiscordLinkRow>();

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
            e.HasKey(x => new { x.ServerId, x.InviteeId }); // приглашённый привязан к одному пригласившему
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

        mb.Entity<CreditPromoRow>(e =>
        {
            e.ToTable("soyuz_credit_promo");
            e.HasKey(x => new { x.ServerId, x.Code });
            e.Property(x => x.Code).IsRequired();
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        mb.Entity<CreditPromoUseRow>(e =>
        {
            e.ToTable("soyuz_credit_promo_use");
            e.HasKey(x => new { x.ServerId, x.Code, x.UserId }); // один игрок использует код один раз
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        mb.Entity<CreditDiscordLinkRow>(e =>
        {
            e.ToTable("soyuz_credit_discord_link");
            e.HasKey(x => new { x.ServerId, x.UserId });
            e.Property(x => x.DiscordId).IsRequired();
            e.HasIndex(x => new { x.ServerId, x.DiscordId }).IsUnique();
            e.HasQueryFilter(x => x.ServerId == CurrentServerId);
        });

        // snake_case для колонок, как в остальной БД проекта.
        foreach (var et in mb.Model.GetEntityTypes())
        {
            foreach (var p in et.GetProperties())
                p.SetColumnName(ToSnake(p.Name));
        }
    }

    // Второй рубеж: нельзя записать/изменить/удалить строку чужого сервера.
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
    // Отдельная таблица истории миграций, чтобы не мешать миграциям апстрима.
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

// Нужны только для `dotnet ef migrations add` (подключение к реальной БД не требуется).
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