using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WhoCastThat.Audio;

namespace WhoCastThat.Visuals
{
    /// <summary>
    /// The black screen with the spinning logo that covers the Boot -> Lobby hand-off.
    ///
    /// It holds for exactly as long as the transition sting, driven by the clip's own length via
    /// <see cref="GameAudioDirector.TransitionStingerStarted"/> — so the sound and the picture end
    /// together no matter which clip is swapped in later.
    ///
    /// It covers the ARRIVAL rather than the departure, on purpose. SceneManager.LoadScene is
    /// synchronous and is called from the login flow, which is a teammate's file; the only way to
    /// black out before it would be to intercept that call. Covering arrival works out better
    /// anyway — the first frames of a freshly loaded scene are where the hitching actually is.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        private const string SettingsResourceName = "LoadingScreen";

        private static LoadingScreen instance;

        private LoadingScreenSettings settings;
        private Canvas canvas;
        private Image black;
        private RectTransform logo;
        private Coroutine routine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            var go = new GameObject("LoadingScreen");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LoadingScreen>();
        }

        private void Awake()
        {
            settings = Resources.Load<LoadingScreenSettings>(SettingsResourceName);
            if (settings == null)
            {
                Debug.LogWarning($"[LoadingScreen] No {SettingsResourceName} in Resources — the " +
                                 "transition will simply not be covered.");
                enabled = false;
                return;
            }

            GameAudioDirector.TransitionStingerStarted += OnTransition;
        }

        private void OnDestroy()
        {
            GameAudioDirector.TransitionStingerStarted -= OnTransition;

            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnTransition(float stingerSeconds)
        {
            float hold = stingerSeconds > 0.05f ? stingerSeconds : settings.FallbackSeconds;

            if (routine != null)
            {
                StopCoroutine(routine);
            }
            routine = StartCoroutine(Show(hold));
        }

        private IEnumerator Show(float holdSeconds)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                // Nothing to parent to and no way to know where the eyes are. Better to skip the
                // curtain than to leave a black panel stranded at the world origin.
                yield break;
            }

            Build(cam);

            // The whole point is that the black is up before the player sees anything, so it starts
            // opaque rather than fading in.
            SetAlpha(1f);

            float elapsed = 0f;
            float fade = Mathf.Max(0.01f, settings.FadeOutSeconds);
            float visible = Mathf.Max(fade, holdSeconds);

            while (elapsed < visible)
            {
                elapsed += Time.unscaledDeltaTime;
                Animate(elapsed);

                // Fade out at the END of the hold, so black lifts exactly as the sting finishes.
                float remaining = visible - elapsed;
                SetAlpha(remaining >= fade ? 1f : Mathf.Clamp01(remaining / fade));

                yield return null;
            }

            Destroy(canvas.gameObject);
            canvas = null;
            black = null;
            logo = null;
            routine = null;
        }

        private void Build(Camera cam)
        {
            if (canvas != null)
            {
                Destroy(canvas.gameObject);
            }

            var go = new GameObject("LoadingCurtain", typeof(RectTransform));

            // Parented to the head. A loading screen is the one panel that SHOULD follow the gaze:
            // the world behind it is mid-swap and must not be glimpsed around the edges. This is
            // the opposite of the pause menu, which is world-locked for exactly the same reason
            // reversed -- there, the world is what you want to see.
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, settings.Distance);
            go.transform.localRotation = Quaternion.identity;

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Well beyond anything else, so no world geometry or UI can poke through the curtain.
            canvas.sortingOrder = 32000;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(1400f, 1400f);

            // 1400px at 0.003 is 4.2m across at 1m away: about 130 degrees, wider than the headset
            // can see, so there is no edge to peek around.
            go.transform.localScale = Vector3.one * 0.003f;

            var blackGo = new GameObject("Black", typeof(RectTransform));
            blackGo.transform.SetParent(rt, false);
            black = blackGo.AddComponent<Image>();
            black.color = Color.black;
            var brt = black.rectTransform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;

            var logoGo = new GameObject("Logo", typeof(RectTransform));
            logoGo.transform.SetParent(rt, false);
            var image = logoGo.AddComponent<RawImage>();
            image.texture = settings.Logo;
            image.raycastTarget = false;
            logo = image.rectTransform;

            // Size is given in metres and converted through the canvas scale, so retuning the
            // canvas cannot silently resize the logo. RawImage has no preserveAspect, so the
            // aspect is applied here from the texture itself rather than trusting a square source.
            float pixels = settings.LogoSize / 0.003f;
            float aspect = settings.Logo != null && settings.Logo.height > 0
                ? (float)settings.Logo.width / settings.Logo.height
                : 1f;
            logo.sizeDelta = new Vector2(pixels * aspect, pixels);

            if (settings.Logo == null)
            {
                Debug.LogWarning("[LoadingScreen] No logo texture assigned — showing a plain " +
                                 "black curtain.");
                image.enabled = false;
            }
        }

        private void Animate(float elapsed)
        {
            if (logo == null)
            {
                return;
            }

            // Rolls about Z rather than spinning about Y: a flat sprite turned edge-on vanishes,
            // which reads as a glitch rather than as loading.
            logo.localRotation = Quaternion.Euler(0f, 0f, -elapsed * settings.SpinDegreesPerSecond);

            float bob = Mathf.Sin(elapsed * settings.FloatDegreesPerSecond * Mathf.Deg2Rad)
                        * settings.FloatAmplitude;

            // Converted out of metres into canvas pixels for the same reason as the size above.
            logo.anchoredPosition = new Vector2(0f, bob / 0.003f);
        }

        private void SetAlpha(float alpha)
        {
            if (black != null)
            {
                black.color = new Color(0f, 0f, 0f, alpha);
            }

            if (logo != null)
            {
                var image = logo.GetComponent<RawImage>();
                if (image != null)
                {
                    Color c = image.color;
                    image.color = new Color(c.r, c.g, c.b, alpha);
                }
            }
        }
    }
}
