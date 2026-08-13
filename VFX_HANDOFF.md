# Potion VFX — Handoff

Working doc for continuing the visual-effects work on **Who Cast That?!**
Written for a fresh session with no prior context.

> **Last updated:** 13 Aug 2026. Covers the Curse/Warp recolour, the
> `Potion_<Ability>` rename + prefab-variant pass, and the PR #32 merge to `main`.
> **Next task: recolour the tubes again — see §6 Priority 1, and read the
> ⚠️ procedure-changed box there before touching anything.**
> Sections marked ⚠️ correct claims made in earlier versions of this doc that turned
> out to be wrong.

---

## 1. The project

**Who Cast That?!** — a VR multiplayer card game for a Year 3 Capstone (NP, School of
InfoComm Technology). Inspired by Exploding Kittens, re-themed as a *magic misfire*
game with original mechanics and assets.

The key conceptual point: **cards are physical potions in test tubes.** Players grab
and throw real 3D potion bottles in VR instead of holding flat cards. That's why all
the VFX work targets `testtube_potionShape` objects.

- 2–4 players, turn-based, last player standing wins
- Each turn: optional Play Phase → mandatory Draw Phase
- Draw a **Curse** and you're eliminated unless you hold a **Counterspell**

### Deck — 40 potions, 9 types

| Type | Count | Effect | Exploding Kittens analogue |
|---|---|---|---|
| Hex | 5 | End turn without drawing; next player takes 2 turns | Attack |
| Tribute | 4 | A player gives you a card of their choice | Favor |
| Dispel | 4 | Cancel any action; playable out of turn | Nope |
| Foresight | 5 | Privately view top 3 of the draw pile | See the Future |
| Warp | 4 | Shuffle the draw pile | Shuffle |
| Phase | 4 | End turn without drawing | Skip |
| Reflection | 4 | Copy the last card played (cannot copy Dispel) | — |
| Counterspell | 6 | Survive a Curse; secretly reinsert it into the deck | Defuse |
| Curse | 4 | You explode | Exploding Kitten |

### Team / branch context

- Working branch: **`YK9`** (Tan Ye Kai), branched from the merged `main`.
  Main branch is `main`. ⚠️ The branch has moved twice — earlier versions of this doc
  said `YK7`, then `YK8`. `YK8` is merged (PR #32) and finished; see §4f.
- Teammates own other scenes — **`RaphaelScene.unity`**, **`zelda.unity`**,
  `LobbyScene.unity`, `BootScene.unity`. **Do not touch these.**
- VFX work is confined to **`Assets/Scenes/YeKai.unity`**, `Assets/VFX/`, and
  `Assets/Prefabs/` (potion + pot prefabs only).

---

## 2. Tech stack

| | |
|---|---|
| Unity | **6000.0.74f1** |
| Pipeline | **URP 17.0.4**, asset `URP-Performant` at `Assets/VRMPAssets/Settings/` |
| XR | XR Interaction Toolkit 3.3.1, OpenXR 1.16.1, XR Hands 1.7.3, Android XR |
| Backend | Firebase |
| **VFX Graph** | **NOT installed** — use Shuriken + handwritten HLSL / Shader Graph |

Target is standalone/mobile-class XR (Quest via Android XR) as well as PC VR.

### Verified render settings (measured, not assumed)

| Setting | Value | Consequence |
|---|---|---|
| `supportsHDR` | **False** | Anything above 1.0 clips to flat colour |
| `colorGradingMode` | **LowDynamicRange** | Same |
| Volumes in `YeKai.unity` | **0** | No Bloom anywhere in the project |
| Camera post-processing | **Off** | — |
| `maxAdditionalLightsCount` | **4** | Only 4 extra lights affect any object |
| `additionalLightsRenderingMode` | PerPixel | — |
| Renderer features | **none** | — |

**Everything currently on screen must therefore work without bloom.** The shaders
written so far all clamp to 1.0 deliberately.

---

## 3. Core design decision (read before adding effects)

Card VFX do not port to VR. Cards are viewed on a flat screen, so camera-facing
billboards read perfectly. In VR a test tube sits ~40cm from your face and you see it
in **stereo** — those same billboards read as flat cardboard stickers, because each
eye proves they have no depth.

**Build effects on real geometry and real 3D space, not flat sprites.**

### Motion matters more than colour

~8% of male players have colour vision deficiency. **Hex (red) and Phase (green)
collapse into the same tan for deuteranopes** — and both are "end my turn" adjacent
cards, so that's real gameplay confusion. Each ability therefore has a distinct
**motion signature** in the `SpellMotion` enum:

| Type | `SpellMotion` | Intended motion |
|---|---|---|
| Hex | `Strike` | fast directional stab **at the victim** |
| Tribute | `Pull` | ribbon dragging target → caster |
| Dispel | `Negate` | instant flat shockwave, no ease-in |
| Foresight | `Reveal` | slow calm upward drift |
| Warp | `Swirl` | rotation around the deck |
| Phase | `Dissipate` | soft downward fade |
| Reflection | `Mirror` | shimmer echoing the copied spell |
| Counterspell | `Ward` | protective dome snapping shut |
| Curse | `Implode` | suck inward, hold, then erupt |

**None of these are implemented yet.** The enum is data only — nothing consumes it.

---

## 4. What is DONE

### 4a. Potion aura system

| File | Purpose |
|---|---|
| `Assets/VFX/PotionVFXProfile.cs` | ScriptableObject per type — colour, `SpellMotion`, duration, scale, burst, light, pulse, audio. Defines `SpellMotion`. |
| `Assets/VFX/PotionVFXLibrary.cs` | `PotionType` → profile lookup |
| `Assets/VFX/PotionAura.cs` | Drives liquid colour + outline + motes + light + trail from ONE colour. `[ExecuteAlways]`. |

- `Assets/VFX/Profiles/VFX_<Type>.asset` — all 9, committed in `4448a10`
- `Assets/VFX/PotionVFXLibrary.asset` — all 9 wired

### 4b. Outlines replaced the glow lights ✅

The 9 per-potion `Glow` point lights were **removed** and replaced with an
inverted-hull outline. Verified 9/9 in scene.

| File | Purpose |
|---|---|
| `Assets/VFX/Shaders/PotionOutline.shader` | Cull Front, world-space normal extrusion, stencil-tested |
| `Assets/VFX/Shaders/PotionOutlineMask.shader` | Stamps the tube silhouette into stencil **bit 6** |
| `Assets/VFX/Meshes/testtube_outline_hull.mesh` | Smoothed-normal copy of the tube mesh |
| `Assets/VFX/Materials/M_PotionOutline.mat` | queue **1999** |
| `Assets/VFX/Materials/M_PotionOutlineMask.mat` | queue **1998** (must draw first) |

Each potion has an `Outline` child: hull mesh + **two materials** (mask, then hull),
shadows off, wired to `PotionAura.outlineRenderer`.

**Why it's built this way — two non-obvious findings:**

1. **`testtube_v2.fbx` has split normals.** 610 of 738 vertices had normals that would
   crack the outline open at every hard edge. The hull mesh is a smoothed-normal copy
   baked into `Assets/VFX/Meshes/`. **The source FBX was not modified** — teammates
   share that model.

2. ⚠️ **The glass is already transparent** (`_Surface=1`, queue 3000, `_ZWrite=0`).
   Earlier versions of this doc listed "make the glass transparent" as Priority 2 —
   that is **done/wrong**. Because transparent glass writes no depth, the hull's
   interior showed straight through and the tubes rendered as solid slabs of colour.
   Fixed with the stencil mask. A depth prime would have been simpler but would have
   occluded the liquid *and* every mote orbiting behind the tube.

Width is in **metres** (`_OutlineWidth`, default 0.0006 = 0.6mm) because the tube mesh
is authored at a strange scale — 0.0039 object-space units tall, then multiplied by a
~9.45 lossy scale to land at 3.8cm. `_DistanceCompensation` (0.45) thickens the
outline with distance so it stays legible across a table.

### 4c. Boiling cauldron ✅

The `pot` object (34×29×35cm, world pos `-0.727, 0.147, -0.035`) is the **draw pile** —
it carries a `DrawTriggerZone` child, which is gameplay and was left untouched.

The soup was **baked into the single 92,249-triangle Tripo mesh with one material**, so
there was nothing to animate. Solution: classify faces by base-map colour + geometry
and write a two-submesh copy.

| File | Purpose |
|---|---|
| `Assets/VFX/Meshes/pot_soup_split.mesh` | submesh 0 = cauldron (74,527 tris), submesh 1 = soup (17,722 tris) |
| `Assets/VFX/Shaders/SoupBoil.shader` | Vertex heave + two counter-scrolling noise octaves + emission |
| `Assets/VFX/Materials/M_SoupBoil.mat` | on submesh 1 |
| `Assets/VFX/CauldronGlow.cs` | Two-sine flicker on the glow light |
| `Assets/VFX/Materials/M_SoupSteam.mat` | **alpha** blended |
| `Assets/VFX/Materials/M_SoupBubble.mat` | additive |

Children added to `pot`: `SoupSteam` (7/sec, 1.3–2.3s life), `SoupBubbles` (9/sec,
0.5–1.0s), `SoupGlow` (point light).

**`Assets/Models/pot.fbx` was not modified.**

Useful measured geometry: soup plane world **Y ≈ 0.2700**, soup rises to **0.2943**,
inner wall radius **~0.140**, soup max radius **0.1304**, rim top **~0.288**.

**Brightness was the explicit brief — "not so bright it blinds".** First attempt at
2.6cd blew the cauldron rim to pure white. Final: **0.45cd, range 0.45**, light dropped
below the rim. Measured from a player viewpoint: **0.08% of pixels fully clipped**
(essentially just sky), mean luminance 0.379. The shader also hard-clamps to 1.0.
Dials: `_EmissionStrength` (0.38) and `CauldronGlow.baseIntensity` (0.45).

### 4d. Bug fixed — `Liquid.cs.cs` material churn

`Assets/Textures/potions/testtube_v2/Liquid.cs.cs` is `[ExecuteInEditMode]` and wrote
`rend.sharedMaterial.SetFloat(...)`. `sharedMaterial` is the **.mat asset on disk**, so
it rewrote the material file every editor frame → constant git noise, merge conflicts,
and all tubes sloshing in lockstep. Fixed via `MaterialPropertyBlock` in
`ApplyToRenderer()`. **Verified still holding** — `Shader Graphs_fake liquid.mat` has
not reappeared as modified.

### 4e. Git housekeeping ✅

A leftover GitHub Desktop stash was blocking commits in the Desktop UI (git itself was
always healthy — no conflicts, no locks, no `pre-commit` hook). The stash was dropped
after verifying it was redundant:

```
stash@{0} = stash-backup-YK7 = stash-backup-YK7-tag = 5b364ff
```

Its `YeKai.unity` was 11,222 lines vs the then-current 56,857 — restoring it would have
destroyed the session's work. **Do not click Restore** if Desktop offers it again.

Safety refs `stash-backup-YK7` (branch) and `stash-backup-YK7-tag` still exist at
`5b364ff`. The work they were guarding is now merged to `main`, so they are safe to
delete whenever you like:

```bash
git branch -D stash-backup-YK7 && git tag -d stash-backup-YK7-tag
```

### 4f. Branch state as of 13 Aug 2026

`YK8` was merged into `main` via **PR #32** (`3be1a5f`). Work has moved to **`YK9`**,
branched from the merged `main`.

That PR initially blocked on a conflict in `Assets/Scripts/Flow.meta`. Worth
understanding, because the same thing will happen again with any new folder:

- The file did not exist at the merge base. Unity generates a `.meta` per **folder**
  holding a random GUID, and `main` and `YK8` each generated their own.
- Two branches adding the same path with different content is an **add/add conflict**.
  Git cannot auto-merge it, and it shows up **only on the GitHub PR page** — the local
  branch looks perfectly clean, which is what makes it confusing.
- Fixed in `988ac58` by setting `YK8`'s copy byte-identical to `main`'s
  (`guid: a7dcb57456355bd4483e63d9ef5fcf18`). When both sides match, the conflict
  resolves itself — no merge commit, no web editor.
- Neither folder GUID was referenced by any asset, so the choice was free. **Taking
  `main`'s value is the default** — it keeps churn off teammates.

⚠️ **Scene line count is no longer a health signal.** `YeKai.unity` went 56,857 →
~2,000 lines, which looks alarming and is completely fine: the nine tubes became
prefab variants, so ~45,600 lines of inline object data moved out of the scene and
into `Assets/Prefabs/Potion_*.prefab`. Inline `GameObject:` blocks dropped 33 → 3
while `PrefabInstance:` held steady at 20. The three remaining plain GameObjects are
`Main Camera`, `Directional Light` and `Cube`; everything else is a prefab instance.
**Judge the scene by root-object count in the editor, not by file size.**

---

## 5. Current colour palette

Stored as `primary` in `Assets/VFX/Profiles/VFX_<Type>.asset`. Read from disk and
verified — every stored value is exactly base × multiplier.

| Type | Role | Base sRGB | Hex | ×mul | Stored `primary` |
|---|---|---|---|---|---|
| Hex | aggression | 1.00, 0.23, 0.09 | `#FF3B17` | 1.25 | `1.25, 0.2875, 0.1125` |
| Tribute | theft | 1.00, 0.31, 0.64 | `#FF4FA3` | 1.15 | `1.15, 0.3565, 0.736` |
| Dispel | negation | 0.94, 0.96, 1.00 | `#F0F5FF` | 1.60 | `1.504, 1.536, 1.6` |
| Foresight | knowledge | 0.16, 0.82, 1.00 | `#29D1FF` | 1.10 | `0.176, 0.902, 1.1` |
| Warp | chaos | 0.30, 0.45, 1.00 | `#4D73FF` | 1.15 | `0.345, 0.5175, 1.15` |
| Phase | evasion | 0.49, 1.00, 0.70 | `#7DFFB3` | 1.10 | `0.539, 1.1, 0.77` |
| Reflection | mimicry | 0.85, 0.89, 1.00 | `#D9E3FF` | 1.20 | `1.02, 1.068, 1.2` |
| Counterspell | salvation | 1.00, 0.84, 0.40 | `#FFD666` | 1.30 | `1.3, 1.092, 0.52` |
| Curse | death | 0.40, 0.03, 0.55 | `#66088C` | 1.00 | `0.40, 0.03, 0.55` |

⚠️ **Curse and Warp were recoloured on 12 Aug 2026** (the old §6 Priority 1 task).
Curse moved from crimson `#8C0A1A` to purple `#66088C`; Warp vacated purple and moved
to indigo `#4D73FF`. The pre-recolour values are recoverable from commit `4448a10`.

Known readability problems in the current set:

- **Dispel and Reflection are effectively the same colour** — `#F0F5FF` vs `#D9E3FF`,
  both near-white, indistinguishable as thin outlines at rack distance.
- **Curse is the only card below 1.0 on every channel**, so it has no bloom headroom
  and will never glow even after Bloom is added. Deliberate for the death card.
- *Reflection* was originally intended to **borrow the colour of the card it copies**
  rather than keep a fixed hue. Currently fixed pearlescent silver as a placeholder.

---

## 6. What still NEEDS doing

### ✅ DONE (12 Aug 2026) — Recolour Curse to purple, move Warp off purple

Applied values, verified on disk and in the scene:

| Type | New base sRGB | Hex | ×mul | Stored `primary` |
|---|---|---|---|---|
| **Curse** | 0.40, 0.03, 0.55 | `#66088C` | 1.00 | `0.40, 0.03, 0.55` |
| **Warp** | 0.30, 0.45, 1.00 | `#4D73FF` | 1.15 | `0.345, 0.5175, 1.15` |

`ApplyProfile` was re-run on the Curse and Warp tubes only — the other seven were
left alone so their inspector values could not drift. `lightIntensity`, `pulseSpeed`
and `pulseDepth` were already identical to the profiles, so colour was the only
change. Confirmed the outline `MaterialPropertyBlock` now carries `_OutlineColour =
(0.400, 0.030, 0.550)` and `(0.345, 0.517, 1.150)` respectively.

**Reasoning, and the trade-off that was accepted.** Nine distinct hues is genuinely
over-subscribed for this palette — every remaining slot collides with something:

- *Indigo for Warp* sits near Foresight's cyan, and Foresight/Warp are **both
  draw-pile manipulation**, so confusing them matters mechanically.
- *Amber/orange for Warp* sits between Hex's red-orange and Counterspell's gold.

Indigo is recommended because the collision is separable by **value** rather than hue:
Curse becomes notably dark (0.55 max channel) while Warp stays bright (1.15 max), and
that separation survives greyscale and colourblindness where hue does not. Foresight
stays cyan-bright but far greener.

Curse keeps ×1.00 so it remains the one card with no bloom headroom — preserving the
existing "death card does not glow" intent. If you want it to glow later, ×1.35 gives
`0.54, 0.04, 0.7425` with headroom on blue only.

Note for anyone changing a profile in future: **MPB values are runtime-only**, so
after editing `primary` you must re-run `ApplyProfile` (or trigger `OnValidate`) on
the affected tubes and re-save the scene, or the tube keeps its old serialized colour.

### ✅ DONE (12 Aug 2026) — Renamed tubes to `Potion_<Ability>` + variant prefabs

All nine scene objects renamed and all nine Prefab Variants now exist in
`Assets/Prefabs/`, each verified as `assetType=Variant` with
`base=Assets/Prefabs/testtube_potionShape 1.prefab`:

| Old scene object | New name | Prefab |
|---|---|---|
| `testtube_potionShape 1` | `Potion_Hex` | already existed, reused |
| `testtube_potionShape 1 (1)` | `Potion_Tribute` | created |
| `testtube_potionShape 1 (2)` | `Potion_Dispel` | created |
| `testtube_potionShape 1 (3)` | `Potion_Foresight` | created |
| `testtube_potionShape 1 (4)` | `Potion_Warp` | created |
| `testtube_potionShape 1 (5)` | `Potion_Phase` | created |
| `testtube_potionShape 1 (6)` | `Potion_Reflection` | created |
| `testtube_potionShape 1 (7)` | `Potion_Counterspell` | created |
| `testtube_potionShape 1 (8)` | `Potion_Curse` | created |

Hierarchy order was **not** trusted — each tube's `Potion.type` component was read
first. It happened to match the table above exactly. All nine assets were then
re-read back and confirmed to carry `testtube_outline_hull` +
`M_PotionOutlineMask` + `M_PotionOutline`, shadows off, and the right colour.

No script resolves these tubes by name (`GameObject.Find` was grepped across
`Assets/Scripts/`), so the rename broke no lookups.

### ⭐ Priority 1 — Recolour the tubes again (THE NEXT TASK)

**This is what the next session is for.** The owner wants to change the potion
colours again. The current palette is the table in §5.

#### ⚠️ Read this first — the procedure CHANGED on 12 Aug 2026

Colour used to live in exactly one place. It now lives in **three**, because the nine
tubes became prefab variants:

| Where | What holds the colour | Persists? |
|---|---|---|
| `Assets/VFX/Profiles/VFX_<Type>.asset` | `primary` — the source of truth | yes |
| `Assets/Prefabs/Potion_<Type>.prefab` | `PotionAura.potionColour`, serialized | yes |
| The scene instance in `YeKai.unity` | may carry a `potionColour` **override** | yes |
| The renderer's `MaterialPropertyBlock` | what you actually SEE | **no — runtime only** |

The old instructions ("edit the profile, re-run `ApplyProfile` on the tube, save the
scene") are now **incomplete**. Editing only the scene instance leaves the prefab
asset stale, so anything spawned from the prefab gets the old colour. Editing only the
profile changes nothing visible at all, because nothing re-reads it automatically.

#### Recommended order

1. **Edit `primary`** in `Assets/VFX/Profiles/VFX_<Type>.asset`. This is the source of
   truth — always change it, even though nothing reads it at runtime yet.
2. **Push it into the prefab asset**, so spawned potions are correct:

```csharp
var profile = AssetDatabase.LoadAssetAtPath<PotionVFXProfile>("Assets/VFX/Profiles/VFX_Curse.asset");
var path    = "Assets/Prefabs/Potion_Curse.prefab";
var root    = PrefabUtility.LoadPrefabContents(path);      // edit the ASSET, not an instance
root.GetComponent<PotionAura>().ApplyProfile(profile);
PrefabUtility.SaveAsPrefabAsset(root, path);
PrefabUtility.UnloadPrefabContents(root);
```

3. **Then check the scene instance for a `potionColour` override.** If it has one it
   will win over the prefab and silently keep the old colour. Either clear the
   override, or call `ApplyProfile` on the instance too and re-save the scene.
4. **Verify visually.** `manage_camera` with `view_position [-0.36, 0.115, 0.115]` and
   `view_target [-0.36, 0.072, 0.003]` frames the whole rack head-on — that exact
   shot is how the last recolour was checked.

⚠️ Step 2 is the **recommended** flow but was **not exercised** in the 12 Aug session.
That session edited the scene instances *first* and created the prefab variants
*afterwards*, so the colours got baked in as a side effect. Expect to debug the
`LoadPrefabContents` path a little; the API itself is correct.

#### Palette advice for whatever you pick

Two collisions are already known and are the obvious things to fix:

- **Dispel `#F0F5FF` vs Reflection `#D9E3FF`** — both near-white and genuinely
  indistinguishable as thin outlines at rack distance. This is the worst one.
- **The blue-violet region is now crowded**: Foresight `#29D1FF` cyan, Warp `#4D73FF`
  indigo, Curse `#66088C` purple. Curse survives on being much darker, but do not add
  a fourth colour in that arc.

Free-ish regions if you need somewhere to move a card: **deep green**, **amber/brown**,
and **warm grey**. Remember §3 — nine distinct hues is over-subscribed, so separate by
**value** (light vs dark) rather than hue wherever you can. That is what makes
Curse/Warp work and what survives colourblindness and greyscale.

Also keep §7's HDR rule: dominant channel between **1.0 and 1.6**, never push all
three past 1.0 or the colour renders as white.

### ⭐ Priority 2 — Decide what happens to the SECOND tube rack

⚠️ **This corrects the old Priority 2 audit note, which was wrong.** The nine
`* Variant` prefabs are *not* orphaned assets left over from a pre-outline tube.
The editor audit found:

- They are **a different lineage entirely** — each is a Variant of
  `Assets/Models/tubes/<Type>.prefab`, **not** of `testtube_potionShape 1.prefab`.
- They have **no `PotionAura` component at all** — so no outline, no motes, no aura.
  They carry only a `Potion` component and 2 renderers.
- **Six of them are live, active GameObjects in `YeKai.unity`** — `Hex Variant`,
  `Dispel Variant`, `Foresight Variant`, `Counterspell Variant`, `Reflection Variant`,
  `Curse Variant` — sitting at `x = -0.952, y = 0.118, z = 0.196 → 0.485` at scale
  8.5. That is a **second rack**, laid out along Z, physically separate from the nine
  aura tubes (`x = -0.28 → -0.44, y = 0.066, z = 0.003`, scale 9.455, spaced along X).
- `Tribute Variant`, `Warp Variant` and `Phase Variant` exist as assets but are **not**
  placed in the scene.
- Nothing holds a serialized reference to them — `PotionGameManager` is **not present
  in `YeKai.unity` at all**, so they are not a spawn list.

So this is a genuine fork in the scene, not dead weight, and deleting it would remove
six visible objects. **Needs an owner decision** between:

1. **Delete the six scene objects and the nine assets** — if that rack was superseded
   by the aura rack.
2. **Retrofit the aura onto them** — add `PotionAura` + an `Outline` child to
   `Assets/Models/tubes/<Type>.prefab` so both racks match. Costs a second outline
   hull bake, because those are a different mesh from `testtube_v2.fbx`.
3. **Leave it alone** — if a teammate placed that rack and owns it.

Do not act on this without asking; it is the one part of the rename task that could
destroy someone else's work.

### Priority 3 — Cast VFX per ability

`SpellMotion` is defined in data but **nothing consumes it**. Needs a `PotionVFXPlayer`
that reads a profile and plays the matching motion. Existing raw material:
`PS_CardBusrt_Universal` (typo "Busrt" is in the actual GameObject name) with
`TintableVFX`, plus `BurstTester` on a `Cube` — press **Space** in Play Mode.

This is also the fix for the Dispel/Reflection colour collision — motion, not hue.

### Priority 4 — Hook into gameplay

`PotionGameManager.GetPrefabForType()` (~line 102 of
`Assets/Scripts/PotionGameManager.cs`) maps one prefab per `PotionType`. That's 9
prefabs to keep in sync, fighting the plan's own scalability requirement.

Recommended refactor — one prefab + the library:

```csharp
newPotion.GetComponent<PotionAura>()?.ApplyProfile(vfxLibrary.Get(type));
```

**Deliberately NOT done.** `Assets/Scripts/` is game logic. Get the owner's agreement.

### Priority 5 — Bloom (deferred, not blocking)

HDR is off and there is no Volume, so every value above 1.0 clips. The outlines and
cauldron were built to work without bloom, so this is now an enhancement rather than a
blocker. If you do it: enable HDR on `URP-Performant`, add a Global Volume with Bloom
(threshold ~1.0), tick Post Processing on the camera.

⚠️ `URP-Performant.asset` lives in `Assets/VRMPAssets/Settings/` and is **shared with
teammates' scenes**. Enabling HDR changes how `RaphaelScene` and `zelda.unity` render.
Duplicate it for `YeKai` only, or get agreement first.

Bloom is expensive on standalone headsets. Profile on-device.

### Priority 6 — Remaining aura layers

`trail` is still unassigned on every `PotionAura`. `OnGrabbed()` / `OnReleased()` exist
and should be hooked to `XRGrabInteractable`'s Select Entered / Select Exited for the
held-glow boost (which now also brightens the outline).

---

## 7. Gotchas — read before editing

**MaterialPropertyBlock: always `GetPropertyBlock` first.**
`SetPropertyBlock` replaces the *entire* block. `Liquid.cs` (wobble/fill) and
`PotionAura` (colours) both write to the same renderer. If either skips the read-back
it wipes the other's properties every frame. Both currently do it correctly.

**MPB values are not serialized into prefabs or scenes.** They are runtime-only. A
freshly instantiated potion shows its outline colour only once `PotionAura` runs
`Awake`/`OnValidate`. Do not expect to see the colour in the Project window preview.

**Colour now lives in three places, and a scene override beats the prefab.**
Since the tubes became prefab variants, `PotionAura.potionColour` is serialized into
`Assets/Prefabs/Potion_<Type>.prefab` *and* can be overridden on the scene instance,
on top of the `primary` in the profile asset. Change one and the others go stale —
the classic symptom is "I edited it and the rack still looks the same" (scene override
winning) or "the rack is right but spawned potions are wrong" (prefab left stale).
Full procedure in §6 Priority 1.

**Never use `.material` or `.sharedMaterial` for per-potion values.**
`.material` clones per object (breaks batching, leaks in editor); `.sharedMaterial`
edits the asset on disk (the git-churn bug above).

**HDR colours: don't push every channel past 1.0.**
Counterspell at `(1.00, 0.84, 0.40) × 4.0` = all three channels clipped → gold rendered
as **white**. Keep the dominant channel 1.0–1.6.

**The two generated meshes are one-time bakes.** `testtube_outline_hull.mesh` and
`pot_soup_split.mesh` are generated from `testtube_v2.fbx` and `pot.fbx`. If either
source model is re-imported, the generated mesh does **not** update and the pot will
keep pointing at the stale split. Re-run the bake.

**Stencil bit 6 (value 64)** is claimed by the outline. Explicit read/write masks are
used so it cannot tread on URP's own stencil bits — keep it that way if you add
another stencil effect.

**VR particle settings that matter:**
- Simulation Space **Local** — World smears particles behind the hand when grabbed
- Soft Particles **off** — needs a depth prepass, expensive on mobile
- Keep counts low; transparent overdraw is the budget

**Particle module curves must share a mode.** Setting only `velocityOverLifetime.y`
while x/z stay constants throws *"Particle Velocity curves must all be in the same
mode"* every evaluation. Set all three axes.

**Shader properties** on `fake liquid 1.shadergraph` / `Shader Graphs_fake liquid.mat`:
`_Fill`, `_SideColour`, `_TopColour`, `_WobbleX`, `_WobbleZ`. Outputs **BaseColor +
Alpha only (unlit)** — no emission port.

---

## 8. Known pre-existing issues (not caused by this work)

- **`Main Camera` in `YeKai.unity` has a missing script reference.** Needs the owner
  to decide.
- **`Potion_Hex.prefab` has a stale root-name override**, `testtube_potionShape 1
  Variant` instead of `Potion_Hex`. Purely cosmetic — the scene instance overrides it
  to `Potion_Hex`, and the other eight prefabs are correct. Fix by renaming the prefab
  asset's root in the editor if it bothers you.
- Unity Account API throws `HTTP/1.1 403 Forbidden` and a Vivox warning on startup —
  licensing/sign-in noise, unrelated to gameplay. **This is the only console output
  during normal operation; treat anything else as new.**
- `Liquid.cs` writes `_FillAmount` as a Vector but the shader exposes `_Fill` as a
  float. That write is a **no-op** — leftover from the original tutorial shader.
- **The pot is 92,249 triangles** (~90× the tube's 1,040). Very heavy for Quest.
  Decimation candidate if you hit frame budget.
- Untracked orphans `Assets/Materials/hat *.meta` resolve when the branch merges
  `main`.
- `Assets/Screenshots/` holds verification screenshots from the VFX sessions. Probably
  wants gitignoring rather than committing.

---

## 9. Tooling

Work is done through a **Unity MCP** connection. Two servers may appear:

| Server | Notes |
|---|---|
| `UnityMCP` (MCP for Unity bridge) | **The one that works.** Full access — components, GameObjects, materials, shaders, console, C# execution, play mode, screenshots. |
| `unity-mcp` (Unity's built-in AI) | Has been `Connection revoked` all along. Not needed. Re-approve at *Project Settings → AI → Unity MCP* if you want it. |

**If the bridge disconnects mid-session it cannot re-attach** — MCP servers are bound
at session start. Restart Claude Code with Unity already running.

`execute_code` runs C# via CodeDom (roughly C# 6): **no local functions**, and
`Object` is ambiguous — use `UnityEngine.Object`. `AssetDatabase.DeleteAsset` is
blocked by a safety check; load-and-overwrite instead.

Without the bridge an assistant can still read/write files and run git, but cannot
touch the editor.

---

## 10. Next session opener

> I'm continuing VFX work on a Unity 6 URP VR card game. Read `VFX_HANDOFF.md` in the
> project root for full context, then confirm the Unity MCP bridge is connected.
>
> I'm continuing VFX work on a Unity 6 URP VR card game. Read `VFX_HANDOFF.md` in the
> project root for full context, then confirm the Unity MCP bridge is connected before
> doing anything in the editor.
>
> **I want to change the potion tube colours again.**
>
> Before proposing values, read §6 Priority 1 — the recolour procedure changed once the
> tubes became prefab variants, and colour now lives in three places that can go stale
> independently. §5 has the current palette; §3 explains why motion matters more than
> hue and why nine distinct colours is over-subscribed.
>
> Tell me which colours you'd change and why before applying anything. The known
> problems are Dispel vs Reflection (near-identical whites) and a crowded blue-violet
> arc (Foresight / Warp / Curse).
>
> Only touch `YeKai.unity`, `Assets/VFX/`, and the potion/pot prefabs — teammates own
> the other scenes. Current branch is `YK9`, off the merged `main`.

**Also open, lower priority:** the second tube rack decision (§6 Priority 2) and cast
VFX per ability (§6 Priority 3, `SpellMotion` is still data-only).
