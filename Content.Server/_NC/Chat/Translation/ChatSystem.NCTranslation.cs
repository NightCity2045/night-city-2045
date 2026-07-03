using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._NC.Localization;
using Content.Server._NC.Chat.Translation;
using Content.Server.Language;
using Content.Shared._NC.Chat.Translation;
using Content.Shared.Chat;
using Content.Shared.Language;
using Content.Shared.Radio;
using Content.Shared.Speech;
using Content.Shared.CCVar;
using Robust.Shared.Asynchronous;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    [Dependency] private readonly INCChatTranslationService _ncChatTranslation = default!;
    [Dependency] private readonly NCPlayerCultureTracker _ncPlayerCulture = default!;
    [Dependency] private readonly ITaskManager _ncTaskManager = default!;

    private bool TryDispatchTranslatedEntitySpeak(
        EntityUid source,
        string message,
        string wrappedMessage,
        SpeechVerbPrototype speech,
        string escapedName,
        ChatTransmitRange range,
        LanguagePrototype language)
    {
        if (!_ncChatTranslation.IsConfiguredForChannel(ChatChannel.Local))
            return false;

        var fallbackLanguage = _ncPlayerCulture.ResolveLanguageCode(source);
        var sourceLanguage = NCChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!NCChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
            return false;

        ObserveTranslationTask(DispatchTranslatedEntitySpeakAsync(
            source,
            message,
            wrappedMessage,
            speech,
            escapedName,
            range,
            language,
            fallbackLanguage,
            sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedEntitySpeakAsync(
        EntityUid source,
        string message,
        string wrappedMessage,
        SpeechVerbPrototype speech,
        string escapedName,
        ChatTransmitRange range,
        LanguagePrototype language,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _ncChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.Local);
        await RunTranslationOnMainThreadAsync(() =>
        {
            if (!CanDispatchFromSource(source))
                return;

            var translation = translationDispatch.ImmediateTranslation;
            uint? serverMessageId = null;
            if (translation == null && translationDispatch.PendingTranslation != null)
            {
                translation = NCChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                serverMessageId = _ncChatTranslation.AllocateMessageId();
            }

            var obfuscated = SanitizeInGameICMessage(source, _language.ObfuscateSpeech(message, language), out _, true, _configurationManager.GetCVar(CCVars.ChatPunctuation), (!CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Parent.Name == "en") || (CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Name == "en"));
            var wrappedObfuscated = WrapPublicMessage(source, escapedName, obfuscated, speech, language: language);
            var initialTranslation = translation;

            SendInVoiceRange(
                ChatChannel.Local,
                escapedName,
                message,
                wrappedMessage,
                obfuscated,
                wrappedObfuscated,
                source,
                range,
                languageOverride: language,
                wrapForListener: (listener, content) =>
                {
                    var displayName = FormattedMessage.EscapeText(_ncCharacterNotes.GetDisplayNameForViewer(source, listener, escapedName));
                    if (initialTranslation != null &&
                        content == message &&
                        TryComp<ActorComponent>(listener, out var actor) &&
                        _ncPlayerCulture.TryResolveChatLanguageCode(actor.PlayerSession, out var recipientLanguage))
                    {
                        var preserveOriginal = IsSourceAuthorSession(source, actor.PlayerSession);
                        var visibleContent = NCChatTranslationFormatting.ResolveVisibleText(
                            initialTranslation,
                            message,
                            initialTranslation.SourceLanguage,
                            recipientLanguage,
                            preserveOriginal);

                        return NCChatTranslationFormatting.PrefixWithLanguageTag(
                            WrapPublicMessage(source, displayName, visibleContent, speech, language: language),
                            recipientLanguage,
                            initialTranslation.SourceLanguage,
                            NCChatTranslationFormatting.ResolveOriginalTextForTag(
                                initialTranslation,
                                message,
                                initialTranslation.SourceLanguage,
                                recipientLanguage,
                                preserveOriginal));
                    }

                    return WrapPublicMessage(source, displayName, content, speech, language: language);
                },
                serverMessageId: serverMessageId);

            if (translationDispatch.PendingTranslation != null && serverMessageId is { } pendingMessageId)
            {
                ObserveTranslationTask(DispatchDelayedEntitySpeakUpdateAsync(
                    pendingMessageId,
                    source,
                    message,
                    speech,
                    escapedName,
                    range,
                    language,
                    translationDispatch.PendingTranslation));
            }
        });
    }

    private bool TryDispatchTranslatedEntityWhisper(
        EntityUid source,
        string message,
        ChatTransmitRange range,
        string rawName,
        string rawIdentityName,
        SpeechVerbPrototype speech,
        LanguagePrototype language)
    {
        if (!_ncChatTranslation.IsConfiguredForChannel(ChatChannel.Whisper))
            return false;

        var fallbackLanguage = _ncPlayerCulture.ResolveLanguageCode(source);
        var sourceLanguage = NCChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!NCChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
            return false;

        ObserveTranslationTask(DispatchTranslatedEntityWhisperAsync(
            source,
            message,
            range,
            rawName,
            rawIdentityName,
            speech,
            language,
            fallbackLanguage,
            sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedEntityWhisperAsync(
        EntityUid source,
        string message,
        ChatTransmitRange range,
        string rawName,
        string rawIdentityName,
        SpeechVerbPrototype speech,
        LanguagePrototype language,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _ncChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.Whisper);
        await RunTranslationOnMainThreadAsync(() =>
        {
            if (!CanDispatchFromSource(source))
                return;

            var translation = translationDispatch.ImmediateTranslation;
            uint? serverMessageId = null;
            if (translation == null && translationDispatch.PendingTranslation != null)
            {
                translation = NCChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                serverMessageId = _ncChatTranslation.AllocateMessageId();
            }

            DispatchWhisperRecipientsWithTranslation(
                source,
                message,
                range,
                rawName,
                rawIdentityName,
                speech,
                language,
                translation,
                serverMessageId);

            var replayWrap = WrapWhisperMessage(source, "chat-manager-entity-whisper-wrap-message", FormattedMessage.EscapeText(rawName), message, speech, language);
            _replay.RecordServerMessage(new ChatMessage(ChatChannel.Whisper, message, replayWrap, GetNetEntity(source), null, MessageRangeHideChatForReplay(range), serverMessageId: serverMessageId));

            if (translationDispatch.PendingTranslation != null && serverMessageId is { } pendingMessageId)
            {
                ObserveTranslationTask(DispatchDelayedWhisperUpdateAsync(
                    pendingMessageId,
                    source,
                    message,
                    range,
                    rawName,
                    rawIdentityName,
                    speech,
                    language,
                    fallbackLanguage,
                    translationDispatch.PendingTranslation));
            }
        });
    }

    private void DispatchWhisperRecipientsWithTranslation(
        EntityUid source,
        string message,
        ChatTransmitRange range,
        string rawName,
        string rawIdentityName,
        SpeechVerbPrototype speech,
        LanguagePrototype language,
        NCChatTranslationPayload? translation,
        uint? serverMessageId)
    {
        var languageObfuscatedMessage = SanitizeInGameICMessage(source, _language.ObfuscateSpeech(message, language), out _, true, _configurationManager.GetCVar(CCVars.ChatPunctuation), (!CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Parent.Name == "en") || (CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Name == "en"));

        foreach (var (session, data) in GetRecipients(source, Transform(source).GridUid == null ? 0.3f : WhisperMuffledRange))
        {
            if (session.AttachedEntity is not { Valid: true } listener)
                continue;

            if (Transform(session.AttachedEntity.Value).GridUid != Transform(source).GridUid &&
                !CheckAttachedGrids(source, session.AttachedEntity.Value))
                continue;

            if (MessageRangeCheck(session, data, range) != MessageRangeCheckResult.Full)
                continue;

            if (_hearing.IsBlockedByDeafness(session, ChatChannel.Whisper, language))
                continue;

            var canUnderstandLanguage = _language.CanUnderstand(listener, language.ID);
            var perceivedMessage = canUnderstandLanguage ? message : languageObfuscatedMessage;
            var viewerName = FormattedMessage.EscapeText(_ncCharacterNotes.GetDisplayNameForViewer(source, listener, rawName));
            var viewerNameIdentity = FormattedMessage.EscapeText(_ncCharacterNotes.GetDisplayNameForViewer(source, listener, rawIdentityName));

            string result;
            string wrappedMessage;
            if (data.Range <= (TryComp<ChatModifierComponent>(listener, out var modifier) ? modifier.WhisperListeningRange : WhisperClearRange))
            {
                result = perceivedMessage;
                wrappedMessage = WrapWhisperMessage(source, "chat-manager-entity-whisper-wrap-message", viewerName, result, speech, language);
            }
            else if (_examineSystem.InRangeUnOccluded(source, listener, WhisperMuffledRange))
            {
                result = ObfuscateMessageReadability(perceivedMessage);
                wrappedMessage = WrapWhisperMessage(source, "chat-manager-entity-whisper-wrap-message", viewerNameIdentity, result, speech, language);
            }
            else
            {
                result = ObfuscateMessageReadability(perceivedMessage);
                wrappedMessage = WrapWhisperMessage(source, "chat-manager-entity-whisper-unknown-wrap-message", string.Empty, result, speech, language);
            }

            if (translation != null &&
                canUnderstandLanguage &&
                result == message &&
                _ncPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
            {
                var preserveOriginal = IsSourceAuthorSession(source, session);
                result = NCChatTranslationFormatting.ResolveVisibleText(
                    translation,
                    message,
                    translation.SourceLanguage,
                    recipientLanguage,
                    preserveOriginal);

                wrappedMessage = NCChatTranslationFormatting.PrefixWithLanguageTag(
                    WrapWhisperMessage(source, "chat-manager-entity-whisper-wrap-message", viewerName, result, speech, language),
                    recipientLanguage,
                    translation.SourceLanguage,
                    NCChatTranslationFormatting.ResolveOriginalTextForTag(
                        translation,
                        message,
                        translation.SourceLanguage,
                        recipientLanguage,
                        preserveOriginal));
            }

            _chatManager.ChatMessageToOne(ChatChannel.Whisper, result, wrappedMessage, source, false, session.Channel, serverMessageId: serverMessageId);
        }
    }

    private bool TryDispatchTranslatedLooc(
        EntityUid source,
        ICommonSession player,
        string message,
        string wrappedMessage,
        bool hideChat,
        string escapedName)
    {
        if (!_ncChatTranslation.IsConfiguredForChannel(ChatChannel.LOOC))
            return false;

        var fallbackLanguage = _ncPlayerCulture.ResolveLanguageCode(player);
        var sourceLanguage = NCChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!NCChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
            return false;

        ObserveTranslationTask(DispatchTranslatedLoocAsync(source, player, message, wrappedMessage, hideChat, escapedName, fallbackLanguage, sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedLoocAsync(
        EntityUid source,
        ICommonSession player,
        string message,
        string wrappedMessage,
        bool hideChat,
        string escapedName,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _ncChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.LOOC);
        await RunTranslationOnMainThreadAsync(() =>
        {
            if (!CanDispatchFromSource(source))
                return;

            var translation = translationDispatch.ImmediateTranslation;
            uint? serverMessageId = null;
            if (translation == null && translationDispatch.PendingTranslation != null)
            {
                translation = NCChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                serverMessageId = _ncChatTranslation.AllocateMessageId();
            }

            var initialTranslation = translation;
            SendInVoiceRange(ChatChannel.LOOC, escapedName, message, wrappedMessage,
                obfuscated: string.Empty,
                obfuscatedWrappedMessage: string.Empty,
                source,
                hideChat ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal,
                player.UserId,
                languageOverride: LanguageSystem.Universal,
                wrapForListener: (listener, content) =>
                {
                    var displayName = FormattedMessage.EscapeText(_ncCharacterNotes.GetDisplayNameForViewer(source, listener, escapedName));
                    if (initialTranslation != null &&
                        TryComp<ActorComponent>(listener, out var actor) &&
                        _ncPlayerCulture.TryResolveChatLanguageCode(actor.PlayerSession, out var recipientLanguage))
                    {
                        var preserveOriginal = actor.PlayerSession.UserId == player.UserId;
                        var visibleContent = NCChatTranslationFormatting.ResolveVisibleText(
                            initialTranslation,
                            message,
                            initialTranslation.SourceLanguage,
                            recipientLanguage,
                            preserveOriginal);

                        return NCChatTranslationFormatting.PrefixWithLanguageTag(
                            Loc.GetString("chat-manager-entity-looc-wrap-message",
                                ("entityName", displayName),
                                ("message", FormattedMessage.EscapeText(visibleContent))),
                            recipientLanguage,
                            initialTranslation.SourceLanguage,
                            NCChatTranslationFormatting.ResolveOriginalTextForTag(
                                initialTranslation,
                                message,
                                initialTranslation.SourceLanguage,
                                recipientLanguage,
                                preserveOriginal));
                    }

                    return Loc.GetString("chat-manager-entity-looc-wrap-message",
                        ("entityName", displayName),
                        ("message", FormattedMessage.EscapeText(content)));
                },
                serverMessageId: serverMessageId);

            if (translationDispatch.PendingTranslation != null && serverMessageId is { } pendingMessageId)
            {
                ObserveTranslationTask(DispatchDelayedLoocUpdateAsync(
                    pendingMessageId,
                    source,
                    player,
                    message,
                    escapedName,
                    hideChat,
                    fallbackLanguage,
                    translationDispatch.PendingTranslation));
            }
        });
    }

    private bool TryDispatchTranslatedDeadChat(
        EntityUid source,
        ICommonSession player,
        string message,
        string wrappedMessage,
        bool hideChat)
    {
        if (!_ncChatTranslation.IsConfiguredForChannel(ChatChannel.Dead))
            return false;

        var fallbackLanguage = _ncPlayerCulture.ResolveLanguageCode(player);
        var sourceLanguage = NCChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!NCChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
            return false;

        ObserveTranslationTask(DispatchTranslatedDeadChatAsync(source, player, message, wrappedMessage, hideChat, fallbackLanguage, sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedDeadChatAsync(
        EntityUid source,
        ICommonSession player,
        string message,
        string wrappedMessage,
        bool hideChat,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _ncChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.Dead);
        await RunTranslationOnMainThreadAsync(() =>
        {
            var translation = translationDispatch.ImmediateTranslation;
            uint? serverMessageId = null;
            if (translation == null && translationDispatch.PendingTranslation != null)
            {
                translation = NCChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                serverMessageId = _ncChatTranslation.AllocateMessageId();
            }

            foreach (var client in GetDeadChatClients())
            {
                var finalMessage = message;
                var finalWrapped = wrappedMessage;

                if (translation != null &&
                    _playerManager.TryGetSessionById(client.UserId, out var recipient) &&
                    _ncPlayerCulture.TryResolveChatLanguageCode(recipient, out var recipientLanguage))
                {
                    var preserveOriginal = recipient.UserId == player.UserId;
                    finalMessage = NCChatTranslationFormatting.ResolveVisibleText(
                        translation,
                        message,
                        translation.SourceLanguage,
                        recipientLanguage,
                        preserveOriginal);

                    finalWrapped = _adminManager.IsAdmin(player)
                        ? Loc.GetString("chat-manager-send-admin-dead-chat-wrap-message",
                            ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                            ("userName", player.Channel.UserName),
                            ("message", FormattedMessage.EscapeText(finalMessage)))
                        : Loc.GetString("chat-manager-send-dead-chat-wrap-message",
                            ("deadChannelName", Loc.GetString("chat-manager-dead-channel-name")),
                            ("playerName", Name(source)),
                            ("message", FormattedMessage.EscapeText(finalMessage)));

                    finalWrapped = NCChatTranslationFormatting.PrefixWithLanguageTag(
                        finalWrapped,
                        recipientLanguage,
                        translation.SourceLanguage,
                        NCChatTranslationFormatting.ResolveOriginalTextForTag(
                            translation,
                            message,
                            translation.SourceLanguage,
                            recipientLanguage,
                            preserveOriginal));
                }

                _chatManager.ChatMessageToOne(ChatChannel.Dead, finalMessage, finalWrapped, source, hideChat, client, recordReplay: false, author: player.UserId, serverMessageId: serverMessageId);
            }

            _replay.RecordServerMessage(new ChatMessage(ChatChannel.Dead, message, wrappedMessage, GetNetEntity(source), null, hideChat, serverMessageId: serverMessageId));

            if (translationDispatch.PendingTranslation != null && serverMessageId is { } pendingMessageId)
            {
                ObserveTranslationTask(DispatchDelayedDeadUpdateAsync(
                    pendingMessageId,
                    source,
                    player,
                    message,
                    hideChat,
                    fallbackLanguage,
                    translationDispatch.PendingTranslation));
            }
        });
    }

    private bool CanDispatchFromSource(EntityUid source)
    {
        return source.Valid && TryComp<TransformComponent>(source, out _);
    }

    private bool IsSourceAuthorSession(EntityUid source, ICommonSession session)
    {
        return TryComp<ActorComponent>(source, out var actor) &&
               actor.PlayerSession.UserId == session.UserId;
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
            Logger.Error($"NC chat translation task failed: {e}");
        }
    }

    private Task RunTranslationOnMainThreadAsync(Action action)
    {
        // Fast translations can complete on the main thread synchronously.
        // Posting back into the same main-thread queue and then awaiting it would deadlock.
        if (IsOnRobustMainThread())
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

    private static bool IsOnRobustMainThread()
    {
        return SynchronizationContext.Current?.GetType().FullName ==
               "Robust.Shared.Asynchronous.RobustSynchronizationContext";
    }

    private async Task DispatchDelayedEntitySpeakUpdateAsync(
        uint serverMessageId,
        EntityUid source,
        string message,
        SpeechVerbPrototype speech,
        string escapedName,
        ChatTransmitRange range,
        LanguagePrototype language,
        Task<NCChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunTranslationOnMainThreadAsync(() =>
        {
            foreach (var (session, data) in GetRecipients(source, VoiceRange))
            {
                var entRange = MessageRangeCheck(session, data, range);
                if (entRange == MessageRangeCheckResult.Disallowed)
                    continue;

                if (session.AttachedEntity is not { Valid: true } listener ||
                    !_ncPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var displayName = FormattedMessage.EscapeText(_ncCharacterNotes.GetDisplayNameForViewer(source, listener, escapedName));
                var preserveOriginal = IsSourceAuthorSession(source, session);
                var visibleText = NCChatTranslationFormatting.ResolveVisibleText(
                    translation,
                    message,
                    translation.SourceLanguage,
                    recipientLanguage,
                    preserveOriginal);
                var wrapped = NCChatTranslationFormatting.PrefixWithLanguageTag(
                    WrapPublicMessage(source, displayName, visibleText, speech, language),
                    recipientLanguage,
                    translation.SourceLanguage,
                    NCChatTranslationFormatting.ResolveOriginalTextForTag(
                        translation,
                        message,
                        translation.SourceLanguage,
                        recipientLanguage,
                        preserveOriginal));

                _chatManager.UpdateChatMessageToOne(ChatChannel.Local, visibleText, wrapped, source, entRange == MessageRangeCheckResult.HideChat, session.Channel, serverMessageId);
            }
        });
    }

    private async Task DispatchDelayedLoocUpdateAsync(
        uint serverMessageId,
        EntityUid source,
        ICommonSession author,
        string message,
        string escapedName,
        bool hideChat,
        string? fallbackLanguage,
        Task<NCChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunTranslationOnMainThreadAsync(() =>
        {
            foreach (var (session, data) in GetRecipients(source, VoiceRange))
            {
                var entRange = MessageRangeCheck(session, data, hideChat ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal);
                if (entRange == MessageRangeCheckResult.Disallowed)
                    continue;

                if (session.AttachedEntity is not { Valid: true } listener ||
                    !_ncPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var preserveOriginal = session.UserId == author.UserId;
                var visibleText = NCChatTranslationFormatting.ResolveVisibleText(
                    translation,
                    message,
                    translation.SourceLanguage,
                    recipientLanguage,
                    preserveOriginal);
                var displayName = FormattedMessage.EscapeText(_ncCharacterNotes.GetDisplayNameForViewer(source, listener, escapedName));
                var wrapped = NCChatTranslationFormatting.PrefixWithLanguageTag(
                    Loc.GetString("chat-manager-entity-looc-wrap-message",
                        ("entityName", displayName),
                        ("message", FormattedMessage.EscapeText(visibleText))),
                    recipientLanguage,
                    translation.SourceLanguage,
                    NCChatTranslationFormatting.ResolveOriginalTextForTag(
                        translation,
                        message,
                        translation.SourceLanguage,
                        recipientLanguage,
                        preserveOriginal));

                _chatManager.UpdateChatMessageToOne(ChatChannel.LOOC, visibleText, wrapped, source, entRange == MessageRangeCheckResult.HideChat, session.Channel, serverMessageId, author: author.UserId);
            }
        });
    }

    private async Task DispatchDelayedDeadUpdateAsync(
        uint serverMessageId,
        EntityUid source,
        ICommonSession player,
        string message,
        bool hideChat,
        string? fallbackLanguage,
        Task<NCChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunTranslationOnMainThreadAsync(() =>
        {
            foreach (var client in GetDeadChatClients())
            {
                if (!_playerManager.TryGetSessionById(client.UserId, out var recipient) ||
                    !_ncPlayerCulture.TryResolveChatLanguageCode(recipient, out var recipientLanguage))
                    continue;

                var preserveOriginal = recipient.UserId == player.UserId;
                var visibleText = NCChatTranslationFormatting.ResolveVisibleText(
                    translation,
                    message,
                    translation.SourceLanguage,
                    recipientLanguage,
                    preserveOriginal);
                var wrapped = _adminManager.IsAdmin(player)
                    ? Loc.GetString("chat-manager-send-admin-dead-chat-wrap-message",
                        ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                        ("userName", player.Channel.UserName),
                        ("message", FormattedMessage.EscapeText(visibleText)))
                    : Loc.GetString("chat-manager-send-dead-chat-wrap-message",
                        ("deadChannelName", Loc.GetString("chat-manager-dead-channel-name")),
                        ("playerName", Name(source)),
                        ("message", FormattedMessage.EscapeText(visibleText)));

                wrapped = NCChatTranslationFormatting.PrefixWithLanguageTag(
                    wrapped,
                    recipientLanguage,
                    translation.SourceLanguage,
                    NCChatTranslationFormatting.ResolveOriginalTextForTag(
                        translation,
                        message,
                        translation.SourceLanguage,
                        recipientLanguage,
                        preserveOriginal));

                _chatManager.UpdateChatMessageToOne(ChatChannel.Dead, visibleText, wrapped, source, hideChat, client, serverMessageId, author: player.UserId);
            }
        });
    }

    private async Task DispatchDelayedWhisperUpdateAsync(
        uint serverMessageId,
        EntityUid source,
        string message,
        ChatTransmitRange range,
        string rawName,
        string rawIdentityName,
        SpeechVerbPrototype speech,
        LanguagePrototype language,
        string? fallbackLanguage,
        Task<NCChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunTranslationOnMainThreadAsync(() =>
        {
            var languageObfuscatedMessage = SanitizeInGameICMessage(source, _language.ObfuscateSpeech(message, language), out _, true, _configurationManager.GetCVar(CCVars.ChatPunctuation), (!CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Parent.Name == "en") || (CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Name == "en"));

            foreach (var (session, data) in GetRecipients(source, WhisperMuffledRange))
            {
                if (session.AttachedEntity is not { Valid: true } listener)
                    continue;

                if (MessageRangeCheck(session, data, range) != MessageRangeCheckResult.Full)
                    continue;

                if (!_ncPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var canUnderstandLanguage = _language.CanUnderstand(listener, language.ID);
                var perceivedMessage = canUnderstandLanguage ? message : languageObfuscatedMessage;
                var viewerName = FormattedMessage.EscapeText(_ncCharacterNotes.GetDisplayNameForViewer(source, listener, rawName));
                var viewerNameIdentity = FormattedMessage.EscapeText(_ncCharacterNotes.GetDisplayNameForViewer(source, listener, rawIdentityName));

                string result;
                string wrappedMessage;
                if (data.Range <= (TryComp<ChatModifierComponent>(listener, out var modifier) ? modifier.WhisperListeningRange : WhisperClearRange))
                {
                    var preserveOriginal = canUnderstandLanguage && IsSourceAuthorSession(source, session);
                    result = canUnderstandLanguage
                        ? NCChatTranslationFormatting.ResolveVisibleText(translation, message, translation.SourceLanguage, recipientLanguage, preserveOriginal)
                        : perceivedMessage;
                    wrappedMessage = NCChatTranslationFormatting.PrefixWithLanguageTag(
                        WrapWhisperMessage(source, "chat-manager-entity-whisper-wrap-message", viewerName, result, speech, language),
                        recipientLanguage,
                        translation.SourceLanguage,
                        NCChatTranslationFormatting.ResolveOriginalTextForTag(translation, message, translation.SourceLanguage, recipientLanguage, preserveOriginal));
                }
                else if (_examineSystem.InRangeUnOccluded(source, listener, WhisperMuffledRange))
                {
                    result = ObfuscateMessageReadability(perceivedMessage);
                    wrappedMessage = WrapWhisperMessage(source, "chat-manager-entity-whisper-wrap-message", viewerNameIdentity, result, speech, language);
                }
                else
                {
                    result = ObfuscateMessageReadability(perceivedMessage);
                    wrappedMessage = WrapWhisperMessage(source, "chat-manager-entity-whisper-unknown-wrap-message", string.Empty, result, speech, language);
                }

                _chatManager.UpdateChatMessageToOne(ChatChannel.Whisper, result, wrappedMessage, source, false, session.Channel, serverMessageId);
            }
        });
    }
}
