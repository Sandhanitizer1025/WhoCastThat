# Handover for Zelda — the bake is done, voiceovers are unblocked

_Who Cast That?! · written 2026-08-14 · `main` @ `523a82b`_

Your own `ZELDA_HANDOVER.md` said **bake first, then voiceovers**. The bake is finished and
merged, so you're clear to start.

This doc covers two things: **what the bake changed** that you need to know about, and
**the state of the audio wiring as it actually is on disk today** — I read all of it back
from the code and assets rather than trusting the earlier write-up, and a few things have
moved.

---

## ⚠️ 0. Read this first — your recordings are not in the repo

**`Assets/Audio/wizard recs/` does not exist.** Not empty — absent. And
`git log --all --diff-filter=A` finds no commit that ever added anything under that path,
so it has never been tracked on any branch.

Everything under `Assets/Audio/` that *is* tracked:

```
Assets/Audio/click.mp3
Assets/Audio/Music/     MenuTheme, game_music, lobby_music, tutorial_music
Assets/Audio/SFX/       cast_sfx, cursed_sfx, foresight_sfx, scene_transition_sfx
```

So the 20 clips are local to your machine only. **Before anything else, copy them in and
commit them** — otherwise nothing you wire will work for anyone but you, and a fresh clone
gets null clips.

There is a second reason to do this urgently. Earlier today a "discard all changes" in
GitHub Desktop wiped a session's uncommitted work in this repo, **including untracked
files**. Untracked audio is exactly the kind of thing that vanishes without warning. Commit
it, don't just copy it.

---

## 1. What the lighting bake changed

All three shipping scenes are now baked. Nothing about gameplay, prefabs or audio was
touched, but three things affect how you work.

### The light rig lives in `RoomV2.prefab` — never edit a scene instance

This was already true; it matters more now because the baked data is keyed to it. If you
change a light on a scene's `RoomV2` instance you create an override, the three scenes
diverge, and the bake silently stops matching the lighting. **Open the prefab.**

### Runtime lights went from 24 to 2

Only `Chandelier Key` and `Fireplace Light` are still Mixed. The other 22 — the 14-candle
chandelier ring, 6 wall orbs, 2 shelf lamps — are now **Baked**, contributing to the
lightmap and costing nothing at runtime.

The consequence for you: **lightmaps do not light dynamic objects.** Players, hats and
potions are lit *only* by those two Mixed lights plus ambient. If someone demotes either of
them to Baked, avatars go flat and dark in a beautifully lit room. Don't.

### If you re-bake, use the existing settings asset

`Assets/Settings/LS_RoomV2_Quest.lighting`, assigned to all three scenes. Progressive GPU,
25 texels/unit, 1024 max, AO on, Non-Directional, Mixed mode **Baked Indirect**. A bake at
these settings takes a couple of minutes on this machine.

Also new: a `Mirror Glow` baked point light in front of the magic mirror, so the mirror
alcove is no longer black. Relevant to you because it's the lobby's menu surface.

| Scene | Renderers lightmapped | Lightmap |
|---|---|---|
| `InteractionTestScene` | 389 / 389 | 1024×1024 |
| `TutorialScene` | 389 / 389 | 1024×1024 |
| `LobbyMirrorScene` | 385 / 385 | 1024×1024 |

⚠️ **Nothing here has been checked on a headset.** Every brightness call was made on a
monitor. Quest displays differ enough that this genuinely needs an on-device look, and
that's part of your play test.

---

## 2. The audio wiring, as verified today

Everything in this section I read from the current files, not from the previous handover.

### `Assets/Resources/GameAudioLibrary.asset`

```
sceneTracks    3 entries  (LobbyMirrorScene, InteractionTestScene, TutorialScene)
castSfx        cast_sfx.mp3
spellStings    ONE entry: type 3  ->  foresight_sfx
cursedSfx      cursed_sfx.mp3          <- already wired
sceneTransitionSfx  scene_transition_sfx.mp3
```

`type: 3` is Foresight. That single entry points at an **SFX**, not a voice recording, so
**none of your recordings are referenced anywhere.** That matches what you were told.

### What `GameAudioDirector` actually subscribes to

Verified at `Assets/Scripts/Audio/GameAudioDirector.cs:93-104`:

```csharp
NetworkedSpellGame.SpellCastStarted += OnSpellCastStarted;
NetworkedSpellGame.PlayerCursed     += OnPlayerCursed;
```

**That is the complete list.** `SpellResolved` and `SpellFizzled` exist and fire, but
**nothing subscribes to them anywhere in the project.** Your Dispel/Reflection component
will be their first consumer — so if it doesn't work, suspect your own wiring before you
suspect the events.

### The event seam — signatures confirmed

`Assets/Scripts/InteractionTest/NetworkedSpellGame.cs:1876-1885`:

```csharp
public static event Action<PotionType, ulong, float> SpellCastStarted;  // type, caster, windowSeconds
public static event Action<PotionType, ulong, ulong> SpellResolved;     // type, caster, target
public static event Action<PotionType, ulong>        SpellFizzled;      // type = the DISPELLED spell
public static event Action<ulong>                    PlayerCursed;      // player
```

**`windowSeconds` really can be `0`.** Line 1747 raises the event with a literal `0f` when
there is no interrupt window; line 1756 uses `Mathf.Max(0.1f, interruptWindowSeconds)` when
there is. Treat `0` as "resolves right now", not as "I have time".

These four events are all `static` and are explicitly nulled on shutdown (lines 1897-1900).
That is a safety net, not permission to leak — **subscribe and unsubscribe in strict pairs
with a method group, never a lambda.**

### `CastSfxFor` behaviour

`GameAudioLibrary.cs:69-84` walks `spellStings` for a matching `type` with a non-null clip
and returns it. So adding an entry is genuinely all that's needed — a spell's own clip
plays *instead of* the generic `castSfx`, never both.

---

## 3. Suggested order of work

1. **Commit the recordings.** Section 0. Nothing else matters until this is done.
2. **Wire the five easy spells** — Hex, Tribute, Foresight, Warp, Phase. Pure data: grow
   `spellStings`, one `PotionType` + `AudioClip` per entry. Note Foresight already has an
   entry pointing at `foresight_sfx`; you're replacing it, not adding a second.
3. **Curse** goes in `cursedSfx`, not `spellStings` — it's only ever drawn, so it never
   raises `SpellCastStarted`. That slot is already wired to `cursed_sfx.mp3`, so decide
   whether your `curse.mp3` replaces it or plays alongside.
4. **Counterspell** — test before trusting. It runs through the curse-defence branch, and
   the code comments say it cannot currently reach `ApplyEffect`.
5. **Dispel and Reflection** — the ~40-line component. `SpellFizzled` gives you the
   dispelled spell's type directly; `SpellResolved` fires twice on a Reflection, first with
   `Reflection` then with the copied type.
6. **Play test.** Keyboard fallback (hold Left Ctrl) is in your own handover §5 and is the
   fast way to hear every line without a headset.

`PotionType` by index, since the inspector shows it numerically:

```
0 Hex   1 Tribute   2 Dispel   3 Foresight   4 Warp
5 Phase 6 Reflection 7 Counterspell 8 Curse
```

Don't spend time on `reflection (dispel).mp3` — a Reflection can never copy a Dispel.

---

## 4. Two uncommitted changes waiting for an owner

Both were sitting in the working tree and I deliberately left them out of every commit.

### `Assets/Prefabs/InteractionTest/NetworkedPotion.prefab`

One line: `GlobalObjectIdHash: 3032154966 -> 3927466592`. Netcode's network-prefab identity.
It changed on its own during a mass model reimport. Clients must agree on this value to
spawn potions, so it needs whoever owns the netcode to decide — not a lighting change and
not mine to commit.

### `Assets/Scenes/LobbyMirrorScene.unity` — **this one is yours, and it's the trap you documented**

After I saved and committed the scene, three lines went dirty again on their own:

```
m_LocalScale       0.0011695712  ->  0.0009916751
m_AnchoredPosition {0.1056, 0.6817} -> {0.1056, 0.5310}
m_Color            {0.05, 0.02, 0.12, a: 0.88} -> a: 0
```

That is the `MirrorMenu` world-space Canvas doing exactly what your handover §3 warns about:
its RectTransform values are *driven*, so they get rewritten on reserialize. **The alpha
going to 0 is the worrying one** — a panel background at zero alpha is invisible.

I did not commit it, so `main` still holds the values that were live when I screenshotted
the lobby menu and it rendered correctly. But it will keep re-dirtying whenever the scene is
saved. It's your scene and your documented failure mode, so it's your call whether the new
values are correct or a regression.

---

## 5. Corrections to `LIGHTING_BAKE_HANDOVER.md`

For Raphael, if it's useful — three of its location claims were wrong, all harmless:

- `Directional Light` is **inside** `RoomV2.prefab` (disabled), not outside it.
- The 2 particle renderers to exclude are `fire_particle` and `smoke_particle` under
  `Props (YeKai)/fireplace` — **not** `SoupSteam`/`SoupBubbles`, which live outside the
  prefab in the game scenes.
- §7's reference screenshots `ITS_final_spot.png` / `ITS_chandelier_check.png` were never
  committed to git and don't exist in the repo.

Also, §5 doesn't specify a **mixed lighting mode**, which materially changes the result.
I chose Baked Indirect, because §6's spot-over-point shadow-map argument only pays off if
the chandelier keeps realtime shadows.

---

## 6. Quick reference

```
Bake settings      Assets/Settings/LS_RoomV2_Quest.lighting   (all 3 scenes)
Light rig          Assets/Prefabs/RoomV2.prefab               <- edit the PREFAB
Old room           Assets/Prefabs/room.prefab                 <- zelda.unity only, leave alone

Audio director     Assets/Scripts/Audio/GameAudioDirector.cs      subscribes: 93-104
Audio library      Assets/Scripts/Audio/GameAudioLibrary.cs       CastSfxFor: 69-84
Library asset      Assets/Resources/GameAudioLibrary.asset        spellStings lives here
Event seam         Assets/Scripts/InteractionTest/NetworkedSpellGame.cs:1876-1885
Recordings         Assets/Audio/wizard recs/                  <- MISSING, see section 0
```
