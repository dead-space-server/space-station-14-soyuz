// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Robust.Shared.IoC;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public enum DiscordLinkResult { Ok, InvalidCode, DiscordAlreadyLinked, UserAlreadyLinked }

/// <summary>
/// Привязка Discord-аккаунта к игроку. Игрок получает в игре короткий одноразовый код (credits_discord), вводит его в Discord командой бота, бот передаёт код серверу, и сервер связывает аккаунты
/// Так Discord-аккаунт нельзя привязать к чужому игроку: код знает только тот, кто сидит в игре
/// </summary>
public interface ICreditDiscordLinks
{
    /// <summary>Новый код привязки для игрока (действует 10 минут, предыдущий код игрока перестаёт работать)</summary>
    string CreateCode(Guid userId);

    Task<(DiscordLinkResult Result, Guid? UserId)> LinkAsync(string discordId, string code);
    Task<Guid?> FindUserAsync(string discordId);
    Task<string?> GetDiscordIdAsync(Guid userId);
    Task<bool> UnlinkAsync(Guid userId);
}

public sealed class CreditDiscordLinks : ICreditDiscordLinks
{
    [Dependency] private readonly ICreditRepository _repo = default!;

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 10;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly object _lock = new();
    private readonly Dictionary<string, (Guid User, DateTime Expires)> _codes = new();

    public string CreateCode(Guid userId)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var stale = _codes
                .Where(kv => kv.Value.Expires <= now || kv.Value.User == userId)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in stale)
                _codes.Remove(key);

            string code;
            do
            {
                code = Generate();
            } while (_codes.ContainsKey(code));

            _codes[code] = (userId, now + Lifetime);
            return code;
        }
    }

    public async Task<(DiscordLinkResult Result, Guid? UserId)> LinkAsync(string discordId, string code)
    {
        code = code.Trim().ToUpperInvariant();

        Guid user;
        lock (_lock)
        {
            if (!_codes.TryGetValue(code, out var entry) || entry.Expires <= DateTime.UtcNow)
                return (DiscordLinkResult.InvalidCode, null);

            user = entry.User;
        }

        var result = await _repo.TryLinkDiscordAsync(user, discordId);
        switch (result)
        {
            case DiscordLinkDbResult.Ok:
                lock (_lock)
                {
                    _codes.Remove(code);
                }

                return (DiscordLinkResult.Ok, user);
            case DiscordLinkDbResult.DiscordAlreadyLinked:
                return (DiscordLinkResult.DiscordAlreadyLinked, null);
            default:
                return (DiscordLinkResult.UserAlreadyLinked, null);
        }
    }

    public Task<Guid?> FindUserAsync(string discordId) => _repo.FindUserByDiscordAsync(discordId);

    public Task<string?> GetDiscordIdAsync(Guid userId) => _repo.GetDiscordIdAsync(userId);

    public Task<bool> UnlinkAsync(Guid userId) => _repo.UnlinkDiscordAsync(userId);

    private static string Generate()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
