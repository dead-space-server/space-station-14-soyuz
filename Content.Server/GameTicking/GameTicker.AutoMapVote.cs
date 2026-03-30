// DS14-Soyuz start: automatic map vote lifecycle
using System;
using Content.Shared.CCVar;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [ViewVariables]
    private bool _automaticMapVoteHandled;

    public bool TryStartAutomaticMapVote()
    {
        if (_automaticMapVoteHandled ||
            !_cfg.GetCVar(CCVars.VoteAutoMapEnabled) ||
            RunLevel != GameRunLevel.PreRoundLobby ||
            Paused ||
            _playerManager.PlayerCount == 0 ||
            _roundStartCountdownHasNotStartedYetDueToNoPlayers)
        {
            return false;
        }

        if (_gameMapManager.GetSelectedMap() != null)
        {
            _automaticMapVoteHandled = true;
            return false;
        }

        var candidates = _gameMapManager.GetAutomaticVoteCandidates();
        if (candidates.Count == 0)
            return false;

        if (candidates.Count == 1)
        {
            if (!_gameMapManager.TrySelectMapIfEligible(candidates[0].ID))
                return false;

            _automaticMapVoteHandled = true;
            UpdateInfoText();
            return true;
        }

        var maxDuration = _roundStartTime - RoundPreloadTime - _gameTiming.CurTime - TimeSpan.FromSeconds(1);
        if (maxDuration <= TimeSpan.Zero)
            return false;

        _automaticMapVoteHandled = true;
        _voteManager.CreateAutomaticMapVote(candidates, maxDuration);
        UpdateInfoText();
        return true;
    }
}
// DS14-Soyuz end
