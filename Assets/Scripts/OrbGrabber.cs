using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// One per hand anchor. With tracked hands, pinch an orb or just close your hand around
    /// it; with a controller, grip or trigger. Tracked fingertips also paint faintly into the feedback
    /// field, so bare hands leave traces on the walls even without an orb.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class OrbGrabber : MonoBehaviour
    {
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;
        public OVRHand hand;
        public OVRSkeleton skeleton;
        public float reach = 0.2f;
        [Range(0, 1)] public float paintHue = 0.55f;

        public static readonly System.Collections.Generic.List<OrbGrabber> All = new System.Collections.Generic.List<OrbGrabber>();
        /// <summary>True for one frame: middle-finger pinch with a free hand, or A / X on a controller.</summary>
        public bool MenuDown { get; private set; }
        public Vector3 GrabPoint { get; private set; }
        public bool HandMode { get; private set; }
        public bool Holding => held != null;
        public Vector3 PalmPoint => HandMode && palm != null ? palm.position : transform.position;
        /// <summary>Index fingertip when the hand is tracked, otherwise the controller.</summary>
        public Vector3 TouchPoint => HandMode && indexTip != null ? indexTip.position : transform.position;

        GrabbableOrb held;
        Transform thumbTip, indexTip, middleTip, ringTip, littleTip, palm;
        bool pinching, gripping, handOpen = true, vibrating;
        Vector3 probe;
        float lostTime;
        Vector3 lastPoint;
        float menuHold, menuCooldown;

        void OnEnable() { All.Add(this); }

        void ResolveBones()
        {
            if (thumbTip != null || skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null) return;
            var type = skeleton.GetSkeletonType();
            bool xr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;
            var thumbId = xr ? OVRSkeleton.BoneId.XRHand_ThumbTip : OVRSkeleton.BoneId.Hand_ThumbTip;
            var indexId = xr ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;
            var middleId = xr ? OVRSkeleton.BoneId.XRHand_MiddleTip : OVRSkeleton.BoneId.Hand_MiddleTip;
            var ringId = xr ? OVRSkeleton.BoneId.XRHand_RingTip : OVRSkeleton.BoneId.Hand_RingTip;
            var littleId = xr ? OVRSkeleton.BoneId.XRHand_LittleTip : OVRSkeleton.BoneId.Hand_PinkyTip;
            var palmId = xr ? OVRSkeleton.BoneId.XRHand_Palm : OVRSkeleton.BoneId.Hand_Middle1;
            foreach (var b in skeleton.Bones)
            {
                if (b == null || b.Transform == null) continue;
                if (b.Id == thumbId) thumbTip = b.Transform;
                else if (b.Id == indexId) indexTip = b.Transform;
                else if (b.Id == middleId) middleTip = b.Transform;
                else if (b.Id == ringId) ringTip = b.Transform;
                else if (b.Id == littleId) littleTip = b.Transform;
                else if (b.Id == palmId) palm = b.Transform;
            }
        }

        /// <summary>
        /// How closed the hand is: mean fingertip-to-palm distance in metres (about 0.11 flat,
        /// 0.07 wrapped round an orb, 0.04 in a fist). Also returns the middle of the grip.
        /// </summary>
        bool Grip(out float span, out Vector3 centre)
        {
            span = 1f; centre = transform.position;
            if (palm == null || indexTip == null || middleTip == null || ringTip == null || littleTip == null) return false;
            Vector3 p = palm.position;
            Vector3 tips = (indexTip.position + middleTip.position + ringTip.position + littleTip.position) * 0.25f;
            span = ((indexTip.position - p).magnitude + (middleTip.position - p).magnitude
                  + (ringTip.position - p).magnitude + (littleTip.position - p).magnitude) * 0.25f;
            centre = (p + tips) * 0.5f;
            return true;
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            // A headset lying on a table still sees hands. Nobody is playing unless it is being worn.
            bool worn = Application.isEditor || OVRPlugin.userPresent;
            bool tracked = worn && hand != null && hand.IsTracked;
            bool down;
            if (tracked)
            {
                HandMode = true; lostTime = 0f;
                ResolveBones();
                // Two ways to take an orb: pinch it, or simply close your hand around it.
                Vector3 pinchPoint = thumbTip != null && indexTip != null ? (thumbTip.position + indexTip.position) * 0.5f : transform.position;
                float s = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                pinching = pinching ? s > 0.25f : s > 0.6f;
                bool hasGrip = Grip(out float span, out Vector3 gripPoint);
                gripping = hasGrip && (gripping ? span < 0.092f : span < 0.076f);
                handOpen = !hasGrip || span > 0.095f;
                Vector3 want = pinching || !gripping ? pinchPoint : gripPoint;
                // Ease between the two anchor points so a held orb never jumps when the grip changes.
                GrabPoint = held != null ? Vector3.Lerp(GrabPoint, want, 0.35f) : want;
                probe = gripping && !pinching ? gripPoint : pinchPoint;
                down = pinching || gripping;
            }
            else
            {
                bool trigger = worn && (OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controller)
                               || OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller));
                // A hand that drops out of tracking for a moment should not drop the orb.
                lostTime += dt;
                if (HandMode && held != null && lostTime < 0.4f && !trigger) down = true;
                else { HandMode = false; pinching = false; gripping = false; handOpen = true; GrabPoint = probe = transform.position; down = trigger; }
            }
#if UNITY_EDITOR
            if (controller == OVRInput.Controller.RTouch && Input.GetKey(KeyCode.Space)) down = true;
#endif
            if (down && held == null)
            {
                // Search from both the pinch point and the middle of the grip, generously.
                float best = reach * reach;
                Vector3 alt = HandMode && palm != null ? palm.position : probe;
                GrabbableOrb pick = null;
                var all = GrabbableOrb.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].IsHeld) continue;
                    Vector3 op = all[i].transform.position;
                    float d = Mathf.Min((op - probe).sqrMagnitude, (op - alt).sqrMagnitude);
                    if (d < best) { best = d; pick = all[i]; }
                }
                if (pick != null) { held = pick; held.Grab(this); }
            }
            else if (!down && held != null)
            {
                held.Release(); held = null;
            }

            // Menu gesture. Needs a deliberate middle pinch so playing never triggers it.
            MenuDown = false;
            menuCooldown -= dt;
            if (HandMode)
            {
                bool mid = held == null && !gripping && hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle) > 0.9f
                           && hand.GetFingerPinchStrength(OVRHand.HandFinger.Index) < 0.3f
                           && hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring) < 0.3f;
                menuHold = mid ? menuHold + dt : 0f;
                if (menuHold > 0.18f && menuCooldown <= 0f) { MenuDown = true; menuCooldown = 1.2f; menuHold = 0f; }
            }
            else if (OVRInput.GetDown(OVRInput.Button.One, controller) && menuCooldown <= 0f) { MenuDown = true; menuCooldown = 0.4f; }
#if UNITY_EDITOR
            if (controller == OVRInput.Controller.RTouch && Input.GetKeyDown(KeyCode.P)) MenuDown = true;
#endif

            // Controller haptics follow the beat while you are holding the instrument.
            if (!HandMode)
            {
                if (held != null)
                {
                    var an = SpectrumAnalyzer.Instance;
                    float p = an != null ? an.BeatPulse : 0f;
                    OVRInput.SetControllerVibration(0.5f, 0.06f + p * p * 0.45f, controller);
                    vibrating = true;
                }
                else if (vibrating) { OVRInput.SetControllerVibration(0f, 0f, controller); vibrating = false; }
            }

            // Bare fingers paint, brighter the faster they move.
            var field = FeedbackField.Instance;
            if (field != null && HandMode && held == null && indexTip != null)
            {
                Vector3 tip = indexTip.position;
                float speed = (tip - lastPoint).magnitude / dt;
                lastPoint = tip;
                float k = Mathf.Clamp01((speed - 0.25f) * 0.8f);
                if (k > 0.02f) field.AddSplat(tip, Color.HSVToRGB(paintHue, 0.7f, 1f), 0.25f + 0.9f * k, 0.03f);
            }
        }

        void OnDisable()
        {
            All.Remove(this);
            if (held != null) { held.Release(); held = null; }
            if (vibrating) { OVRInput.SetControllerVibration(0f, 0f, controller); vibrating = false; }
        }
    }
}
