using System;
using Capy.NoRules.Addons;
using Capy.NoRules.Modules;
using Capy.NoRules.Concepts;
using Capy.NoRules.Scps;
using CommandSystem;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

/// <summary>
/// Ручная синхронизация донат-привилегий с веб-сервером (.sync / .syncrank).
/// </summary>
[CommandHandler(typeof(ClientCommandHandler))]
[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class SyncRankCommand : ICommand
{
    public string Command => "sync";
    public string[] Aliases => new[] { "syncrank", "donatesync", "обновитьранг" };
    public string Description => "Синхронизировать свой ранг и бейдж с сайтом доната.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        var donateFeature = NoRulesPlugin.Instance?.DonateController;
        if (donateFeature == null)
        {
            response = "<color=red>[ОШИБКА]</color> Модуль DonateController не загружен.";
            return false;
        }

        Player? player = Player.Get(sender);
        if (player == null || !player.IsConnected)
        {
            response = "<color=red>[ОШИБКА]</color> Команда доступна только игрокам на сервере.";
            return false;
        }

        _ = donateFeature.CheckAndApplyRankAsync(player);
        response = "<color=green>[ДОНАТ]</color> Запрос синхронизации ранга отправлен на сайт...";
        return true;
    }
}

