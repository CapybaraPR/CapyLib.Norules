using System;
using System.IO;
using System.Linq;
using Capy.NoRules.Config;
using Exiled.API.Features;
using UnityEngine;

namespace Capy.NoRules.Modules;

/// <summary>
/// Модуль фоновой музыки в 3D-лобби ожидания.
/// - Автоматически загружает и проигрывает случайный трек из папки Audio/Lobby.
/// - Бесшовно останавливает воспроизведение при старте матча.
/// </summary>
public sealed class LobbyMusicFeature
{
    private readonly LobbyConfig _config;
    private const string AudioPlayerName = "CapyLobbyMusic";

    public LobbyMusicFeature(LobbyConfig config)
    {
        _config = config;
    }

    public void OnWaitingForPlayers()
    {
        if (!_config.IsEnabled || !_config.EnableMusic) return;
        PlayRandomLobbyTrack();
    }

    public void OnRoundStarted()
    {
        StopMusic();
    }

    public void OnRoundEnded()
    {
        StopMusic();
    }

    public void OnRestartingRound()
    {
        StopMusic();
    }

    public void PlayRandomLobbyTrack()
    {
        try
        {
            string audioDir = Path.Combine(Paths.Plugins, "CapyLib", "Audio", "Lobby");
            if (!Directory.Exists(audioDir))
            {
                audioDir = Path.Combine(Paths.Configs, "Plugins", "CapyLib", "Audio", "Lobby");
            }

            if (!Directory.Exists(audioDir))
            {
                Directory.CreateDirectory(audioDir);
                Log.Warn($"[LobbyMusic] Папка {audioDir} создана, но пуста.");
                return;
            }

            string[] oggFiles = Directory.GetFiles(audioDir, "*.ogg");
            if (oggFiles.Length == 0)
            {
                Log.Warn($"[LobbyMusic] Не найдено .ogg треков в папке: {audioDir}");
                return;
            }

            string randomFile = oggFiles[UnityEngine.Random.Range(0, oggFiles.Length)];
            string clipName = Path.GetFileNameWithoutExtension(randomFile).ToLowerInvariant();

            try
            {
                if (!AudioClipStorage.AudioClips.ContainsKey(clipName))
                {
                    AudioClipStorage.LoadClip(randomFile, clipName);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[LobbyMusic] Ошибка при загрузке аудиофайла {clipName}: {ex.Message}");
            }

            float volume = Mathf.Clamp01(_config.MusicVolume);

            var audioPlayer = AudioPlayer.CreateOrGet(AudioPlayerName, onIntialCreation: p =>
            {
                p.AddSpeaker("Main", isSpatial: false, maxDistance: 5000f, volume: volume);
            });

            audioPlayer.RemoveAllClips();
            audioPlayer.AddClip(clipName, volume: volume, loop: true, destroyOnEnd: false);

            Log.Info($"[LobbyMusic] Включен фоновый трек лобби: {clipName} (громкость: {volume})");
        }
        catch (Exception ex)
        {
            Log.Error($"[LobbyMusic] Ошибка воспроизведения музыки в лобби: {ex}");
        }
    }

    public void StopMusic()
    {
        try
        {
            if (AudioPlayer.TryGet(AudioPlayerName, out AudioPlayer player))
            {
                player.RemoveAllClips();
                player.Destroy();
                Log.Debug("[LobbyMusic] Музыка лобби остановлена.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[LobbyMusic] Ошибка при остановке музыки лобби: {ex}");
        }
    }
}
