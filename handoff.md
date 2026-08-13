# Handoff — voiceovers wired, mirror menu fitted, hat seated

_Unity 6 (6000.0.74f1) URP VR multiplayer card game "Who Cast That?!" — Netcode for GameObjects._
_Read `C:\Users\zelda\Downloads\FOR_ZELDA.md` too — Raphael's handoff, defines the team rules below._

Last updated: 2026-08-14. Branch `zelda22`, merged with `main`'s lighting bake. Supersedes the
previous version of this file.

---

## 1. The goal (our next step)

**The tutorial plays end to end, the wizard narration is wired, and the mirror menu has its full
button set inside the glass. Everything below now needs a headset, not more code.**

In priority order:

1. **Play test on device.** Nothing in §2 has been heard or seen in a headset — the voice lines,
   the hat fit, the menu layout and the lighting bake were all judged on a monitor.
2. **Add a Counterspell event to `NetworkedSpellGame`** so a rival's Counterspell can be narrated,
   not just the local player's. Two lines in a **shared file** — Raphael and Ye Kai's call. See §5.
3. **Tutorial cleanup** — remove `DiagnosticLoop()` from `TutorialDriver`, record voice slot 11.
4. **Decide the Gabriola font question** before any Android build. See §5.

---

## 2. What works now

### The wizard speaks
18 of the 19 recordings in `Assets/Audio/wizard recs/` are wired and play off the rules.

- **`SpellVoiceLibrary.cs` / `SpellVoiceDirector.cs`** (new) — a narration layer that is
  **separate from `GameAudioLibrary`** on purpose. `spellStings` plays a clip *instead of* the
  generic cast sting, so narration in there would silence `cast_sfx` and drop the project's only
  reference to `foresight_sfx`. `GameAudioLibrary.asset` is untouched.
- The director installs itself from a runtime hook, like `GameAudioDirector` — no scene wiring in
  either `InteractionTestScene` or `TutorialScene`.
- Event map: `SpellCastStarted` → the five cast spells; `SpellFizzled` → the `dispel (x)` lines;
  `SpellResolved` → the `reflection (x)` lines; `PlayerCursed` → the Curse line (victim only).

### The mirror menu fits the mirror, with every button back
- **Fitted by measurement.** `BuildMenu()` reads the mirror's renderer bounds and derives one
  uniform scale with a 14% frame inset. It was 1.17 m × 2.11 m against a 1.10 m × 1.88 m mirror;
  it is now 0.90 m × 1.61 m.
- **No purple backdrop.** The mirror's own glass is the background. Built in four places — main,
  settings, the runtime settings panel, the customise panel — all changed.
- **Host and Join are back.** They were never missing functionality: `MirrorMenuRouter` has always
  had working `OnHost`, `OnJoin` and a full `RoomCodePad`. `BuildMenu` was nulling `hostButton`
  and `joinButton`, so the router's `Rewire()` hit its null guard and silently did nothing.
- Rows: Play, Host, Join, How to Play, Settings, Quit, then Customise added at runtime.

### The hat sits on the head
All five hats share one mesh whose bounds centre is **3.5 mm behind its own pivot** (~3 cm once
scaled), so it was seated facing backwards off the back of the head.
- `PlayerHatLibrary.yawDegrees` (new, 180) is read by **both** the worn hat and the lobby preview.
- `forwardOffset` 0 → 0.02.
- Two preview-only bugs fixed: `HatMount` never inherited the mannequin's 180° turn, and
  `FitPreviewHat` silently dropped `ForwardOffset`. The preview now matches what you wear.

### Inherited from `main`
The lighting bake for all three scenes, `LS_RoomV2_Quest.lighting`, and the `Mirror Glow` light in
the mirror alcove. **Only `Chandelier Key` and `Fireplace Light` are still Mixed** — demote either
and every dynamic object (players, hats, potions) goes flat and dark. The light rig lives in
`RoomV2.prefab`; **edit the prefab, never a scene instance.**

---

## 3. Files actively edited (all `.cs` — merge as text, safe to own)

| File | What |
|---|---|
| `Assets/Scripts/Audio/SpellVoiceLibrary.cs` | New. The narration clip table. |
| `Assets/Scripts/Audio/SpellVoiceDirector.cs` | New. Subscribes to the rules; self-installing. |
| `Assets/Scripts/MagicMirrorMenu.cs` | Menu + its editor builder. **All menu work goes in `BuildMenu()`**, never hand-placed (FOR_ZELDA §0b). Now also owns the row layout (`RowPosition` / `RowSize` / `BuiltInRowCount`). |
| `Assets/Scripts/Flow/LobbyCustomisePanel.cs` | Hat preview fixes + no backdrop + asks for its row. |
| `Assets/Scripts/Flow/LobbySettingsPanel.cs` | No backdrop. |
| `Assets/Scripts/Visuals/PlayerHat.cs` | Applies `FitRotation` before measuring. |
| `Assets/Scripts/Visuals/PlayerHatLibrary.cs` | Added `yawDegrees` / `FitRotation`. |
| `Assets/Scripts/Tutorial/TutorialDriver.cs` | The whole tutorial. **Still contains a temporary `DiagnosticLoop()`** logging `[TUT-DIAG]` every 1.5 s — remove it. |
| `Assets/Scripts/Tutorial/TutorialHudFollow.cs` | New. Lazy head-follow for the HUD. |
| `Assets/Scripts/Tutorial/TutorialOfflineHost.cs` | New. Local host, no lobby. |
| `Assets/Scripts/XRUIInputModuleGuard.cs` | New. Fixes XR UI input in every scene. |
| `Assets/Scripts/GameAudioSettings.cs` | New. Audio prefs shared with `BootAudioManager`. |
| `Assets/Scripts/InteractionTest/NetworkedSpellGame.cs` | **SHARED FILE** — only the `allowSoloPlay` flag + guard. Tell Raphael and Ye Kai. |
| `Assets/Scripts/PotDrawZone.cs` | Added a null guard. Orphaned prototype code otherwise — see §5. |

**Scenes:** `TutorialScene.unity` is **Zelda's, confirmed**. `LobbyMirrorScene.unity` is Raphael's
and ships — don't hand-edit it, but **`WhoCastThat → Build Mirror Menu` is the sanctioned way to
change the menu in it.** `zelda.unity` / `Tutorial.unity` are dead.

---

## 4. ⚠️ `MirrorMenuRouter` — the position has CHANGED

The previous version of this file proposed disabling `LobbyFlow` so the mirror's own wiring would
win. **Do not do that now.** With Host and Join restored, the router is the only thing that knows
how to create or join a session — disabling it strips session handling from all three buttons and
loads `InteractionTestScene` with no session at all.

`MirrorMenuRouter.Start()` calls `RemoveAllListeners()` on Play, Host, Join and How to Play and
installs its own, and `Start()` runs after `Awake()`, so the router wins. That is now the desired
behaviour. Settings, Quit and Back are untouched by it and run `MagicMirrorMenu`'s handlers.

Keep the public `hostButton` / `joinButton` fields — the router reads them, and deleting them stops
that file compiling for everyone.

---

## 5. Pending / cleanup

- **Counterspell is only narrated for the local player.** The curse-defence branch
  (`NetworkedSpellGame.cs:1649`) consumes the potion, calls `RemoveCurse` and goes straight to
  placement — it raises **no event at all**. `SpellVoiceDirector` infers it from the local curse
  flag clearing, screening out elimination. A rival's Counterspell needs a real event on that
  shared file.
- **`reflection (dispel).mp3` can never play.** `ApplyEffect` excludes Dispel from
  `lastResolvedSpell`, so a Reflection cannot copy one. Not a wiring gap — leave it or change the
  rule.
- **`NetworkedSpellGame` nulls its static presentation events on despawn**, which silently
  unsubscribes `GameAudioDirector` — the existing stings likely go quiet after the first match of a
  session. `SpellVoiceDirector` re-arms on scene load and on a new game instance; `GameAudioDirector`
  does not. Their file, worth telling them.
- **Voice slot 11 is empty** — "Tutorial complete — you're ready to duel!" is silent. Slots are
  assigned **file number → slot number**, so `step N.mp3` lands in element `N-1`.
- **Remove `DiagnosticLoop()`** from `TutorialDriver`.
- **`enableWordWrapping`** still deprecated in `TutorialController.cs:152`, `MirrorMenuRouter.cs:80`
  and `RoomCodePad.cs:186`.
- **Dead code:** `PotDrawZone.cs` + `PotionGameManager` are the old prototype. `CauldronDrawZone.cs`
  is in **neither** scene — superseded by `StirZone`.
- **`Setting linear/angular velocity of a kinematic body is not supported`** spams on every potion
  spawn. Pre-existing, harmless, noisy.
- **`Seat_2` (z −14.79) and `Seat_3` (z −13.24) sit outside the table** (z range ≈ −13.08…−10.28).
  Fine for the solo tutorial, wrong for a 4-player match.
- **`CauldronOrbit.tableCenter` may be null in `InteractionTestScene`** — if so the pot never orbits
  in the real game either. Worth checking.
- **Gabriola is a Windows system font.** Licensed for use *on* Windows; embedding it in an Android
  APK is outside that. For distribution swap to an open font (IM Fell English, Cinzel Decorative,
  MedievalSharp) — all the plumbing exists, it's one file drop.

### Git hygiene (FOR_ZELDA §5)
Stage explicitly, **never `git add -A`**. Never commit `ProjectSettings/`.
Correct enabled build order:
`BootScene, LobbyScene, zelda, Tutorial, TutorialScene, LobbyMirrorScene, InteractionTestScene`.

**Three files go dirty on their own — do not commit them:**
- `Assets/Fonts/Gabriola SDF.asset` — TMP re-baking its glyph atlas, thousands of lines.
- `Assets/Prefabs/InteractionTest/NetworkedPotion.prefab` — `GlobalObjectIdHash` changes during
  mass model reimports. Clients must agree on it, so it needs the netcode owner, not a drive-by commit.
- `Assets/Scenes/New Lighting Settings.lighting` — Unity's stray default, superseded by
  `LS_RoomV2_Quest.lighting`.

---

## 6. What we tried that FAILED (read before re-trying)

### 6a. "The tutorial doesn't work" was THREE bugs, not one
1. **Solo win** — `CheckForWinner()` ended the game instantly → casts rejected, draws refused.
2. **Lobby rate-limit** — session churn → no connection at all → no potions spawned *whatsoever*.
3. **Unreachable cauldron** — `tableCenter` null → pot 2.9 m away → curse step could never fire.

**The previous handoff blamed the lobby for all of it. That was wrong.** Read the diagnostic before
theorising.

### 6b. Offline host — topology mismatch (the subtle one)
Setting `NetworkConfig.NetworkTransport = UnityTransport` is **not enough**. `NetworkTopology` was
still `DistributedAuthority`, so the SDK logged `[Topology Mismatch]` and shut the NetworkManager
down about a second after the hand was dealt.
**Fix:** also set `nm.NetworkConfig.NetworkTopology = NetworkTopologyTypes.ClientServer`.

### 6c. `gripPressed` silently matched nothing
`<XRController>{LeftHand}/gripPressed` resolved to **zero controls** — it is `gripButton`. Bind by
**usage** (`{GripButton}`). **Always check `action.controls.Count` at runtime.** Grip was dropped
anyway: grip is also *grab*, so reaching for a potion threw the book open.

### 6d. Watching only one cauldron
There are **two pots**: `room/main table/pot/DrawTriggerZone` (static, on the table) and
`CauldronRig/StirZone` (floating). The player reaches into the table one.

### 6e. `StirZone.LocalHandCanDraw` is far stricter than it looks
Pot settled, hand empty, cooldown elapsed, `CanBrew` true, **and** `OnTriggerEnter` fired for a
collider it accepts. Any one failing stalls a `WaitUntil` forever with no error. Don't gate tutorial
steps on it.

### 6f. Mirror menu — wrong input module (not a device-vs-simulator issue)
`LobbyMirrorScene`'s EventSystem carried `InputSystemUIInputModule`. XR interactors only drive UI
through `XRUIInputModule`. Both end up on the EventSystem and the pre-existing one wins, so
`currentInputModule as XRUIInputModule` returns null — rays hover, clicks do nothing.
**Deterministic, not simulator-only.**

### 6g. A transparent Image still eats UI raycasts
Removing a panel background by setting alpha to 0 leaves an invisible slab swallowing every click
meant for what is behind it. Either drop the `Image` component entirely or set
`raycastTarget = false` with it.

### 6h. Rotate BEFORE you measure
`FitSize` and the base-seating calculation both read **world-space** bounds, and the hat mesh carries
a baked 16° tilt, so its AABB is not rotation-invariant. Applying the yaw after measuring moves the
geometry out from under numbers taken in the old orientation.

### 6i. Layout constants copied into a second file go stale silently
`LobbyCustomisePanel` placed its button at a literal `y=-590`, correct for a four-button menu and
commented "Quit sits at y=-420". Both stopped being true two layouts ago. The menu now owns
`RowPosition()` and attachments ask for the next row.

### 6k. A reference can be "assigned" and still be dangling on every other machine
The tutorial stayed silent after `0ca3946` "fixed" it. All 11 slots were filled in the scene YAML
and the author verified them in play mode — but the GUIDs belonged to a **local** copy of the clips
whose `.meta` files were never committed. `git log -S <guid> -- "*.meta"` found them in no commit on
any branch. `Say()` skips a null clip silently, so the symptom was identical to the original bug.

**How to check in one line:** compare the GUIDs a scene asks for against the GUIDs the assets
actually have. If a `.meta` is untracked, everyone but the author gets a broken reference.
**Committing an asset is not enough — the `.meta` beside it carries the GUID every reference uses.**

### 6j. Assorted traps
- **`.m4a` cannot be imported by Unity.** Convert to mp3/wav/ogg or clips silently don't exist.
- **Renaming audio outside Unity breaks asset references** — rename in the Project window instead.
- **Changing a C# default does NOT change an already-serialized value.** `PlayerHatLibrary.asset`
  held `forwardOffset 0` and `widthMetres 0.496` while the C# defaults said `0.01` and `0.30`.
  A **newly added** field does take its C# default, since it was never serialized before.
- **`▸` (U+25B8) is not in LiberationSans** — use `→`, or set a font fallback.
- **Overlay canvases are invisible in VR.** Must be `RenderMode.WorldSpace`.
- **Editing a scene file on disk while Unity has it open** pops a modal that blocks the MCP bridge
  until someone clicks it.

---

## 7. Hard-won values

| Thing | Value |
|---|---|
| Player spawn (`CharacterResetter` offline+online) | `(-6.21, -1.0, -10.39)` |
| `Seat_0` | `(-5.66, -2.15, -11.69)` |
| `PlayZone` / table centre | `(-4.58, -0.89, -11.69)` |
| Cauldron settle point (seat 0) | `(-5.13, -11.38)`, 0.62 m from the seat |
| Table pot trigger | `(-4.25, -0.79, -11.66)` |
| HUD | 1.5 m out, 0.4 m above eye; banner +300 units; book −380 / 450 forward |
| Local rack | `testtube_stand (1)` |
| Mirror face (`magik mirror` bounds) | 1.101 m wide (Z) × 1.876 m tall (Y), centre `(0.175, 0.715, -13.200)` |
| Mirror menu canvas | 1000 × 1800 units, scale ≈ 0.000896 → 0.90 m × 1.61 m |
| Menu rows | first y 470, step 150, size 700 × 120 |
| Hat fit | yaw 180, height 0.074, forward 0.02, width 0.496 |
| `PotionType` order | 0 Hex, 1 Tribute, 2 Dispel, 3 Foresight, 4 Warp, 5 Phase, 6 Reflection, 7 Counterspell, 8 Curse |

**Player spawns in the wrong place?** It's `CharacterResetter` on the rig, not the transform.

---

## 8. Environment quirks

- The Unity MCP bridge **drops frequently** — reconnect and re-verify the `WhoCastThat` instance.
  Some `execute_code` calls fail with an empty error; just retry. A modal dialog in the Editor
  blocks it completely.
- Scene edits can't be saved during Play mode. Play-mode changes are discarded on stop.
- Compiles are slow. Poll `isCompiling` and verify a new field/method by reflection before
  assuming code is live.
- A merge that touches `Assets/Models/*.fbx.meta` triggers a long reimport of every model.
