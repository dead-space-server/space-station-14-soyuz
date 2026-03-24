using Content.Server.Administration.Managers;
using Content.Server.CharacterInfo;
using Content.Server.EUI;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Shared.DeadSpace.Skills;
using Content.Shared.DeadSpace.Skills.Components;
using Content.Shared.DeadSpace.Skills.Events;
using Content.Shared.DeadSpace.Skills.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.DeadSpace.Skill;

public sealed class SkillShareSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly CharacterInfoSystem _characterInfo = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly EuiManager _euiManager = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SkillSystem _skillSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;

    private const float MaxShareDistance = 3f;
    private const float SkillTransferTickDuration = 1f;
    private const float SkillTransferTickProgress = 0.25f;
    private static readonly SoundCollectionSpecifier LearnBookSound = new("LearnBook");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SkillComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<SkillComponent, SkillTransferDoAfterEvent>(OnSkillTransferDoAfter);
    }

    private void OnGetVerbs(Entity<SkillComponent> entity, ref GetVerbsEvent<Verb> args)
    {
        if (args.User == args.Target)
            return;

        var user = args.User;
        var target = entity.Owner;

        var isAdminGhost = false;
        if (HasComp<GhostComponent>(args.User) &&
            _playerManager.TryGetSessionByEntity(args.User, out var userSession) &&
            _adminManager.IsAdmin(userSession))
        {
            isAdminGhost = true;
        }

        if (!isAdminGhost)
        {
            if (HasComp<GhostComponent>(args.User))
                return;

            if (!_mindSystem.TryGetMind(args.User, out _, out _))
                return;

            if (!HasComp<SkillComponent>(args.User))
                return;

            if (!args.CanInteract || !args.CanAccess)
                return;

            var verb = new Verb
            {
                Text = Loc.GetString("skill-share-verb-text"),
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/information.svg.192dpi.png")),
                Act = () => RequestSkillShare(user, target),
                Priority = -1
            };

            args.Verbs.Add(verb);
            return;
        }

        var adminVerb = new Verb
        {
            Text = Loc.GetString("skill-share-verb-admin-text"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/information.svg.192dpi.png")),
            Act = () => OpenSkillsDirectly(user, target),
            Priority = -1
        };

        args.Verbs.Add(adminVerb);
    }

    private void RequestSkillShare(EntityUid requester, EntityUid target)
    {
        if (!InShareRange(requester, target))
            return;

        if (!_playerManager.TryGetSessionByEntity(target, out var targetSession))
            return;

        var requesterName = MetaData(requester).EntityName;
        _euiManager.OpenEui(new SkillShareRequestEui(target, requester, this, requesterName), targetSession);
    }

    private void OpenSkillsDirectly(EntityUid viewer, EntityUid target)
    {
        if (!_playerManager.TryGetSessionByEntity(viewer, out var viewerSession))
            return;

        OpenSkillsList(viewer, target, viewerSession, false);
    }

    public void HandleSkillShareResponse(EntityUid target, EntityUid requester, ICommonSession targetSession, bool accepted)
    {
        if (!accepted || !EntityManager.EntityExists(requester) || !InShareRange(requester, target))
            return;

        if (!_playerManager.TryGetSessionByEntity(requester, out var requesterSession))
            return;

        OpenSkillsList(requester, target, requesterSession, true);
    }

    public void HandleSkillLearnRequest(
        EntityUid viewer,
        EntityUid target,
        ICommonSession viewerSession,
        string prototypeId,
        bool allowLearningRequests)
    {
        if (!allowLearningRequests)
            return;

        if (viewerSession.AttachedEntity != viewer)
            return;

        if (!TryValidateTransfer(viewer, target, prototypeId, out var prototype))
            return;

        var title = Loc.GetString("skill-share-transfer-confirm-title");
        var message = Loc.GetString(
            "skill-share-transfer-student-confirm-message",
            ("skill", prototype.Name),
            ("teacher", MetaData(target).EntityName));

        _euiManager.OpenEui(new SkillTransferConfirmEui(title, message, accepted =>
        {
            if (!accepted)
                return;

            HandleLearnerConfirmation(viewer, target, prototypeId);
        }), viewerSession);
    }

    private void HandleLearnerConfirmation(
        EntityUid learner,
        EntityUid teacher,
        string prototypeId)
    {
        if (!TryValidateTransfer(learner, teacher, prototypeId, out var prototype, popupMissingRequirements: true))
            return;

        if (!_playerManager.TryGetSessionByEntity(teacher, out var teacherSession))
            return;

        var title = Loc.GetString("skill-share-transfer-confirm-title");
        var message = Loc.GetString(
            "skill-share-transfer-teacher-confirm-message",
            ("skill", prototype.Name),
            ("student", MetaData(learner).EntityName));

        _euiManager.OpenEui(new SkillTransferConfirmEui(title, message, accepted =>
        {
            HandleTeacherConfirmation(learner, teacher, prototypeId, accepted);
        }), teacherSession);
    }

    private void HandleTeacherConfirmation(EntityUid learner, EntityUid teacher, string prototypeId, bool accepted)
    {
        if (!accepted)
        {
            if (_prototypeManager.TryIndex<SkillPrototype>(prototypeId, out var declinedSkill))
            {
                _popup.PopupEntity(
                    Loc.GetString(
                        "skill-share-transfer-declined-message",
                        ("skill", declinedSkill.Name),
                        ("teacher", MetaData(teacher).EntityName)),
                    learner,
                    learner);
            }

            return;
        }

        if (!TryValidateTransfer(learner, teacher, prototypeId, out var prototype, popupMissingRequirements: true))
            return;

        StartSkillTransfer(learner, teacher, prototype);
    }

    private void StartSkillTransfer(EntityUid learner, EntityUid teacher, SkillPrototype prototype)
    {
        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            learner,
            TimeSpan.FromSeconds(SkillTransferTickDuration),
            new SkillTransferDoAfterEvent(prototype.ID),
            eventTarget: learner,
            target: teacher)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = 0.01f,
            DistanceThreshold = MaxShareDistance,
            DuplicateCondition = DuplicateConditions.SameTarget | DuplicateConditions.SameEvent,
            BlockDuplicate = true,
            CancelDuplicate = false
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
        {
            _popup.PopupEntity(Loc.GetString("skill-canlearn-already-learning"), learner, learner);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString(
                "skill-share-transfer-start-student",
                ("skill", prototype.Name),
                ("teacher", MetaData(teacher).EntityName)),
            learner,
            learner);

        _popup.PopupEntity(
            Loc.GetString(
                "skill-share-transfer-start-teacher",
                ("skill", prototype.Name),
                ("student", MetaData(learner).EntityName)),
            teacher,
            teacher);
    }

    private void OnSkillTransferDoAfter(Entity<SkillComponent> learner, ref SkillTransferDoAfterEvent args)
    {
        if (!_prototypeManager.TryIndex<SkillPrototype>(args.SkillId, out var prototype))
            return;

        if (args.Target is not { } teacher)
            return;

        if (!EntityManager.EntityExists(teacher))
            return;

        if (args.Cancelled)
        {
            PopupTransferCancelled(learner.Owner, teacher, prototype.Name);
            return;
        }

        if (!TryComp<SkillComponent>(teacher, out var teacherSkills) ||
            !_skillSystem.CnowThisSkill(teacher, args.SkillId, teacherSkills))
        {
            PopupTransferCancelled(learner.Owner, teacher, prototype.Name);
            return;
        }

        if (!_skillSystem.CanLearn(learner.Owner, args.SkillId, learner.Comp))
            return;

        _skillSystem.AddSkillProgress(learner.Owner, args.SkillId, SkillTransferTickProgress, learner.Comp);
        RefreshCharacterInfo(learner.Owner);
        _audio.PlayPvs(LearnBookSound, learner.Owner);

        if (_skillSystem.GetSkillProgress(learner.Owner, args.SkillId, learner.Comp) < 1f)
        {
            args.Repeat = true;
            return;
        }

        _popup.PopupEntity(
            Loc.GetString(
                "skill-share-transfer-complete-student",
                ("skill", prototype.Name),
                ("teacher", MetaData(teacher).EntityName)),
            learner.Owner,
            learner.Owner);

        _popup.PopupEntity(
            Loc.GetString(
                "skill-share-transfer-complete-teacher",
                ("skill", prototype.Name),
                ("student", MetaData(learner.Owner).EntityName)),
            teacher,
            teacher);
    }

    private void OpenSkillsList(EntityUid viewer, EntityUid target, ICommonSession viewerSession, bool allowLearningRequests)
    {
        if (!TryComp<SkillComponent>(target, out var targetSkills))
            return;

        var skills = BuildSkillsList(viewer, target, targetSkills, allowLearningRequests);
        var targetName = MetaData(target).EntityName;

        _euiManager.OpenEui(
            new SkillsListEui(viewer, target, targetName, skills, this, allowLearningRequests),
            viewerSession);
    }

    private List<SkillInfo> BuildSkillsList(
        EntityUid viewer,
        EntityUid target,
        SkillComponent targetSkills,
        bool allowLearningRequests)
    {
        var result = new List<SkillInfo>();
        var viewerHasSkillComponent = TryComp<SkillComponent>(viewer, out var viewerSkills);

        foreach (var skill in targetSkills.Skills)
        {
            var targetKnowsSkill = skill.Value >= 1f;
            var highlightAsUnknown = allowLearningRequests
                && targetKnowsSkill
                && viewerHasSkillComponent
                && !_skillSystem.CnowThisSkill(viewer, skill.Key, viewerSkills);

            var info = _skillSystem.GetSkillInfo(
                target,
                skill.Key,
                targetSkills,
                highlightAsUnknown: highlightAsUnknown,
                canLearnFromSource: highlightAsUnknown);

            if (info != null)
                result.Add(info.Value);
        }

        return result;
    }

    private bool TryValidateTransfer(
        EntityUid learner,
        EntityUid teacher,
        string prototypeId,
        out SkillPrototype prototype,
        bool popupMissingRequirements = false)
    {
        prototype = default!;

        if (!EntityManager.EntityExists(learner) || !EntityManager.EntityExists(teacher) || !InShareRange(learner, teacher))
            return false;

        if (!_prototypeManager.TryIndex<SkillPrototype>(prototypeId, out var skillPrototype) || skillPrototype == null)
            return false;

        prototype = skillPrototype;

        if (!TryComp<SkillComponent>(teacher, out var teacherSkills) ||
            !_skillSystem.CnowThisSkill(teacher, prototypeId, teacherSkills))
            return false;

        if (!TryComp<SkillComponent>(learner, out var learnerSkills))
            return false;

        if (_skillSystem.CnowThisSkill(learner, prototypeId, learnerSkills))
        {
            _popup.PopupEntity(
                Loc.GetString("skill-share-transfer-already-known", ("skill", prototype.Name)),
                learner,
                learner);
            return false;
        }

        if (popupMissingRequirements && !_skillSystem.CanLearn(learner, prototypeId, learnerSkills))
            return false;

        return true;
    }

    private bool InShareRange(EntityUid first, EntityUid second)
    {
        var firstXform = Transform(first);
        var secondXform = Transform(second);

        if (!firstXform.Coordinates.TryDistance(EntityManager, _transformSystem, secondXform.Coordinates, out var distance))
            return false;

        return distance <= MaxShareDistance;
    }

    private void PopupTransferCancelled(EntityUid learner, EntityUid teacher, string skillName)
    {
        _popup.PopupEntity(
            Loc.GetString("skill-share-transfer-cancelled-student", ("skill", skillName)),
            learner,
            learner);

        _popup.PopupEntity(
            Loc.GetString(
                "skill-share-transfer-cancelled-teacher",
                ("skill", skillName),
                ("student", MetaData(learner).EntityName)),
            teacher,
            teacher);
    }

    private void RefreshCharacterInfo(EntityUid learner)
    {
        if (!_playerManager.TryGetSessionByEntity(learner, out var learnerSession))
            return;

        _characterInfo.SendCharacterInfo(learner, learnerSession);
    }
}
