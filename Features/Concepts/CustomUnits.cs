using System.Collections.Concurrent;
using Exiled.API.Features;

namespace Capy.NoRules.Features.Concepts;

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
            // EXILED запрещает rich-text в CustomInfo — чистый текст
            player.CustomInfo = $"{rank} | {unitName}";
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
    }

    public static bool IsMember(Player? player)
        => player != null && UnitByUserId.ContainsKey(player.UserId);

    public static void Clear() => UnitByUserId.Clear();
}
