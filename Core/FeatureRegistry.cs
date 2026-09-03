using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Exiled.API.Features;
using Capy.NoRules.Config;

namespace Capy.NoRules.Core;

/// <summary>
/// Реестр и фабрика фичей, концептов и контроллеров CapyLib.NoRules на базе рефлексии.
/// Автоматически обнаруживает все типы фичей, сопоставляет их с конфигурациями и управляет их жизненным циклом.
/// </summary>
public static class FeatureRegistry
{
    private static readonly Dictionary<Type, object> _features = new();
    private static readonly List<object> _initializedFeatures = new();
    private static readonly object _sync = new();

    public static IReadOnlyDictionary<Type, object> Features
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<Type, object>(_features);
            }
        }
    }

    /// <summary>
    /// Автоматическая инициализация всех фичей, аддонов и концептов через рефлексию.
    /// </summary>
    public static void InitializeAll(NoRulesConfig config)
    {
        lock (_sync)
        {
            _features.Clear();
            _initializedFeatures.Clear();

            var featureTypes = typeof(NoRulesPlugin).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && (
                    t.Name.EndsWith("Feature") ||
                    t.Name.EndsWith("Concept") ||
                    t.Name.EndsWith("Controller")
                ))
                .ToList();

            var configProperties = typeof(NoRulesConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var type in featureTypes)
            {
                try
                {
                    object? instance = null;
                    var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

                    // Сначала пробуем конструктор с типизированным конфигом
                    foreach (var ctor in constructors)
                    {
                        var parameters = ctor.GetParameters();
                        if (parameters.Length == 1)
                        {
                            var paramType = parameters[0].ParameterType;
                            var matchingProp = configProperties.FirstOrDefault(p => paramType.IsAssignableFrom(p.PropertyType));
                            if (matchingProp != null)
                            {
                                var cfgVal = matchingProp.GetValue(config);
                                instance = Activator.CreateInstance(type, cfgVal);
                                break;
                            }
                        }
                    }

                    // Если не подошел конструктор с конфигом, пробуем конструктор без параметров
                    if (instance == null)
                    {
                        var parameterlessCtor = constructors.FirstOrDefault(c => c.GetParameters().Length == 0);
                        if (parameterlessCtor != null)
                        {
                            instance = Activator.CreateInstance(type);
                        }
                    }

                    if (instance != null)
                    {
                        _features[type] = instance;
                        _initializedFeatures.Add(instance);

                        // Автоматический вызов Enable() если метод существует
                        var enableMethod = type.GetMethod("Enable", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        enableMethod?.Invoke(instance, null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[FeatureRegistry] Ошибка рефлексивной инициализации {type.Name}: {ex}");
                }
            }

            Log.Debug($"[FeatureRegistry] Рефлексией успешно зарегистрировано и активировано {_features.Count} фичей и концептов.");
        }
    }

    /// <summary>
    /// Получить зарегистрированный экземпляр фичи по её типу.
    /// </summary>
    public static T Get<T>() where T : class
    {
        lock (_sync)
        {
            if (_features.TryGetValue(typeof(T), out var obj) && obj is T typed)
                return typed;

            var fallback = _features.Values.OfType<T>().FirstOrDefault();
            if (fallback != null)
                return fallback;

            return null!;
        }
    }

    /// <summary>
    /// Рефлексивная выгрузка и освобождение всех фичей.
    /// </summary>
    public static void ShutdownAll()
    {
        lock (_sync)
        {
            for (int i = _initializedFeatures.Count - 1; i >= 0; i--)
            {
                var feat = _initializedFeatures[i];
                try
                {
                    var type = feat.GetType();
                    var disableMethod = type.GetMethod("Disable", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (disableMethod != null)
                    {
                        disableMethod.Invoke(feat, null);
                    }
                    else if (feat is IDisposable disp)
                    {
                        disp.Dispose();
                    }
                    else
                    {
                        var despawnMethod = type.GetMethod("DespawnLobby", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        despawnMethod?.Invoke(feat, null);

                        var stopMusicMethod = type.GetMethod("StopMusic", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        stopMusicMethod?.Invoke(feat, null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[FeatureRegistry] Ошибка выгрузки {feat.GetType().Name}: {ex.Message}");
                }
            }

            _features.Clear();
            _initializedFeatures.Clear();
            Log.Info("[FeatureRegistry] Все фичи NoRules успешно выгружены.");
        }
    }
}
