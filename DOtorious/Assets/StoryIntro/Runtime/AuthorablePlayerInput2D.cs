using JYW.Game.EventPlay;
using UnityEngine;
using UnityEngine.Events;

namespace Dotorious.StoryIntro
{
    /// <summary>Flat-ground controls with editor-writable permissions and optional movement signals.</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class AuthorablePlayerInput2D : MonoBehaviour
    {
        [SerializeField] private Transform facingVisual;
        [SerializeField] private LayerMask obstacles = ~0;
        [SerializeField] private float walkSpeed = 5.6f;
        [SerializeField] private float dashSpeed = 16f;
        [SerializeField] private float dashDuration = .22f;
        [SerializeField] private KeyCode dashKey = KeyCode.D;
        [SerializeField] private string movementSignal, successfulDashSignal;
        [SerializeField] private UnityEvent onInteract = new UnityEvent(), onAttack = new UnityEvent();

        public bool MoveAllowed { get; set; }
        public bool DashAllowed { get; set; }
        public bool InteractAllowed { get; set; }
        public bool AttackAllowed { get; set; }
        private Rigidbody2D body;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];
        private float horizontal, facing = 1, dashFacing = 1, remaining, priorX;
        private bool dashRequested, movedLastStep, dashedLastStep, alreadyMoved, alreadyDashed;
        public int SuccessfulDashCount { get; private set; }

        public void Configure(Transform visual, string moved, string dashed)
        {
            facingVisual = visual;
            movementSignal = moved;
            successfulDashSignal = dashed;
        }

        private void Awake() { body = GetComponent<Rigidbody2D>(); priorX = body.position.x; }
        private void Update()
        {
            SetHorizontal((Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) -
                          (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f));
            if (Input.GetKeyDown(dashKey)) RequestDash();
            if (InteractAllowed && Input.GetKeyDown(KeyCode.E)) onInteract.Invoke();
            if (AttackAllowed && Input.GetKeyDown(KeyCode.J)) onAttack.Invoke();
        }

        public void SetHorizontal(float value) => horizontal = Mathf.Clamp(value, -1, 1);
        public void RequestDash() => dashRequested |= DashAllowed;

        private void FixedUpdate() => SimulateControls(Time.fixedDeltaTime);

        public void SimulateControls(float dt)
        {
            float actual = body.position.x - priorX;
            if (Mathf.Abs(actual) > .001f)
            {
                if (movedLastStep && !alreadyMoved) alreadyMoved = Publish(movementSignal);
                if (dashedLastStep)
                {
                    SuccessfulDashCount++;
                    if (!alreadyDashed) alreadyDashed = Publish(successfulDashSignal);
                    // This request counts once, even though subsequent dash frames may move.
                    dashedLastStep = false;
                }
            }

            priorX = body.position.x;
            if (Mathf.Abs(horizontal) > .001f && MoveAllowed && remaining <= 0)
                facing = Mathf.Sign(horizontal);
            if (facingVisual != null)
            {
                Vector3 scale = facingVisual.localScale;
                scale.x = facing * Mathf.Abs(scale.x);
                facingVisual.localScale = scale;
            }

            if (!DashAllowed) remaining = 0;
            bool dashStarted = false;
            if (dashRequested && DashAllowed && remaining <= 0 &&
                Travel(facing, dashSpeed * dt) > .001f)
            {
                remaining = dashDuration;
                dashFacing = facing;
                dashStarted = true;
            }
            dashRequested = false;

            bool dashing = remaining > 0;
            float direction = dashing ? dashFacing : Mathf.Sign(horizontal);
            float requested = dashing ? dashSpeed * Mathf.Min(remaining, dt) :
                MoveAllowed ? Mathf.Abs(horizontal) * walkSpeed * dt : 0;
            float distance = Travel(direction, requested);
            remaining = dashing && distance > .001f ? Mathf.Max(0, remaining - dt) : 0;
            movedLastStep = distance > .001f;
            dashedLastStep = dashStarted && movedLastStep;
            if (movedLastStep) body.MovePosition(body.position + Vector2.right * (distance * direction));
        }

        private float Travel(float direction, float distance)
        {
            if (distance <= .001f) return 0;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(obstacles);
            int count = body.Cast(Vector2.right * direction, filter, hits, distance + .01f);
            for (int i = 0; i < count; i++)
                if (Mathf.Abs(hits[i].normal.x) > .3f)
                    distance = Mathf.Min(distance, Mathf.Max(0, hits[i].distance - .01f));
            return distance;
        }

        private static bool Publish(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            var manager = EventPlayManager.Instance;
            if (manager == null || !manager.isActiveAndEnabled) return false;
            manager.SetGlobals(key, true);
            return true;
        }
    }
}
