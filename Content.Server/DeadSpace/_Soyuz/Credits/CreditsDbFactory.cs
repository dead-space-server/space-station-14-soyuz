// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using Content.Server.Database.DeadSpace._Soyuz.Credits;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public interface ICreditsDbFactory
{
    SoyuzCreditsDbContext Create();
    void Migrate();
}

/// <summary>Создаёт контекст Кредитов на тех же настройках БД, что и основной сервер</summary>
public sealed class CreditsDbFactory : ICreditsDbFactory
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ICreditServerIdProvider _server = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    private readonly string _memoryName = $"soyuz_credits_{Guid.NewGuid():N}";
    private readonly object _memoryLock = new();
    private SqliteConnection? _memoryKeeper;

    public SoyuzCreditsDbContext Create()
    {
        var engine = _cfg.GetCVar(CCVars.DatabaseEngine).ToLowerInvariant();

        SoyuzCreditsDbContext ctx = engine switch
        {
            "postgres" => new SoyuzCreditsPostgresDbContext(SoyuzCreditsDbOptions.Postgres(PostgresConnection())),
            "sqlite" => new SoyuzCreditsSqliteDbContext(SoyuzCreditsDbOptions.Sqlite(SqliteConnectionString())),
            _ => throw new InvalidOperationException($"Неизвестный database.engine: {engine}"),
        };

        ctx.CurrentServerId = _server.ServerId;
        return ctx;
    }

    public void Migrate()
    {
        _ = _server.ServerId;
        using var db = Create();
        db.Database.Migrate();

        var pending = db.Database.GetPendingMigrations().ToList();
        if (pending.Count > 0)
            throw new InvalidOperationException($"Миграции Советских кредитов не применены: {string.Join(", ", pending)}.");
    }

    private string PostgresConnection()
    {
        return new NpgsqlConnectionStringBuilder
        {
            Host = _cfg.GetCVar(CCVars.DatabasePgHost),
            Port = _cfg.GetCVar(CCVars.DatabasePgPort),
            Database = _cfg.GetCVar(CCVars.DatabasePgDatabase),
            Username = _cfg.GetCVar(CCVars.DatabasePgUsername),
            Password = _cfg.GetCVar(CCVars.DatabasePgPassword),
        }.ConnectionString;
    }

    private string SqliteConnectionString()
    {
        var root = _res.UserData.RootDir;
        if (root == null)
        {
            var memory = $"Data Source={_memoryName};Mode=Memory;Cache=Shared";
            lock (_memoryLock)
            {
                if (_memoryKeeper == null)
                {
                    _memoryKeeper = new SqliteConnection(memory);
                    _memoryKeeper.Open();
                }
            }

            return memory;
        }

        var path = _cfg.GetCVar(CCVars.DatabaseSqliteDbPath);
        return $"Data Source={Path.Combine(root, path)}";
    }
}