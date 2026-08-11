# Who Cast That?!

A VR multiplayer card game — a magic-themed Exploding-Kittens variant built on the Unity VR
Multiplayer template (Netcode for GameObjects, Distributed Authority).

Flow: **BootScene** (Firebase login) → **LobbyMirrorScene** (magic-mirror menu) → **InteractionTestScene** (the game).

---

## ⚠️ Before you build an Android/Quest APK — read this

Two build settings live in `ProjectSettings/`, which this team **never commits** (see the git
rules in `handoff.md`). They are therefore **local to each machine**, and a fresh clone will not
have them. You must set them yourself or the build fails.

### 1. Target SDK must be 34 or higher

**Edit → Project Settings → Player → Android → Other Settings → Target API Level → 34** (or higher).

If it is left at 32 the build dies during Gradle packaging with:

```
> Task :launcher:checkReleaseAarMetadata FAILED

Dependency 'androidx.datastore:datastore-core-android:1.1.7' requires libraries and
applications that depend on it to compile against version 34 or later of the Android APIs.
  :launcher is currently compiled against android-32.
```

Eleven AndroidX/Firebase dependencies require it. Unity derives `compileSdk` from Target SDK, so
this one setting is what satisfies them.

**Leave Minimum API Level at 30.** minSdk is what gates installation; Quest 3 is API 32, so 30
installs fine. A targetSdk higher than the device's API level is allowed — it is a declaration,
not a requirement.

The error message names Firebase and AndroidX, so it reads like a dependency problem rather than
a project-settings one. It is not.

### 2. Scenes in the build list

**File → Build Profiles → Scene List.** Required, in this order:

| # | Scene | Why |
|---|---|---|
| 0 | `BootScene` | Must be first — it is the startup scene |
| — | `LobbyMirrorScene` | The magic-mirror lobby |
| — | `InteractionTestScene` | The game |
| — | `TutorialScene` | Reached from *How to Play* |
| — | **`zelda`** | **Must stay enabled** — see below |

⚠️ **`zelda` must remain in the build list even though it is not part of the flow.**
`TutorialDriver`'s Back button hardcodes `LoadScene("zelda")`, and `TutorialReturnRedirect`
intercepts that load *after* it happens to bounce the player to `LobbyMirrorScene`. Remove zelda
from the build and that `LoadScene` call fails, so the redirect never fires and the player is
stranded in the tutorial.

---

## Testing on headsets

See `QUEST_TEST_GUIDE.md` for installing and testing on two Meta Quest 3s.

The single most important point: **each headset must sign in with a different account.** Login
identity becomes the player's permanent seat identity, so two headsets on one account makes the
second look like the first reconnecting.
