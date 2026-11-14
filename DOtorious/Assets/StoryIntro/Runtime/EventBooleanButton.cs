using JYW.Game.EventPlay;
using UnityEngine;

namespace Dotorious.StoryIntro
{
    /// <summary>Publishes a configurable Presentation bool from a Button click.</summary>
    public sealed class EventBooleanButton : MonoBehaviour
    {
        [SerializeField] private string signalName;
        private bool pending;

        public void Configure(string name) => signalName = name;
        public void Publish() { pending = true; TryPublish(); }
        private void Update() { if (pending) TryPublish(); }

        private void TryPublish()
        {
            var manager = EventPlayManager.Instance;
            if (manager == null || !manager.isActiveAndEnabled) return;
            manager.SetGlobals(signalName, true);
            pending = false;
        }
    }
}
