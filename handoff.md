# Handoff — mirror menu, VR UI input, and the lobby flow

_Unity 6 (6000.0.74f1) URP VR multiplayer card game "Who Cast That?!" — Netcode for GameObjects._
_Read `C:\Users\zelda\Downloads\FOR_ZELDA.md` alongside this — it is Raphael's handoff and defines
the team rules referenced throughout._

Last updated: 2026-08-12. Supersedes the previous version of this file.

---

## 1. SOLVED — VR controllers could not press the magic-mirror buttons

This was the headline bug and it is fixed. The cause was **not** in the menu, and **not** a
simulator-vs-device difference.

**Root cause:** `LobbyMirrorScene`'s `EventSystem` carried **`InputSystemUIInputModule`**. XR
interactors only ever drive UI through **`XRUIInputModule`**.

The failure chain, verified in Play mode:

1. Each interactor's `RegisteredUIInteractorCache.FindOrCreateXRUIInputModule()` finds no
   `XRUIInputModule` and **adds one at runtime**.
2. That code only knows to remove the older `StandaloneInputModule`, so it never notices
   `InputSystemUIInputModule` and both modules end up on the same EventSystem.
3. The EventSystem activates the module that was already there. `XRUIInputModule` is never
   `currentInputModule`, so its `Process()` never runs.
4. `TrackedDeviceEventData` does `currentInputModule as XRUIInputModule`, which is null — every
   tracked-device press is dropped. Rays still hover, clicks do nothing.

**Why the other scenes were fine:**

| Scene | Module in scene | Result |
|---|---|---|
| `BootScene` | `XRUIInputModule` | correct module authored in the scene |
| `InteractionTestScene` | *no EventSystem at all* | XRI creates a clean one — nothing to compete |
| `LobbyMirrorScene` | `InputSystemUIInputModule` | broken |
| `zelda.unity` | `InputSystemUIInputModule` | broken |

`LobbyMirrorScene` is a `CopyAsset` clone of `zelda.unity`, so it **inherited** the broken
EventSystem. Having no EventSystem is better than having the wrong one.

**The fix:** `Assets/Scripts/XRUIInputModuleGuard.cs`. Runs on every scene load; when it finds an
EventSystem carrying `InputSystemUIInputModule` it ensures `XRUIInputModule` exists and disables the
screen-space one. Disabled rather than destroyed, because `XRUIInputModule` handles mouse and touch
itself — the simulator keeps working.

It is a runtime component in Zelda's own file precisely so **no teammate's scene has to be
hand-edited** (team rule §5.1). If Raphael later swaps the component on the EventSystem directly,
the guard becomes a no-op automatically — it only acts when the screen module is present and enabled.

**Verified, not assumed.** Before: `currentInputModule = InputSystemUIInputModule`. After:
`XRUIInputModule`, with the screen module disabled. Both **left and right** controllers were aimed
at the Play button, produced a live UI raycast at 1.05 m, and fired `onClick` end-to-end through
their `uiPressInput` readers.

**Both hands were already wired correctly** — `m_EnableUIInteraction = True` on both
`NearFarInteractor`s, and both UI Press bindings present (`XRI Left/Right Interaction/UI Press`).
The input module was the single point of failure for both.

> Tell Raphael: his device test of PR #30 cannot pass. That fix targeted the wrong cause — the
> failure is deterministic and fails identically on a Quest.

---

## 2. Mirror menu — current button wiring

All menu work lives in `MagicMirrorMenu.BuildMenu()` and is reproduced with
**`WhoCastThat → Build Mirror Menu`** (FOR_ZELDA §0b). Never hand-place menu objects in a scene.

| Button | Goes to |
|---|---|
| Play | `InteractionTestScene` |
| How to Play | `TutorialScene` |
| Settings | opens the settings panel |
| Quit | `BootScene` |

**Host and Join were removed** from the menu.

> **The public `hostButton` / `joinButton` fields must stay.** `MirrorMenuRouter.cs:48-49` reads
> them; deleting the fields stops that file compiling for the whole team. They are left unassigned
> and every use is null-guarded.

Quit loads `BootScene` rather than calling `Application.Quit()` — on a headset, quitting drops the
player to the system menu and reads as a crash.

### Settings panel

Three sliders — Master, Music, Sound Effects — plus Back, built in `BuildMenu()`.
Backed by `Assets/Scripts/GameAudioSettings.cs`.

- **Master** drives `AudioListener.volume`, so it is audible immediately in any scene, and is
  re-applied on launch via `[RuntimeInitializeOnLoadMethod]`.
- **Music / SFX** write the same PlayerPrefs keys `BootAudioManager` already reads
  (`MusicVolume` 0.6, `UISfxVolume` 0.8), so no second settings system exists.
- `BootAudioManager` lives only in `BootScene` and is not `DontDestroyOnLoad`, so those two sliders
  have nothing to move while you are standing in the lobby. That is expected, not a bug.

---

## 3. ⚠️ OPEN — `MirrorMenuRouter` still overrides Play and How to Play

`MirrorMenuRouter.Start()` calls `RemoveAllListeners()` on Play, Host, Join and How to Play, then
installs its own handlers. `Start()` runs after `Awake()`, so **the router wins**.

- **Settings, Quit and Back are untouched by the router and work today.**
- **Play and How to Play still run Raphael's Quick Play flow**, not the handlers in §2.

To get the §2 behaviour, the `LobbyFlow` GameObject in `LobbyMirrorScene` must be disabled or
removed — and that is Raphael's scene, so it is his call.

**Know the consequence before doing it.** The router is what sets `LobbyIntent`, which is what tells
`SessionIntentConnector` to create or join a session. Remove it and `InteractionTestScene` loads
with **no network session at all** — which by §5 below is exactly the state where no potions are
dealt. Play would reach a game scene that does not play. That may be acceptable if the tutorial and
game go self-contained anyway, but it is a removal of the multiplayer lobby, not a button rewire.

---

## 4. Build settings — the recurring merge conflict

`ProjectSettings/EditorBuildSettings.asset` **must not be committed** (FOR_ZELDA §0a). It was
committed anyway in `e8febd6 "menu"`, and immediately caused a merge conflict against `main`:
both sides appended to the end of the scene list, `main` adding `InteractionTestScene` and this
branch adding `LobbyMirrorScene` + `InteractionTestScene`. Same intent, conflicting text.

Resolved by keeping this branch's list, which is the superset. The correct enabled order:

```
BootScene, LobbyScene, zelda, Tutorial, TutorialScene, LobbyMirrorScene, InteractionTestScene
```

**This will conflict again on every merge** while the file stays tracked. The permanent fix is to
untrack it (`git rm --cached` + `.gitignore`), which needs Raphael's and Ye Kai's agreement because
it affects their clones — a fresh clone would then start with an empty build list.

### Git hygiene (FOR_ZELDA §5)

- Stage explicitly. **Never `git add -A`** — that is how the above happened.
- Never commit `ProjectSettings/` or `Assets/Screenshots/`.
- **A permanently "modified" scene is usually not your work.** Opening a scene lets TextMeshPro
  recompute label widths straight back into the YAML. It regenerates every time. Do not commit it,
  and do not go hunting for what you changed — you probably changed nothing.
- Still outstanding: `Assets/Scenes/LobbyMirrorScene.unity` and `Assets/Scenes/zelda.unity` are in
  this branch's history carrying **only** that TMP noise. Worth reverting out. `LobbyMirrorScene` is
  Raphael's and scene YAML cannot be hand-merged once you both have real edits in it.

---

## 5. OPEN — the tutorial still does not play

Unchanged from the previous handoff, and still the biggest open piece.

**Symptom:** potions sometimes do not spawn; when they do, dropping one in the ring returns it to
the rack.

**Diagnosis:** the config is correct. `NetworkedSpellGame` simply is not reliably `GameActive` for
one player. `RejectPotion` fires when the game is not active, it is not your turn, or a spell is
resolving — so a bounced cast is the authority rejecting it. No potions means no hand was dealt
because `GameActive` was false when `StartGame` ran.

**Root cause (FOR_ZELDA §2c):** entering the tutorial auto-creates and abandons a live Netcode
session ("Player's Room") every run. Unity's lobby service **rate-limits after that churn**, so
sessions stop being created — which looks exactly like a code regression. This matches the
"works sometimes, then stops" flakiness.

**Recommendation:** make the tutorial self-contained — no live network session, drive
potions/cast/draw/curse locally. This also stops the tutorial from rate-limiting the real lobby for
the whole team. The alternative means touching shared `NetworkedSpellGame.cs`, which violates team
rule §5.1.

**Ownership: `TutorialScene.unity` is Zelda's.** That question is settled and no longer blocks.

---

## 6. Hard-won values (don't lose these)

| Thing | Value | Where |
|---|---|---|
| Player spawn (CharacterResetter offline+online) | `(-6.21, -1.0, -10.39)` | rig `XR Interaction Setup (MP Variant)` |
| Seat / seated-lock anchor (`Seat_0`) | `(-5.65945, -2.147, -11.69185)`, rot y=180 | `TutorialScene` |
| Play ring (`PlayZone`) | `(-4.58, -0.89, -11.69)` | `TutorialScene` |
| Local seat rack (`seatRacks[0]`) | `testtube_stand (1)` | `SpellGameManager` |
| Guide book sprite | `Assets/Textures/guide-removebg-preview.png` (Single sprite) | `TutorialDriver.bookBackground` |

**Player spawns in the wrong place?** It is `CharacterResetter` on the rig teleporting to its
`offlinePosition` on `Start`, not the transform. Setting the transform in-editor does nothing.

**"Invisible wall" reaching the chair?** Not a mystery collider — it was the old `(0,0,-12)` spawn
forcing a walk into the `main table` collider. `rtchair (8)` is a child of `main table`, which has a
large scale, so convert chair positions via `chair.parent.TransformPoint(local)` and keep the
**floor Y**.

**Guide-book image reverting to NULL?** The PNG imported as Sprite Mode = Multiple with no slices,
so no Sprite sub-asset exists. Set `spriteImportMode = Single`, `SaveAndReimport`, then re-assign.

---

## 7. Files owned here

- `Assets/Scripts/MagicMirrorMenu.cs` — the menu and its editor builder. All menu work goes here.
- `Assets/Scripts/GameAudioSettings.cs` — audio prefs (new).
- `Assets/Scripts/XRUIInputModuleGuard.cs` — the VR UI fix (new).
- `Assets/Scripts/Tutorial/TutorialDriver.cs` — the whole tutorial. **Still contains a temporary
  `DiagnosticLoop()`** logging `[TUT-DIAG]` every 1.5 s — remove it once the tutorial is fixed.
- `TutorialScene.unity` — Zelda's, confirmed.

**Do not edit:** `LobbyMirrorScene.unity` (Raphael's — the scene that actually ships),
`Assets/Scripts/Flow/*` (Raphael's). `zelda.unity` and `Tutorial.unity` are dead and will be
deleted; work there does not reach players.

### Remaining cleanup

`enableWordWrapping` is obsolete and is most of the compile noise. Fixed in `MagicMirrorMenu.cs`.
Still present in `TutorialController.cs:152` and `Assets/Scripts/Tutorial/TutorialDriver.cs:488` —
replace with `textWrappingMode` (`TextWrappingModes.Normal` / `.NoWrap`).

---

## 8. Environment quirks

- The Unity MCP bridge drops frequently; reconnect and re-verify the `WhoCastThat` instance.
- Scene edits cannot be saved during Play mode — stop Play first.
- Compiles and domain reloads are slow. Poll `isCompiling` and verify a new method or field by
  reflection before assuming the code is live.
