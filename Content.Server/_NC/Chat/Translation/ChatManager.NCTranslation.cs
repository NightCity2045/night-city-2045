using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._NC.Localization;
using Content.Server._NC.Chat.Translation;
using Content.Shared._NC.Chat.Translation;
using Content.Shared.Chat;
using Content.Shared.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Managers;

internal sealed partial class ChatManager
{
    [Dependency] private readonly INCChatTranslationService _ncChatTranslation = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystems = default!;
    [Dependency] private readonly ITaskManager _ncTaskManager = default!;

    private NCPlayerCultureTracker NCPlayerCulture => _entitySystems.GetEntitySystem<NCPlayerCultureTracker>();

    private bool TryDispatchTranslatedHookOoc(string sender, string message, string wrappedMessage)
    {
        if (!_ncChatTranslation.IsConfiguredForChannel(ChatChannel.OOC))
            return false;

        var sourceLanguage = NCChatTranslationMarkup.ResolveLanguageFromText(message);
        if (!NCChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
            return false;

        ObserveTranslationTask(DispatchTranslatedHookOocAsync(sender, message, wrappedMessage, sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedHookOocAsync(
        string sender,
        string message,
        string wrappedMessage,
        string sourceLanguage)
    {
        var translationDispatch = await _ncChatTranslation.TranslateWithSoftHoldAsync(message, null, ChatChannel.OOC);
        await RunTranslationOnMainThreadAsync(() =>
        {
            var initialTranslation = translationDispatch.ImmediateTranslation;
            uint? serverMessageId = null;
            if (initialTranslation == null && translationDispatch.PendingTranslation != null)
            {
                initialTranslation = NCChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                serverMessageId = _ncChatTranslation.AllocateMessageId();
            }

            if (initialTranslation == null)
            {
                ChatMessageToAll(ChatChannel.OOC, message, wrappedMessage, source: EntityUid.Invalid, hideChat: false, recordReplay: true, serverMessageId: serverMessageId);
            }
            else
            {
                foreach (var session in _playerManager.Sessions)
                {
                    var finalMessage = message;
                    var finalWrapped = wrappedMessage;

                    if (NCPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    {
                        finalMessage = NCChatTranslationFormatting.ResolveVisibleText(
                            initialTranslation,
                            message,
                            initialTranslation.SourceLanguage,
                            recipientLanguage);

                        finalWrapped = NCChatTranslationFormatting.PrefixWithLanguageTag(
                            BuildHookOocWrappedMessage(sender, finalMessage),
                            recipientLanguage,
                            initialTranslation.SourceLanguage,
                            NCChatTranslationFormatting.ResolveOriginalTextForTag(
                                initialTranslation,
                                message,
                                initialTranslation.SourceLanguage,
                                recipientLanguage));
                    }

                    ChatMessageToOne(ChatChannel.OOC, finalMessage, finalWrapped, EntityUid.Invalid, false, session.Channel, recordReplay: false, serverMessageId: serverMessageId);
                }

                _replay.RecordServerMessage(new ChatMessage(ChatChannel.OOC, message, wrappedMessage, NetEntity.Invalid, null, false, serverMessageId: serverMessageId));
            }

            if (translationDispatch.PendingTranslation != null && serverMessageId is { } pendingMessageId)
            {
                ObserveTranslationTask(DispatchDelayedHookOocUpdateAsync(sender, message, pendingMessageId, translationDispatch.PendingTranslation));
            }
        });
    }

    private bool TryDispatchTranslatedOoc(
        ICommonSession player,
        string message,
        string wrappedMessage,
        Color? colorOverride)
    {
        if (!_ncChatTranslation.IsConfiguredForChannel(ChatChannel.OOC))
            return false;

        var fallbackLanguage = NCPlayerCulture.ResolveLanguageCode(player);
        var sourceLanguage = NCChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!NCChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
            return false;

        ObserveTranslationTask(DispatchTranslatedOocAsync(player, message, wrappedMessage, colorOverride, fallbackLanguage, sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedOocAsync(
        ICommonSession player,
        string message,
        string wrappedMessage,
        Color? colorOverride,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _ncChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.OOC);
        await RunTranslationOnMainThreadAsync(() =>
        {
            var initialTranslation = translationDispatch.ImmediateTranslation;
            uint? serverMessageId = null;
            if (initialTranslation == null && translationDispatch.PendingTranslation != null)
            {
                initialTranslation = NCChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                serverMessageId = _ncChatTranslation.AllocateMessageId();
            }

            if (initialTranslation == null)
            {
                ChatMessageToAll(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, hideChat: false, recordReplay: true, colorOverride: colorOverride, author: player.UserId, serverMessageId: serverMessageId);
            }
            else
            {
                foreach (var session in _playerManager.Sessions)
                {
                    var finalMessage = message;
                    var finalWrapped = wrappedMessage;

                    if (NCPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    {
                        var preserveOriginal = session.UserId == player.UserId;
                        finalMessage = NCChatTranslationFormatting.ResolveVisibleText(
                            initialTranslation,
                            message,
                            initialTranslation.SourceLanguage,
                            recipientLanguage,
                            preserveOriginal);

                        finalWrapped = _netConfigManager.GetClientCVar(session.Channel, CCVars.ShowOocPatronColor) &&
                                       player.Channel.UserData.PatronTier is { } sessionPatron &&
                                       PatronOocColors.TryGetValue(sessionPatron, out var sessionPatronColor)
                            ? Loc.GetString("chat-manager-send-ooc-patron-wrap-message", ("patronColor", sessionPatronColor), ("playerName", player.Name), ("message", FormattedMessage.EscapeText(finalMessage)))
                            : Loc.GetString("chat-manager-send-ooc-wrap-message", ("playerName", player.Name), ("message", FormattedMessage.EscapeText(finalMessage)));

                        finalWrapped = NCChatTranslationFormatting.PrefixWithLanguageTag(
                            finalWrapped,
                            recipientLanguage,
                            initialTranslation.SourceLanguage,
                            NCChatTranslationFormatting.ResolveOriginalTextForTag(
                                initialTranslation,
                                message,
                                initialTranslation.SourceLanguage,
                                recipientLanguage,
                                preserveOriginal));
                    }

                    ChatMessageToOne(ChatChannel.OOC, finalMessage, finalWrapped, EntityUid.Invalid, false, session.Channel, colorOverride: colorOverride, recordReplay: false, author: player.UserId, serverMessageId: serverMessageId);
                }

                _replay.RecordServerMessage(new ChatMessage(ChatChannel.OOC, message, wrappedMessage, NetEntity.Invalid, null, false, colorOverride, serverMessageId: serverMessageId));
            }

            if (translationDispatch.PendingTranslation != null && serverMessageId is { } pendingOocMessageId)
            {
                ObserveTranslationTask(DispatchDelayedOocUpdateAsync(player, message, colorOverride, pendingOocMessageId, fallbackLanguage, translationDispatch.PendingTranslation));
            }
        });
    }

    private string BuildHookOocWrappedMessage(string sender, string visibleMessage)
    {
        return Loc.GetString(
            "chat-manager-send-hook-ooc-wrap-message",
            ("senderName", sender),
            ("message", FormattedMessage.EscapeText(visibleMessage)));
    }

    private async Task DispatchDelayedHookOocUpdateAsync(
        string sender,
        string message,
        uint serverMessageId,
        Task<NCChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunTranslationOnMainThreadAsync(() =>
        {
            foreach (var session in _playerManager.Sessions)
            {
                if (!NCPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var visibleText = NCChatTranslationFormatting.ResolveVisibleText(
                    translation,
                    message,
                    translation.SourceLanguage,
                    recipientLanguage);
                var wrapped = NCChatTranslationFormatting.PrefixWithLanguageTag(
                    BuildHookOocWrappedMessage(sender, visibleText),
                    recipientLanguage,
                    translation.SourceLanguage,
                    NCChatTranslationFormatting.ResolveOriginalTextForTag(
                        translation,
                        message,
                        translation.SourceLanguage,
                        recipientLanguage));

                UpdateChatMessageToOne(ChatChannel.OOC, visibleText, wrapped, EntityUid.Invalid, false, session.Channel, serverMessageId);
            }
        });
    }

    private async Task DispatchDelayedOocUpdateAsync(
        ICommonSession player,
        string message,
        Color? colorOverride,
        uint serverMessageId,
        string? fallbackLanguage,
        Task<NCChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunTranslationOnMainThreadAsync(() =>
        {
            foreach (var session in _playerManager.Sessions)
            {
                if (!NCPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var preserveOriginal = session.UserId == player.UserId;
                var visibleText = NCChatTranslationFormatting.ResolveVisibleText(
                    translation,
                    message,
                    translation.SourceLanguage,
                    recipientLanguage,
                    preserveOriginal);
                var wrapped = Loc.GetString("chat-manager-send-ooc-wrap-message", ("playerName", player.Name), ("message", FormattedMessage.EscapeText(visibleText)));

                if (_netConfigManager.GetClientCVar(session.Channel, CCVars.ShowOocPatronColor) &&
                    player.Channel.UserData.PatronTier is { } patron &&
                    PatronOocColors.TryGetValue(patron, out var patronColor))
                {
                    wrapped = Loc.GetString("chat-manager-send-ooc-patron-wrap-message", ("patronColor", patronColor), ("playerName", player.Name), ("message", FormattedMessage.EscapeText(visibleText)));
                }

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

                UpdateChatMessageToOne(ChatChannel.OOC, visibleText, wrapped, EntityUid.Invalid, false, session.Channel, serverMessageId, colorOverride: colorOverride, author: player.UserId);
            }
        });
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
            Logger.Error($"NC chat manager translation task failed: {e}");
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
}
