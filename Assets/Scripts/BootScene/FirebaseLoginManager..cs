using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FirebaseLoginManager : MonoBehaviour
{
    [Header("Firebase / Unity Auth")]
    [SerializeField] private string unityOidcProviderName = "oidc-firebase";
    [SerializeField] private string lobbySceneName = "LobbyScene";

    [Header("Firebase Database")]
    [SerializeField] private string databaseUrl = "https://whocastthat-default-rtdb.asia-southeast1.firebasedatabase.app/";

    [Header("Scene Transition")]
    [Tooltip("Seconds for the black screen to fade in before loading the next scene.")]
    [SerializeField] private float fadeDuration = 2f;

    [Header("UI References")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_Text statusText;

    private FirebaseAuth firebaseAuth;
    private DatabaseReference databaseRoot;

    private const string LocalUsernameKey = "PlayerUsername";
    private const string LocalFirebaseUidKey = "FirebaseUid";

    private async void Start()
    {
        await InitializeServices();
    }

    private async Task InitializeServices()
    {
        SetStatus("Connecting...");

        try
        {
            DependencyStatus dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();

            if (dependencyStatus != DependencyStatus.Available)
            {
                SetStatus("Firebase dependencies are not available.");
                Debug.LogError("Firebase dependency error: " + dependencyStatus);
                return;
            }

            firebaseAuth = FirebaseAuth.DefaultInstance;
            databaseRoot = FirebaseDatabase.GetInstance(databaseUrl).RootReference; 

            await UnityServices.InitializeAsync();

            SetStatus("Ready. Login or sign up.");
        }
        catch (Exception e)
        {
            SetStatus("Failed to initialize services.");
            Debug.LogException(e);
        }
    }

    public async void Login()
    {
        await LoginAsync();
    }

    public async void SignUp()
    {
        await SignUpAsync();
    }

    private async Task LoginAsync()
    {
        string username = usernameInput.text.Trim();
        string password = passwordInput.text;

        if (!ValidateInput(username, password))
            return;

        string internalEmail = ConvertUsernameToInternalEmail(username);
        Debug.Log("Login username: " + username);
        Debug.Log("Internal Firebase email: " + internalEmail);

        try
        {
            SetStatus("Logging in...");

            if (firebaseAuth.CurrentUser != null)
            {
                firebaseAuth.SignOut();
            }

            AuthResult result = await firebaseAuth.SignInWithEmailAndPasswordAsync(internalEmail, password);
            FirebaseUser user = result.User;

            await UpdateLastLogin(user.UserId);
            await SignIntoUnityWithFirebase(user);

            PlayerPrefs.SetString(LocalUsernameKey, username);
            PlayerPrefs.SetString(LocalFirebaseUidKey, user.UserId);
            PlayerPrefs.Save();

            SetStatus("Login successful.");

            StartCoroutine(FadeAndLoadScene(lobbySceneName));
        }
        catch (Firebase.FirebaseException firebaseException)
        {
            Firebase.Auth.AuthError authError = (Firebase.Auth.AuthError)firebaseException.ErrorCode;

            string message = "Login failed: " + authError;

            switch (authError)
            {
                case Firebase.Auth.AuthError.WrongPassword:
                case Firebase.Auth.AuthError.UserNotFound:
                case Firebase.Auth.AuthError.InvalidEmail:
                case Firebase.Auth.AuthError.UserDisabled:
                case Firebase.Auth.AuthError.Failure:
                    message = "Wrong username or password.";
                    break;

                case Firebase.Auth.AuthError.NetworkRequestFailed:
                    message = "Network error. Check your internet connection.";
                    break;

                case Firebase.Auth.AuthError.TooManyRequests:
                    message = "Too many attempts. Try again later.";
                    break;

                default:
                    message = "Wrong username or password.";
                    break;
            }

            SetStatus(message);
            Debug.LogError(message);
            Debug.LogException(firebaseException);
        }
        catch (Exception e)
        {
            SetStatus("Login failed.");
            Debug.LogException(e);
        }
    }

    private async Task SignUpAsync()
    {
        string username = usernameInput.text.Trim();
        string password = passwordInput.text;

        if (!ValidateInput(username, password))
            return;

        string internalEmail = ConvertUsernameToInternalEmail(username);

        try
        {
            SetStatus("Creating account...");

            AuthResult result = await firebaseAuth.CreateUserWithEmailAndPasswordAsync(internalEmail, password);
            FirebaseUser user = result.User;

            await CreatePlayerProfile(user.UserId, username);
            await SignIntoUnityWithFirebase(user);

            PlayerPrefs.SetString(LocalUsernameKey, username);
            PlayerPrefs.SetString(LocalFirebaseUidKey, user.UserId);
            PlayerPrefs.Save();

            SetStatus("Account created.");

            StartCoroutine(FadeAndLoadScene(lobbySceneName));
        }
        catch (Firebase.FirebaseException firebaseException)
        {
            Firebase.Auth.AuthError authError = (Firebase.Auth.AuthError)firebaseException.ErrorCode;

            string message = "Sign up failed: " + authError;

            switch (authError)
            {
                case Firebase.Auth.AuthError.EmailAlreadyInUse:
                    message = "Username already exists.";
                    break;

                case Firebase.Auth.AuthError.WeakPassword:
                    message = "Password is too weak.";
                    break;

                case Firebase.Auth.AuthError.InvalidEmail:
                    message = "Invalid username format.";
                    break;

                case Firebase.Auth.AuthError.NetworkRequestFailed:
                    message = "Network error. Check your internet connection.";
                    break;

                default:
                    message = "Sign up failed: " + authError;
                    break;
            }

            SetStatus(message);
            Debug.LogError(message);
            Debug.LogException(firebaseException);
        }
        catch (Exception e)
        {
            SetStatus("Sign up failed.");
            Debug.LogException(e);
        }
    }

    private async Task SignIntoUnityWithFirebase(FirebaseUser firebaseUser)
    {
        string idToken = await firebaseUser.TokenAsync(true);

        await AuthenticationService.Instance.SignInWithOpenIdConnectAsync(
            unityOidcProviderName,
            idToken
        );

        Debug.Log("Unity Authentication Player ID: " + AuthenticationService.Instance.PlayerId);
    }

    private async Task CreatePlayerProfile(string uid, string username)
    {
        Dictionary<string, object> playerData = new Dictionary<string, object>
        {
            { "username", username },
            { "createdAt", ServerValue.Timestamp },
            { "lastLoginAt", ServerValue.Timestamp },
            { "gamesPlayed", 0 },
            { "wins", 0 },
            { "selectedHatColor", "Purple" },
            { "selectedRobeColor", "Blue" }
        };

        await databaseRoot.Child("players").Child(uid).UpdateChildrenAsync(playerData);
    }

    private async Task UpdateLastLogin(string uid)
    {
        Dictionary<string, object> updateData = new Dictionary<string, object>
        {
            { "lastLoginAt", ServerValue.Timestamp }
        };

        await databaseRoot.Child("players").Child(uid).UpdateChildrenAsync(updateData);
    }

    private bool ValidateInput(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            SetStatus("Enter a username.");
            return false;
        }

        if (username.Length < 3 || username.Length > 20)
        {
            SetStatus("Username must be 3–20 characters.");
            return false;
        }

        if (!Regex.IsMatch(username, "^[a-zA-Z0-9._-]+$"))
        {
            SetStatus("Username can only use letters, numbers, '.', '_' or '-'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Enter a password.");
            return false;
        }

        if (password.Length < 8)
        {
            SetStatus("Password must be at least 8 characters.");
            return false;
        }

        return true;
    }

    private string ConvertUsernameToInternalEmail(string username)
    {
    string normalizedUsername = username.Trim().ToLowerInvariant();

    // If user accidentally types the internal email, use it directly.
    if (normalizedUsername.EndsWith("@whocastthat.local"))
    {
        return normalizedUsername;
    }

    return normalizedUsername + "@whocastthat.local";
    }

    private IEnumerator FadeAndLoadScene(string sceneName)
    {
        CanvasGroup fadeGroup = CreateVrFadeOverlay();

        // Fade the boot music out in step with the visual fade so audio doesn't cut abruptly.
        BootAudioManager audioManager = FindAnyObjectByType<BootAudioManager>();
        if (audioManager != null)
        {
            audioManager.FadeOutMusic(fadeDuration);
        }

        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, fadeDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            fadeGroup.alpha = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }

        fadeGroup.alpha = 1f;

        SceneManager.LoadScene(sceneName);
    }

    // Builds a world-space black overlay parented to the head camera so it renders
    // through the headset (a ScreenSpaceOverlay canvas is invisible in VR) and stays
    // locked in front of the eyes as the player turns their head during the fade.
    private CanvasGroup CreateVrFadeOverlay()
    {
        Camera headCamera = Camera.main;
        if (headCamera == null)
        {
            headCamera = FindAnyObjectByType<Camera>();
        }

        GameObject canvasObject = new GameObject("SceneFadeCanvas");

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasGroup fadeGroup = canvasObject.AddComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = true;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(2000f, 2000f);

        if (headCamera != null)
        {
            // Lock the overlay just in front of the eyes and let it move with the head.
            Transform camTransform = headCamera.transform;
            canvasObject.transform.SetParent(camTransform, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.001f;

            canvas.worldCamera = headCamera;
            canvas.sortingOrder = short.MaxValue;
        }

        GameObject imageObject = new GameObject("FadeImage");
        imageObject.transform.SetParent(canvasObject.transform, false);

        Image fadeImage = imageObject.AddComponent<Image>();
        fadeImage.color = Color.black;

        RectTransform rect = fadeImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return fadeGroup;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log(message);
    }
}
