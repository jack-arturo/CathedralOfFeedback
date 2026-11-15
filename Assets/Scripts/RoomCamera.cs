using System.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Borrows the headset's passthrough camera (Quest 3, Horizon OS v74+) so the real room
    /// becomes the stained glass: each lancet window shows a leaded mosaic of whatever the
    /// wearer is looking at, and its colours feed the light that climbs the walls.
    ///
    /// The feed is permission-gated and device-only, so "no camera" is a designed outcome:
    /// the mix simply stays at zero and the building is purely virtual. Frames are copied
    /// into a render texture this component owns; nothing ever samples the feed's own texture.
    /// Frames never leave the headset.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class RoomCamera : MonoBehaviour
    {
        public static RoomCamera Instance { get; private set; }
        const string HeadsetCamera = "horizonos.permission.HEADSET_CAMERA";

        [SerializeField] int requestWidth = 640, requestHeight = 480;
        [Tooltip("Flip vertically if the device delivers frames upside down.")]
        public bool flipY;
        [Tooltip("Share of the frame's width shown in one window (they are tall and narrow).")]
        [Range(0.1f, 1f)] public float strip = 0.46f;

        /// <summary>0..1, set by the director: how much of the room the glass should show.</summary>
        public float Mix { get; set; }
        public bool HasFrame { get; private set; }
        public float Shown { get; private set; }

        static readonly int RoomId = Shader.PropertyToID("_CathedralRoom");
        static readonly int ParamsId = Shader.PropertyToID("_CathedralRoomParams");

        WebCamTexture cam;
        RenderTexture target;
        bool decode;

        void Awake() { Instance = this; Push(0f); Shader.SetGlobalTexture(RoomId, Texture2D.blackTexture); }

        bool opening;

        /// <summary>
        /// The camera is only held open while the room is actually wanted. Horizon OS mutes an
        /// app's audio for as long as it holds a headset camera (the audio service reports the
        /// player as mutedState:appOps), so holding it open by default would silence the piece.
        /// </summary>
        IEnumerator Open()
        {
            opening = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Granted())
            {
                bool done = false;
                var cb = new UnityEngine.Android.PermissionCallbacks();
                cb.PermissionGranted += _ => done = true;
                cb.PermissionDenied += _ => done = true;
                UnityEngine.Android.Permission.RequestUserPermissions(new[] { UnityEngine.Android.Permission.Camera, HeadsetCamera }, cb);
                for (float t = 0f; !done && t < 90f; t += Time.unscaledDeltaTime) yield return null;
                yield return new WaitForSecondsRealtime(0.75f);          // the second permission answers after the first
            }
            if (!Granted()) { Debug.Log("[Cathedral] room camera: permission not granted, staying virtual"); opening = false; yield break; }
            yield return null;

            // Quest lists a front-facing avatar camera (for video calls) ahead of the two
            // passthrough cameras. Only ever open a world-facing one.
            string pick = null;
            foreach (var d in WebCamTexture.devices) if (!d.isFrontFacing) { pick = d.name; break; }
            if (pick == null) { Debug.Log("[Cathedral] room camera: no world-facing camera, staying virtual"); opening = false; yield break; }
            if (Mix > 0f)
            {
                Debug.Log("[Cathedral] room camera: opening " + pick);
                cam = new WebCamTexture(pick, requestWidth, requestHeight, 30);
                cam.Play();
            }
#else
            yield return null;
#endif
            opening = false;
        }

        void Close()
        {
            if (cam == null) return;
            Debug.Log("[Cathedral] room camera: closing");
            cam.Stop(); Destroy(cam); cam = null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool Granted() =>
            UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) &&
            UnityEngine.Android.Permission.HasUserAuthorizedPermission(HeadsetCamera);
#endif

        void Update()
        {
            if (Mix > 0f && cam == null && !opening) StartCoroutine(Open());
            else if (Mix <= 0f && cam != null && Shown <= 0f) Close();
            if (cam != null && cam.isPlaying && cam.didUpdateThisFrame && cam.width > 16)
            {
                if (!HasFrame) Debug.Log($"[Cathedral] room camera: first frame {cam.width}x{cam.height} {cam.graphicsFormat} rot {cam.videoRotationAngle} mirrored {cam.videoVerticallyMirrored}");
                LatchFrom(cam);
            }
            Shown = Mathf.MoveTowards(Shown, HasFrame ? Mix : 0f, Time.deltaTime * 0.6f);
            Push(Shown);
        }

        /// <summary>Copy a frame into our own target. Source-agnostic so a plain texture can stand in for the feed.</summary>
        public void LatchFrom(Texture source)
        {
            if (source == null) return;
            if (target == null || target.width != source.width || target.height != source.height || target.graphicsFormat != source.graphicsFormat)
            {
                ReleaseTarget();
                var desc = new RenderTextureDescriptor(source.width, source.height, source.graphicsFormat, 0) { useMipMap = true, autoGenerateMips = true };
                target = new RenderTexture(desc) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, name = "CathedralRoom", hideFlags = HideFlags.DontSave };
                target.Create();
                // Video is sRGB-encoded. If the format does not say so, the shaders decode it themselves.
                decode = !GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            }
            Graphics.Blit(source, target);
            HasFrame = true;
            Shader.SetGlobalTexture(RoomId, target);
        }

        public void Push(float shown)
        {
            Shader.SetGlobalVector(ParamsId, new Vector4(shown, decode ? 1f : 0f, flipY ? 1f : 0f, strip));
        }

        void ReleaseTarget()
        {
            if (target == null) return;
            target.Release(); DestroyImmediate(target); target = null;
        }

        void OnDisable() { Close(); HasFrame = false; Push(0f); }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (cam != null) { cam.Stop(); Destroy(cam); cam = null; }
            Shader.SetGlobalTexture(RoomId, Texture2D.blackTexture);
            ReleaseTarget();
        }
    }
}
