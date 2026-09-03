using System;
using System.Linq;
using Capy.Engine.Studio.Core;
using CommandSystem;
using Exiled.API.Enums;
using Exiled.API.Features;
using UnityEngine;

namespace Capy.NoRules.Commands;

/// <summary>
/// .cptp &lt;название&gt; — телепорт к структуре концепта (панели СО₂, хакерская панель, трубки 008, точка AirDrop).
/// .cptp list — список всех доступных точек.
/// </summary>
[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class ConceptTeleportCommand : ICommand
{
    public string Command => "cptp";
    public string[] Aliases => new[] { "concepts_tp", "cpt", "ctp" };
    public string Description => "Телепорт к структурам концептов и лобби (для отладки)";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (Player.Get(sender) is not { } player)
        {
            response = "Только для игроков.";
            return false;
        }

        string target = arguments.Count > 0 ? arguments.At(0).ToLowerInvariant() : "list";

        if (target == "list" || target == "л")
        {
            response = "\n<color=#38bdf8><b>[ Concept & Lobby Teleport ]</b></color>\n" +
                       "<color=#ffd285>• ctp lobby</color> — Лобби ожидания с капибарами\n" +
                       "<color=#a3e635>• ctp co2</color> — Панели СО₂ (HczHid)\n" +
                       "<color=#a3e635>• ctp hackers</color> — Панель Хакеров (HczServerRoom)\n" +
                       "<color=#a3e635>• ctp 008_173</color> — Трубка 008 (Lcz173)\n" +
                       "<color=#a3e635>• ctp 008_049</color> — Трубка 008 (Hcz049)\n" +
                       "<color=#a3e635>• ctp 008_939</color> — Трубка 008 (Hcz939)\n" +
                       "<color=#a3e635>• ctp 008_ez</color> — Трубка 008 (EzShelter)\n" +
                       "<color=#a3e635>• ctp 294</color> — Кофемашина SCP-294 (EzUpstairsPcs)\n" +
                       "<color=#a3e635>• ctp 120</color> — Бассейн SCP-120 (LczGlassBox)\n" +
                       "<color=#a3e635>• ctp 1162</color> — Дыра SCP-1162 (Lcz173)\n";
            return true;
        }

        Vector3? destination = target switch
        {
            "lobby" or "лобби" => GetOrSpawnLobbyPos(),
            "co2" or "со2" => GetRoomPos(RoomType.HczHid, 0f, 1f, -5f),
            "hackers" or "хакеры" => GetRoomPos(RoomType.HczServerRoom, 0f, 1f, -2.5f),
            "008_173" => GetRoomPos(RoomType.Lcz173, 0f, 1f, -4f),
            "008_049" => GetRoomPos(RoomType.Hcz049, 0f, 1f, -4f),
            "008_939" => GetRoomPos(RoomType.Hcz939, 0f, 1f, -4f),
            "008_ez" => GetRoomPos(RoomType.EzShelter, 0f, 1f, -3f),
            "294" => GetSchematicPos("SCP294", RoomType.EzUpstairsPcs, 0f, 1.5f, 0f),
            "120" => GetSchematicPos("SCP120", RoomType.LczGlassBox, 0f, 1.5f, 0f),
            "1162" => GetSchematicPos("SCP1162", RoomType.Lcz173, 0f, 1f, 0f),
            _ => null
        };

        if (destination == null)
        {
            response = $"<color=red>Неизвестная точка '{target}'.</color> Введите <b>ctp list</b> для списка.";
            return false;
        }

        player.Position = destination.Value;
        player.ShowHint($"<color=#a3e635>Телепорт к '{target}'</color>", 2f);
        response = string.Empty;
        return true;
    }

    private static Vector3? GetOrSpawnLobbyPos()
    {
        var cfg = NoRulesPlugin.Instance?.Config?.Lobby;
        Vector3 lobbyPos = cfg?.LobbyPosition ?? new Vector3(-0.598f, 325.782f, -46.28f);
        Vector3 offset = cfg?.PlayerSpawnOffset ?? new Vector3(2.35f, -1.21f, 5.84f);

        var schematic = SchematicLoader.SpawnedSchematics.FirstOrDefault(s =>
            s.Name.IndexOf("Lobby", StringComparison.OrdinalIgnoreCase) >= 0 && !s.IsDestroyed);

        if (schematic == null)
        {
            schematic = SchematicLoader.Spawn("Lobby", lobbyPos, Quaternion.identity);
        }

        Vector3 spawnTarget = (schematic != null ? schematic.Position : lobbyPos) + offset;
        return spawnTarget;
    }

    private static Vector3? GetRoomPos(RoomType roomType, float ox, float oy, float oz)
    {
        var room = Room.List.FirstOrDefault(r => r.Type == roomType);
        if (room == null) return null;
        return room.Position + room.Rotation * new Vector3(ox, oy, oz);
    }

    private static Vector3? GetSchematicPos(string name, RoomType fallbackRoom, float ox, float oy, float oz)
    {
        var schematic = SchematicLoader.SpawnedSchematics.FirstOrDefault(s =>
            s.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 && !s.IsDestroyed);

        if (schematic != null)
            return schematic.Position + Vector3.up * oy;

        return GetRoomPos(fallbackRoom, ox, oy, oz);
    }
}


