using UnityEngine;

namespace JYW.FrameWork
{
    public enum FrameWorkModule
    {
        Presentation,
        ObjectMaker
    }

    /// <summary>
    /// 현재 프로젝트에서 자동 적용할 FrameWork 모듈을 선택합니다.
    /// 에셋은 Resources/FrameWork/Settings에 하나만 둡니다.
    /// </summary>
    public sealed class FrameWorkSettings : ScriptableObject
    {
        [SerializeField] private bool presentationEnabled = true;
        [SerializeField] private bool objectMakerEnabled = true;

        public bool PresentationEnabled => presentationEnabled;
        public bool ObjectMakerEnabled => objectMakerEnabled;

        public bool IsEnabled(FrameWorkModule module)
        {
            return module == FrameWorkModule.Presentation
                ? presentationEnabled
                : objectMakerEnabled;
        }
    }

    public static class FrameWorkFeatureGate
    {
        public const string ResourcePath = "FrameWork/Settings";

        private static FrameWorkSettings cachedSettings;

        public static bool IsEnabled(FrameWorkModule module)
        {
            if (cachedSettings == null)
                cachedSettings = Resources.Load<FrameWorkSettings>(ResourcePath);

            // Settings 에셋이 누락된 기존 프로젝트는 이전 동작을 그대로 유지합니다.
            return cachedSettings == null || cachedSettings.IsEnabled(module);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            cachedSettings = null;
        }

#if UNITY_EDITOR
        public static void InvalidateCache()
        {
            cachedSettings = null;
        }
#endif
    }
}
