using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Studio.Core;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using GameCore;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Modules;

/// <summary>
/// 3D-Лобби ожидания игроков на поверхности.
/// Работает нативно и легковесно (по образцу Lobby-main):
/// - При WaitingForPlayers скрывает игровой объект "StartRound" (localScale = 0).
/// - 2D-оверлей игры полностью исчезает сам собой без единого патча.
/// - Игроки спавнятся в роли Tutorial на схематике с бессмертием.
/// - Remote Admin кнопки (Force Start, Restart, Lobby Lock) работают на 100% штатно.
/// - При старте раунда игра сама спавнит всех игроков в комплексе.
/// </summary>
public sealed class LobbyFeature
{
    private readonly LobbyConfig _config;
    private SchematicObject? _lobbyInstance;
    private CoroutineHandle _safetyLoop;
    private bool _isWaitingPhase;

    public LobbyFeature(LobbyConfig config)
    {
        _config = config;
    }

    public bool IsInLobby => _isWaitingPhase && !Round.IsStarted;
    public Vector3 PlayerSpawnPosition => _config.LobbyPosition + _config.PlayerSpawnOffset;

    public void OnWaitingForPlayers()
    {
        if (!_config.IsEnabled) return;

        _isWaitingPhase = true;

        Timing.CallDelayed(0.1f, () =>
        {
            try
            {
                // Скрываем стандартный 2D-объект ожидания игры
                var startRoundObj = GameObject.Find("StartRound");
                if (startRoundObj != null)
                {
                    startRoundObj.transform.localScale = Vector3.zero;
                    Log.Debug("[Lobby] 2D-объект StartRound успешно скрыт.");
                }

                DespawnLobby();
                SchematicLoader.ClearCache();
                _lobbyInstance = SchematicLoader.Spawn(_config.SchematicName, _config.LobbyPosition, Quaternion.identity);

                if (_lobbyInstance != null)
                {
                    Log.Info($"[Lobby] 3D-Лобби '{_config.SchematicName}' успешно заспавнено на {_config.LobbyPosition}.");
                }

                // Спавним всех текущих игроков
                foreach (var player in Player.List)
                {
                    if (player.IsConnected && !player.IsHost)
                    {
                        SetupPlayerInLobby(player);
                    }
                }

                if (_safetyLoop.IsRunning)
                    Timing.KillCoroutines(_safetyLoop);

                _safetyLoop = Timing.RunCoroutine(SafetyCheckLoop());
            }
            catch (Exception ex)
            {
                Log.Error($"[Lobby] Ошибка при спавне лобби: {ex}");
            }
        });
    }

    private IEnumerator<float> SafetyCheckLoop()
    {
        while (_isWaitingPhase && !Round.IsStarted)
        {
            try
            {
                int count = Player.List.Count(p => p.IsConnected && !p.IsHost);
                var netTimer = RoundStart.singleton != null ? RoundStart.singleton.NetworkTimer : -2;

                string statusText;
                if (netTimer == -2)
                {
                    statusText = $"\u041E\u0436\u0438\u0434\u0430\u043D\u0438\u0435 \u0438\u0433\u0440\u043E\u043A\u043E\u0432... | \u0412 \u043B\u043E\u0431\u0431\u0438: <color=#68D391>{count}</color>";
                }
                else if (netTimer > 0)
                {
                    statusText = $"\u0414\u043E \u0441\u0442\u0430\u0440\u0442\u0430 \u0440\u0430\u0443\u043D\u0434\u0430: <color=#E53E3E><b>{netTimer}</b> \u0441\u0435\u043A</color> | \u0412 \u043B\u043E\u0431\u0431\u0438: <color=#68D391>{count}</color>";
                }
                else
                {
                    statusText = $"\u0417\u0430\u043F\u0443\u0441\u043A \u0440\u0430\u0443\u043D\u0434\u0430...";
                }

                string hint = $"<size=28><color=#DDAA55><b>\u041B\u041E\u0411\u0411\u0418 \u041A\u0410\u041F\u0418\u0411\u0410\u0420</b></color></size>\n<color=#C2C2C2>{statusText}</color>";

                foreach (var player in Player.List)
                {
                    if (!player.IsConnected || player.IsHost) continue;

                    // Если игрок умер или сменил роль
                    if (player.Role.Type != _config.LobbyRole && _isWaitingPhase && !Round.IsStarted)
                    {
                        SetupPlayerInLobby(player);
                        continue;
                    }

                    // Защита от падения в пустоту
                    if (player.Position.y < _config.LobbyPosition.y - 5f || player.Position.y > _config.LobbyPosition.y + 30f)
                    {
                        player.Position = PlayerSpawnPosition;
                    }

                    player.IsGodModeEnabled = true;
                    player.ShowHint(hint, 1.2f);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Lobby] Ошибка в цикле лобби: {ex}");
            }

            yield return Timing.WaitForSeconds(1.0f);
        }
    }

    public void OnPlayerVerified(VerifiedEventArgs ev)
    {
        if (!_config.IsEnabled || !_isWaitingPhase || Round.IsStarted || ev.Player == null || ev.Player.IsHost)
            return;

        Timing.CallDelayed(0.5f, () =>
        {
            if (!_isWaitingPhase || Round.IsStarted || ev.Player == null || !ev.Player.IsConnected)
                return;

            SetupPlayerInLobby(ev.Player);
        });
    }

    public void OnPlayerSpawned(SpawnedEventArgs ev)
    {
        if (!_config.IsEnabled || !_isWaitingPhase || Round.IsStarted || ev.Player == null || ev.Player.IsHost)
            return;

        if (ev.Player.Role.Type != _config.LobbyRole)
            return;

        Timing.CallDelayed(0.1f, () =>
        {
            if (!_isWaitingPhase || Round.IsStarted || ev.Player == null || !ev.Player.IsConnected)
                return;

            ev.Player.Position = PlayerSpawnPosition;
            ev.Player.IsGodModeEnabled = true;
        });
    }

    public void OnPlayerHurting(HurtingEventArgs ev)
    {
        if (_config.IsEnabled && _isWaitingPhase && !Round.IsStarted)
        {
            ev.IsAllowed = false;
            ev.Amount = 0f;
        }
    }

    public void OnRoundStarted()
    {
        _isWaitingPhase = false;

        if (_safetyLoop.IsRunning)
            Timing.KillCoroutines(_safetyLoop);

        DespawnLobby();

        // 1. Переводим игроков в Spectator
        foreach (var player in Player.List)
        {
            if (player.IsConnected && !player.IsHost)
            {
                player.IsGodModeEnabled = false;
                player.Role.Set(RoleTypeId.Spectator, RoleSpawnFlags.None);
            }
        }

        // 2. Вызываем стандартный спавнер ролей игры, когда игроки перешли в Spectator
        Timing.CallDelayed(0.15f, () =>
        {
            try
            {
                PlayerRoles.RoleAssign.RoleAssigner.AlreadySpawnedPlayers.Clear();
                PlayerRoles.RoleAssign.RoleAssigner._spawned = false;
                PlayerRoles.RoleAssign.RoleAssigner.OnRoundStarted();
                Log.Info("[Lobby] Стандартный спавнер RoleAssigner успешно распределил роли в комплексе.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Lobby] Ошибка при вызове RoleAssigner: {ex}");
            }
        });
    }

    public void OnRoundEnded()
    {
        _isWaitingPhase = false;
        DespawnLobby();
    }

    public void OnRestartingRound()
    {
        _isWaitingPhase = false;
        DespawnLobby();
    }

    public void SetupPlayerInLobby(Player player)
    {
        if (!player.IsConnected || player.IsHost || !_isWaitingPhase || Round.IsStarted) return;

        if (player.Role.Type != _config.LobbyRole)
        {
            player.Role.Set(_config.LobbyRole, RoleSpawnFlags.None);
        }

        Timing.CallDelayed(0.2f, () =>
        {
            if (!player.IsConnected || !_isWaitingPhase || Round.IsStarted) return;

            player.Position = PlayerSpawnPosition;
            player.IsGodModeEnabled = true;
            player.ClearInventory();
        });
    }

    public void DespawnLobby()
    {
        if (_safetyLoop.IsRunning)
            Timing.KillCoroutines(_safetyLoop);

        if (_lobbyInstance != null)
        {
            try
            {
                _lobbyInstance.Destroy();
                Log.Info("[Lobby] Схематика 3D-лобби успешно выгружена.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Lobby] Ошибка при удалении схематики лобби: {ex}");
            }
            finally
            {
                _lobbyInstance = null;
            }
        }

        foreach (var player in Player.List)
        {
            if (player.IsConnected)
            {
                player.IsGodModeEnabled = false;
            }
        }
    }
}
