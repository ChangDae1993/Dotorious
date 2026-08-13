using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;


//스태틱 싱글톤으로 사용한다는 것은 다른 곳에서 이것을 참조할 일이 있다는 것,
//즉, 이 싱글턴이 속한 씬은 반드시 로드되어 있어야 한다는 뜻이다.
//2026-01-28: 그래서 싱글턴이 속한 씬을 자동으로 additive 로드하는 기능을 추가

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    // 같은 프레임/타이밍에 여러 곳에서 Instance를 호출하면
    // LoadScene(Additive)가 중복 호출되어 같은 씬이 2번 로드되는 문제가 생길 수 있어
    // 로드 "진행 중"인 씬 이름을 전역으로 추적한다.
    private static readonly System.Collections.Generic.HashSet<string> _loadingScenes =
        new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

    private static string GetOwningSceneNameForType()
    {
        try
        {
            // 각 싱글턴 타입이 스스로 정적으로 제공:
            // public static string OwningSceneName => "SceneName";
            // 또는 public const string OwningSceneName = "SceneName";
            var prop = typeof(T).GetProperty(
                "OwningSceneName",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (prop != null && prop.PropertyType == typeof(string))
                return prop.GetValue(null) as string;

            var field = typeof(T).GetField(
                "OwningSceneName",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field != null && field.FieldType == typeof(string))
                return field.GetValue(null) as string;
        }
        catch { }

        return null;
    }

    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<T>();

                if (_instance == null)
                {
                    EnsureOwningSceneLoadedAdditivelyForType();
                    _instance = FindObjectOfType<T>();
                }

                if (_instance == null)
                    return null;
            }

            return _instance;
        }
    }

    private static void EnsureOwningSceneLoadedAdditivelyForType()
    {
        var sceneName = GetOwningSceneNameForType();
        if (string.IsNullOrEmpty(sceneName)) return;

        if (_loadingScenes.Contains(sceneName)) return;

        try
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded) return;

            _loadingScenes.Add(sceneName);

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op != null)
            {
                op.completed += _ =>
                {
                    try { _loadingScenes.Remove(sceneName); } catch { }
                };
            }
            else
            {
                _loadingScenes.Remove(sceneName);
            }
        }
        catch (Exception ex)
        {
            try { _loadingScenes.Remove(sceneName); } catch { }
            Debug.LogWarning($"[Singleton<{typeof(T).Name}>] 씬 '{sceneName}' additive 로드 실패: {ex.Message}");
        }
    }

    protected virtual void Awake()
    {
        // (주의) 여기서는 이미 오브젝트가 존재한다=씬이 로드되었다는 뜻이므로
        // EnsureOwnerSceneLoadedAdditively는 사실상 대부분 no-op 입니다.
        EnsureOwnerSceneLoadedAdditively();

        if (_instance == null)
        {
            _instance = this as T;
            return;
        }

        if (_instance == this) return;

        var existingMb = _instance as MonoBehaviour;
        var existingGo = existingMb?.gameObject;

        bool existingIsPersistent = existingGo != null && IsInDontDestroyOnLoadScene(existingGo);
        bool thisIsPersistent = IsInDontDestroyOnLoadScene(gameObject);

        if (existingIsPersistent && !thisIsPersistent)
        {
            try
            {
                if (existingGo != null)
                    Destroy(existingGo);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Singleton<{typeof(T).Name}>] 기존 전역 인스턴스 파괴 중 예외: {ex.Message}");
            }

            _instance = this as T;
            return;
        }

        Destroy(gameObject);
    }

    private void EnsureOwnerSceneLoadedAdditively()
    {
        try
        {
            var ownerScene = gameObject.scene;
            if (!ownerScene.IsValid()) return;
            if (ownerScene.isLoaded) return;

            var sceneName = ownerScene.name;
            if (string.IsNullOrEmpty(sceneName)) return;

            if (_loadingScenes.Contains(sceneName)) return;

            var loadedScene = SceneManager.GetSceneByName(sceneName);
            if (loadedScene.IsValid() && loadedScene.isLoaded) return;

            _loadingScenes.Add(sceneName);
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op != null)
            {
                op.completed += _ =>
                {
                    try { _loadingScenes.Remove(sceneName); } catch { }
                };
            }
            else
            {
                _loadingScenes.Remove(sceneName);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Singleton<{typeof(T).Name}>] 소유 씬 additive 로드 실패: {ex.Message}");
        }
    }

    private bool IsInDontDestroyOnLoadScene(GameObject go)
    {
        return go != null && go.scene.name == "DontDestroyOnLoad";
    }

    private static bool HasAssignedSerializedFields(T obj)
    {
        if (obj == null) return false;
        var type = obj.GetType();
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (var field in fields)
        {
            bool isPublic = field.IsPublic;
            bool isSerializedField = field.GetCustomAttributes(typeof(SerializeField), true).Any();
            if (!isPublic && !isSerializedField) continue;

            var value = field.GetValue(obj);
            if (value == null) continue;

            if (!field.FieldType.IsValueType) return true;

            var defaultValue = Activator.CreateInstance(field.FieldType);
            if (!Equals(value, defaultValue)) return true;
        }
        return false;
    }
}

internal static class SingletonAutoInitializer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitAllSingletons()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var t in types)
                {
                    if (t == null || !t.IsClass || t.IsAbstract) continue;

                    var cur = t.BaseType;
                    while (cur != null && cur != typeof(object))
                    {
                        if (cur.IsGenericType && cur.GetGenericTypeDefinition() == typeof(Singleton<>))
                        {
                            try
                            {
                                var singletonClosed = typeof(Singleton<>).MakeGenericType(t);
                                var prop = singletonClosed.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                                prop?.GetValue(null);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"Singleton 자동 초기화 실패: {t.FullName} -> {ex.Message}");
                            }
                            break;
                        }
                        cur = cur.BaseType;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"SingletonAutoInitializer 실패: {ex.Message}");
        }
    }
}