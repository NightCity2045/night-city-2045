using System.Threading;
using System.Threading.Tasks;
using Content.Server._White.Hearing;
using Content.Server._NC.Chat.Translation;
using Content.Server._NC.Localization;
using Content.Shared._NC.Chat.Translation;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Language;
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Language;
using Content.Shared.Language.Systems;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Microsoft.CodeAnalysis.Host;
using Content.Shared.Ghost; // Nuclear-14
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

/// <summary>
///     This system handles intrinsic radios and the general process of converting radio messages into chat messages.
/// </summary>
public sealed class RadioSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly IReplayRecordingManager _replay = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly HearingSystem _hearing = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly INCChatTranslationService _ncChatTranslation = default!;
    [Dependency] private readonly NCPlayerCultureTracker _ncPlayerCulture = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly ITaskManager _ncTaskManager = default!;

    // set used to prevent radio feedback loops.
    private readonly HashSet<string> _messages = new();

    private EntityQuery<TelecomExemptComponent> _exemptQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IntrinsicRadioReceiverComponent, RadioReceiveEvent>(OnIntrinsicReceive);
        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EntitySpokeEvent>(OnIntrinsicSpeak);

        _exemptQuery = GetEntityQuery<TelecomExemptComponent>();
    }

    private void OnIntrinsicSpeak(EntityUid uid, IntrinsicRadioTransmitterComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null && component.Channels.Contains(args.Channel.ID))
        {
            SendRadioMessage(uid, args.Message, args.Channel, uid, args.Language);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    //Nuclear-14
    /// <summary>
    /// Gets the message frequency, if there is no such frequency, returns the standard channel frequency.
    /// </summary>
    public int GetFrequency(EntityUid source, RadioChannelPrototype channel)
    {
        if (TryComp<RadioMicrophoneComponent>(source, out var radioMicrophone))
            return radioMicrophone.Frequency;

        return channel.Frequency;
    }

    private void OnIntrinsicReceive(EntityUid uid, IntrinsicRadioReceiverComponent component, ref RadioReceiveEvent args)
    {
        if (TryComp(uid, out ActorComponent? actor))
        {
            // WWDP Deafening
            if (_hearing.IsBlockedByDeafness(actor.PlayerSession, ChatChannel.Radio, args.Language))
                return;
            // WWDP end

            // Einstein-Engines - languages mechanic
            var listener = component.Owner;
            var msg = args.OriginalChatMsg;

            if (listener != null && !_language.CanUnderstand(listener, args.Language.ID))
                msg = args.LanguageObfuscatedChatMsg;
            else if (args.OriginalChatMsg.ServerMessageId != null)
            {
                // Placeholder messages are updated asynchronously by the sender-side radio flow.
            }

            _netMan.ServerSendMessage(new MsgChatMessage { Message = msg}, actor.PlayerSession.Channel);
        }
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    public void SendRadioMessage(
        EntityUid messageSource,
        string message,
        ProtoId<RadioChannelPrototype> channel,
        EntityUid radioSource,
        int? frequency = null,
        LanguagePrototype? language = null,
        bool escapeMarkup = false // WD edit
        ) =>
        SendRadioMessage(messageSource, message, _prototype.Index(channel), radioSource, escapeMarkup: escapeMarkup, frequency: frequency, language: language);

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    public void SendRadioMessage(
        EntityUid messageSource,
        string message,
        RadioChannelPrototype channel,
        EntityUid radioSource,
        LanguagePrototype? language = null,
        int? frequency = null,
        bool escapeMarkup = false) // WD edit
    {
        if (language == null)
            language = _language.GetLanguage(messageSource);

        if (!language.SpeechOverride.AllowRadio)
            return;

        // TODO if radios ever garble / modify messages, feedback-prevention needs to be handled better than this.
        if (!_messages.Add(message))
            return;

        var evt = new TransformSpeakerNameEvent(messageSource, Name(messageSource), channel); // WD EDIT
        RaiseLocalEvent(messageSource, evt);

        var name = evt.VoiceName;
        name = FormattedMessage.EscapeText(name);

        // most radios are relayed to chat, so lets parse the chat message beforehand
        var content = escapeMarkup
            ? FormattedMessage.EscapeText(message)
            : message;

        Task<NCChatTranslationPayload?>? pendingTranslation = null;
        uint? serverMessageId = null;
        var fallbackLanguage = _ncPlayerCulture.ResolveLanguageCode(messageSource);
        var sourceLanguage = NCChatTranslationMarkup.ResolveLanguageFromText(content, fallbackLanguage);

        if (_ncChatTranslation.IsConfiguredForChannel(ChatChannel.Radio) &&
            NCChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
        {
            serverMessageId = _ncChatTranslation.AllocateMessageId();
            pendingTranslation = _ncChatTranslation.TranslateAsync(content, fallbackLanguage, ChatChannel.Radio);
        }

        var wrappedMessage = WrapRadioMessage(messageSource, channel, name, content, evt, language, frequency);
        var msg = new ChatMessage(ChatChannel.Radio, content, wrappedMessage, NetEntity.Invalid, null, serverMessageId: serverMessageId);

        // ... you guess it
        var obfuscated = _language.ObfuscateSpeech(content, language);
        var obfuscatedWrapped = WrapRadioMessage(messageSource, channel, name, obfuscated, evt, language, frequency);
        var notUdsMsg = new ChatMessage(ChatChannel.Radio, obfuscated, obfuscatedWrapped, NetEntity.Invalid, null, serverMessageId: serverMessageId);

        var ev = new RadioReceiveEvent(messageSource, channel, msg, notUdsMsg, language, radioSource);

        var sendAttemptEv = new RadioSendAttemptEvent(channel, radioSource);
        RaiseLocalEvent(ref sendAttemptEv);
        RaiseLocalEvent(radioSource, ref sendAttemptEv);
        var canSend = !sendAttemptEv.Cancelled;

        var sourceMapId = Transform(radioSource).MapID;
        var hasActiveServer = HasActiveServer(sourceMapId, channel.ID);
        var hasMicro = HasComp<RadioMicrophoneComponent>(radioSource);

        var speakerQuery = GetEntityQuery<RadioSpeakerComponent>();
        var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();

        if (frequency == null) // Nuclear-14
            frequency = GetFrequency(messageSource, channel); // Nuclear-14

        while (canSend && radioQuery.MoveNext(out var receiver, out var radio, out var transform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                             !intercom.SupportedChannels.Contains(channel.ID)))
                    continue;
            }

            if (!HasComp<GhostComponent>(receiver) && GetFrequency(receiver, channel) != frequency)
                continue;

            if (!channel.LongRange && transform.MapID != sourceMapId && !radio.GlobalReceive)
                continue;

            // don't need telecom server for long range channels or handheld radios and intercoms
            var needServer = !channel.LongRange && (!hasMicro || !speakerQuery.HasComponent(receiver));
            if (needServer && !hasActiveServer)
                continue;

            // check if message can be sent to specific receiver
            var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
            RaiseLocalEvent(ref attemptEv);
            RaiseLocalEvent(receiver, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            // send the message
            RaiseLocalEvent(receiver, ref ev);
        }

        if (name != Name(messageSource))
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} as {name} on {channel.LocalizedName}: {message}");
        else
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} on {channel.LocalizedName}: {message}");

        _replay.RecordServerMessage(msg);

        if (pendingTranslation != null && serverMessageId is { } pendingMessageId)
        {
            ObserveTranslationTask(DispatchDelayedRadioUpdateAsync(
                pendingMessageId,
                messageSource,
                content,
                channel,
                radioSource,
                language,
                frequency,
                pendingTranslation));
        }

        _messages.Remove(message);
    }

    private string WrapRadioMessage(
        EntityUid source,
        RadioChannelPrototype channel,
        string name,
        string message,
        TransformSpeakerNameEvent transformSpeakerName,
        LanguagePrototype language,
        int? frequency = null)
    {
        // TODO: code duplication with ChatSystem.WrapMessage
        SpeechVerbPrototype speech;
        if (transformSpeakerName.SpeechVerb != null && _prototype.TryIndex(transformSpeakerName.SpeechVerb, out var evntProto))
            speech = evntProto;
        else
            speech = _chat.GetSpeechVerb(source, message);

        var languageColor = channel.Color;
        if (language.SpeechOverride.Color is { } colorOverride)
            languageColor = Color.InterpolateBetween(languageColor, colorOverride, colorOverride.A);
        var languageDisplay = language.IsVisibleLanguage
            ? Loc.GetString("chat-manager-language-prefix", ("language", language.ChatName))
            : "";
        var messageColor = language.IsVisibleLanguage ? languageColor : channel.Color;

        string channelText;
        if (channel.ShowFrequency && frequency.HasValue)
            channelText = $"\\[{frequency}\\]";
        else
            channelText = $"\\[{channel.LocalizedName}\\]";

        return Loc.GetString(speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap",
            ("color", channel.Color),
            ("languageColor", languageColor),
            ("messageColor", messageColor),
            ("fontType", language.SpeechOverride.FontId ?? speech.FontId),
            ("fontSize", language.SpeechOverride.FontSize ?? speech.FontSize),
            ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
            ("channel", channelText),
            ("name", name),
            ("message", message),
            ("language", languageDisplay));
    }

    private void ObserveTranslationTask(Task task)
    {
        _ = ObserveTranslationTaskAsync(task);
    }

    private async Task ObserveTranslationTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            Logger.Error($"NC radio translation task failed: {e}");
        }
    }

    private async Task DispatchDelayedRadioUpdateAsync(
        uint serverMessageId,
        EntityUid messageSource,
        string message,
        RadioChannelPrototype channel,
        EntityUid radioSource,
        LanguagePrototype language,
        int? frequency,
        Task<NCChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunTranslationOnMainThreadAsync(() =>
        {
            var sourceMapId = Transform(radioSource).MapID;
            var hasActiveServer = HasActiveServer(sourceMapId, channel.ID);
            var hasMicro = HasComp<RadioMicrophoneComponent>(radioSource);
            var speakerQuery = GetEntityQuery<RadioSpeakerComponent>();
            var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();

            while (radioQuery.MoveNext(out var receiver, out var radio, out var transform))
            {
                if (!radio.ReceiveAllChannels)
                {
                    if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                                 !intercom.SupportedChannels.Contains(channel.ID)))
                        continue;
                }

                if (!HasComp<GhostComponent>(receiver) && GetFrequency(receiver, channel) != frequency)
                    continue;

                if (!channel.LongRange && transform.MapID != sourceMapId && !radio.GlobalReceive)
                    continue;

                var needServer = !channel.LongRange && (!hasMicro || !speakerQuery.HasComponent(receiver));
                if (needServer && !hasActiveServer)
                    continue;

                var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
                RaiseLocalEvent(ref attemptEv);
                RaiseLocalEvent(receiver, ref attemptEv);
                if (attemptEv.Cancelled)
                    continue;

                if (!TryComp(receiver, out ActorComponent? actor))
                    continue;

                var session = actor.PlayerSession;
                var listener = receiver;

                if (_hearing.IsBlockedByDeafness(session, ChatChannel.Radio, language))
                    continue;

                if (!_language.CanUnderstand(listener, language.ID))
                    continue;

                if (!_ncPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var evt = new TransformSpeakerNameEvent(messageSource, Name(messageSource), channel);
                RaiseLocalEvent(messageSource, evt);

                var preserveOriginal = TryComp<ActorComponent>(messageSource, out var sourceActor) &&
                                       sourceActor.PlayerSession.UserId == session.UserId;

                var visibleText = NCChatTranslationFormatting.ResolveVisibleText(
                    translation,
                    message,
                    translation.SourceLanguage,
                    recipientLanguage,
                    preserveOriginal);

                var wrappedMessage = NCChatTranslationFormatting.PrefixWithLanguageTag(
                    WrapRadioMessage(
                        messageSource,
                        channel,
                        FormattedMessage.EscapeText(evt.VoiceName),
                        visibleText,
                        evt,
                        language,
                        frequency),
                    recipientLanguage,
                    translation.SourceLanguage,
                    NCChatTranslationFormatting.ResolveOriginalTextForTag(
                        translation,
                        message,
                        translation.SourceLanguage,
                        recipientLanguage,
                        preserveOriginal));

                var updated = new ChatMessage(ChatChannel.Radio, visibleText, wrappedMessage, NetEntity.Invalid, null, serverMessageId: serverMessageId);
                _netMan.ServerSendMessage(new MsgUpdateChatMessage { Message = updated }, session.Channel);
            }
        });
    }

    private Task RunTranslationOnMainThreadAsync(Action action)
    {
        if (SynchronizationContext.Current?.GetType().FullName ==
            "Robust.Shared.Asynchronous.RobustSynchronizationContext")
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        _ncTaskManager.RunOnMainThread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        return tcs.Task;
    }

    /// <inheritdoc cref="TelecomServerComponent"/>
    private bool HasActiveServer(MapId mapId, string channelId)
    {
        var servers = EntityQuery<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        foreach (var (_, keys, power, transform) in servers)
        {
            if (transform.MapID == mapId &&
                power.Powered &&
                keys.Channels.Contains(channelId))
            {
                return true;
            }
        }
        return false;
    }
}
