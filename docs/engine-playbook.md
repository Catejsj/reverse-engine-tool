# Engine playbook: modding a new game with RET

[using-ret.md](using-ret.md) is the general workflow. This page is the engine-level detail for the next game: one
recipe for **Unity IL2CPP** (BepInEx) and one for **Unreal Engine 4** (UE4SS Lua), written so they apply to any game on
that engine, not just the two modded here. Every step is marked:

- **tested** — done on *Root* (Unity 2022.3.62f2, IL2CPP metadata v31, BepInEx 6.0.0-be.788) or *Sherlock Holmes: The
  Awakened* (Unreal 4.27, UE4SS 3.0.1), both under Proton on Linux;
- **not tested** — what should transfer, but nobody has run it yet. Treat it as a hypothesis.

Read [RESPONSIBLE_USE.md](../RESPONSIBLE_USE.md) first. A recon verdict of **stop** (anti-cheat) ends the project, and
nothing on this page changes that.

## 0. Before any recipe: recon, pin, snapshot

1. **Recon** (`ret recon "<install folder>"`). Note the engine, its version and the scripting backend. The backend
   picks the recipe, not the game's genre (tested).

   | recon says | recipe |
   |---|---|
   | Unity, IL2CPP (`GameAssembly.dll` + `global-metadata.dat`) | Part A |
   | Unity, Mono (`<Game>_Data/Managed/Assembly-CSharp.dll`) | Part A, simpler: real .NET assemblies, BepInEx 5 or 6 (not tested) |
   | Unreal 4 / 5 (`<Project>/Binaries/Win64/<Game>.exe`, `.pak` / `.utoc`) | Part B |
   | anything else | Part D |

2. **Pin the game build.** Turn off automatic updates for the game in Steam. An update replaces the files your mod was
   built against, and in IL2CPP games it renumbers everything. RET's golden tests check the Steam `buildid` for the same
   reason (tested).
3. **Snapshot** before installing anything (`ret snapshot`). Diff after installing, and again after uninstalling. A clean
   uninstall shows **0 added, 0 changed** (tested on both games; Proton's own `vkd3d-proton.cache*` files change on every
   launch and don't count).
4. **Play offline**, and don't save a game you care about while a mod is installed (tested rule).

---

## Part A — Unity IL2CPP with BepInEx 6

### A1. Install the loader (tested)
- **Choose the BepInEx build by the metadata version.** Recon reads it. Metadata v31 (Unity 2022.3+) needs a 6.x
  *bleeding-edge* build. The 6.0.0-pre.2 release fails with "Unsupported metadata version". Download the build once,
  pin its sha256 in the install script, and keep the archive outside the repo.
- **Install by copying, and record the list.** `src/agents/root-bepinex/deploy.sh` is the template: refuse to run if a
  proxy DLL already exists, check the checksum, copy with `--no-clobber`, write every added path to a list.
  `undeploy.sh` removes exactly that list, plus the `BepInEx/` and `dotnet/` folders the loader generates.
- **Under Proton**, the proxy DLL only loads with a launch option: `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
- **The first launch takes several minutes.** BepInEx runs Cpp2IL and generates `BepInEx/interop/*.dll`, C# stand-ins
  for every game type. Wait for the game's main menu, then quit. The interop is regenerated after a game update.

### A2. Map the game before writing anything (tested)
- **Dump the metadata:** `src/utils/il2cpp_meta.py` lists every type, field and method from `global-metadata.dat`.
  Grep it to find the assemblies that matter. In *Root*, the rules lived in one assembly, the client/UI in another and
  the engine in a third. Write that split down; it decides which interop DLLs the plugin references.
- **Read exact signatures** from the generated interop with `src/tools/interop-inspect`: properties, every overload,
  base classes, nested types. Do this before every patch. Guessed signatures were the most expensive bug class.
- **Find the "asked every time" methods.** Games call a small set of virtual methods on every actor (in *Root*: each
  player's `GetSetupActions`, `GetBirdsongActions`, `GetDaylightActions`, `GetEveningActions`, `AdditionalDraws`, …).
  Those are the hooks. Write a table: *feature → hook → the closest thing the game already does → traced or only
  named*. Only "traced" rows are ready to build.
- **Trace read-only first.** A postfix that only logs which method ran, its arguments, the caller's context and the
  order. One trace run answered what four guessing runs had not. The plugin's `TurnTrace`, `AbilityTrace` and
  `MoveTrace` classes are the templates.

### A3. Plugin skeleton (tested)
- Project: `SpinnersOfMercy.csproj` targets **net6.0**, BepInEx 6's bundled runtime. It references
  `BepInEx.Core`, `BepInEx.Unity.IL2CPP`, `Il2CppInterop.Runtime`, `0Harmony` and only the interop DLLs it uses, all
  with `Private="false"` (never copy them). `Directory.Build.props` puts build output outside the repo, because it
  links against files generated from the game.
- `Load()` logs the plugin version first. Every log line then shows which build ran, and most "the fix didn't work"
  reports turned out to be the old DLL still installed.
- **Patch one method at a time** (`Plugin.PatchOne`), each in its own try/catch. Harmony rejects a whole class for one
  bad target, so one wrong signature silently disabled every other patch in the class.
- Logs: `<game>/BepInEx/LogOutput.log` (yours), and the game's own `Player.log` in the Proton prefix
  (`compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/LocalLow/<company>/<game>/Player.log`). Read both after
  every run; the game's log says why it ended a match.

### A4. Patching rules (each one cost test runs)
| Rule | Why (bug journal) |
|---|---|
| One Harmony owner per IL2CPP method | a second postfix on the same method never ran |
| Never patch or call into small generic value types the engine uses everywhere | patching a shared prefab-lookup struct broke every faction's models, then hard-crashed |
| Never call a method with a side effect to "look" at it (`Next…`, `Pop…`, counters) | calling one to read the setup order crashed the game; observe with a postfix |
| Compare interop objects by `.Pointer`, never `==` / `Contains` / `Distinct` | every click arrives as a new wrapper around the same native object |
| Never key anything by a native pointer unless something keeps the object alive | a freed object's address was reused and mistaken for another phase |
| `Nullable<T>` and other generic value types across the interop boundary: write, then read back | the value silently arrived as zero; the fix was writing the backing fields directly |
| Know what the engine serializes, and when | the game saved its action stack at every player prompt; a plugin-made action on it ended the match |
| Child actions inherit their parent's context: set the target in place | moves and draws read the wrong target and crashed until a trace showed the shape |
| Don't wrap a game phase; extend the game's own loop | the game asks every actor for a phase **twice**, so a wrapper ran twice |
| Never empty a game action; filter what it does | the emptied action also advanced the turn, and the game looped and resigned |
| Before re-wiring a button, read what the client actually sends for it | a button confirmed instantly with an empty selection; no click ever existed |

### A5. Adding things to the game (tested)
- **Register in the tables the game reads**, not by intercepting hot shared code. A new faction was a new enum value
  (unused number), entries in the registry the game consults, and a lobby slot.
- **Host on an existing class (the mask).** The new faction runs on an existing player class and keeps that class's
  identity everywhere the game looks. Every patch checks "is this our player?" and otherwise lets the original run. The
  client then draws, saves and replays it with no new code.
- **Re-wire existing UI instead of inventing it:** postfix the ability's target list, prefix its cost (making it
  unpayable greys the button) and its execution. The game's own buttons, prompts and undo keep working.
- **Make things with the game's own factory** (`createPiece` in *Root*), never a bare constructor. Then compare the
  result field by field with the game's own copy before using it. Hand-made pieces crashed the game's own `Move` later.
- **Art:** load PNGs at runtime with `ImageConversion`, mark the textures and sprites `DontUnloadUnusedAsset`, and swap
  HUD sprites by name. Nothing is cooked into the game's bundles.
- **Balance is a feature.** A complete-looking mod was too strong in its first full match; the fix came from the
  design's own source (the fan board), not from a new number.

---

## Part B — Unreal Engine 4 with UE4SS (Lua)

### B1. Install UE4SS (tested on 4.27, UE4SS 3.0.1)
- Files go next to the game's `.exe` in `<Project>/Binaries/Win64/`: `dwmapi.dll` (the proxy), `UE4SS.dll`,
  `UE4SS-settings.ini`, and `Mods/` with `mods.txt` (`ModName : 1` per line). Template:
  `experiments/s1_probe/deploy.sh` (sha256-pinned, no overwrite). `undeploy.sh` removes them and saves `UE4SS.log`.
- Launch option under Proton: `WINEDLLOVERRIDES="dwmapi=n,b" %command%`.
- **Turn all six `[Hooks]` in `UE4SS-settings.ini` off** (`HookProcessInternal`, `HookProcessLocalScriptFunction`,
  `HookInitGameState`, `HookCallFunctionByNameWithArguments`, `HookBeginPlay`, `HookLocalPlayerExec`). With any of
  them on, this game crashed on start. Turn one on later only if a mod needs it, and test that alone.
- A Steam DRM wrapper on the `.exe` didn't matter: UE4SS works through the engine's reflection and never touches the
  wrapper.

### B2. First mod: the probe (tested)
`experiments/s1_probe/RetS1Probe` is the smallest useful mod. A key binding (`RegisterKeyBind`) finds the player
(`FindFirstOf`), changes one value visibly (time dilation), and a second key restores it. If the probe fails, stop and
fix that before writing anything else.

### B3. Map the game (tested)
- `GetFullName()` on objects you find, and UE4SS's object dump, give the live class and object paths.
  `StaticFindObject("/Script/…")` then fetches classes and defaults by path.
- The pak / IoStore list (`src/utils/iostore.py`) shows what content exists without unpacking anything.
- Studios customise the engine: Frogwares characters hide through their root component, not the actor's hidden flag,
  and they ship skeletal meshes with gravity off. Check how the game's own objects behave before assuming stock
  Unreal.

### B4. Lua rules (each one cost test runs)
| Rule | Why |
|---|---|
| Anything touching game objects from a key binding or `LoopAsync` runs inside `ExecuteInGameThread` | UE4SS's documented rule; the mods followed it throughout (going without it was never tested) |
| `IsValid()` before every use of an object kept across frames | the level or the GC can replace it at any time |
| Everything created from Lua must be owned by an engine object (parent widget, viewport, a hidden image holding each texture) | the garbage collector freed unowned objects after about a minute |
| `StaticConstructObject(class, outer, name)`: give it a real outer | the outer is what keeps it alive and in the right world |
| Don't count on runtime world spawns rendering under UE4SS 3.0.1 | an actor with a widget component or a textured mesh spawned fine but was never drawn; the screen overlay did draw |
| Spawn actors with UE4SS's own `World:SpawnActor(class, location, rotation)`, not the engine's `BeginSpawningActorFromClass` / `BeginDeferredActorSpawnFromClass` | the engine's two spawn functions failed from Lua in 3.0.1. Class and transform arguments themselves worked: a probe disproved the first theory, which blamed them. Probe before you write a theory down |
| Fake "in the world" with measurements: `ProjectWorldToScreen` for placement, world distance for size, `LineTraceSingle` for walls | this gave the 3D look without a 3D object |

### B5. Bringing content in (tested)
- Textures: `ImportFileAsTexture2D` on PNG files at runtime (a Wine path such as `Z:/home/...`). No cooking and no pak
  changes.
- Physics on the game's own actors: `SetSimulatePhysics` plus an impulse, and keep a list so an undo key can restore
  every body you touched.
- Sound: UE4SS Lua had no easy way into the engine's audio, so the mod writes small event files and a native process
  on the host (`ret companion`) plays the sounds.

---

## Part C — Moving content between engines (tested once: Root → Sherlock)

Don't try to import one engine's assets into the other. Read the source assets (UnityPy for Unity bundles; RET's
Mecanim reader for animation clips), **re-create the effect outside both engines** (the particle system was
re-simulated in Python into PNG frames), and show the result with the target engine's simplest working display. The
scripts that make the content run on the user's own copy, so nothing extracted is ever shipped.

## Part D — Other engines (not tested)

- **Unity Mono:** BepInEx 5 or 6 with Harmony on the real `Assembly-CSharp.dll`. Signatures can be read with any .NET
  decompiler, and Part A's interop rules (wrappers, pointers, `Nullable`) mostly don't apply.
- **Unreal 5:** UE4SS supports UE5 versions. Re-check the `[Hooks]` rule and the runtime-spawn limit on the new game
  before relying on them.
- **Godot, GameMaker, custom engines:** recon reports what it finds; the method (recon → snapshot → read → trace →
  imitate → verify) is unchanged. Only the loader and the reading tools differ. Start with a probe that only logs.

## Part E — Restoring a game to stock (tested on both)

1. Run the mod's `undeploy.sh`. The Unity template removes exactly the recorded install list plus the generated
   folders; the UE4SS template removes the proxy, UE4SS and `Mods/`.
2. Clear the game's Steam launch option.
3. `ret diff "<install folder>" <snapshot.json>` must show **0 added, 0 changed** apart from Proton's shader caches.
   If it doesn't, the list says exactly what's left.
4. Uninstalling in Steam only removes files Steam installed, so do steps 1–3 first. The Proton prefix
   (`compatdata/<appid>/`) keeps saves and logs after an uninstall.

## Checklist for the next game
- [ ] recon verdict is not **stop**; engine + backend noted; auto-update off; snapshot taken
- [ ] loader chosen by version (Part A1 / B1), checksum-pinned, installed by a script that records what it adds
- [ ] probe mod: logs its version, makes one visible reversible change, passes
- [ ] game mapped: assemblies or classes, the "asked every time" hooks, a feature → hook → analogue table
- [ ] each feature: trace the game's own version → imitate it → one hypothesis per run → read both logs
- [ ] three identical failures → stop guessing, go back to reading or tracing
- [ ] dated log per run: what changed, why, tested how, what was assumed, how to undo
- [ ] evidence recorded (`record_match.sh`), trimmed to game-only footage before anything is published
- [ ] uninstall → `ret diff` shows the game back to stock
