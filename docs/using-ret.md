# Using RET on your own project

This is the workflow from [method.md](method.md) as a checklist (engine-level detail for Unity IL2CPP and Unreal: [engine-playbook.md](engine-playbook.md)) you can follow on a game (or other program) of your
own. Read [RESPONSIBLE_USE.md](../RESPONSIBLE_USE.md) first: RET is for software you own, offline, and it refuses some
targets on purpose.

## 0. Set up

```bash
python3.11 -m venv .venv
.venv/bin/pip install -r requirements.txt      # UnityPy; only needed for Unity asset extraction
.venv/bin/python -m unittest discover -s tests
```

Pick a work folder outside the repository (RET uses `~/ret-work/<game>/`). Everything you extract or log goes there;
nothing from the target is ever committed.

## 1. Recon: is this target safe and possible?

```bash
.venv/bin/python src/main.py recon "<install folder>"
```

You get the engine and version, the scripting backend (Mono, IL2CPP and its metadata version, Unreal IoStore), any DRM
wrapper on the executables, online services, anti-cheat, and a verdict:

| Verdict | Meaning | What to do |
|---|---|---|
| **go** | nothing in the way | continue |
| **caution** | DRM wrapper or online services present | continue only with tools that leave the protection untouched, and play offline |
| **stop** | anti-cheat present | stop. Nothing is attached to this target |

## 2. Snapshot: make every change provable and reversible

```bash
.venv/bin/python src/main.py snapshot "<install folder>"          # before installing anything
.venv/bin/python src/main.py diff "<install folder>" <snapshot.json> # after, and after uninstalling
```

Write install scripts the way `src/agents/root-bepinex/deploy.sh` does: check the loader archive's sha256, refuse to
overwrite existing files, record every file added, and pair it with an uninstall script that removes exactly that list.

## 3. Choose the way in (from the recon result)

| Engine / backend | Loader | Example in this repo |
|---|---|---|
| Unity, IL2CPP | BepInEx 6 (bleeding edge for metadata v31) + HarmonyX | `src/agents/root-bepinex/` |
| Unity, Mono | BepInEx 5 or 6 | (same patterns, simpler: real .NET assemblies) |
| Unreal 4/5 | UE4SS (Lua) | `src/agents/sherlock-ue4ss/`, `experiments/s1_probe/` |

Start with the smallest possible probe: a mod that only logs, then one visible, reversible change (the speed probe is
the model). If the probe fails, stop and read; don't build on it.

## 4. Extract and read before writing

- **IL2CPP:** `src/utils/il2cpp_meta.py` dumps every type, field and method from `global-metadata.dat`. After the
  loader's first run, `src/tools/interop-inspect` prints exact signatures from the generated assemblies:
  ```bash
  dotnet build -c Release src/tools/interop-inspect
  dotnet <path>/interop-inspect.dll <assembly.dll> <type name suffix>
  ```
- **Unity assets:** UnityPy (see `experiments/root_*.py` for bundles, particle settings, Mecanim animation).
- **Unreal:** `src/utils/iostore.py` lists the files in IoStore containers; UE4SS's object dump shows live classes.

## 5. Observe, then imitate

For every behaviour you want to add, find the closest thing the program already does and **trace it read-only**
first: a postfix that only logs which method ran, with which arguments and context, in which order. Then write your
version in the same shape, with the program's own building blocks. The plugin's `TurnTrace`, `AbilityTrace` and
`MoveTrace` classes are the templates.

Patterns that carried over well (Root plugin):

- **Re-wire an existing feature instead of inventing UI**: postfix its target list, prefix its cost and its execution,
  and the program's own buttons, prompts and undo keep working.
- **Hosting**: run a new "kind of thing" on an existing class and change behaviour only for your instance (the mask).
- **Use the program's own factories and actions** (piece creation, moves, prompts), so it can save and replay them.

## 6. Test like an experiment

- One hypothesis per test run. Write down what you expect before running.
- Read the logs after every run (the loader's log and the program's own log).
- After three failures of the same kind, stop changing the guess; go back to step 4 or 5.
- Keep a dated entry per run: what changed, why, how it was tested, **what you assumed**, how to undo it.

## 7. Record the result

`src/agents/root-bepinex/record_match.sh` records the screen with a timeline of your mod's log lines and a manifest
(version, sha256, event counts, errors). Change `TAGS` and the log path for your own mod; choose the whole screen in the
share dialog; trim the ends before publishing so no desktop, launcher or account page is visible.

## Beyond games

Nothing in the method is game-specific. Recon reads Windows executables (PE), Unity and Unreal installs; the
read → trace → imitate → verify loop applies to any program you own and want to understand or extend, for example
to make two programs interoperate, to keep an old program working, to learn how a format is laid out, or to add a
feature its maker never shipped. The same limits apply: your own copy, no protection removed, no harm to anyone else.
See [RESPONSIBLE_USE.md](../RESPONSIBLE_USE.md).
