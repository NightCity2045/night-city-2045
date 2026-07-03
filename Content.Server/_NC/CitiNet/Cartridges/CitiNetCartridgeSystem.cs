using System.Linq;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Content.Shared.Access.Components;
using Content.Server.CartridgeLoader;
using Content.Server.Power.Components;
using Content.Shared._NC.CitiNet;
using Content.Shared._NC.CitiNet.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server.Chat.Managers;
using Content.Server._NC.Bank;
using Content.Server._NC.CitiNet.Live;
using Content.Shared._NC.CitiNet.Live;
using Content.Server.PowerCell;
using Content.Shared.Inventory;
using Content.Server._NC.Ncpd;
using Content.Server._NC.Trauma;
using Content.Shared._NC.Ncpd;
using Content.Shared.Mind.Components;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._NC.CitiNet.Cartridges;

/// <summary>
/// Серверная система картриджа CitiNet.
/// Обрабатывает P2P звонки, групповые звонки и BBS-каналы.
/// Маршрутизация через CitiNet Relay (требует питания).
/// </summary>
public sealed class CitiNetCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridge = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly CitiNetStreamSystem _liveStream = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly NcpdDispatchSystem _ncpdDispatch = default!;
    [Dependency] private readonly CitiNetMapSystem _citiNetMap = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly Content.Server._NC.Dispatch.OverwatchSystem _overwatch = default!;
    [Dependency] private readonly SharedMindSystem _mindSystem = default!;
    [Dependency] private readonly SharedJobSystem _jobSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    // Интервал проверки Relay (в секундах)
    private const float RelayCheckInterval = 2.0f;
    private float _relayCheckTimer;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<OpenCitiNetUiMessage>(OnOpenCitiNetUiMessage);

        SubscribeLocalEvent<CitiNetCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<CitiNetCartridgeComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<CitiNetCartridgeComponent, CartridgeAddedEvent>(OnAdded);

        // Phase 2: FLATLINE — слушаем смерть/крит участников групповых звонков
        // Используем HandsComponent, т.к. MobStateComponent уже занят в SharedStunSystem
        SubscribeLocalEvent<HandsComponent, MobStateChangedEvent>(OnMobStateChanged);

        // Voice Relay
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpeak);
    }

    private void OnOpenCitiNetUiMessage(OpenCitiNetUiMessage msg, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.AttachedEntity;
        if (user == null)
            return;

        // Ищем PDA в слоте ID
        if (!_inventory.TryGetSlotEntity(user.Value, "id", out var pdaUid))
            return;

        // Убеждаемся что это КПК с установленным CartridgeLoader
        if (!TryComp<CartridgeLoaderComponent>(pdaUid, out var loader))
            return;

        // Ищем внутри CitiNetCartridgeComponent
        if (!_cartridge.TryGetProgram<CitiNetCartridgeComponent>(pdaUid.Value, out var citiNetUid, false, loader))
            return;

        // Если СитиНет найден, делаем его активной программой
        if (loader.ActiveProgram != citiNetUid)
            _cartridge.ActivateProgram(pdaUid.Value, citiNetUid.Value, loader);

        // Открываем UI PDA для игрока
        _ui.OpenUi(pdaUid.Value, PdaUiKey.Key, user.Value);
    }

    // ========== Периодическая проверка Relay ==========

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _relayCheckTimer += frameTime;
        if (_relayCheckTimer < RelayCheckInterval)
            return;
        _relayCheckTimer = 0f;

        // Проверяем все картриджи в активном или исходящем звонке
        var query = EntityQueryEnumerator<CitiNetCartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.CallState == CitiNetCallState.None)
                continue;

            // Если Relay пропал — принудительно разрываем звонок
            if (!HasActiveCitiNetRelay((uid, comp)))
            {
                ForceDisconnectCall((uid, comp));
            }
        }
    }

    // ========== Инициализация ==========

    /// <summary>
    /// При установке картриджа в PDA генерируем уникальный номер Агента.
    /// </summary>
    private void OnAdded(Entity<CitiNetCartridgeComponent> ent, ref CartridgeAddedEvent args)
    {
        ent.Comp.LoaderUid = args.Loader;

        // Генерируем номер если ещё нет
        if (string.IsNullOrEmpty(ent.Comp.AgentNumber))
        {
            ent.Comp.AgentNumber = GenerateAgentNumber();
        }
    }

    private void OnUiReady(Entity<CitiNetCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        // Регистрируем как фоновую программу чтобы получать события даже когда не активна
        _cartridge.RegisterBackgroundProgram(args.Loader, ent);
        UpdateUI(ent, args.Loader);
    }

    // ========== Обработка UI-сообщений ==========

    private void OnMessage(Entity<CitiNetCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        var loader = GetEntity(args.LoaderUid);

        if (args is not CitiNetUiMessageEvent msg)
            return;

        // Проверяем наличие CitiNet Relay для большинства операций
        switch (msg.Type)
        {
            // Общее
            case CitiNetUiMessageType.SelectTab:
                if (Enum.TryParse<CitiNetTab>(msg.Content, out var tab))
                {
                    ent.Comp.ActiveTab = tab;
                    if (tab == CitiNetTab.BBS)
                        ent.Comp.ActiveChatTarget = null;
                }
                break;

            // P2P чаты и звонки
            case CitiNetUiMessageType.StartChat:
                HandleStartChat(ent, msg);
                break;
            case CitiNetUiMessageType.CloseChat:
                HandleCloseChat(ent);
                break;
            case CitiNetUiMessageType.InitiateCall:
                HandleInitiateCall(ent, msg);
                break;
            case CitiNetUiMessageType.AcceptCall:
                HandleAcceptCall(ent);
                break;
            case CitiNetUiMessageType.DeclineCall:
                HandleDeclineCall(ent);
                break;
            case CitiNetUiMessageType.HangUp:
                HandleHangUp(ent);
                break;
            case CitiNetUiMessageType.SendCallMessage:
                HandleSendCallMessage(ent, msg);
                break;
            case CitiNetUiMessageType.PingLocation:
                HandlePingLocation(ent);
                break;

            // Групповые звонки
            case CitiNetUiMessageType.CreateGroup:
                HandleCreateGroup(ent);
                break;
            case CitiNetUiMessageType.InviteToGroup:
                HandleInviteToGroup(ent, msg);
                break;
            case CitiNetUiMessageType.LeaveGroup:
                HandleLeaveGroup(ent);
                break;
            case CitiNetUiMessageType.SendGroupMessage:
                HandleSendGroupMessage(ent, msg);
                break;
            case CitiNetUiMessageType.JoinGroupVoice:
                HandleJoinGroupVoice(ent);
                break;
            case CitiNetUiMessageType.LeaveGroupVoice:
                HandleLeaveGroupVoice(ent);
                break;

            // BBS
            case CitiNetUiMessageType.JoinChannel:
                HandleJoinChannel(ent, msg);
                break;
            case CitiNetUiMessageType.LeaveChannel:
                HandleLeaveChannel(ent, msg);
                break;
            case CitiNetUiMessageType.SendBBSMessage:
                HandleSendBBSMessage(ent, msg);
                break;
            case CitiNetUiMessageType.SelectChannel:
                HandleSelectChannel(ent, msg);
                break;
            case CitiNetUiMessageType.InviteToChannel:
                HandleInviteToChannel(ent, msg);
                break;

            // Экстренные вызовы
            case CitiNetUiMessageType.CallPolice:
                HandleCallPolice(ent);
                break;
            case CitiNetUiMessageType.CallTrauma:
                HandleCallTrauma(ent);
                break;
        }

        UpdateUI(ent, loader);
    }

    // ========== P2P Звонки и Чаты ==========

    private void HandleStartChat(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        if (msg.TargetId == null || !HasActiveCitiNetRelay(ent))
            return;

        // Ищем Агент с таким номером
        var target = FindCartridgeByNumber(msg.TargetId);
        if (target == null)
            return;

        if (target.Value.Owner == ent.Owner)
            return; // Нельзя открыть чат с самим собой

        if (ent.Comp.ActiveChatTarget != target.Value.Owner)
        {
            ent.Comp.ActiveChatTarget = target.Value.Owner;
            if (!ent.Comp.ChatHistories.ContainsKey(target.Value.Owner))
                ent.Comp.ChatHistories[target.Value.Owner] = new List<CitiNetCallMessage>();
        }

        ent.Comp.ActiveTab = CitiNetTab.P2P;
        UpdateUIForCartridge(ent);
    }

    private void HandleCloseChat(Entity<CitiNetCartridgeComponent> ent)
    {
        if (ent.Comp.ActiveChatTarget != null)
            ent.Comp.ChatHistories.Remove(ent.Comp.ActiveChatTarget.Value);

        ent.Comp.ActiveChatTarget = null;
        UpdateUIForCartridge(ent);
    }

    private void HandleInitiateCall(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        if (ent.Comp.ActiveChatTarget == null || ent.Comp.CallState != CitiNetCallState.None || !HasActiveCitiNetRelay(ent))
            return;

        var targetUid = ent.Comp.ActiveChatTarget.Value;
        if (!TryComp<CitiNetCartridgeComponent>(targetUid, out var targetComp))
            return;

        var target = new Entity<CitiNetCartridgeComponent>(targetUid, targetComp);
        if (!HasActiveCitiNetRelay(target))
            return;

        // Если цель уже в звонке — отклоняем с уведомлением "Занято"
        if (targetComp.CallState != CitiNetCallState.None)
        {
            var busyMsg = new CitiNetCallMessage(_timing.CurTime, Loc.GetString("citinet-sender-system"),
                Loc.GetString("citinet-call-busy"), true);
            AddP2PMessage(ent, targetUid, busyMsg);
            UpdateUIForCartridge(ent);
            return;
        }

        // Устанавливаем состояние "звоним" + фиксируем партнёра звонка
        ent.Comp.CallState = CitiNetCallState.Ringing;
        ent.Comp.ActiveCallPartner = targetUid;

        // У цели — входящий вызов (автоматически открывает чат)
        target.Comp.CallState = CitiNetCallState.Incoming;
        target.Comp.IncomingCaller = ent.Owner;
        target.Comp.ActiveCallPartner = ent.Owner;
        target.Comp.ActiveChatTarget = ent.Owner;
        target.Comp.ActiveTab = CitiNetTab.P2P;

        // Инициализируем историю чата у вызываемого, если ещё нет
        if (!target.Comp.ChatHistories.ContainsKey(ent.Owner))
            target.Comp.ChatHistories[ent.Owner] = new List<CitiNetCallMessage>();

        // NC — Звук входящего вызова для цели
        var holder = GetPdaHolderUid(target);
        if (holder != null)
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/double_ring.ogg"), holder.Value);

        // Обновляем UI цели и звонящего
        UpdateUIForCartridge(target);
        UpdateUIForCartridge(ent);
    }

    private void HandleAcceptCall(Entity<CitiNetCartridgeComponent> ent)
    {
        if (ent.Comp.CallState != CitiNetCallState.Incoming || ent.Comp.IncomingCaller == null)
            return;

        if (!TryComp<CitiNetCartridgeComponent>(ent.Comp.IncomingCaller, out var callerComp))
            return;

        var callerUid = ent.Comp.IncomingCaller.Value;

        // Устанавливаем активный звонок для обеих сторон
        ent.Comp.CallState = CitiNetCallState.Active;
        ent.Comp.ActiveCallPartner = callerUid;
        ent.Comp.ActiveChatTarget = callerUid;
        ent.Comp.IncomingCaller = null;

        callerComp.CallState = CitiNetCallState.Active;
        callerComp.ActiveCallPartner = ent.Owner;

        // NC — Звук принятия вызова
        var holder = GetPdaHolderUid(ent);
        if (holder != null)
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/button.ogg"), holder.Value);

        // Обновляем UI звонящего
        UpdateUIForCartridge((callerUid, callerComp));
    }

    private void HandleDeclineCall(Entity<CitiNetCartridgeComponent> ent)
    {
        if (ent.Comp.CallState != CitiNetCallState.Incoming || ent.Comp.IncomingCaller == null)
            return;

        // Сбрасываем состояние у звонящего
        if (TryComp<CitiNetCartridgeComponent>(ent.Comp.IncomingCaller, out var callerComp))
        {
            callerComp.CallState = CitiNetCallState.None;
            callerComp.ActiveCallPartner = null;

            // NC — Звук сброса
            var callerHolder = GetPdaHolderUid((ent.Comp.IncomingCaller.Value, callerComp));
            if (callerHolder != null)
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/button.ogg"), callerHolder.Value);

            UpdateUIForCartridge((ent.Comp.IncomingCaller.Value, callerComp));
        }

        ent.Comp.CallState = CitiNetCallState.None;
        ent.Comp.ActiveCallPartner = null;
        ent.Comp.IncomingCaller = null;
    }

    private void HandleHangUp(Entity<CitiNetCartridgeComponent> ent)
    {
        // Используем ActiveCallPartner для поиска второй стороны, а не ActiveChatTarget
        var targetUid = ent.Comp.ActiveCallPartner ?? ent.Comp.IncomingCaller;
        if (targetUid != null && TryComp<CitiNetCartridgeComponent>(targetUid, out var targetComp))
        {
            if (targetComp.CallState != CitiNetCallState.None)
            {
                targetComp.CallState = CitiNetCallState.None;
                targetComp.ActiveCallPartner = null;
                targetComp.IncomingCaller = null;

                // NC — Звук завершения
                var targetHolder = GetPdaHolderUid((targetUid.Value, targetComp));
                if (targetHolder != null)
                    _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/button.ogg"), targetHolder.Value);

                UpdateUIForCartridge((targetUid.Value, targetComp));
            }
        }

        ent.Comp.CallState = CitiNetCallState.None;
        ent.Comp.ActiveCallPartner = null;
        ent.Comp.IncomingCaller = null;
        UpdateUIForCartridge(ent);
    }

    private void HandleSendCallMessage(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        if (ent.Comp.ActiveChatTarget == null || string.IsNullOrWhiteSpace(msg.Content))
            return;

        if (!HasActiveCitiNetRelay(ent))
            return;

        var content = msg.Content.Trim();
        if (content.Length > CitiNetCallMessage.MaxContentLength)
            content = content[..CitiNetCallMessage.MaxContentLength];

        var senderName = GetOwnerName(ent);
        var message = new CitiNetCallMessage(_timing.CurTime, senderName, content);

        var targetUid = ent.Comp.ActiveChatTarget.Value;

        // Добавляем сообщение обеим сторонам
        AddP2PMessage(ent, targetUid, message);

        if (TryComp<CitiNetCartridgeComponent>(targetUid, out var targetComp))
        {
            var targetEnt = new Entity<CitiNetCartridgeComponent>(targetUid, targetComp);
            AddP2PMessage(targetEnt, ent.Owner, message);
            DispatchCitiNetServerMessage(targetEnt, Loc.GetString("citinet-p2p-game-chat",
                ("sender", senderName),
                ("message", content)));

            // NC — Звук сообщения для получателя
            var targetHolder = GetPdaHolderUid(targetEnt);
            if (targetHolder != null)
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/chime.ogg"), targetHolder.Value);

            UpdateUIForCartridge(targetEnt);
        }

        // Обновляем UI отправителя
        DispatchCitiNetServerMessage(ent, Loc.GetString("citinet-p2p-game-chat",
            ("sender", senderName),
            ("message", content)));
        UpdateUIForCartridge(ent);
    }

    private void HandlePingLocation(Entity<CitiNetCartridgeComponent> ent)
    {
        if (ent.Comp.ActiveChatTarget == null)
            return;

        // Phase 2: получаем реальные координаты PDA через TransformSystem
        var coords = GetPdaCoordinates(ent);
        var senderName = GetOwnerName(ent);
        var coordText = coords != null
            ? $"X:{coords.Value.X:F0} Y:{coords.Value.Y:F0}"
            : "[N/A]";

        var sysMsg = new CitiNetCallMessage(_timing.CurTime, Loc.GetString("citinet-sender-system"),
            Loc.GetString("citinet-call-ping-location", ("sender", senderName), ("coords", coordText)), true);

        var targetUid = ent.Comp.ActiveChatTarget.Value;
        AddP2PMessage(ent, targetUid, sysMsg);

        if (TryComp<CitiNetCartridgeComponent>(targetUid, out var targetComp))
        {
            var targetEnt = new Entity<CitiNetCartridgeComponent>(targetUid, targetComp);
            AddP2PMessage(targetEnt, ent.Owner, sysMsg);

            UpdateUIForCartridge(targetEnt);
        }

        UpdateUIForCartridge(ent);
    }

    private void AddP2PMessage(Entity<CitiNetCartridgeComponent> ent, EntityUid target, CitiNetCallMessage message)
    {
        if (!ent.Comp.ChatHistories.ContainsKey(target))
            ent.Comp.ChatHistories[target] = new List<CitiNetCallMessage>();

        ent.Comp.ChatHistories[target].Add(message);

        if (ent.Comp.ChatHistories[target].Count > ent.Comp.MaxMessagesPerChat)
            ent.Comp.ChatHistories[target].RemoveAt(0);
    }

    // ========== Phase 2: FLATLINE ==========

    /// <summary>
    /// При смерти/крите участника группового звонка — рассылаем FLATLINE всем в группе.
    /// </summary>
    private void OnMobStateChanged(EntityUid uid, HandsComponent hands, ref MobStateChangedEvent args)
    {
        // Только крит или смерть
        if (args.NewMobState != MobState.Critical && args.NewMobState != MobState.Dead)
            return;

        // Находим CitiNet картридж этого моба (через PDA в руках или на нём)
        var cartridge = FindCartridgeByOwner(uid);
        if (cartridge == null || !cartridge.Value.Comp.InGroup)
            return;

        var ownerName = GetOwnerName(cartridge.Value);
        var stateText = args.NewMobState == MobState.Dead
            ? Loc.GetString("citinet-flatline-dead", ("name", ownerName))
            : Loc.GetString("citinet-flatline-critical", ("name", ownerName));

        var flatlineMsg = new CitiNetCallMessage(_timing.CurTime, Loc.GetString("citinet-sender-flatline"), stateText, true);

        // Добавляем в общую историю группы
        cartridge.Value.Comp.GroupMessages.Add(flatlineMsg);

        // Обновляем статус участника (IsAlive = false)
        UpdateUIForGroup(cartridge.Value.Comp.GroupMembers);
    }

    // ========== Групповые звонки ==========

    private void HandleCreateGroup(Entity<CitiNetCartridgeComponent> ent)
    {
        if (ent.Comp.InGroup || !HasActiveCitiNetRelay(ent))
            return;

        ent.Comp.InGroup = true;
        ent.Comp.GroupMembers.Clear();
        ent.Comp.GroupMembers.Add(ent.Owner);
        ent.Comp.GroupMessages.Clear();
        ent.Comp.ActiveTab = CitiNetTab.Group;

        UpdateUIForCartridge(ent);
    }

    private void HandleInviteToGroup(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        if (!ent.Comp.InGroup || msg.TargetId == null)
            return;

        if (ent.Comp.GroupMembers.Count >= ent.Comp.MaxGroupParticipants)
            return;

        var target = FindCartridgeByNumber(msg.TargetId);
        if (target == null || target.Value.Comp.InGroup)
            return;

        // Добавляем участника
        target.Value.Comp.InGroup = true;
        target.Value.Comp.GroupMembers = ent.Comp.GroupMembers; // Общая ссылка на группу
        ent.Comp.GroupMembers.Add(target.Value.Owner);
        target.Value.Comp.GroupMessages = ent.Comp.GroupMessages; // Общая история

        // Обновляем UI всех участников
        UpdateUIForGroup(ent.Comp.GroupMembers);
    }

    private void HandleLeaveGroup(Entity<CitiNetCartridgeComponent> ent)
    {
        if (!ent.Comp.InGroup)
            return;

        var members = ent.Comp.GroupMembers;
        members.Remove(ent.Owner);

        ent.Comp.InGroup = false;
        ent.Comp.GroupMembers = new HashSet<EntityUid>();
        ent.Comp.GroupMessages = new List<CitiNetCallMessage>();

        // Обновляем UI вышедшего игрока
        UpdateUIForCartridge(ent);

        // Если группа пуста, расформировываем
        if (members.Count <= 1)
        {
            foreach (var memberUid in members)
            {
                if (TryComp<CitiNetCartridgeComponent>(memberUid, out var memberComp))
                {
                    memberComp.InGroup = false;
                    memberComp.GroupMembers = new HashSet<EntityUid>();
                    memberComp.GroupMessages = new List<CitiNetCallMessage>();
                    UpdateUIForCartridge((memberUid, memberComp));
                }
            }
        }
        else
        {
            UpdateUIForGroup(members);
        }
    }

    private void HandleSendGroupMessage(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        if (!ent.Comp.InGroup || string.IsNullOrWhiteSpace(msg.Content))
            return;

        if (!HasActiveCitiNetRelay(ent))
            return;

        var content = msg.Content.Trim();
        if (content.Length > CitiNetCallMessage.MaxContentLength)
            content = content[..CitiNetCallMessage.MaxContentLength];

        var senderName = GetOwnerName(ent);
        var message = new CitiNetCallMessage(_timing.CurTime, senderName, content);

        // Добавляем в общую историю (shared reference)
        ent.Comp.GroupMessages.Add(message);

        // NC — Звук сообщения для всех участников (кроме отправителя)
        foreach (var memberUid in ent.Comp.GroupMembers)
        {
            if (!TryComp<CitiNetCartridgeComponent>(memberUid, out var memberComp))
                continue;

            var memberEnt = new Entity<CitiNetCartridgeComponent>(memberUid, memberComp);
            DispatchCitiNetServerMessage(memberEnt, Loc.GetString("citinet-group-game-chat",
                ("sender", senderName),
                ("message", content)));

            if (memberUid == ent.Owner)
                continue;

            var holder = GetPdaHolderUid(memberEnt);
            if (holder != null)
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/chime.ogg"), holder.Value);
        }

        // Обновляем UI всех участников
        UpdateUIForGroup(ent.Comp.GroupMembers);
    }

    private void HandleJoinGroupVoice(Entity<CitiNetCartridgeComponent> ent)
    {
        if (!ent.Comp.InGroup) return;
        ent.Comp.InGroupVoice = true;
        UpdateUIForGroup(ent.Comp.GroupMembers);
    }

    private void HandleLeaveGroupVoice(Entity<CitiNetCartridgeComponent> ent)
    {
        if (!ent.Comp.InGroup) return;
        ent.Comp.InGroupVoice = false;
        UpdateUIForGroup(ent.Comp.GroupMembers);
    }

    // ========== BBS-каналы ==========

    // ========== Экстренные вызовы ==========

    private void HandleCallPolice(Entity<CitiNetCartridgeComponent> ent)
    {
        // Проверяем cooldown
        var elapsed = _timing.CurTime - ent.Comp.LastPoliceCalled;
        if (ent.Comp.LastPoliceCalled != TimeSpan.Zero && elapsed.TotalSeconds < ent.Comp.EmergencyCooldownSeconds)
            return;

        if (!HasActiveCitiNetRelay(ent))
            return;

        ent.Comp.LastPoliceCalled = _timing.CurTime;

        var callerName = GetOwnerName(ent);
        var coords = GetPdaCoordinates(ent);

        // Определяем сектор через MapSectorComponent
        var sector = "Unknown Sector";
        if (TryComp<CartridgeComponent>(ent, out var cart) && cart.LoaderUid != null)
        {
            var loaderXform = Transform(cart.LoaderUid.Value);
            var loaderPos = _transform.GetWorldPosition(cart.LoaderUid.Value);
            var sectorQuery = EntityQueryEnumerator<MapSectorComponent>();
            while (sectorQuery.MoveNext(out _, out var sec))
            {
                if (sec.Bounds.Contains(loaderPos))
                {
                    sector = sec.SectorName;
                    break;
                }
            }
        }

        // Получаем сетевые координаты для метки на карте
        NetCoordinates netCoords = default;
        if (TryComp<CartridgeComponent>(ent, out var cartComp) && cartComp.LoaderUid != null)
            netCoords = GetNetCoordinates(Transform(cartComp.LoaderUid.Value).Coordinates);

        _overwatch.AddEntityAlert(ent.Owner, "CIVILIAN SOS", Loc.GetString("citinet-emergency-police-desc", ("caller", callerName)));
    }

    private void HandleCallTrauma(Entity<CitiNetCartridgeComponent> ent)
    {
        var elapsed = _timing.CurTime - ent.Comp.LastTraumaCalled;
        if (ent.Comp.LastTraumaCalled != TimeSpan.Zero && elapsed.TotalSeconds < ent.Comp.EmergencyCooldownSeconds)
            return;

        if (!HasActiveCitiNetRelay(ent))
            return;

        ent.Comp.LastTraumaCalled = _timing.CurTime;

        var callerName = GetOwnerName(ent);

        // Определяем сектор
        var sector = "Unknown Sector";
        if (TryComp<CartridgeComponent>(ent, out var cart) && cart.LoaderUid != null)
        {
            var loaderPos = _transform.GetWorldPosition(cart.LoaderUid.Value);
            var sectorQuery = EntityQueryEnumerator<MapSectorComponent>();
            while (sectorQuery.MoveNext(out _, out var sec))
            {
                if (sec.Bounds.Contains(loaderPos))
                {
                    sector = sec.SectorName;
                    break;
                }
            }
        }

        _overwatch.AddEntityAlert(ent.Owner, "TRAUMA SOS", Loc.GetString("citinet-emergency-trauma-desc", ("caller", callerName), ("sector", sector)));
    }

    // ========== BBS-каналы ==========

    private void HandleJoinChannel(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        if (msg.TargetId == null)
            return;

        if (!_prototype.TryIndex<CitiNetBBSChannelPrototype>(msg.TargetId, out var channel))
            return;

        // Проверка доступа по ID-карте (пропускаем если агент приглашён)
        var isInvited = ent.Comp.InvitedToChannels.Contains(msg.TargetId);
        if (!isInvited && channel.Access != null && channel.Access.Count > 0)
        {
            HashSet<string> pdaAccess = new();
            if (ent.Comp.LoaderUid != null && TryComp<PdaComponent>(ent.Comp.LoaderUid.Value, out var pda) && pda.ContainedId != null)
            {
                if (TryComp<AccessComponent>(pda.ContainedId.Value, out var accessComp))
                {
                    pdaAccess = accessComp.Tags.Select(t => (string) t).ToHashSet();
                }
            }

            bool hasAccess = false;
            foreach (var requiredTag in channel.Access)
            {
                if (pdaAccess.Contains(requiredTag))
                {
                    hasAccess = true;
                    break;
                }
            }

            if (!hasAccess)
                return; // У персонажа нет нужного доступа
        }

        // Проверяем пароль если нужен
        if (channel.RequiresPassword)
        {
            if (string.IsNullOrEmpty(msg.Content) || msg.Content != channel.Password)
                return; // Неверный пароль
        }

        ent.Comp.JoinedChannels.Add(msg.TargetId);

        // Always select it upon joining
        ent.Comp.CurrentChannel = msg.TargetId;

        ent.Comp.ActiveTab = CitiNetTab.BBS;
        ent.Comp.ActiveChatTarget = null;

        // Инициализируем кеш сообщений для канала
        if (!ent.Comp.ChannelMessages.ContainsKey(msg.TargetId))
            ent.Comp.ChannelMessages[msg.TargetId] = new List<CitiNetBBSMessage>();
    }

    private void HandleLeaveChannel(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        if (msg.TargetId == null)
            return;

        ent.Comp.JoinedChannels.Remove(msg.TargetId);
        ent.Comp.ChannelMessages.Remove(msg.TargetId);

        if (ent.Comp.CurrentChannel == msg.TargetId)
            ent.Comp.CurrentChannel = ent.Comp.JoinedChannels.FirstOrDefault();
    }

    private void HandleSelectChannel(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        if (msg.TargetId != null && _prototype.HasIndex<CitiNetBBSChannelPrototype>(msg.TargetId))
        {
            ent.Comp.CurrentChannel = msg.TargetId;
            ent.Comp.ActiveTab = CitiNetTab.BBS;
            ent.Comp.ActiveChatTarget = null;
        }
    }

    /// <summary>
    /// Приглашает агента в BBS-канал по номеру.
    /// Любой участник закрытого канала может приглашать новых агентов.
    /// TargetId = номер агента, Content = ID канала.
    /// </summary>
    private void HandleInviteToChannel(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        // msg.TargetId = номер агента
        if (msg.TargetId == null)
            return;

        var channelId = ent.Comp.CurrentChannel;
        if (channelId == null)
            return;

        // Проверяем что канал существует
        if (!_prototype.TryIndex<CitiNetBBSChannelPrototype>(channelId, out var channel))
            return;

        // Инвайтер должен быть участником канала
        if (!ent.Comp.JoinedChannels.Contains(channelId))
            return;

        // Ищем целевой картридж по номеру Агента
        var target = FindCartridgeByNumber(msg.TargetId);
        if (target == null)
            return;

        // Нельзя приглашать самого себя
        if (target.Value.Owner == ent.Owner)
            return;

        // Добавляем приглашение: канал теперь виден и доступен для входа
        target.Value.Comp.InvitedToChannels.Add(channelId);

        // Автоматически присоединяем и открываем канал у цели
        target.Value.Comp.JoinedChannels.Add(channelId);
        if (!target.Value.Comp.ChannelMessages.ContainsKey(channelId))
            target.Value.Comp.ChannelMessages[channelId] = new List<CitiNetBBSMessage>();

        target.Value.Comp.CurrentChannel = channelId;
        target.Value.Comp.ActiveTab = CitiNetTab.BBS;

        // Системное уведомление в чат канала для приглашённого
        var inviterName = GetOwnerName(ent);
        var sysMsg = new CitiNetBBSMessage(_timing.CurTime, Loc.GetString("citinet-sender-system"),
            Loc.GetString("citinet-bbs-invite-received", ("inviter", inviterName), ("channel", channel.LocalizedName)),
            channelId);
        target.Value.Comp.ChannelMessages[channelId].Add(sysMsg);

        UpdateUIForCartridge(target.Value);

        // Уведомляем инвайтера об успехе
        var targetName = GetOwnerName(target.Value);
        var confirmMsg = new CitiNetBBSMessage(_timing.CurTime, Loc.GetString("citinet-sender-system"),
            Loc.GetString("citinet-bbs-invite-sent", ("target", targetName), ("channel", channel.LocalizedName)),
            channelId);
        if (ent.Comp.ChannelMessages.ContainsKey(channelId))
            ent.Comp.ChannelMessages[channelId].Add(confirmMsg);
    }

    private void HandleSendBBSMessage(Entity<CitiNetCartridgeComponent> ent, CitiNetUiMessageEvent msg)
    {
        if (ent.Comp.CurrentChannel == null || string.IsNullOrWhiteSpace(msg.Content))
            return;

        if (!HasActiveCitiNetRelay(ent))
            return;

        if (!_prototype.TryIndex<CitiNetBBSChannelPrototype>(ent.Comp.CurrentChannel, out var channel))
            return;

        var content = msg.Content.Trim();
        if (content.Length > CitiNetBBSMessage.MaxContentLength)
            content = content[..CitiNetBBSMessage.MaxContentLength];

        var senderName = channel.IsAnonymous
            ? Loc.GetString("citinet-bbs-anonymous")
            : GetOwnerName(ent);

        var message = new CitiNetBBSMessage(_timing.CurTime, senderName, content, ent.Comp.CurrentChannel);

        // Рассылаем всем подключённым к этому каналу
        var query = EntityQueryEnumerator<CitiNetCartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.JoinedChannels.Contains(ent.Comp.CurrentChannel))
                continue;

            // Проверяем CitiNet Relay для получателя
            if (!HasActiveCitiNetRelay((uid, comp)))
                continue;

            if (!comp.ChannelMessages.ContainsKey(ent.Comp.CurrentChannel))
                comp.ChannelMessages[ent.Comp.CurrentChannel] = new List<CitiNetBBSMessage>();

            comp.ChannelMessages[ent.Comp.CurrentChannel].Add(message);

            // Обрезаем историю
            if (comp.ChannelMessages[ent.Comp.CurrentChannel].Count > comp.MaxMessagesPerChannel)
                comp.ChannelMessages[ent.Comp.CurrentChannel].RemoveAt(0);

            // Обновляем UI получателя
            UpdateUIForCartridge((uid, comp));
            DispatchCitiNetServerMessage((uid, comp), Loc.GetString("citinet-bbs-game-chat",
                ("channel", channel.LocalizedName),
                ("sender", senderName),
                ("message", content)));

            if (comp.CurrentChannel == ent.Comp.CurrentChannel)
            {
                var holder = GetPdaHolderUid((uid, comp));
                if (holder != null)
                {
                    _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/chime.ogg"), holder.Value);
                }
            }
        }
    }

    // ========== Утилиты ==========

    /// <summary>
    /// Проверяет наличие работающего CitiNet Relay на той же карте, что и PDA.
    /// </summary>
    private bool HasActiveCitiNetRelay(Entity<CitiNetCartridgeComponent> cartridge)
    {
        // Получаем PDA-загрузчик
        if (!TryComp<CartridgeComponent>(cartridge, out var cart) || cart.LoaderUid == null)
            return false;

        // Get actual map coordinates to account for containers (inventory, hands, etc)
        var mapCoords = _transform.GetMapCoordinates(cart.LoaderUid.Value);

        if (mapCoords.MapId == Robust.Shared.Map.MapId.Nullspace)
            return false;

        // Ищем активный CitiNet Relay на той же карте
        var query = EntityQueryEnumerator<CitiNetRelayComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var power, out var transform))
        {
            if (transform.MapID == mapCoords.MapId && power.Powered)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ищет картридж CitiNet по номеру Агента.
    /// </summary>
    private Entity<CitiNetCartridgeComponent>? FindCartridgeByNumber(string number)
    {
        var query = EntityQueryEnumerator<CitiNetCartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.AgentNumber == number)
                return (uid, comp);
        }
        return null;
    }

    /// <summary>
    /// Генерирует уникальный 4-значный номер Агента.
    /// </summary>
    private string GenerateAgentNumber()
    {
        // Собираем занятые номера
        var usedNumbers = new HashSet<string>();
        var query = EntityQueryEnumerator<CitiNetCartridgeComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (!string.IsNullOrEmpty(comp.AgentNumber))
                usedNumbers.Add(comp.AgentNumber);
        }

        // Генерируем уникальный
        string number;
        do
        {
            number = _random.Next(1000, 9999).ToString();
        } while (usedNumbers.Contains(number));

        return number;
    }

    /// <summary>
    /// Получает имя владельца PDA.
    /// </summary>
    private string GetOwnerName(Entity<CitiNetCartridgeComponent> ent)
    {
        if (!TryComp<CartridgeComponent>(ent, out var cart) || cart.LoaderUid == null)
            return ent.Comp.AgentNumber;

        if (TryComp<PdaComponent>(cart.LoaderUid, out var pda) && !string.IsNullOrEmpty(pda.OwnerName))
            return pda.OwnerName;

        return ent.Comp.AgentNumber;
    }

    /// <summary>
    /// Phase 2: Получает EntityUid моба, держащего PDA с этим картриджем.
    /// </summary>
    private EntityUid? GetPdaHolderUid(Entity<CitiNetCartridgeComponent> ent)
    {
        if (!TryComp<CartridgeComponent>(ent, out var cart) || cart.LoaderUid == null)
            return null;

        var xform = Transform(cart.LoaderUid.Value);
        return xform.ParentUid;
    }

    /// <summary>
    /// Phase 2: Получает координаты PDA на карте.
    /// </summary>
    private Vector2? GetPdaCoordinates(Entity<CitiNetCartridgeComponent> ent)
    {
        if (!TryComp<CartridgeComponent>(ent, out var cart) || cart.LoaderUid == null)
            return null;

        var worldPos = _transform.GetWorldPosition(cart.LoaderUid.Value);
        return worldPos;
    }

    private string GetRoleName(EntityUid actorUid)
    {
        if (_mindSystem.TryGetMind(actorUid, out var mindId, out _) &&
            _jobSystem.MindTryGetJobName(mindId, out var roleName))
        {
            return roleName;
        }

        return Loc.GetString("generic-unknown-title");
    }

    /// <summary>
    /// NC: отправляет серверное CitiNet-уведомление владельцу PDA, если у него есть активная сессия.
    /// </summary>
    private void DispatchCitiNetServerMessage(Entity<CitiNetCartridgeComponent> ent, string message)
    {
        var holder = GetPdaHolderUid(ent);
        if (holder == null || !_playerManager.TryGetSessionByEntity(holder.Value, out var session))
            return;

        _chatManager.DispatchServerMessage(session, message);
    }

    /// <summary>
    /// Phase 2: Ищет картридж CitiNet по владельцу (EntityUid моба).
    /// Проверяет PDA через HandsComponent → перебираем все PDA в руках.
    /// </summary>
    private Entity<CitiNetCartridgeComponent>? FindCartridgeByOwner(EntityUid ownerUid)
    {
        if (!TryComp<HandsComponent>(ownerUid, out var hands))
            return null;

        // Перебираем все предметы в руках
        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity == null)
                continue;

            // Проверяем является ли предмет PDA с CartridgeLoader
            if (!TryComp<CartridgeLoaderComponent>(hand.HeldEntity, out var loader))
                continue;

            // Ищем CitiNet картридж среди активных/фоновых программ
            if (loader.ActiveProgram != null && TryComp<CitiNetCartridgeComponent>(loader.ActiveProgram, out var activeCitinet))
                return (loader.ActiveProgram.Value, activeCitinet);

            foreach (var programUid in loader.BackgroundPrograms)
            {
                if (TryComp<CitiNetCartridgeComponent>(programUid, out var citinet))
                    return (programUid, citinet);
            }
        }

        // Phase 2b: Ищем PDA в инвентаре (карманы/пояс) если нет в руках
        if (TryComp<Content.Shared.Inventory.InventoryComponent>(ownerUid, out var inv))
        {
            var invSystem = EntityManager.System<Content.Shared.Inventory.InventorySystem>();
            if (invSystem.TryGetSlotEntity(ownerUid, "id", out var idPda))
            {
                if (TryComp<CartridgeLoaderComponent>(idPda, out var loader))
                {
                    if (loader.ActiveProgram != null && TryComp<CitiNetCartridgeComponent>(loader.ActiveProgram, out var activeCitinet))
                        return (loader.ActiveProgram.Value, activeCitinet);

                    foreach (var programUid in loader.BackgroundPrograms)
                    {
                        if (TryComp<CitiNetCartridgeComponent>(programUid, out var citinet))
                            return (programUid, citinet);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Перехват локальной речи для ретрансляции через открытые звонки (Voice Relay).
    /// </summary>
    private void OnSpeak(EntitySpokeEvent args)
    {
        if (args.Channel != null || args.IsWhisper || string.IsNullOrWhiteSpace(args.Message))
            return;

        // Только живые игроки
        if (!HasComp<MobStateComponent>(args.Source))
            return;

        var cartTuple = FindCartridgeByOwner(args.Source);
        if (cartTuple == null)
            return;

        var ent = cartTuple.Value;
        if (!HasActiveCitiNetRelay(ent))
            return;

        var message = args.Message;
        var sourceName = GetOwnerName(ent);

        // P2P звонок — используем ActiveCallPartner вместо ActiveChatTarget
        if (ent.Comp.CallState == CitiNetCallState.Active && ent.Comp.ActiveCallPartner != null)
        {
            var targetUid = ent.Comp.ActiveCallPartner.Value;
            if (TryComp<CitiNetCartridgeComponent>(targetUid, out var targetComp))
            {
                var target = new Entity<CitiNetCartridgeComponent>(targetUid, targetComp);
                if (HasActiveCitiNetRelay(target))
                {
                    if (TryComp<CartridgeComponent>(target.Owner, out var targetCart) && targetCart.LoaderUid != null)
                    {
                        _chat.TrySendInGameICMessage(targetCart.LoaderUid.Value, message, InGameICChatType.Speak, ChatTransmitRange.Normal, nameOverride: sourceName, checkRadioPrefix: false, ignoreActionBlocker: true, languageOverride: args.Language);
                    }
                }
            }
        }
        // Групповой звонок
        else if (ent.Comp.InGroup && ent.Comp.InGroupVoice)
        {
            foreach (var memberUid in ent.Comp.GroupMembers)
            {
                if (memberUid == ent.Owner)
                    continue;

                if (TryComp<CartridgeComponent>(memberUid, out var targetCart) && targetCart.LoaderUid != null)
                {
                    if (TryComp<CitiNetCartridgeComponent>(memberUid, out var memberComp) && memberComp.InGroupVoice && HasActiveCitiNetRelay((memberUid, memberComp)))
                    {
                        _chat.TrySendInGameICMessage(targetCart.LoaderUid.Value, message, InGameICChatType.Speak, ChatTransmitRange.Normal, nameOverride: sourceName, checkRadioPrefix: false, ignoreActionBlocker: true, languageOverride: args.Language);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Обновляет UI для конкретного картриджа.
    /// </summary>
    private void UpdateUIForCartridge(Entity<CitiNetCartridgeComponent> ent)
    {
        if (!TryComp<CartridgeComponent>(ent, out var cart) || cart.LoaderUid == null)
            return;

        UpdateUI(ent, cart.LoaderUid.Value);
    }

    /// <summary>
    /// Обновляет UI для всех участников группы.
    /// </summary>
    private void UpdateUIForGroup(HashSet<EntityUid> members)
    {
        foreach (var memberUid in members)
        {
            if (TryComp<CitiNetCartridgeComponent>(memberUid, out var comp))
                UpdateUIForCartridge((memberUid, comp));
        }
    }

    /// <summary>
    /// Собирает и отправляет полное состояние UI.
    /// </summary>
    private void UpdateUI(Entity<CitiNetCartridgeComponent> ent, EntityUid loader)
    {
        var hasRelay = HasActiveCitiNetRelay(ent);

        // Собираем список контактов
        var contacts = new List<CitiNetContact>();
        foreach (var uid in ent.Comp.ChatHistories.Keys)
        {
            if (TryComp<CitiNetCartridgeComponent>(uid, out var contactComp))
            {
                var name = GetOwnerName((uid, contactComp));
                contacts.Add(new CitiNetContact(contactComp.AgentNumber, name));
            }
        }

        string? currentContactNumber = null;
        var currentMessages = new List<CitiNetCallMessage>();

        // Определяем текущий контакт для отображения в UI:
        // Приоритет: открытый чат > входящий звонок > партнёр активного звонка
        var displayTarget = ent.Comp.ActiveChatTarget
                            ?? ent.Comp.IncomingCaller
                            ?? ent.Comp.ActiveCallPartner;

        if (displayTarget != null && TryComp<CitiNetCartridgeComponent>(displayTarget, out var displayComp))
        {
            currentContactNumber = displayComp.AgentNumber;
            if (ent.Comp.ChatHistories.TryGetValue(displayTarget.Value, out var p2pMsgs))
                currentMessages = p2pMsgs;
        }

        // Участники группы + Phase 2: проверяем IsAlive через MobState
        var groupParticipants = new List<CitiNetGroupParticipant>();
        foreach (var memberUid in ent.Comp.GroupMembers)
        {
            if (!TryComp<CitiNetCartridgeComponent>(memberUid, out var memberComp))
                continue;

            var name = GetOwnerName((memberUid, memberComp));
            var isAlive = true;

            // Проверяем MobState владельца PDA
            var memberOwner = GetPdaHolderUid((memberUid, memberComp));
            if (memberOwner != null && TryComp<MobStateComponent>(memberOwner, out var mobState))
                isAlive = mobState.CurrentState == MobState.Alive;

            groupParticipants.Add(new CitiNetGroupParticipant(name, isAlive));
        }

        // Собираем доступы (теги) из ID-карты в PDA
        HashSet<string> pdaAccess = new();
        if (ent.Comp.LoaderUid != null && TryComp<PdaComponent>(ent.Comp.LoaderUid.Value, out var pda) && pda.ContainedId != null)
        {
            if (TryComp<AccessComponent>(pda.ContainedId.Value, out var accessComp))
            {
                pdaAccess = accessComp.Tags.Select(t => (string) t).ToHashSet();
            }
        }

        // Список BBS-каналов (только публичные, по доступу, или те, к которым мы уже присоединились)
        var channels = new List<CitiNetChannelInfo>();
        foreach (var proto in _prototype.EnumeratePrototypes<CitiNetBBSChannelPrototype>())
        {
            var isJoined = ent.Comp.JoinedChannels.Contains(proto.ID);

            // Проверка доступа по ID-тегам
            bool hasNativeAccess = true;
            if (proto.Access != null && proto.Access.Count > 0)
            {
                hasNativeAccess = false;
                foreach (var requiredTag in proto.Access)
                {
                    if (pdaAccess.Contains(requiredTag))
                    {
                        hasNativeAccess = true;
                        break;
                    }
                }
            }

            // Проверяем приглашение: агент видит канал если есть нативный доступ ИЛИ приглашение
            var isInvited = ent.Comp.InvitedToChannels.Contains(proto.ID);
            if (!hasNativeAccess && !isInvited)
                continue;

            // Показываем только если мы присоединены, либо если канал не скрыт
            if (isJoined || !proto.IsHidden)
            {
                channels.Add(new CitiNetChannelInfo(
                    proto.ID,
                    proto.LocalizedName,
                    proto.Color,
                    proto.RequiresPassword,
                    isJoined,
                    isJoined && proto.Access != null && proto.Access.Count > 0));  // Можно приглашать в любом закрытом канале где ты участник
            }
        }

        // Сообщения текущего BBS-канала
        var channelMessages = new List<CitiNetBBSMessage>();
        if (ent.Comp.CurrentChannel != null &&
            ent.Comp.ChannelMessages.TryGetValue(ent.Comp.CurrentChannel, out var msgs))
        {
            channelMessages = msgs;
        }

        // Глобальный справочник всех активных агентов (CitiNet картриджи)
        var globalDirectory = new List<CitiNetContact>();
        var agentQuery = EntityQueryEnumerator<CitiNetCartridgeComponent>();
        while (agentQuery.MoveNext(out var agUid, out var agComp))
        {
            if (string.IsNullOrEmpty(agComp.AgentNumber))
                continue;

            var agentName = GetOwnerName((agUid, agComp));
            globalDirectory.Add(new CitiNetContact(agComp.AgentNumber, agentName));
        }

        // Все подключённые игроки (для вкладки Contacts) — через ActorComponent
        var allPlayers = new List<CitiNetContact>();
        var actorQuery = EntityQueryEnumerator<ActorComponent, MetaDataComponent>();
        while (actorQuery.MoveNext(out var actorUid, out var actor, out var meta))
        {
            // Ищем CitiNet номер этого игрока, если есть
            var citiNumber = "N/A";
            var citiCart = FindCartridgeByOwner(actorUid);
            if (citiCart != null && !string.IsNullOrEmpty(citiCart.Value.Comp.AgentNumber))
                citiNumber = citiCart.Value.Comp.AgentNumber;

            var roleName = GetRoleName(actorUid);
            allPlayers.Add(new CitiNetContact(citiNumber, $"{meta.EntityName} ({roleName})"));
        }

        // Рассчитываем оставшийся cooldown для кнопок экстренных вызовов
        var now = _timing.CurTime;
        var policeCd = ent.Comp.LastPoliceCalled == TimeSpan.Zero
            ? 0f
            : (float) Math.Max(0, ent.Comp.EmergencyCooldownSeconds - (now - ent.Comp.LastPoliceCalled).TotalSeconds);
        var traumaCd = ent.Comp.LastTraumaCalled == TimeSpan.Zero
            ? 0f
            : (float) Math.Max(0, ent.Comp.EmergencyCooldownSeconds - (now - ent.Comp.LastTraumaCalled).TotalSeconds);

        var state = new CitiNetUiState(
            ent.Comp.AgentNumber,
            hasRelay,
            ent.Comp.ActiveTab,
            contacts,
            currentContactNumber,
            ent.Comp.CallState,
            currentMessages,
            ent.Comp.InGroup,
            ent.Comp.InGroupVoice,
            groupParticipants,
            ent.Comp.MaxGroupParticipants,
            ent.Comp.GroupMessages,
            channels,
            ent.Comp.CurrentChannel,
            channelMessages,
            globalDirectory,
            allPlayers,
            policeCd,
            traumaCd);

        _cartridge.UpdateCartridgeUiState(loader, state);
    }

    // ========== Fix 3: Принудительный разрыв звонка при потере Relay ==========

    /// <summary>
    /// Разрывает активный/входящий/исходящий звонок с системным уведомлением.
    /// Вызывается при потере CitiNet Relay.
    /// </summary>
    private void ForceDisconnectCall(Entity<CitiNetCartridgeComponent> ent)
    {
        var partnerUid = ent.Comp.ActiveCallPartner ?? ent.Comp.IncomingCaller;

        // Уведомляем вторую сторону
        if (partnerUid != null && TryComp<CitiNetCartridgeComponent>(partnerUid, out var partnerComp))
        {
            if (partnerComp.CallState != CitiNetCallState.None)
            {
                var disconnectMsg = new CitiNetCallMessage(_timing.CurTime, Loc.GetString("citinet-sender-system"),
                    Loc.GetString("citinet-call-connection-lost"), true);
                AddP2PMessage((partnerUid.Value, partnerComp), ent.Owner, disconnectMsg);

                partnerComp.CallState = CitiNetCallState.None;
                partnerComp.ActiveCallPartner = null;
                partnerComp.IncomingCaller = null;
                UpdateUIForCartridge((partnerUid.Value, partnerComp));
            }
        }

        // Уведомляем себя
        if (partnerUid != null)
        {
            var selfMsg = new CitiNetCallMessage(_timing.CurTime, Loc.GetString("citinet-sender-system"),
                Loc.GetString("citinet-call-connection-lost"), true);
            AddP2PMessage(ent, partnerUid.Value, selfMsg);
        }

        ent.Comp.CallState = CitiNetCallState.None;
        ent.Comp.ActiveCallPartner = null;
        ent.Comp.IncomingCaller = null;
        UpdateUIForCartridge(ent);
    }

}
