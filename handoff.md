# Handoff — Zelda's tutorial + mirror-menu work

_Unity 6 (6000.0.74f1) URP VR multiplayer card game "Who Cast That?!" — Netcode for GameObjects._
_Paste this into a fresh session to continue. Read `C:\Users\zelda\Downloads\FOR_ZELDA.md` too — it
is the teammate (Raphael) handoff and defines the team rules referenced below._

---

## 1. The goal (our next step)

**Make the tutorial actually playable end-to-end, and make the magic-mirror menu pressable in VR.**

Two concrete next steps, in priority order:

1. **Fix the tutorial gameplay** (potions won't reliably spawn, cast returns the potion to the rack).
   The decision to make: **convert the tutorial to self-contained / no live network session** (recommended
   by Raphael's handoff §2c and by our own debugging) **vs.** keep the real networked game and make it
   survive solo. See §4 for exactly why the networked path keeps failing.
2. **Apply the mirror-menu VR fix to the shipping scene** (`LobbyMirrorScene`) once ownership/coordination
   is sorted, and confirm whether the "can't press buttons" is simulator- or device-only.

**Blocking prerequisite:** confirm with Raphael **who owns `TutorialScene.unity`** (he also claims it).
Do not make more `TutorialScene` scene edits until that's settled.

---

## 2. Current state (what this session was doing)

- The tutorial lives in **`TutorialScene.unity`** (a `CopyAsset` clone of `InteractionTestScene`, the real
  networked gameplay), running as a **solo host** with `NetworkedSpellGame.minPlayersToStart = 1`.
- **`TutorialDriver`** (GameObject `TutorialDriver` in `TutorialScene`) drives a screen-space HUD + 7-step
  flow: guide book → walk to seat → sit+lock → show hand → cast → draw→curse→counterspell → win.
- The **Guide Book** is a 5-page flip-book drawn over `Assets/Textures/guide-removebg-preview.png`
  (Controls / How to Play / How to Win / The Potions / About), opened with the **A/X controller button**
  (or menu button, or E). `bookBackground` is assigned on the `TutorialDriver` component.
- The **magic-mirror menu** (`MirrorMenu` object, built by `MagicMirrorMenu.BuildMenu()`) exists in
  `zelda.unity` **and** the shipping `LobbyMirrorScene.unity`. VR button presses don't work there yet.

### Hard-won values (don't lose these)
| Thing | Value | Where |
|---|---|---|
| Player spawn (CharacterResetter offline+online) | `(-6.21, -1.0, -10.39)` | rig `XR Interaction Setup (MP Variant)` |
| Seat / seated-lock anchor (`Seat_0`) | `(-5.65945, -2.147, -11.69185)`, rot y=180 | `TutorialScene` |
| Play ring (`PlayZone`) | `(-4.58, -0.89, -11.69)` | `TutorialScene` |
| Local seat rack (`seatRacks[0]`) | `testtube_stand (1)` | `SpellGameManager` |
| Guide book sprite | `Assets/Textures/guide-removebg-preview.png` (Single sprite) | assigned to `TutorialDriver.bookBackground` |

---

## 3. Files I am actively editing (all `.cs`, merge as text — safe to own)

- **`Assets/Scripts/Tutorial/TutorialDriver.cs`** — the whole tutorial. Screen-space HUD, 7-step
  coroutine, guide-book flip UI, `SeatPlayerAtChair()`, `LockLocomotion()`, curse-on-dip, `guideToggle`
  InputAction. **Contains a TEMPORARY `DiagnosticLoop()`** that logs `[TUT-DIAG] …` game state each
  1.5 s — remove it once the gameplay is fixed.
- **`Assets/Scripts/MagicMirrorMenu.cs`** — editor `BuildMenu()` constructs the world-space mirror menu.
  Just edited `AddXRRaycaster()` (blocking mask = 0) and removed the plain `GraphicRaycaster`. **All menu
  work must live here**, not hand-placed in a scene (FOR_ZELDA §0b/§5).
- `Assets/Scripts/TutorialController.cs` — older world-space "guide boards" for the orphaned `Tutorial.unity`.
  Mostly dead; deck counts already stripped.
- `Assets/Scripts/Tutorial/CameraFollowUI.cs` — from the earlier follow-UI approach; no longer used.

### Scenes (per FOR_ZELDA — read before touching)
- **`TutorialScene.unity`** — the LIVE tutorial. **Ownership contested with Raphael — settle first.**
- **`LobbyMirrorScene.unity`** — Raphael's; **the scene that actually ships**. Don't hand-edit; a
  `MirrorMenuRouter` overrides the menu button handlers at runtime.
- `zelda.unity`, `Tutorial.unity` — **dead**, will be deleted. My earlier hand-edits here (mirror
  collider disable, menu tweaks) **do not reach players**. Don't invest more here.

### Git hygiene (FOR_ZELDA §5)
Stage explicitly, never `git add -A`. **Never commit `ProjectSettings/` or `Assets/Screenshots/`** — I
generated many screenshots in `Assets/Screenshots/` this session; make sure they aren't staged.

---

## 4. What we tried that FAILED (and why) — read before re-trying

### 4a. Running the real networked game solo — the core failure
**Symptom:** potions sometimes don't spawn at all; when they do, dropping one in the ring just **returns it
to the rack** with no effect.
**Diagnosis:** the game config is 100% correct (potion prefab, rack, `minPlayers=1`, cauldron all wired).
The failure is that **`NetworkedSpellGame` isn't reliably `GameActive` for one player**:
- **`RejectPotion` (potion → rack)** fires only when the game is not active, it's not your turn, or a spell
  is resolving. So a bounced cast = the authority is rejecting it.
- **No potions** = no hand dealt = `GameActive` was false when `StartGame` ran (not connected).
- **Root cause (FOR_ZELDA §2c):** entering the tutorial auto-creates + abandons a live Netcode session
  ("Player's Room") every run; Unity's lobby service **rate-limits after churn** → sessions stop being
  created → not connected → no game. This exactly matches the "works sometimes, then stops" flakiness.
- **Secondary:** the game is "last player standing", so a 1-player match can be judged already won
  (`CheckForWinner` sets `GameActive=false`), after which casts are rejected.
**Conclusion / recommended fix:** stop using a live network session for the tutorial (self-contained
gameplay). This is also what Raphael's handoff recommends. A `DiagnosticLoop` was added to log
`connected/active/myTurn/potions`; the plan was to Play ~10 s, stop, and read the console — **not yet
captured.** If we keep networking instead, we'd need a reliable local host + a solo "don't end" guard
(touches the shared `NetworkedSpellGame.cs`).

### 4b. Moving the player spawn transform — didn't stick
Setting the XR rig's transform in-editor did nothing at runtime: **`CharacterResetter` (on the rig)
teleports the player to its `offlinePosition` on `Start`.** Fixed by editing that component's
`offlinePosition`/`onlinePosition` to `(-6.21,-1.0,-10.39)`. Any future "player spawns in the wrong place"
is this component, not the transform.

### 4c. "Can't reach the chair / invisible wall"
Not a mystery collider — the spawn was `(0,0,-12)` (see 4b), forcing a walk across the room straight into
the big **`main table`** collider. Fixed by moving the spawn next to the chair. `rtchair (8)` is a child of
`main table` (which has a large scale), so a chair "local position" converts to a very different world
position — convert via `chair.parent.TransformPoint(localValue)`, and keep the **floor Y** (chair pivot Y
sinks you).

### 4d. Guide-book image wouldn't assign / kept reverting to NULL
The PNG imported as **Sprite Mode = Multiple** with no slices → **no Sprite sub-asset exists**, so it can't
be dragged onto the field and `LoadAssetAtPath<Sprite>` returns null. Fix: set importer
`spriteImportMode = Single`, `SaveAndReimport`, then assign. (A reimport can also orphan an existing sprite
reference → re-assign after reimporting.)

### 4e. Mirror menu "can't press buttons" — no editor-side bug found
Inspected `LobbyMirrorScene`: canvas has `TrackedDeviceGraphicRaycaster`, `NearFar`/`Poke` interactors have
`UIInteraction=True`, the **UI-Press input is bound** (`XRI Right Interaction/UI Press`), buttons are
`interactable` with `raycastTarget=True`, and the menu (`x≈0.831`) sits **in front** of the mirror collider
(`x 0.845–0.917`) so nothing occludes it. **No obvious editor bug** → likely the device-vs-simulator
world-UI difference FOR_ZELDA §3 warned about. Applied best-effort hardening in `BuildMenu()` (removed the
extra `GraphicRaycaster`; set the tracked raycaster's `m_BlockingMask = 0`). **Not yet applied to
`LobbyMirrorScene`** and **unknown whether the failure is simulator or device** — that answer decides the
next move.

### 4f. Earlier dead-end: world-space "follow the camera" guide boards
`CameraFollowUI` + world-space rule boards were built, then abandoned in favor of the screen-space guide
book. The tutorial approach also changed from the old `Tutorial.unity` (single-player `PotionGameManager`
prototype) to `TutorialScene.unity` (real networked game) — which is what led to §4a.

---

## 5. Immediate next actions

1. **Settle `TutorialScene` ownership with Raphael.** Blocking everything tutorial-scene.
2. **Decide the tutorial gameplay approach** (self-contained vs fix networked-solo). Recommended:
   self-contained — remove the networking objects from `TutorialScene`, drive potions/cast/draw/curse
   locally. This also stops the tutorial from rate-limiting the real lobby.
3. **Remove the temporary `DiagnosticLoop()`** from `TutorialDriver.cs`.
4. **Mirror menu:** confirm simulator vs device; then apply the `BuildMenu()` fix to `LobbyMirrorScene`
   the sanctioned way (re-run `WhoCastThat → Build Mirror Menu`, coordinating with Raphael).
5. **Cleanup:** replace deprecated `enableWordWrapping` → `textWrappingMode` in `MagicMirrorMenu.cs`,
   `TutorialController.cs`, `TutorialDriver.cs` (kills most compile noise).

## 6. Environment quirks
- The Unity MCP bridge **drops frequently**; reconnect via the MCP-for-Unity window, then re-verify the
  `WhoCastThat` instance. Scene edits **cannot be saved during Play mode** — stop Play first.
- Compiles/domain reloads are slow; poll `EditorApplication.isCompiling` and verify a new
  method/field via reflection before assuming code is live.
