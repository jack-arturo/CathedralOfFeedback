using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Orb that can be held by <see cref="OrbGrabber"/>. While held it leaves a trail, and the
    /// movement history is replayed as beat-synced echo ghosts (see <see cref="TrailEcho"/>).
    /// Uses plain OVRInput grabbing to stay dependency-light; swap in Meta Interaction SDK
    /// Grabbable/HandGrabInteractable later if hand tracking is wanted.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GrabbableOrb : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        public bool IsHeld { get; private set; }
        public TrailRenderer Trail;
        Transform holder;
        Vector3 lastPos, velocity;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            if (Trail == null) Trail = GetComponentInChildren<TrailRenderer>();
            if (Trail != null) Trail.emitting = false;
        }

        public void Grab(Transform hand)
        {
            holder = hand; IsHeld = true;
            Body.isKinematic = true;
            lastPos = transform.position;
            if (Trail != null) Trail.emitting = true;
        }

        public void Release()
        {
            IsHeld = false; holder = null;
            Body.isKinematic = false;
            Body.linearVelocity = velocity;
            if (Trail != null) Trail.emitting = false;
        }

        void Update()
        {
            if (!IsHeld) return;
            transform.position = holder.position;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            velocity = Vector3.Lerp(velocity, (transform.position - lastPos) / dt, 0.5f);
            lastPos = transform.position;
        }
    }
}
