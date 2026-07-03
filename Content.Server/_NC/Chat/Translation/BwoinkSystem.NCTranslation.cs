using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._NC.Localization;
using Content.Server._NC.Chat.Translation;
using Content.Shared._NC.Chat.Translation;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Robust.Shared.Asynchronous;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    [Dependency] private readonly INCChatTranslationService _ncChatTranslation = default!;
    [Dependency] private readonly NCPlayerCultureTracker _ncPlayerCulture = default!;
    [Dependency] private readonly ITaskManager _ncTaskManager = default!;

    private bool TryDispatchTranslatedAHelp(
        SharedBwoinkSystem.BwoinkTextMessage message,
        ICommonSession senderSession,
        AdminData? senderAdmin,
        IList<INetChannel> admins,
        bool playSound)
    {
        if (!_ncChatTranslation.IsConfiguredForAHelp())
            return false;

        var fallbackLanguage = _ncPlayerCulture.ResolveLanguageCode(senderSession);
        var sourceLanguage = NCChatTranslationMarkup.ResolveLanguageFromText(message.Text, fallbackLanguage);
        if (!NCChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
            return false;

        ObserveTranslationTask(DispatchTranslatedAHelpAsync(message, senderSession, senderAdmin, admins, playSound, fallbackLanguage));
        return true;
    }

    private async Task DispatchTranslatedAHelpAsync(
        SharedBwoinkSystem.BwoinkTextMessage message,
        ICommonSession senderSession,
        AdminData? senderAdmin,
        IList<INetChannel> admins,
        bool playSound,
        string? fallbackLanguage)
    {
        NCChatTranslationPayload? translation = null;
        try
        {
            translation = await _ncChatTranslation.TranslateAHelpAsync(message.Text, fallbackLanguage);
        }
        catch (Exception e)
        {
            Logger.Error($"NC AHelp translation task failed: {e}");
        }

        await RunTranslationOnMainThreadAsync(() =>
            DispatchTranslatedAHelpOnMainThread(message, senderSession, senderAdmin, admins, playSound, translation));
    }

    private void DispatchTranslatedAHelpOnMainThread(
        SharedBwoinkSystem.BwoinkTextMessage message,
        ICommonSession senderSession,
        AdminData? senderAdmin,
        IList<INetChannel> admins,
        bool playSound,
        NCChatTranslationPayload? translation)
    {
        var adminStatusPrefix = ResolveAHelpStatusPrefix(message.AdminOnly, message.PlaySound);
        var adminSenderMarkup = BuildAHelpSenderMarkup(
            senderSession.Name,
            senderAdmin,
            _config.GetCVar(Content.Shared.CCVar.CCVars.AhelpAdminPrefix),
            false);

        foreach (var channel in admins)
        {
            if (!_playerManager.TryGetSessionById(channel.UserId, out var recipient))
                continue;

            var translatedText = BuildAHelpTextForRecipient(
                recipient,
                translation,
                message.Text,
                adminSenderMarkup,
                adminStatusPrefix);

            RaiseNetworkEvent(new SharedBwoinkSystem.BwoinkTextMessage(
                    message.UserId,
                    senderSession.UserId,
                    translatedText,
                    playSound: playSound,
                    adminOnly: message.AdminOnly),
                channel);
        }

        if (!_playerManager.TryGetSessionById(message.UserId, out var playerSession) ||
            message.AdminOnly ||
            admins.Contains(playerSession.Channel))
        {
            return;
        }

        var playerStatusPrefix = message.PlaySound
            ? null
            : Loc.GetString("bwoink-message-silent");

        var playerSenderMarkup = BuildAHelpSenderMarkup(
            senderAdmin != null && _overrideClientName != string.Empty ? _overrideClientName : senderSession.Name,
            senderAdmin,
            _config.GetCVar(Content.Shared.CCVar.CCVars.AhelpAdminPrefixWebhook),
            fromWebhook: _overrideClientName != string.Empty);

        var playerText = BuildAHelpTextForRecipient(
            playerSession,
            translation,
            message.Text,
            playerSenderMarkup,
            _overrideClientName != string.Empty ? playerStatusPrefix : adminStatusPrefix);

        RaiseNetworkEvent(new SharedBwoinkSystem.BwoinkTextMessage(
                message.UserId,
                senderSession.UserId,
                playerText,
                playSound: playSound,
                adminOnly: false),
            playerSession.Channel);
    }

    private string BuildAHelpTextForRecipient(
        ICommonSession recipient,
        NCChatTranslationPayload? translation,
        string originalText,
        string senderMarkup,
        string? statusPrefix)
    {
        if (translation == null || !_ncPlayerCulture.TryResolveChatLanguageCode(recipient, out var recipientLanguage))
            return NCChatTranslationFormatting.BuildPlainWrappedMessage(senderMarkup, originalText, statusPrefix);

        var visibleText = translation.GetVisibleText(recipientLanguage);
        return NCChatTranslationFormatting.PrefixWithLanguageTag(
            NCChatTranslationFormatting.BuildPlainWrappedMessage(senderMarkup, visibleText, statusPrefix),
            recipientLanguage,
            translation.SourceLanguage,
            NCChatTranslationFormatting.ResolveOriginalTextForTag(
                translation,
                originalText,
                translation.SourceLanguage,
                recipientLanguage));
    }

    private static string BuildAHelpSenderMarkup(string senderName, AdminData? senderAdmin, bool includePrefix, bool fromWebhook)
    {
        var adminPrefix = includePrefix && senderAdmin?.Title is { Length: > 0 } title
            ? $"[bold]\\[{title}\\][/bold] "
            : string.Empty;

        if (senderAdmin is not null && senderAdmin.Flags == AdminFlags.Adminhelp)
            return $"[color=purple]{adminPrefix}{senderName}[/color]";

        if (senderAdmin is not null && (fromWebhook || senderAdmin.HasFlag(AdminFlags.Adminhelp)))
            return $"[color=red]{adminPrefix}{senderName}[/color]";

        return senderName;
    }

    private string? ResolveAHelpStatusPrefix(bool adminOnly, bool playSound)
    {
        if (adminOnly)
            return Loc.GetString("bwoink-message-admin-only");

        if (!playSound)
            return Loc.GetString("bwoink-message-silent");

        return null;
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
            Logger.Error($"NC AHelp translation dispatch failed: {e}");
        }
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
}
