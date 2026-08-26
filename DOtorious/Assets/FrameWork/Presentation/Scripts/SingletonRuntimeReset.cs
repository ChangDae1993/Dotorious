using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JYW.Game.EventPlay
{
    internal static class SingletonRuntimeReset
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            try
            {
                var resetTypes = new HashSet<Type>();
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
                {
                    Type[] types;
                    try
                    {
                        types = assemblies[assemblyIndex].GetTypes();
                    }
                    catch (ReflectionTypeLoadException exception)
                    {
                        types = exception.Types.Where(type => type != null).ToArray();
                    }

                    for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
                    {
                        Type current = types[typeIndex]?.BaseType;
                        while (current != null && current != typeof(object))
                        {
                            if (current.IsGenericType &&
                                current.GetGenericTypeDefinition() == typeof(Singleton<>))
                            {
                                if (!resetTypes.Add(current)) break;

                                current.GetField(
                                    "_instance",
                                    BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
                                var loadingScenes = current.GetField(
                                    "_loadingScenes",
                                    BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                                    as HashSet<string>;
                                loadingScenes?.Clear();
                                break;
                            }
                            current = current.BaseType;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Singleton 정적 상태 초기화 실패: {exception.Message}");
            }
        }
    }
}
