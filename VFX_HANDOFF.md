# Potion VFX — Handoff

Working doc for continuing the potion visual-effects work on **Who Cast That?!**
Written for a fresh session with no prior context.

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

- Working branch: **`YK7`** (Tan Ye Kai). Main branch is `main`.
- Teammates own other scenes — **`RaphaelScene.unity`**, **`zelda.unity`**,
  `LobbyScene.unity`, `BootScene.unity`. **Do not touch these.**
- All VFX work is confined to **`Assets/Scenes/YeKai.unity`** and `Assets/VFX/`.
- Per the project plan, Animation & VFX is a Weeks 10–11 deliverable.

---

## 2. Tech stack

| | |
|---|---|
| Unity | **6000.0.74f1** |
| Pipeline | **URP 17.0.4**, asset named `URP-Performant` |
| XR | XR Interaction Toolkit 3.3.1, OpenXR 1.16.1, XR Hands 1.7.3, Android XR |
| Backend | Firebase |
| **VFX Graph** | **NOT installed** — use Shuriken (built-in Particle System) + Shader Graph |

Target is standalone/mobile-class XR (Quest via Android XR) as well as PC VR, so
**transparent overdraw is the main performance constraint.**

---

## 3. Core design decision (read this before adding effects)

The reference material for this work was a *card game* VFX tutorial. *Card VFX do not
port directly to VR.*

Cards are viewed on a flat screen, so camera-facing billboard sprites read perfectly.
In VR a test tube sits ~40cm from your face and you see it in **stereo**. Those same
billboards read as flat cardboard stickers, because each eye proves they have no depth.

**Therefore: build effects on real geometry and real 3D space**, not flat sprites.
Ranked by value-for-cost:

| Layer | Cost | Status |
|---|---|---|
| Physics-driven liquid slosh | ~free | ✅ done (pre-existing `Liquid.cs`) |
| Per-type colour on the liquid | ~free | ✅ done |
| Real `Light` that tints the player's hands | low | ✅ created, but see Bloom gap |
| Orbiting motes (3D placed) | medium | ✅ created |
| Cast burst per ability | medium | ❌ not built |

### Motion matters more than colour

~8% of male players have colour vision deficiency. **Hex (red) and Phase (green)
collapse into the same tan for deuteranopes** — and both are "end my turn" adjacent
cards, so that's a real gameplay confusion. Each ability therefore has a distinct
**motion signature**, defined in the `SpellMotion` enum, which survives colourblindness
and reads faster than hue in a headset:

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

Directionality is a VR-specific win: Hex and Tribute should visibly point at a player.

---

## 4. What is DONE

### Files created (all new, in `Assets/VFX/`)

| File | Purpose |
|---|---|
| `PotionVFXProfile.cs` | ScriptableObject: one per potion type — colour, `SpellMotion`, duration, scale, burst count, light, pulse, audio. Defines the `SpellMotion` enum. |
| `PotionVFXLibrary.cs` | `PotionType` → profile lookup. Warns on duplicate/missing types. Has `TryGetMissing()`. |
| `PotionAura.cs` | Drives liquid colour + light + motes + trail from ONE colour. `[ExecuteAlways]` so it previews in the editor. |

### Assets authored

- `Assets/VFX/Profiles/VFX_<Type>.asset` — **all 9**, validated complete
- `Assets/VFX/PotionVFXLibrary.asset` — all 9 wired in
- `Assets/VFX/Materials/M_Motes.mat` — URP Particles/Unlit, additive
- `Assets/VFX/Materials/T_Mote.asset` — procedurally generated 64×64 soft round mote

### Scene wiring — `YeKai.unity` (saved to disk)

9 test tubes configured, one per type, in hierarchy order:

| GameObject | Type |
|---|---|
| `testtube_potionShape 1` | Hex |
| `testtube_potionShape 1 (1)` | Tribute |
| `testtube_potionShape 1 (2)` | Dispel |
| `testtube_potionShape 1 (3)` | Foresight |
| `testtube_potionShape 1 (4)` | Warp |
| `testtube_potionShape 1 (5)` | Phase |
| `testtube_potionShape 1 (6)` | Reflection |
| `testtube_potionShape 1 (7)` | Counterspell |
| `testtube_potionShape 1 (8)` | Curse |

Each has: a `Potion` type tag, a `PotionAura`, a **`Glow`** child (point light,
shadows off), and a **`Motes`** child (26 particles, donut shape, orbital drift,
**Local** simulation space so they follow the tube when grabbed).

> Note: `testtube_potionShape` (no " 1") and `testtube_potion_empty` are separate
> inactive objects — deliberately left alone.

### Final colour palette

Grouped by **mechanical role**, so players read function at a glance.
Values are `primary` as stored in the profile assets (base sRGB × HDR multiplier):

| Type | Role | Base sRGB | ×mul | Stored `primary` |
|---|---|---|---|---|
| Hex | aggression | 1.00, 0.23, 0.09 | 1.25 | `(1.25, 0.29, 0.11)` |
| Tribute | theft | 1.00, 0.31, 0.64 | 1.15 | `(1.15, 0.36, 0.74)` |
| Dispel | negation | 0.94, 0.96, 1.00 | 1.60 | `(1.50, 1.54, 1.60)` |
| Foresight | knowledge | 0.16, 0.82, 1.00 | 1.10 | `(0.18, 0.90, 1.10)` |
| Warp | chaos | 0.65, 0.29, 1.00 | 1.15 | `(0.75, 0.33, 1.15)` |
| Phase | evasion | 0.49, 1.00, 0.70 | 1.10 | `(0.54, 1.10, 0.77)` |
| Reflection | mimicry | 0.85, 0.89, 1.00 | 1.20 | `(1.02, 1.07, 1.20)` |
| Counterspell | salvation | 1.00, 0.84, 0.40 | 1.30 | `(1.30, 1.09, 0.52)` |
| Curse | death | 0.55, 0.04, 0.10 | 1.00 | `(0.55, 0.04, 0.10)` |

**Design intent worth preserving:**
- *Dispel* has the highest value in the game — it's the only out-of-turn interrupt,
  so it must punch above whatever it's cancelling.
- *Reflection* should ideally **borrow the colour of the card it copies** rather than
  keep a fixed hue. Currently a fixed pearlescent silver as a placeholder.

### Bug fixed — `Liquid.cs.cs` material churn

`Assets/Textures/potions/testtube_v2/Liquid.cs.cs` is `[ExecuteInEditMode]` and used to
write `rend.sharedMaterial.SetFloat(...)`. `sharedMaterial` is the **.mat asset on disk**,
so it rewrote the material file every editor frame. Consequences:

1. `Shader Graphs_fake liquid.mat` showed as permanently modified in GitHub Desktop
   → constant git noise and **merge conflicts with teammates**
2. All test tubes shared one wobble value → they sloshed in lockstep

Fixed by routing through a `MaterialPropertyBlock` in a new `ApplyToRenderer()` method.
Verified: the `.mat` now stays clean through profile changes and transform moves.

---

## 5. What still NEEDS doing

### Priority 1 — Bloom (blocking the whole look)

**There is no Volume in `YeKai.unity` at all** (`Volumes in scene: 0`). Every HDR value
above 1.0 currently just **clips** instead of glowing, so the potions read as flat
colour. Nothing will look like a "spell effect" until this exists.

Required:
1. URP asset (`URP-Performant`) → enable **HDR**
2. Add a **Global Volume** to `YeKai.unity` with a **Bloom** override, threshold ~1.0
3. Confirm the camera has Post Processing ticked

⚠️ Bloom is expensive on standalone headsets. Profile it on-device; if it hurts,
drop bloom and lean on the lights + slosh, which cost almost nothing.

### Priority 2 — Glass is opaque white

At rack distance the white tube body swallows the potion colour — only a thin strip of
liquid shows. Up close it looks correct. Since players read these from across a table
in VR, the **glass material needs to be transparent** so the liquid dominates.

### Priority 3 — Cast VFX per ability

The `SpellMotion` archetypes are defined in data but **nothing consumes them yet**.
Needs a `PotionVFXPlayer` that reads a profile and plays the matching motion.

Existing raw material to reuse: `PS_CardBusrt_Universal` (note: typo "Busrt" in the
GameObject name) with `TintableVFX`, plus `BurstTester` on a `Cube` — press **Space**
in Play Mode to fire it. This is the one-effect-serves-every-card pattern to extend.

### Priority 4 — Hook into gameplay

`PotionGameManager` currently maps **one prefab per `PotionType`** via
`GetPrefabForType()` (~line 102 of `Assets/Scripts/PotionGameManager.cs`).
That's 9 prefabs to keep in sync — it fights the project's own stated scalability
requirement ("allow for future addition of new cards").

**Recommended refactor:** collapse to **one prefab + the library**. After `Instantiate`:

```csharp
newPotion.GetComponent<PotionAura>()?.ApplyProfile(vfxLibrary.Get(type));
```

This was deliberately NOT done — `Assets/Scripts/` is game logic and was left untouched.
Get the owner's agreement before refactoring.

### Priority 5 — Remaining aura layers

`trail` is still unassigned on every `PotionAura`. `OnGrabbed()`/`OnReleased()` exist
and should be hooked to `XRGrabInteractable`'s Select Entered / Select Exited events
for the held-glow boost.

---

## 6. Gotchas — read before editing

**MaterialPropertyBlock: always `GetPropertyBlock` first.**
`SetPropertyBlock` replaces the *entire* block. `Liquid.cs` (wobble/fill) and
`PotionAura` (colours) both write to the same renderer. If either one skips the
read-back, it wipes the other's properties every frame. Both currently do it correctly
— don't break this.

**Never use `.material` or `.sharedMaterial` to set per-potion values.**
`.material` clones a material per object (breaks batching, leaks in editor);
`.sharedMaterial` edits the asset on disk (git churn — the bug fixed above).

**HDR colours: don't push every channel past 1.0.**
This was a real bug during development. Counterspell at `(1.00, 0.84, 0.40) × 4.0`
= `(4.00, 3.36, 1.60)` — all three channels clip, so gold rendered as **white**.
Keep the dominant channel around 1.0–1.6; anything above 1.0 is bloom headroom.

**VR particle settings that matter:**
- Simulation Space **Local** — World smears particles behind the hand when grabbed
- Soft Particles **off** — needs a depth prepass, expensive on mobile GPUs
- Keep particle counts low; overdraw is the budget

**Shader properties** on `fake liquid 1.shadergraph` / `Shader Graphs_fake liquid.mat`:
`_Fill`, `_SideColour`, `_TopColour`, `_WobbleX`, `_WobbleZ`.
It outputs **BaseColor + Alpha only (unlit)** — there is no emission port, so glow comes
purely from pushing colour above 1.0 into bloom.

---

## 7. Known pre-existing issues (not caused by this work)

- **`Main Camera` in `YeKai.unity` has a missing script reference.** Throws a console
  error every load. Untouched — needs the owner to decide.
- Unity Account API throws `HTTP/1.1 403 Forbidden` on startup — a licensing/sign-in
  hiccup, unrelated to gameplay.
- `Liquid.cs` writes `_FillAmount` as a Vector, but this shader exposes `_Fill` as a
  float. That write is a **no-op** on the current material — it's leftover from the
  original tutorial shader. Harmless, but don't expect fill control from it.

---

## 8. Git housekeeping

Earlier in this work a GitHub Desktop stash refused to restore (35 phantom `.meta`
conflicts left in the index by a branch switch around a `main` pull). It was resolved
and the work recovered. Two safety refs were created and **still exist**:

```
branch: stash-backup-YK7
tag:    stash-backup-YK7-tag
```

Both point at the recovered stash commit `5b364ff`. Once the current work is committed
and verified, these can be deleted:

```bash
git branch -D stash-backup-YK7 && git tag -d stash-backup-YK7-tag
```

There may also be a leftover `stash@{0}` entry that GitHub Desktop still displays. Its
contents are already in the working tree — drop it with `git stash drop stash@{0}`.

Untracked leftovers `Assets/Materials/hat *.meta` and `Assets/Screenshots.meta` are
orphans from a `main` checkout; they resolve when `YK7` merges `main`.

---

## 9. Suggested next session opener

> I'm continuing VFX work on a Unity 6 URP VR card game. Read `VFX_HANDOFF.md` in the
> project root for full context. Start with Priority 1: enable HDR on the URP asset and
> add a Global Volume with Bloom to `Assets/Scenes/YeKai.unity`, so the HDR potion
> colours actually glow. Only touch `YeKai.unity` and `Assets/VFX/` — teammates own the
> other scenes.

**Tooling note:** this work was done through a Unity MCP connection which is currently
**disconnected** ("Connection revoked" — re-approve at *Unity Editor → Project Settings
→ AI → Unity MCP*). Without it, an assistant can still read/write files and run git, but
cannot add components, read the console, or take screenshots.
