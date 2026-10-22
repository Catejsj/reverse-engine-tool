# Bug journal: what broke, why, and the rule it left behind

Every entry below came from a real test run of the *Root* plugin or the *Sherlock* mod. The full private log has over
fifty; these are the ones that changed how RET works. Format: **symptom → cause → fix → rule.**

### 1. The loader could not read the game
**Symptom.** BepInEx 6.0.0-pre.2 loaded, then failed to generate any interop: "Unsupported metadata version, we
support 23–29, got 31". **Cause.** The stable pre-release predates IL2CPP metadata v31 (Unity 2022.3).
**Fix.** BepInEx bleeding-edge build 788, checksum-pinned in `deploy.sh`.
**Rule.** `ret recon` reads the metadata version first; pick the loader from it, not from its version number.

### 2. One wrong signature disabled a whole patch class
**Symptom.** The new faction never appeared in the lobby. **Cause.** A patch targeted `SavePlayerPreferences()`; the
real overloads take arguments, and Harmony rejected the *entire* class, so the working patches never applied either.
**Fix.** Exact signature, and every method patched on its own. **Rule.** Read the signature (`interop-inspect`) before
writing a patch; one patch per method, so a bad target only disables itself.

### 3. Every value written arrived as zero
**Symptom.** Pieces read back with the wrong type and faction. **Cause.** Il2CppInterop does not carry a
`Nullable<enum>` into IL2CPP's generic `SetAttribute<Nullable<T>>`; the record is created, the value is lost.
**Fix.** Let the game create the record, then write `{hasValue, value}` into its backing fields directly.
**Rule.** For generic value types across the interop boundary, verify by reading back, never assume the write landed.

### 4. Patching a tiny shared struct broke every faction's models
**Symptom.** Selection models vanished for *all* factions; a later diagnostic hard-crashed the game. **Cause.**
Prefixes on `PrefabFlavorContext`, a small value type used by every prefab lookup. **Fix.** Removed; the plugin fills
the tables the game reads instead. **Rule.** Never patch or call into hot shared value-type machinery.

### 5. Calling a "Next…" method to inspect it crashed the game
**Symptom.** Native crash during setup, no exception. **Cause.** To learn the setup order, the plugin called
`NextSetupPlayerTurnNumForFaction` itself; it advances a counter as a side effect. **Fix.** Never call it; a prefix
answers Spinners' slot (140, letter N from the fan board), a postfix only logs what the game got.
**Rule.** Observe side-effecting methods with postfixes; never call them to look.

### 6. Moves and draws crashed: "Invalid target"
**Symptom.** Setup moves, then Gather's card draw, ended the match. **Cause.** In Root's action stack a child runs with
its parent's context; the parent's target was the clicked clearing, and `Move`/`Draw` read the target as their piece /
player. A trace of the game's *own* moves showed the shape: Source = playmat, Target = the piece. **Fix.** Set the
step context's target in place before each action. **Rule.** Trace how the game does it before guessing (this one
cost four runs; the trace took one).

### 7. "This action has no way to serialize itself"
**Symptom.** The match ended at the discard-to-5 prompt. **Cause.** Root serializes the whole action stack whenever it
waits for the player; a plugin-made anonymous action was still on it. **Fix.** Only the game's own serializable
actions stay on the stack across a prompt. **Rule.** Know what the engine saves, and when.

### 8. The Evening ran twice, every turn, for five builds
**Symptom.** The Evening choices repeated over and over. Five patches treated the second run as the bug. **Cause.** A
trace showed Root asks **every** faction (the AI ones too) for its Evening twice per turn, so a plugin wrapper around
the Evening ran twice. **Fix.** The Evening is the game's own again; Shelter moved to the end of the game's own Daylight
loop, where passing already works. **Rule.** Circuit breaker: three identical failures means stop patching and trace.
Don't wrap a phase; extend the game's own loop.

### 9. Comparing two handles to the same object said "different"
**Symptom.** Every Shelter click counted as "stop". **Cause.** `List.Contains` compared C# wrappers; the clicked
clearing arrives as a *new* wrapper around the same native object. **Fix.** Compare the native pointer.
**Rule.** Never compare interop wrappers with `==`, `Contains` or `Distinct`. Related: never key anything by a native
pointer unless something keeps the object alive (a collected object's address was reused, and Daylight was mistaken
for the Evening).

### 10. A button whose click never arrives
**Symptom.** Recruit looped: every click came back as "no clearing selected". Three fixes failed. **Cause.** The game's
own log showed the screen confirms that button **instantly with an empty selection** (the host faction's recruit has
nothing to choose), so no click ever existed. A suit prompt the plugin queued was never shown either.
**Fix.** Swap buttons to fit what the client sends: Recruit moved to a button that carries clicks; Gather (no choice
needed) took the empty one. **Rule.** Before re-wiring a button, read what the client actually sends for it.

### 11. Skipping a phase action looped the turn forever
**Symptom.** "Possible infinite loop detected… Ending game." **Cause.** To stop the host's Birdsong from placing wood
(which used up Spinners' webs), the plugin emptied that action, but the same action also advances the turn to
Daylight. **Fix.** The action runs; only its wood moves (and its wood prompt) are filtered. **Rule.** Never empty a phase
action; filter what it does.

### 12. Pieces made by hand crashed the game's own Move
**Symptom.** The ninth web placed ended the match (a null reference inside `Move`). **Cause.** The 16 extra webs were
built with the bare entity constructor. Root's pieces come from its own factory, `createPiece`. Read-only comparison
found the factory-made webs identical to the game's wood in every readable property except their slot in the supply.
**Fix.** `createPiece`, plus a load-time comparison against the game's own wood that blocks the made pieces if anything
else differs. **Rule.** Make things with the game's own factory, and check them against the real thing before use.

### 13. (Sherlock) Things created from Lua vanished after a minute
**Cause.** Unreal's garbage collector frees objects no engine object references. **Fix.** Keep the overlay in the
viewport for good; hold every texture with its own hidden image. **Rule.** In a garbage-collected host, anything you
create must be owned by the host.

### 14. (Sherlock) The 3D panel spawned, but was never drawn
**Cause.** Under this UE4SS version, nothing added to the world at runtime was rendered. **Fix.** Back to the screen
overlay, but sized and occluded from world measurements, which gave the "in the world" look that was wanted.
**Rule.** When a route is blocked by the platform, keep the goal and change the route.
