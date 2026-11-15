using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>Put on a controller anchor. Grip (hand trigger) grabs the nearest orb in reach.</summary>
    public class OrbGrabber : MonoBehaviour
    {
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;
        public float reach = 0.15f;
        static readonly Collider[] hits = new Collider[8];
        GrabbableOrb held;

        void Update()
        {
            bool down = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controller)
                        || OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller);
#if UNITY_EDITOR
            if (controller == OVRInput.Controller.RTouch && Input.GetKey(KeyCode.Space)) down = true;
#endif
            if (down && held == null)
            {
                int n = Physics.OverlapSphereNonAlloc(transform.position, reach, hits);
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var orb = hits[i].GetComponentInParent<GrabbableOrb>();
                    if (orb == null || orb.IsHeld) continue;
                    float d = (orb.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; held = orb; }
                }
                if (held != null) { held.Grab(transform); OVRInput.SetControllerVibration(0.3f, 0.3f, controller); }
            }
            else if (!down && held != null)
            {
                held.Release(); held = null;
            }
        }
    }
}
