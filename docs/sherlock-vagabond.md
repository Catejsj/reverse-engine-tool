# Root's Vagabond in *Sherlock Holmes: The Awakened*

A cross-game port: *Root*'s **Ball of Violence**, the dust cloud its fights happen in, with the Vagabond leaping into
it, appears inside *Sherlock Holmes: The Awakened* (Unreal Engine 4.27). The struck NPC falls under Sherlock's own
ragdoll physics, Root's battle and death sounds play, and everything can be undone.

<p align="center"><img src="../media/sherlock-strike.gif" width="70%" alt="The strike in game"></p>

## Keys

| Key | Mod | Effect |
|---|---|---|
| **F6** | RetVagabond | strike: the nearest NPC in front of Holmes is swallowed by Root's fight cloud, the Vagabond lands in it, the NPC ragdolls |
| **F10** | RetVagabond | undo: every NPC knocked down by the mod stands back up (nothing is ever destroyed) |
| **F9** | RetS1Probe | 2× time dilation on Holmes (the pawn ticks at double speed) |
| **F7** | RetS1Probe | doubles the game's movement speed cap (no visible effect: it is a cap, not a speed) |
| **F8** | RetS1Probe | restore normal speed |

## How it was built

**1. Can this game be modded at all, without touching its protection?** `ret recon` found the Steam wrapper on the
executable and an unencrypted pak index. UE4SS 3.0.1 loads beside the game through a `dwmapi.dll` override and works
through Unreal's own reflection, so the wrapper is never touched. The first test (`experiments/s1_probe`, the speed
probe) passed: Lua runs, reads the player, and makes a visible, reversible change. One condition was found the hard
way: all six optional UE4SS hooks must stay **off**, otherwise the game crashes on start.

**2. Root's effect, without either engine's editor.** Root's particle settings and textures were read from its Unity
files with UnityPy, and the Ball of Violence was **re-simulated in Python** from those settings into PNG frames
(`experiments/root_fx_*.py`). Root's Vagabond animation clips were decoded by RET's own Mecanim reader
(`src/utils/mecanim.py`) and rendered (`experiments/root_char_*.py`), then composed into one strike sequence: the leap,
the cloud bursting, the Vagabond revealed in his victory pose (`experiments/root_vaga_strike.py`). Nothing extracted is
shipped here; the scripts make the frames from the user's own copy of Root.

**3. Showing it in Sherlock.** The Lua mod (`src/agents/sherlock-ue4ss/RetVagabond/Scripts/main.lua`) loads the frames
as textures at runtime (`ImportFileAsTexture2D`) and plays them on a screen overlay placed with `ProjectWorldToScreen`,
**sized by world distance** (one frame pixel = 1.25 cm at the NPC's spot), so it shrinks with distance like a thing in the
scene. A visibility trace hides the cloud when a wall is between Holmes and the NPC.

**4. The NPC falls.** At the cover frame the NPC's skeletal mesh switches to physics (the studio ships the bodies with
gravity off, so it is turned on) and is shoved away from Holmes. F10 reattaches and restores every body the mod
touched.

**5. Sound.** UE4SS's Lua cannot easily feed audio into the engine, so the mod writes a tiny event file per sound and
`ret companion` (native Python on Linux) plays Root's own WAVs.

## Lessons (each one cost a test)

- **Anything created from Lua must be referenced by an engine object**, or the garbage collector frees it in about a
  minute. The overlay is built once and stays in the viewport; every frame texture is held by its own hidden image.
- **A world-space panel never drew.** Version 5 spawned an actor with a widget component in the 3D world: the spawn
  worked, but nothing added to the world at runtime was ever rendered under this UE4SS version. The screen overlay is
  the display that works, so version 6 went back to it and fixed size and walls by measuring the world instead.
- **Frogwares characters hide through their root component**, not the actor's hidden flag.
- **Close NPCs filled the screen** when the overlay was sized from the NPC's screen height; sizing from world distance
  fixed it for every distance.

## Installing (your own copy; personal use)

1. UE4SS v3.0.1 zip, unpacked where `experiments/s1_probe/deploy.sh` expects it (the script checks its sha256), then
   run `deploy.sh`. Launch option: `WINEDLLOVERRIDES="dwmapi=n,b" %command%`.
2. Make the strike frames from your own Root install: `experiments/root_fx_render.py`, then `root_vaga_strike.py`.
3. `src/agents/sherlock-ue4ss/deploy.sh` installs RetVagabond; start `ret companion` for the sound; play.
4. The undeploy scripts remove exactly what was added. Don't save while the mods are on.

This was done on a personal copy, offline, with the game's protection left intact; nothing from the game is
distributed here.
