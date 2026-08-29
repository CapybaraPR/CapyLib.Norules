using System.Collections.Concurrent;
using Exiled.API.Features;

namespace Capy.NoRules.Concepts;

/// <summary>
/// Кастомные юниты: подменяют имя волны возрождения и подписывают членов отряда
/// (CustomInfo с рангом), чтобы группа отображалась в списке игроков как свой юнит.
/// </summary>
public static class CustomUnits
{
    private static readonly ConcurrentDictionary<string, string> UnitByUserId = new();

    /// <summary>
    /// Помечает игрока как члена кастомного юнита и ставит CustomInfo «ранг | юнит».
    /// </summary>
    public static void AssignMember(Player player, string unitName, string colorHex, string rank)
    {
        if (player == null) return;

        UnitByUserId[player.UserId] = unitName;
        try
        {
            player.CustomInfo = $"<color={colorHex}><b>{rank}</b></color>\n<color=#c2c2c2>{unitName}</color>";
            player.InfoArea |= PlayerInfoArea.CustomInfo;
        }
        catch { }
    }

    /// <summary>
    /// Убирает игрока из юнита (смена роли/смерть/выход).
    /// </summary>
    public static void RemoveMember(Player? player)
    {
        if (player == null) return;
        UnitByUserId.TryRemove(player.UserId, out _);
        try
        {
            player.CustomInfo = string.Empty;
        }
        catch { }
    }

    public static bool IsMember(Player? player)
        => player != null && UnitByUserId.ContainsKey(player.UserId);

    public static void Clear() => UnitByUserId.Clear();
}
