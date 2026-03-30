// DS14-Soyuz start: automatic map vote admin toggle
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server.GameTicking.Commands;

[AdminCommand(AdminFlags.Round)]
public sealed class AutoMapVoteCommand : LocalizedCommands
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override string Command => "automapvote";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("shell-need-between-arguments", ("lower", 0), ("upper", 1)));
            return;
        }

        var enabled = _cfg.GetCVar(CCVars.VoteAutoMapEnabled);
        if (args.Length == 0)
        {
            enabled = !enabled;
        }
        else if (!bool.TryParse(args[0], out enabled))
        {
            shell.WriteError(Loc.GetString("shell-invalid-bool"));
            return;
        }

        _cfg.SetCVar(CCVars.VoteAutoMapEnabled, enabled);

        if (enabled)
            _entManager.EntitySysManager.GetEntitySystem<GameTicker>().TryStartAutomaticMapVote();

        shell.WriteLine(Loc.GetString(enabled
            ? "automapvote-command-enabled"
            : "automapvote-command-disabled"));
    }
}
// DS14-Soyuz end
