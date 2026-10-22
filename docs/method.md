# The RET method (The Reversal Engine)

RET started as a toolkit and turned into a way of working. The toolkit answers "what is this game made of?"; the
method answers "how do I change it without spending a week on crashes?". Both were shaped by two real projects (a new
faction in *Root*, Unity IL2CPP; a cross-game port into *Sherlock Holmes: The Awakened*, Unreal Engine 4), and every
rule below exists because breaking it cost test runs.

## The stages

```
 recon ──► snapshot ──► extract ──► observe ──► imitate ──► verify in game ──► record
   │                                   ▲            │              │
   └─ stop verdict: walk away          └── circuit breaker (3 identical failures) ──┘
```

| Stage | What happens | Tool |
|---|---|---|
| **Recon** | Fingerprint the install: engine and version, scripting backend (Mono / IL2CPP + metadata version, Unreal IoStore), executables wrapped by DRM, online services, anti-cheat. Verdict: **go**, **caution**, or **stop**. | `ret recon` |
| **Snapshot** | Hash every file before anything is installed; `ret diff` later proves exactly what changed. Install scripts record what they add, uninstall scripts remove exactly that. | `ret snapshot`, `ret diff`, `deploy.sh` / `undeploy.sh` |
| **Extract** | Read the game's own data, read-only: IL2CPP metadata (every type, field, method), Unity bundles and animation, Unreal containers. | `src/utils/*`, `experiments/` |
| **Observe** | Before writing behaviour, log how the game does it itself: a read-only patch that only traces (which method runs, with what context, in what order). | the plugin's `*Trace` classes |
| **Imitate** | Write the new behaviour in the same shape the trace showed, with the game's own building blocks (its actions, its piece factory, its prompts). | the mods |
| **Verify** | A human plays a test. The logs are read before the next change. | `LogOutput.log`, `Player.log`, `UE4SS.log` |
| **Record** | The finished mod is played in full on video, with a timeline of every mod event and a manifest (version, sha256, counts, errors). | `record_match.sh` |

## The rules

**1. Respect the verdict.** `ret recon` says **stop** when anti-cheat is present (EasyAntiCheat, ACE, and others it
recognises), and then nothing is attached to that game. DRM is never stripped or bypassed: in *Sherlock Holmes* the
Steam wrapper on the executable stays untouched; UE4SS works through Unreal's own reflection. Games with online
services are played offline while modded.

**2. Everything reversible.** Snapshot first. Install scripts refuse to overwrite anything, check pinned sha256 sums of
the loader archives, and write a list of every file they add; uninstall removes exactly that list. The person who owns
the machine runs anything that copies binaries into a game folder.

**3. Read, don't guess.** Before patching a method, read its exact signature from the generated assemblies
(`interop-inspect`) or the metadata dump. In *Root*, one wrong patch target disabled a whole patch class; patching
each method separately means a bad target only disables itself.

**4. Trace, then imitate.** Four test runs went into guessing how the game's `Move` action finds the piece to move; one
read-only trace of the game's own moves answered it (the piece is the context's target, set in place). From then on,
every new mechanic started with a trace of the closest thing the game already does.

**5. One hypothesis per test run.** If a build changes two things and fails, the log cannot say which one failed.

**6. Circuit breaker.** After three failures of the same kind, stop changing the guess and go back to reading or
tracing. Two examples from *Root*:
- The Evening phase looped for five builds while symptoms were patched. One trace showed that the game asks **every**
  faction for its Evening twice per turn (the AI factions too), so any add-on to the Evening ran twice. The fix was to
  stop wrapping the phase and extend the game's own Daylight loop instead.
- Recruit failed three ways (clicked clearing never arrived, a suit prompt that never appeared, an automatic choice the
  player rejected). The game log showed the screen confirms that button instantly with an **empty** selection, so no
  click could ever arrive. The fix was to move Recruit to a button that does carry clicks.

**7. Use the game's own factories.** Pieces made with a bare constructor looked identical in every readable property
but crashed the game's move code; pieces made with the game's own `createPiece` work. Prompts, moves, scoring and
discards all use the game's own action classes, so the game can save and replay them.

**8. Evidence, not claims.** Every test run gets a dated log entry: what changed, why, what was tested (and how), and
**what was assumed without asking**. The finished mods were recorded with `record_match.sh`, which stamps every mod
log line with its time in the video and writes a manifest with the plugin's version and sha256, event counts and
error counts.

**9. The rules belong to the player.** Where the source rules were unclear (what does "connected by web" mean? should a
bird card count as every suit?), the question went to the person the mod is for, and the answer was written down
before it was built. See [the balance section](root-spinners-of-mercy.md#balance-what-we-changed-and-why).

## Where the method came from

The first plan was reviewed against prior work on universal modding and automated reverse engineering. The lesson
taken from it: a new faction or a ported effect must be accepted by **every** system of the host game (turn order,
prompts, saving, scoring, the screen), so each untested assumption is a future crash. Observing before writing is
cheaper than debugging after.
