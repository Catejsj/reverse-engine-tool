// RET: Spinners of Mercy for Root (decision R12).
// 0.0.1 (slice 0): read the faction registry at load.  0.1.0 (slice 1a, read-only): watch the lobby; it offered
// AvailableFactionsData.Available (a VersionedList of Factions) and looked icons up in FactionSpriteData.TryGetSprite.
// 0.2.0 (slice 1b, first change): faction 29 = Spinners of Mercy is added to the lobby's Available list and given the
// nurse-spider icon (relationship.png from the fan faction's TTS files). It can NOT be picked yet: any attempt is
// refused and logged, because there is no archetype to build the player from. Only faction 29 is added; DLC-locked
// factions stay locked.
// 0.3.0 (slice 1c): 0.2.0's run showed the data list is not what draws the buttons (Sherlock: "if not modding the
// selection i cant see it"). The setup panel PlayerConfigurationPromptBehaviour owns a fixed List<FactionToggle>.
// When it initializes, one owned faction's button (Marquise) is cloned beside it, set to faction 29, given the spider
// icon, and appended to that list; every part of the button is logged so the next version can replace names properly.
// 0.4.0 (slice 1d): Sherlock's screenshot shows the Solo Play screen, which never opens that panel: each slot cycles
// factions with arrows (ConfigureGamePromptBehaviour<T>.changeFaction) over AvailableFactionsForPlayerType's RESULT, which
// filters out 29. So 29 is appended to the result for Human slots only (AI is never given it). Guards: Create Game and
// saving player preferences are refused while a slot is on 29, so nothing tries to build it and an unmodded Root never
// reads 29 back from its prefs.
// 0.5.0 (slice 1e): 0.4.1 showed "factionnames.29" on the slot and Root held Create Game for "Missing reach for faction:
// 29"; the arrow-click tracking never saw Spinners, so the guard could not work. Now: the name comes from Root's own text
// lookup (LocalizationLookup), reach 4 is added to FactionUtils.reachForFaction, and the guard reads each slot's
// FactionData at ConfigureOfflineGamePrompt.Resolve, the hand-off every Create Game passes through.
// 0.6.0 (slice 2a, read-only addition): 0.5.0's Create Game failed inside TuberMatch.createPlayers ("We weren't able to
// create a player instance"), which asks each faction's archetype (FactionArchetypes.*Archetype) to
// CreateTuberPlayerInstance(match, initData). Every such call is now traced: the init data, then the player's entity
// tree (names, archetype classes, counts of repeated pieces), so the Spinners player can follow Root's own recipe.
// Also lists a setup slot's component types, because reading FactionData from it found nothing in 0.5.0.
// 0.7.0 (slice 2b): the trace showed a faction IS its player class (MarquiseDeCatPlayer : TuberPlayerEntity overrides
// initialize / GetSetupActions / Get{Birdsong,Daylight,Evening}Actions ...), and every new player starts as the same
// 10 empty areas. So Spinners is hosted on the Marquise class: ArchIDForFaction(29) answers with the Marquise archetype,
// the player gets Faction 29, and from here each Marquise behaviour is replaced (only when Faction == 29) by Spinners
// rules. In 0.7.0 nothing is replaced yet: faction 29 plays as the Marquise. The Create Game refusal is off so the match
// can start; the prefs guard stays so an unmodded Root never loads 29.
// 0.8.0 (slice 2c, read-only addition): 0.7.0 started a match with Faction 29 on the Marquise class, no errors. Before
// Spinners can get its own pieces (20 warriors, 24 webs, 9 suited infirmaries) we need Root's recipe: the full archetype
// catalogue is written to ~/ret-work/root/spinners/archetypes.txt, and every CreateEntity/CreateEntities call made inside
// a faction's initialize() is logged (archetype, container, count).
// 0.9.0 (slice 2d, read-only addition): 0.8.0 showed pieces are not archetypes: initialize() makes plain entities by name
// (Alliance: Warrior x10, Sympathy x10, Base x3; Marquise: Warrior x25, Keep, Wood x8, Sawmill/Workshop/Recruiter x6),
// and an Entity is an ObjectAttributes whose attributes are keyed by int. The key -> name map is read from
// tuber.canis.TuberAttributes, and the full attribute list of the first piece of each kind is logged.
// 0.10.0 (slice 2e): 0.9.0 showed a piece is a TuberEntity named after its kind with attributes PieceType, TokenType or
// BuildingType, Suit (Alliance Base), Faction. Spinners' own pieces are created after the host initialize(): an
// "Infirmaries" area with 9 Building entities (BuildingType 13 = Infirmary, 3 per suit) and 24 Token entities
// (TokenType 11 = Web) in TokensArea, Faction 29. Off the board for now; the Marquise host pieces stay because the
// Marquise turn code still uses them until Spinners' own turn replaces it.
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using lib.data;
using tuber.client.data;
using tuber.client.match.data;
using tuber.client.menus.behaviours;
using tuber.client.menus.prompts;
using tuber_canis.data;

namespace Ret.SpinnersOfMercy;

[BepInPlugin("ret.spinnersofmercy", "RET Spinners of Mercy", "0.32.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource L;
    internal const Factions Spinners = (Factions)29;   // first number Root does not use (enum ends at 28)
    internal static UnityEngine.Sprite Icon;

    // install_plugin.sh writes the Wine path of ~/ret-work/root/spinners next to the DLL; the art never enters the game folder.
    internal static string ArtDir()
    {
        var f = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "RetSpinners.path");
        return System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f).Trim() : null;
    }

    // Apply one [HarmonyPatch]-annotated method on its own, so a bad target only disables that method.
    static void PatchOne(Harmony h, System.Reflection.MethodInfo m)
    {
        var info = HarmonyMethodExtensions.GetMergedFromType(typeof(SoloPlayArrows))
                   .Merge(HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromMethod(m)));
        var target = info.declaringType.GetMethod(info.methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null,
            info.argumentTypes ?? Type.EmptyTypes, null);
        if (target == null && info.argumentTypes == null)
            target = AccessTools.Method(info.declaringType, info.methodName);
        if (target == null) throw new Exception("no method " + info.declaringType.Name + "." + info.methodName);
        var isPrefix = m.GetCustomAttributes(typeof(HarmonyPrefix), false).Length > 0;
        h.Patch(target, prefix: isPrefix ? new HarmonyMethod(m) : null, postfix: isPrefix ? null : new HarmonyMethod(m));
    }

    internal static UnityEngine.Sprite LoadIcon()
    {
        if (Icon != null) return Icon;
        var dir = ArtDir();
        var png = dir == null ? null : System.IO.Path.Combine(dir, "raw", "relationship.png");
        if (png == null || !System.IO.File.Exists(png)) { L.LogWarning("Spinners icon not found: " + png); return null; }
        var tex = new UnityEngine.Texture2D(2, 2);
        UnityEngine.ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(png));
        tex.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;   // Unity must not unload it between scenes
        Icon = UnityEngine.Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new UnityEngine.Vector2(0.5f, 0.5f));
        Icon.name = "RetSpinnersOfMercyIcon";
        Icon.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
        L.LogInfo("Spinners icon loaded " + tex.width + "x" + tex.height);
        return Icon;
    }

    public override void Load()
    {
        L = Log;
        L.LogInfo("0.32.0 Skitter on the web track (web to web, open paths, ignoring rule); 0.31.1 no Marquise wood prompt in Birdsong; 0.31.0 Evening draw = card icons showing (fan board); 0.30.5 made webs used (diff 0.30.4: only the supply slot differs); 0.30.4 made webs compared to Root wood, used only if identical; 0.30.3 webs made with Root's createPiece; made webs used only if registered with an archetype; 0.30.2 safe mode: Recruit places only Root's own 8 webs (made webs suspected in the turn-2 Move crash) + move/registry traces; 0.30.1 Birdsong runs again, only its wood moves dropped (0.29.3 skip looped the turn); 0.30.0 webs rule (removed web = 0 VP); 24 webs (16 more wood made like Root's own); 0.29.3 no Marquise Birdsong wood for Spinners; Recruit greys without a web; 0.29.2 Recruit: neighbour web must be joined by an open path; 0.29.1 Recruit only on or next to your webs (train track), warrior+web in pairs; 0.29.0 buttons swapped: Overwork = Recruit (click a clearing, its suit), Recruit = Gather; 0.28.1 Recruit picked the suit automatically, never discards for nothing; 0.28.0 Recruit asked a suit with Root's ChooseSuitAction; 0.27.1 Recruit reads the click via Root's GetChosenClearings; 0.27.0 Spinners Recruit on the Recruit button; 0.26.0 Skitter step 1: March repeats its move step while Spinners keeps moving; 0.25.1 Shelter click matched by game object (0.25.0 counted every click as stop); 0.25.0 Shelter at the end of Root's real Daylight loop, Evening left to Root (it is asked twice per turn) + EveningPhase traces; 0.24.0 Shelter: bare choice phase + stop clearings; keep only during setup (no Field Hospitals); removal traces; Shelter runs in Root's Daylight loop (pass works there) + return traces, +0 not offered; action count = shelters possible, once per turn; serializable Evening chain, own action count; GainVP trace; 0.21.1 Establish works; Establish on the Build button (slice C2); 0.20.2 Gather once per turn (cost unpayable after use) + cost-tree trace; 0.20.1 Draw target = the player; Gather on the Overwork button (slice C1); 0.19.1 ability observe traces; 0.19.0 Spinners HUD art (portrait, pattern, warrior, web, infirmaries) + texts; board hides after the match; setup slot N=140 (no plugin calls), Spinners board viewer (F9/F10/tab) + HUD map (F11); 0.17.3 setup slot + traces (Spinners last, faithful to the board): Spinners pieces (on Marquise HUD pieces) + Spinners setup; mask kept: Spinners plays as Faction MarquiseDeCat, recorded by the plugin; Spinners <-> Marquise exclusive");
        LogRegistry();
        var h = new Harmony("ret.spinnersofmercy");
        h.PatchAll(typeof(LobbyWatch));
        h.PatchAll(typeof(SpinnersInLobby));
        h.PatchAll(typeof(SpinnersButton));
        PlayerTrace.PatchAll(h);
        h.PatchAll(typeof(SpinnersHost));
        h.PatchAll(typeof(Census));
        SpinnersSetup.PatchAll(h);
        TurnTrace.PatchAll(h);
        AbilityTrace.PatchAll(h);
        Gather.PatchAll(h);
        Establish.PatchAll(h);
        SpinnersRecruit.PatchAll(h);
        SpinnersBirdsong.PatchAll(h);
        SpinnersDraw.PatchAll(h);
        Skitter.PatchAll(h);
        Shelter.PatchAll(h);
        try { AddComponent<BoardViewer>(); L.LogInfo("board viewer component added"); }
        catch (Exception e) { L.LogError("board viewer failed (the rest is unaffected): " + e); }
        // BorrowVisuals (0.12.0) is NOT applied: patching the PrefabFlavorContext struct broke flavor matching for every
        // faction (MethodAccessException on ICollection<string>.Contains in Player.log; selection models vanished).
        PieceTrace.PatchAll(h);
        // One patch method at a time: 0.4.0 lost every Solo Play patch to one bad target (SavePlayerPreferences()).
        foreach (var m in typeof(SoloPlayArrows).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))
        {
            if (m.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { PatchOne(h, m); L.LogInfo("patched: " + m.Name); }
            catch (Exception e) { L.LogError("patch " + m.Name + " failed (that part inactive): " + e.Message); }
        }
        L.LogInfo("patched: lobby watch + Spinners in lobby");
    }

    static void LogRegistry()
    {
        foreach (Factions f in Enum.GetValues(typeof(Factions)))
            L.LogInfo("  enum " + ((int)f).ToString().PadLeft(3) + " " + f);
        var released = "";
        foreach (var f in FactionUtils.ReleasedPlayerFactions) released += f + " ";
        L.LogInfo("  ReleasedPlayerFactions: " + released);
        // 0.0.1 stopped with a NullReferenceException after the 11th entry: each entry is now read on its own.
        foreach (var kv in FactionUtils.MasterFactionMap)
        {
            try { L.LogInfo("  MasterFactionMap " + kv.Key + " -> " + (kv.Value == null ? "null" : kv.Value.ToString())); }
            catch (Exception e) { L.LogWarning("  MasterFactionMap entry failed: " + e.GetType().Name); }
        }
    }

    // 0.0.1 read reach = 0 for every faction at load: read the dictionary itself, later, when the lobby is used.
    internal static void LogReach()
    {
        var s = "";
        try { foreach (var kv in FactionUtils.reachForFaction) s += kv.Key + "=" + kv.Value + " "; }
        catch (Exception e) { s = "failed: " + e.GetType().Name; }
        L.LogInfo("  reachForFaction (in lobby): " + s);
    }
}

[HarmonyPatch]
static class LobbyWatch
{
    static readonly HashSet<string> once = new();
    static bool First(string key) => once.Add(key);

    [HarmonyPostfix, HarmonyPatch(typeof(AvailableFactionsData), nameof(AvailableFactionsData.AvailableFactionsForPlayerType))]
    static void Available(AvailableFactionsData __instance)
    {
        if (First("reach")) Plugin.LogReach();
        var s = __instance.ToString();
        if (First("avail:" + s)) Plugin.L.LogInfo("lobby offers: " + s);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(FactionSpriteData), nameof(FactionSpriteData.TryGetSprite), new[] { typeof(Factions), typeof(UnityEngine.Sprite) },
        new[] { ArgumentType.Normal, ArgumentType.Ref })]
    static void Sprite(FactionSpriteData __instance, Factions __0, UnityEngine.Sprite __1, bool __result)
    {
        if (First("table:" + __instance.name)) DumpTables(__instance);
        if (First("sprite:" + __0)) Plugin.L.LogInfo("icon lookup " + __0 + " -> " + (__result && __1 != null ? __1.name : "none"));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(LobbyGameFactionsRenderer), "dirtyUpdate")]
    static void Renderer(LobbyGameFactionsRenderer __instance)
    {
        var s = "show=" + __instance.show + " clockwork=" + __instance.includeClockwork + " images=" + (__instance.images == null ? -1 : __instance.images.Count);
        if (First("render:" + __instance.name + s)) Plugin.L.LogInfo("lobby renderer " + __instance.name + ": " + s);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(FactionSelectPrompt), "resolve")]
    static void Picked(Factions __0) => Plugin.L.LogInfo("faction picked: " + __0);

    static void DumpTables(FactionSpriteData sprites)
    {
        var s = "";
        foreach (var e in sprites.factionSprites) s += e.faction + "=" + (e.sprite == null ? "null" : e.sprite.name) + " ";
        Plugin.L.LogInfo("icon table " + sprites.name + ": " + s);
        foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppType.Of<FactionLocKeyData>()))
        {
            var names = o.Cast<FactionLocKeyData>();
            var n = "";
            foreach (var e in names.factionLocKeys) n += e.faction + "=" + e.locKey + " ";
            Plugin.L.LogInfo("name table " + names.name + ": " + n);
        }
    }
}

// Slice 1b: put faction 29 in the lobby, with an icon, and keep it unpickable until it has an archetype.
[HarmonyPatch]
static class SpinnersInLobby
{
    static void AddTo(AvailableFactionsData d, string where)
    {
        if (d == null || d.Available == null || d.Available.Contains(Plugin.Spinners)) return;
        d.Available.Add(Plugin.Spinners);
        Plugin.L.LogInfo("Spinners of Mercy added to the lobby list (" + where + ")");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(AvailableFactionsData), nameof(AvailableFactionsData.ResetAvailableFactions))]
    static void Reset(AvailableFactionsData __instance) => AddTo(__instance, "reset");

    [HarmonyPrefix, HarmonyPatch(typeof(AvailableFactionsData), nameof(AvailableFactionsData.AvailableFactionsForPlayerType))]
    static void BeforeOffer(AvailableFactionsData __instance) => AddTo(__instance, "offer");

    [HarmonyPrefix, HarmonyPatch(typeof(FactionSpriteData), nameof(FactionSpriteData.TryGetSprite), new[] { typeof(Factions), typeof(UnityEngine.Sprite) },
        new[] { ArgumentType.Normal, ArgumentType.Ref })]
    static bool Icon(Factions __0, ref UnityEngine.Sprite __1, ref bool __result)
    {
        if (__0 != Plugin.Spinners) return true;
        __1 = Plugin.LoadIcon();
        __result = __1 != null;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(FactionToggle), nameof(FactionToggle.Event_Toggle))]
    static bool Toggle(FactionToggle __instance, bool __0)
    {
        if (__instance.faction != Plugin.Spinners) return true;
        Plugin.L.LogInfo("Spinners of Mercy toggle clicked (on=" + __0 + "): refused, not playable yet");
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(FactionSelectPrompt), "resolve")]
    static bool Resolve(Factions __0)
    {
        if (__0 != Plugin.Spinners) return true;
        Plugin.L.LogInfo("Spinners of Mercy chosen: refused, not playable yet");
        return false;
    }
}

// Slice 1c: a real button in the per-player setup panel.
[HarmonyPatch]
static class SpinnersButton
{
    static void Describe(UnityEngine.GameObject go, string indent, int depth)
    {
        var parts = "";
        foreach (var c in go.GetComponents<UnityEngine.Component>()) parts += c.GetIl2CppType().Name + " ";
        var img = go.GetComponent<UnityEngine.UI.Image>();
        if (img != null) parts += "[sprite=" + (img.sprite == null ? "null" : img.sprite.name) + "]";
        Plugin.L.LogInfo(indent + go.name + (go.activeSelf ? "" : " (inactive)") + ": " + parts);
        if (depth >= 4) return;
        for (int i = 0; i < go.transform.childCount; i++) Describe(go.transform.GetChild(i).gameObject, indent + "  ", depth + 1);
    }

    // The panel's list holds records (faction, UI Toggle, Animator), not components: clone the Marquise record's
    // button object and point a new record at the clone.
    [HarmonyPostfix, HarmonyPatch(typeof(PlayerConfigurationPromptBehaviour), "initialize")]
    static void AfterInit(PlayerConfigurationPromptBehaviour __instance)
    {
        try
        {
            var list = __instance.factionToggles;
            var s = "";
            PlayerConfigurationPromptBehaviour.FactionToggle template = null;
            foreach (var rec in list)
            {
                s += rec.faction + (rec.toggle != null && rec.toggle.gameObject.activeSelf ? "" : "(off)") + " ";
                if (rec.faction == Plugin.Spinners) return;                    // already added to this panel
                if (rec.faction == Factions.MarquiseDeCat) template = rec;
            }
            Plugin.L.LogInfo("setup panel " + __instance.name + " toggles: " + s);
            if (template == null || template.toggle == null) { Plugin.L.LogWarning("no Marquise button to copy"); return; }
            var src = template.toggle.gameObject;
            Plugin.L.LogInfo("template button under " + src.transform.parent.name + ":");
            Describe(src, "  ", 0);

            var copy = UnityEngine.Object.Instantiate(src, src.transform.parent).Cast<UnityEngine.GameObject>();
            copy.name = "FactionToggle_SpinnersOfMercy";
            var ui = copy.GetComponent<UnityEngine.UI.Toggle>();
            // The copy carries the Marquise button's click wiring: replace the whole event so a click can never
            // select Marquise, and only log.
            ui.onValueChanged = new UnityEngine.UI.Toggle.ToggleEvent();
            ui.onValueChanged.AddListener((UnityEngine.Events.UnityAction<bool>)(on =>
                Plugin.L.LogInfo("Spinners button clicked (on=" + on + "): refused, not playable yet")));
            ui.isOn = false;
            var comp = copy.GetComponent<FactionToggle>();
            if (comp != null) comp.faction = Plugin.Spinners;
            var icon = Plugin.LoadIcon();
            var swapped = 0;
            foreach (var im in copy.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                var n = im.sprite == null ? "" : im.sprite.name.ToLowerInvariant();
                if (icon != null && (n.Contains("cat") || n.Contains("marquise"))) { im.sprite = icon; swapped++; }
            }
            var rec2 = new PlayerConfigurationPromptBehaviour.FactionToggle();
            rec2.faction = Plugin.Spinners;
            rec2.toggle = ui;
            rec2.animator = copy.GetComponent<UnityEngine.Animator>();
            list.Add(rec2);
            Plugin.L.LogInfo("Spinners button added (" + swapped + " Marquise images swapped for the spider)");
        }
        catch (Exception e) { Plugin.L.LogError("adding the Spinners button failed: " + e); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerConfigurationPromptBehaviour), nameof(PlayerConfigurationPromptBehaviour.Event_SelectFaction))]
    static bool Select(Factions __0)
    {
        if (__0 != Plugin.Spinners) return true;
        Plugin.L.LogInfo("Spinners of Mercy selected in the setup panel: refused, not playable yet");
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(PlayerConfigurationPromptBehaviour), nameof(PlayerConfigurationPromptBehaviour.UpdateActiveToggles))]
    static void AfterUpdate(PlayerConfigurationPromptBehaviour __instance)
    {
        foreach (var rec in __instance.factionToggles)
            if (rec.faction == Plugin.Spinners && rec.toggle != null)
                Plugin.L.LogInfo("after UpdateActiveToggles: Spinners button active=" + rec.toggle.gameObject.activeSelf
                                 + " interactable=" + rec.toggle.interactable);
    }
}

// Slice 1d/1e: the Solo Play arrows, the name, the reach, and the guard that keeps an unbuilt faction out of a match.
[HarmonyPatch]
static class SoloPlayArrows
{
    internal const int Reach = 4;   // Sherlock 2026-10-15: between Alliance (3) and Vagabond (5); fan board gives none
    static readonly Dictionary<string, string> texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["factionnames.29"] = "Spinners of Mercy",   // 0.4.1 screenshot showed this raw key on the slot
        [Gather.PromptKey] = "Gather: draw a card (once per turn).",
        [Shelter.PromptKey] = "Shelter: click an infirmary clearing to discard a matching card (or a bird) and score its track (+1/+2). Press pass to stop.",
        [SpinnersRecruit.PromptKey] = "Recruit (once per turn): click a clearing on or next to your webs. You discard a card of its suit (or a bird) and place a warrior and a web in every clearing of that suit that is on or next to your webs.",
        [Establish.PromptKey] = "Establish: choose a clearing with your web. You discard a card of its suit (or a bird) and place an infirmary there.",
    };

    static readonly HashSet<string> seen = new();
    static bool LogOnce(string k) => seen.Add(k);

    [HarmonyPostfix, HarmonyPatch(typeof(AvailableFactionsData), nameof(AvailableFactionsData.AvailableFactionsForPlayerType))]
    static void AddForHumans(tuber.client.data.PlayerTypeData.PlayerType __0, ref Il2CppSystem.Collections.Generic.IEnumerable<Factions> __result)
    {
        var list = new Il2CppSystem.Collections.Generic.List<Factions>(__result);
        var s = "";
        foreach (var f in list) s += f + " ";
        var key = "offer:" + __0 + ":" + s;
        if (__0 == tuber.client.data.PlayerTypeData.PlayerType.Human && !list.Contains(Plugin.Spinners)) list.Add(Plugin.Spinners);
        if (LogOnce(key)) Plugin.L.LogInfo("factions for " + __0 + ": " + s + (__0 == tuber.client.data.PlayerTypeData.PlayerType.Human ? "+ SpinnersOfMercy" : ""));
        __result = new Il2CppSystem.Collections.Generic.IEnumerable<Factions>(list.Pointer);
        // 0.4.1: Root logged "Missing reach for faction: 29" and its validation held Create Game. The reach table is
        // filled by the time the lobby asks for factions (0.1.0 log), so add Spinners here, once.
        var reach = FactionUtils.reachForFaction;
        if (reach != null && !reach.ContainsKey(Plugin.Spinners))
        {
            reach[Plugin.Spinners] = Reach;
            Plugin.L.LogInfo("reach for Spinners of Mercy set to " + Reach);
        }
        // R14: Spinners plays on the Marquise class and shows as Marquise, so a real Marquise must never share the game.
        var excl = FactionUtils.mutuallyExclusiveFactions;
        if (excl != null && !excl.ContainsKey(Plugin.Spinners))
        {
            excl[Plugin.Spinners] = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Factions>(new[] { SpinnersHost.Host });
            var old = excl.ContainsKey(SpinnersHost.Host) ? excl[SpinnersHost.Host] : null;
            var both = new List<Factions>();
            if (old != null) foreach (var f in old) both.Add(f);
            both.Add(Plugin.Spinners);
            excl[SpinnersHost.Host] = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Factions>(both.ToArray());
            var check = "";
            foreach (var f in new Il2CppSystem.Collections.Generic.List<Factions>(FactionUtils.MutuallyExclusiveFactions(Plugin.Spinners))) check += f + " ";
            Plugin.L.LogInfo("Spinners <-> " + SpinnersHost.Host + " mutually exclusive; Root reads for Spinners: " + check);
        }
    }

    // Root's text lookup: answer our keys before the real table (which has no entry and would echo the key).
    [HarmonyPrefix, HarmonyPatch(typeof(Canis.utils.localization.LocalizationLookup), nameof(Canis.utils.localization.LocalizationLookup.TryLocalize),
        new[] { typeof(string), typeof(string), typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>) },
        new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    static bool TryText(string __0, ref string __1, ref bool __result)
    {
        if (__0 == null || !texts.TryGetValue(__0.Trim(), out var v)) return true;
        __1 = v;
        __result = true;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Canis.utils.localization.LocalizationLookup), nameof(Canis.utils.localization.LocalizationLookup.Localize),
        new[] { typeof(string), typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>) })]
    static bool Text(string __0, ref string __result)
    {
        if (__0 == null || !texts.TryGetValue(__0.Trim(), out var v)) return true;
        __result = v;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Canis.utils.localization.LocalizationLookup), nameof(Canis.utils.localization.LocalizationLookup.Has))]
    static bool HasText(string __0, ref bool __result)
    {
        if (__0 == null || !texts.ContainsKey(__0.Trim())) return true;
        __result = true;
        return false;
    }

    static Factions? FactionOf(dwd.core.data.composition.DataComposition slot)
    {
        if (slot == null) return null;
        foreach (var c in slot.components)
        {
            var fd = c.TryCast<FactionData>();
            if (fd == null) continue;
            var text = fd.ToString();
            if (!factionDataLogged) { factionDataLogged = true; Plugin.L.LogInfo("guard: FactionData reads HasValue=" + fd.Faction.HasValue + " text='" + text + "'"); }
            // 0.15.1 run: HasValue=True, text 'FactionData: 29', yet the guard never saw Spinners: fd.Faction.Value comes
            // through interop wrong (Nullable<enum>, as in 0.10.2). The text is right, so it is read first.
            if (text != null && (text.Contains("SpinnersOfMercy") || text.EndsWith(" 29") || text.Contains("(29)") || text.Contains(": 29"))) return Plugin.Spinners;
            var colon = text?.LastIndexOf(':') ?? -1;
            if (colon >= 0 && Enum.TryParse<Factions>(text.Substring(colon + 1).Trim(), out var named)) return named;
            if (fd.Faction.HasValue) return fd.Faction.Value;
        }
        return null;
    }

    static bool slotDumped;
    static bool factionDataLogged;

    static bool HasSpinners(ConfigureOfflineGamePrompt prompt)
    {
        var slots = prompt?.PlayerSlots;
        if (slots == null) { Plugin.L.LogInfo("guard: prompt has no PlayerSlots"); return false; }
        if (!slotDumped && slots.Count > 0)
        {
            slotDumped = true;
            var s = "";
            foreach (var c in slots[0].components) s += c.GetIl2CppType().FullName + " ";
            Plugin.L.LogInfo("guard: slot 0 components: " + s + (slots[0].sourceComposition == null ? "" : "(has sourceComposition)"));
        }
        for (int i = 0; i < slots.Count; i++)
            if (FactionOf(slots[i]) == Plugin.Spinners) return true;
        return false;
    }

    // The guard reads the slots' own data where every Create Game path ends: the prompt hands its result over in Resolve.
    [HarmonyPrefix, HarmonyPatch(typeof(ConfigureOfflineGamePrompt), nameof(ConfigureOfflineGamePrompt.Resolve))]
    static bool Resolve(ConfigureOfflineGamePrompt __instance, bool __0)
    {
        Plugin.L.LogInfo("guard: Resolve(" + __0 + ") reached");
        if (!__0 || !HasSpinners(__instance)) return true;
        // 0.15.0 run: Spinners + an AI Marquise both started; two Marquise players crashed the client at the keep
        // (FixedSlottedLayout index out of range, duplicate Marquise buttons). mutuallyExclusiveFactions did not stop
        // the lobby, so Create Game refuses the pair here.
        var slots = __instance.PlayerSlots;
        var s = "";
        bool marquise = false;
        for (int i = 0; i < slots.Count; i++)
        {
            var f = FactionOf(slots[i]);
            s += i + "=" + (f.HasValue ? f.Value.ToString() : "random/none") + " ";
            if (f == SpinnersHost.Host) marquise = true;
        }
        Plugin.L.LogInfo("Create Game with Spinners of Mercy; slots: " + s);
        if (marquise)
        {
            Plugin.L.LogWarning("refused: Spinners plays on the Marquise class, so no other slot may be Marquise. Pick another faction.");
            return false;
        }
        return true;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ConfigureGamePromptBehaviour<ConfigureOfflineGamePrompt>), nameof(ConfigureGamePromptBehaviour<ConfigureOfflineGamePrompt>.SavePlayerPreferences), new[] { typeof(int) })]
    static bool SavePrefs(ConfigureGamePromptBehaviour<ConfigureOfflineGamePrompt> __instance, int __0)
    {
        var prompt = __instance.Prompt?.TryCast<ConfigureOfflineGamePrompt>();
        var slots = prompt?.PlayerSlots;
        if (slots == null || __0 < 0 || __0 >= slots.Count || FactionOf(slots[__0]) != Plugin.Spinners) return true;
        Plugin.L.LogInfo("player preferences for slot " + __0 + " not saved (Spinners selected)");
        return false;
    }
}

// Slice 2a: watch Root build every faction's player.
static class PlayerTrace
{
    internal static void PatchAll(Harmony h)
    {
        var post = new HarmonyMethod(typeof(PlayerTrace).GetMethod(nameof(Built), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
        var n = 0;
        foreach (var type in typeof(tuber.canis.archetypes.FactionArchetypes.MarquiseDeCatArchetype).Assembly.GetTypes())
        {
            if (type.Namespace != "tuber.canis.archetypes.FactionArchetypes") continue;
            var m = type.GetMethod("CreateTuberPlayerInstance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
            if (m == null || m.IsAbstract) continue;
            try { h.Patch(m, postfix: post); n++; }
            catch (Exception e) { Plugin.L.LogWarning("trace patch failed for " + type.Name + ": " + e.Message); }
        }
        Plugin.L.LogInfo("player trace on " + n + " faction archetypes");
    }

    static void Built(object __instance, tuber.canis.data.matchinitdata.TuberPlayerMatchInitData __1, Canis.entities.Entity __result)
    {
        try
        {
            var who = __instance.GetType().Name;
            Plugin.L.LogInfo("=== player built by " + who + " for faction " + (__1 == null ? "?" : __1.Faction.ToString()));
            if (__1 != null)
            {
                var w = ""; if (__1.StartingWarriors != null) foreach (var x in __1.StartingWarriors) w += x + " ";
                Plugin.L.LogInfo("  init: order=" + __1.PlayerStartingOrder + " vp=" + __1.StartingVP + " scoreToWin=" + __1.ScoreToWin
                    + " warriors=[" + w + "] buildings=" + (__1.StartingBuildings?.Count ?? 0) + " tokens=" + (__1.StartingTokens?.Count ?? 0)
                    + " hand=" + (__1.CardsInHand?.Count ?? 0) + " advanced=" + __1.AdvancedMode);
            }
            if (__result != null) Tree(__result, "  ", 0);
        }
        catch (Exception e) { Plugin.L.LogWarning("trace failed: " + e.Message); }
    }

    static string Arch(Canis.entities.Entity e)
    {
        try { var a = e.GetArchetype(); return a == null ? "-" : a.GetIl2CppType().Name; } catch { return "?"; }
    }

    // Repeated pieces (20 warriors...) are printed once with a count; recursion follows the first of each group.
    static void Tree(Canis.entities.Entity e, string indent, int depth)
    {
        var kids = e.Children;
        Plugin.L.LogInfo(indent + e.EntityName + " <" + Arch(e) + ">" + (kids == null || kids.Count == 0 ? "" : " children=" + kids.Count));
        if (kids == null || depth >= 6) return;
        var groups = new Dictionary<string, (Canis.entities.Entity first, int count)>();
        var order = new List<string>();
        foreach (var k in kids)
        {
            var key = k.EntityName + "|" + Arch(k);
            if (groups.TryGetValue(key, out var g)) groups[key] = (g.first, g.count + 1);
            else { groups[key] = (k, 1); order.Add(key); }
        }
        foreach (var key in order)
        {
            var (first, count) = groups[key];
            if (count > 1) Plugin.L.LogInfo(indent + "  [x" + count + "]");
            Tree(first, indent + "  ", depth + 1);
        }
    }
}

// Slice 2b: build faction 29 on the Marquise player class.
[HarmonyPatch]
static class SpinnersHost
{
    internal const Factions Host = Factions.MarquiseDeCat;

    [HarmonyPrefix, HarmonyPatch(typeof(FactionUtils), nameof(FactionUtils.ArchIDForFaction))]
    static bool ArchID(Factions __0, ref Canis.utils.ids.ArchetypeID __result)
    {
        if (__0 != Plugin.Spinners) return true;
        __result = FactionUtils.MasterFactionMap[Host];
        Plugin.L.LogInfo("ArchIDForFaction(Spinners) -> host " + Host + " archetype " + __result.ToString());
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(tuber.canis.archetypes.FactionArchetypes.MarquiseDeCatArchetype),
        nameof(tuber.canis.archetypes.FactionArchetypes.MarquiseDeCatArchetype.CreateTuberPlayerInstance))]
    static void Built(tuber.canis.data.matchinitdata.TuberPlayerMatchInitData __1, tuber.canis.entities.TuberPlayerEntity __result)
    {
        if (__1 == null || __1.Faction != Plugin.Spinners) return;
        Plugin.L.LogInfo("Spinners of Mercy player built on host class " + (__result == null ? "null" : __result.GetIl2CppType().Name)
                         + ", player.Faction=" + (__result == null ? "?" : __result.Faction.ToString()));
    }
}

// Slice 2c: the pieces each faction's initialize() creates, and Root's archetype catalogue.
static class PieceTrace
{
    static string current;            // faction class whose initialize() is running
    static bool catalogued;

    internal static void PatchAll(Harmony h)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var pre = new HarmonyMethod(typeof(PieceTrace).GetMethod(nameof(InitStart), flags));
        var post = new HarmonyMethod(typeof(PieceTrace).GetMethod(nameof(InitEnd), flags));
        var n = 0;
        foreach (var type in typeof(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer).Assembly.GetTypes())
        {
            if (type.Namespace != "tuber.canis.entities.PlayableClasses") continue;
            var m = type.GetMethod("initialize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            if (m == null) continue;
            try { h.Patch(m, prefix: pre, postfix: post); n++; } catch (Exception e) { Plugin.L.LogWarning("init trace failed for " + type.Name + ": " + e.Message); }
        }
        var create = new HarmonyMethod(typeof(PieceTrace).GetMethod(nameof(Create), flags));
        var c = 0;
        foreach (var m in typeof(tuber.canis.archetypes.TuberArchetypes).GetMethods())
        {
            if (m.Name != "CreateEntity" && m.Name != "CreateEntities") continue;
            try { h.Patch(m, prefix: create); c++; } catch (Exception e) { Plugin.L.LogWarning("create trace failed: " + e.Message); }
        }
        Plugin.L.LogInfo("piece trace on " + n + " initialize methods and " + c + " create methods");
    }

    static void InitStart(object __instance)
    {
        current = __instance.GetType().Name;
        Plugin.L.LogInfo("--- " + current + ".initialize() pieces:");
        if (!catalogued) { catalogued = true; Catalogue(); }
    }

    static void InitEnd(object __instance)
    {
        try
        {
            var e = (__instance as Il2CppSystem.Object)?.TryCast<Canis.entities.Entity>();
            var tp = e?.TryCast<tuber.canis.entities.TuberPlayerEntity>();
            if (tp != null) Plugin.L.LogInfo("--- " + current + " initialize done, Faction=" + tp.Faction);
            var cat = e?.TryCast<tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer>();
            if (cat != null) SpinnersPieces.AfterInit(cat);
            if (e != null) { Plugin.L.LogInfo("--- " + current + " after initialize: " + Summary(e)); AttributeDump.Pieces(e); }
        }
        catch (Exception x) { Plugin.L.LogWarning("summary failed: " + x.Message); }
        current = null;
    }

    static string Summary(Canis.entities.Entity player)
    {
        var s = "";
        foreach (var area in player.Children)
        {
            var counts = new Dictionary<string, int>();
            foreach (var k in area.Children) { var key = k.EntityName; counts[key] = counts.TryGetValue(key, out var v) ? v + 1 : 1; }
            if (counts.Count == 0) continue;
            s += area.EntityName + "{";
            foreach (var kv in counts) s += kv.Key + "x" + kv.Value + " ";
            s += "} ";
        }
        return s;
    }

    static void Create(object[] __args)
    {
        if (current == null) return;
        try
        {
            string what = "?", parent = "?", count = "1";
            var first = __args[0] as Il2CppSystem.Object;
            var arch = first?.TryCast<canis.archetypes.Archetype>();
            if (arch != null) what = arch.GetIl2CppType().Name + " " + arch.ArchetypeID.ToString();
            else if (first != null) what = "id " + first.ToString();
            if (__args.Length > 3 && __args[3] is Il2CppSystem.Object p && p.TryCast<Canis.entities.Entity>() is Canis.entities.Entity pe) parent = pe.EntityName;
            if (__args.Length > 4 && __args[4] is int k) count = k.ToString();
            Plugin.L.LogInfo("    create " + what + " -> " + parent + " x" + count);
        }
        catch (Exception e) { Plugin.L.LogWarning("create trace: " + e.Message); }
    }

    static void Catalogue()
    {
        try
        {
            var dir = Plugin.ArtDir();
            if (dir == null) return;
            var lines = new List<string>();
            var all = tuber.canis.archetypes.TuberArchetypes.Instance.AllArchetypes();
            var en = all.GetEnumerator();
            var it = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (it.MoveNext())
            {
                var a = en.Current;
                if (a == null) continue;
                lines.Add(a.ArchetypeID.ToString() + "\t" + a.GetIl2CppType().FullName);
            }
            lines.Sort(StringComparer.Ordinal);
            var path = System.IO.Path.Combine(dir, "archetypes.txt");
            System.IO.File.WriteAllLines(path, lines);
            Plugin.L.LogInfo("archetype catalogue: " + lines.Count + " written to " + path);
        }
        catch (Exception e) { Plugin.L.LogWarning("catalogue failed: " + e.Message); }
    }
}

// Slice 2d: what a piece is made of.
static class AttributeDump
{
    static Dictionary<int, string> names;
    static readonly HashSet<string> done = new();

    static void Load()
    {
        names = new Dictionary<int, string>();
        foreach (var type in typeof(tuber.canis.TuberAttributes).Assembly.GetTypes().Concat(typeof(Canis.entities.Entity).Assembly.GetTypes()))
        {
            if (!type.Name.EndsWith("Attributes")) continue;
            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public))
            {
                if (!prop.PropertyType.Name.StartsWith("AttributeDefinition")) continue;
                try
                {
                    var def = prop.GetValue(null);
                    if (def == null) continue;
                    var key = (int)def.GetType().GetProperty("Key").GetValue(def);
                    var name = (string)def.GetType().GetProperty("Name").GetValue(def);
                    names[key] = type.Name + "." + (name ?? prop.Name);
                }
                catch { }
            }
        }
        Plugin.L.LogInfo("attribute names known: " + names.Count);
    }

    internal static void Pieces(Canis.entities.Entity player)
    {
        if (names == null) Load();
        foreach (var area in player.Children)
            foreach (var piece in area.Children)
            {
                var key = player.EntityName + "/" + piece.EntityName;
                if (!done.Add(key)) continue;
                Plugin.L.LogInfo("  piece " + key + " (" + piece.GetIl2CppType().Name + ", in " + area.EntityName + "): " + Attrs(piece));
            }
    }

    static string Attrs(Canis.entities.Entity e)
    {
        var s = "";
        try
        {
            var en = e.GetEnumerator();
            var it = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (it.MoveNext())
            {
                var a = en.Current;
                if (a == null) continue;
                var n = names.TryGetValue(a.Name, out var nm) ? nm : a.Name.ToString();
                string v; try { v = a.Value == null ? "null" : a.Value.ToString(); } catch { v = "?"; }
                s += n + "=" + v + "; ";
            }
        }
        catch (Exception x) { s += "(" + x.Message + ")"; }
        return s;
    }
}

// Slice 2e/2f: Spinners of Mercy's identity and its own pieces.
// 0.10.2 log: every attribute written through the generic SetAttribute<Nullable<T>> arrived as 0 (Web came out as
// PieceType=Warrior, TokenType=None; the player's Faction stayed MarquiseDeCat). Il2CppInterop does not pass Nullable<enum>
// values into IL2CPP generics. So SetAttribute is still called (Root creates the attribute record correctly), and then the
// value is written straight into the record: MutableAttribute<Nullable<T>> fields <Value>k__BackingField and
// <ModValue>k__BackingField, Nullable layout {bool hasValue @0, int value @4}. Each write is read back through Root's own
// getter and logged.
[HarmonyPatch]
static class SpinnersPieces
{
    internal const TokenTypes Web = (TokenTypes)11;          // TokenTypes ends at Foothold = 10
    internal const BuildingTypes Infirmary = (BuildingTypes)13; // BuildingTypes ends at Vault = 12
    internal const int Webs = 24, InfirmariesPerSuit = 3, Warriors = 20;

    // R14 mask (0.15.0): the Spinners player keeps Faction = MarquiseDeCat, so the client builds its HUD like any Marquise.
    // Which player is Spinners lives here, keyed by the IL2CPP object (Root's GC does not move objects).
    internal static readonly HashSet<IntPtr> Players = new();
    // 0.30.2: the 16 webs the plugin made (EntityID text). Root's own 8 wood are not in here. Entities live as long as
    // the match holds them, so their IDs stay valid; reset per match in AfterInit.
    internal static readonly HashSet<string> MadeWebs = new();
    internal static bool IsMade(Canis.entities.Entity e) => e != null && MadeWebs.Contains(e.EntityID?.ToString() ?? "");
    internal static bool Registered(Canis.entities.Entity e)
    {
        try { var found = e?.Match?.GetEntity(e.EntityID); return found != null && found.Pointer == e.Pointer; }
        catch { return false; }
    }
    // 0.30.3: made webs are used only when, at creation, every one is in Root's registry and has an archetype like
    // Root's own wood; otherwise Recruit keeps to Root's 8 (the 0.30.2 safe mode, now decided by the check).
    internal static bool MadeUsable;

    // 0.30.4: every readable property (TuberEntity and its bases) and every attribute key that differs between two
    // pieces, identity fields left out.
    internal static List<string> Diff(Canis.entities.Entity a, Canis.entities.Entity b)
    {
        var d = new List<string>();
        if (a == null || b == null) { d.Add("missing piece"); return d; }
        var skip = new HashSet<string> { "EntityID", "Pointer", "ObjectClass", "WasCollected", "isWrapped", "pooledPtr", "PositionInParent" };   // 0.30.5: slot in the supply list is identity
        string Show(object v)
        {
            if (v == null) return "null";
            if (v is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o) return o.GetType().Name + (o is Il2CppSystem.Object so ? ":" + so.ToString() : "");
            return v.ToString();
        }
        var ta = a.TryCast<tuber.canis.entities.TuberEntity>(); var tb = b.TryCast<tuber.canis.entities.TuberEntity>();
        object A = (object)ta ?? a, B = (object)tb ?? b;
        foreach (var pr in A.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (skip.Contains(pr.Name) || pr.GetIndexParameters().Length > 0 || !pr.CanRead) continue;
            string va, vb;
            try { va = Show(pr.GetValue(A)); } catch (Exception e) { va = "!" + (e.InnerException ?? e).GetType().Name; }
            try { vb = Show(pr.GetValue(B)); } catch (Exception e) { vb = "!" + (e.InnerException ?? e).GetType().Name; }
            if (va.Length > 60) va = va.Substring(0, 60); if (vb.Length > 60) vb = vb.Substring(0, 60);
            if (va != vb && !(va.Contains("-") && vb.Contains("-") && pr.Name.EndsWith("ID"))) d.Add(pr.Name + ": wood=" + va + " made=" + vb);
        }
        try
        {
            // attribute keys are small ints (TuberAttributes definitions); probe them (the IL2CPP dictionary does not
            // enumerate from C#)
            var ka = new HashSet<string>(); var kb = new HashSet<string>();
            for (int k = 0; k < 2048; k++) { if (a.attributes.data.ContainsKey(k)) ka.Add(k.ToString()); if (b.attributes.data.ContainsKey(k)) kb.Add(k.ToString()); }
            // 0.30.5: the int probe finds no keys (the dictionary's key type is not a plain int from C#), so only the
            // attribute counts are compared; the values that matter (PieceType, TokenType, Faction) are compared above.
            if (a.attributes.data.Count != b.attributes.data.Count) d.Add("attribute count: wood " + a.attributes.data.Count + " made " + b.attributes.data.Count);
            foreach (var k in ka.Except(kb)) d.Add("attribute " + k + " only on wood");
            foreach (var k in kb.Except(ka)) d.Add("attribute " + k + " only on made");
        }
        catch (Exception e) { d.Add("attributes unreadable: " + e.Message); }
        return d;
    }
    internal static bool Usable(Canis.entities.Entity k) => !IsMade(k) || MadeUsable;
    internal static bool IsSpinners(Il2CppSystem.Object p) => p != null && Players.Contains(p.Pointer);
    internal const bool WriteFaction29 = false;   // 0.11.0-0.14.1 wrote 29: client UI crashed (PlayerUILoader NRE)
    internal const bool CreatePieces = false;     // slice A step 1 tests the mask alone; pieces come back next

    internal static void AfterInit(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer __instance)
    {
        var init = __instance.tpMatchInitData;
        if (init == null || init.Faction != Plugin.Spinners) return;
        // New match: forget the last match's player and corners (one Spinners player per game).
        Players.Clear();
        MadeWebs.Clear();
        MadeUsable = false;
        SpinnersSetup.Home = SpinnersSetup.Reserved = null;
        SpinnersSetup.SetupOver = false;
        Players.Add(__instance.Pointer);
        Plugin.L.LogInfo("R14 mask: Spinners player recorded (" + Players.Count + "); Faction stays " + __instance.Faction
                         + (CreatePieces ? "" : "; 0.10-era piece creation off (SpinnersSetup reshapes the Marquise pieces)"));
        SpinnersSetup.Reshape(__instance);
        if (!WriteFaction29 && !CreatePieces) return;
        try
        {
            if (WriteFaction29)
            {
                Set(__instance, tuber.canis.TuberAttributes.Faction, (int)Plugin.Spinners);
                Plugin.L.LogInfo("Spinners player faction after write: Faction=" + __instance.Faction);
            }
            if (!CreatePieces) return;
            var match = __instance.Match;
            var area = new tuber.canis.entities.TuberEntity(match, __instance, null, "Infirmaries");
            __instance.AddChild(area);
            foreach (var suit in new[] { Suits.Mouse, Suits.Rabbit, Suits.Fox })
                for (int i = 0; i < InfirmariesPerSuit; i++)
                {
                    var b = Piece(match, __instance, "Infirmary", area);
                    Set(b, tuber.canis.TuberAttributes.PieceType, (int)PieceTypes.Building);
                    Set(b, tuber.canis.TuberAttributes.BuildingType, (int)Infirmary);
                    Set(b, tuber.canis.TuberAttributes.Suit, (int)suit);
                }
            for (int i = 0; i < Webs; i++)
            {
                var w = Piece(match, __instance, "Web", __instance.TokensArea);
                Set(w, tuber.canis.TuberAttributes.PieceType, (int)PieceTypes.Token);
                Set(w, tuber.canis.TuberAttributes.TokenType, (int)Web);
            }
            var first = area.Children[0].TryCast<tuber.canis.entities.TuberEntity>();
            Plugin.L.LogInfo("Spinners pieces created: " + area.Children.Count + " infirmaries, " + Webs + " webs; first infirmary reads Suit="
                             + first?.Suit + " Faction=" + first?.Faction);
            AttributeDump.Pieces(__instance.Cast<Canis.entities.Entity>());
        }
        catch (Exception e) { Plugin.L.LogError("Spinners identity/pieces failed: " + e); }
    }

    static tuber.canis.entities.TuberEntity Piece(Canis.Match match, Canis.entities.PlayerEntity owner, string name, Canis.entities.Entity into)
    {
        var e = new tuber.canis.entities.TuberEntity(match, owner, null, name);
        Set(e, tuber.canis.TuberAttributes.Faction, (int)Plugin.Spinners);
        into.AddChild(e);
        return e;
    }

    // Create the record through Root (value lands as 0), then write the real value into it.
    internal static unsafe void Set<T>(Canis.attributes.ObjectAttributes o, Canis.attributes.AttributeDefinition<Il2CppSystem.Nullable<T>> def, int value)
        where T : unmanaged
    {
        o.SetAttribute(def, new Il2CppSystem.Nullable<T>());
        var record = o.attributes.data[def.Key];
        var obj = record.Pointer;
        var klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(obj);
        foreach (var field in new[] { "<Value>k__BackingField", "<ModValue>k__BackingField" })
        {
            var f = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, field);
            if (f == IntPtr.Zero) throw new Exception("no field " + field + " on attribute " + def.Name);
            var at = (byte*)obj + Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(f);
            at[0] = 1;                 // hasValue
            *(int*)(at + 4) = value;   // value (enum, 4 bytes, aligned)
        }
    }

    // Raw read of the same record (0.25.0): ModValue if set, else Value, else -1 (no record).
    internal static unsafe int Get<T>(Canis.attributes.ObjectAttributes o, Canis.attributes.AttributeDefinition<Il2CppSystem.Nullable<T>> def)
        where T : unmanaged
    {
        if (!o.attributes.data.ContainsKey(def.Key)) return -1;
        var obj = o.attributes.data[def.Key].Pointer;
        var klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(obj);
        foreach (var field in new[] { "<ModValue>k__BackingField", "<Value>k__BackingField" })
        {
            var f = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, field);
            if (f == IntPtr.Zero) continue;
            var at = (byte*)obj + Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(f);
            if (at[0] != 0) return *(int*)(at + 4);
        }
        return -1;
    }
}

// Slice 2g (layer 4 of R13): the screen borrows Marquise visuals for faction 29.
// 0.11.0: with Faction 29 real, the client crashed in PlayerUILoader.InitFromContext (NullReferenceException), because it
// loads each player's board by "flavor" tags (PrefabByFlavorLoader / PrefabFlavorContext) and nothing is tagged for 29.
// Every flavor request is built from a string array or list; tags that name faction 29 are rewritten to the host's name
// before the request exists. Rules keep seeing 29; only the art is borrowed.
[HarmonyPatch]
static class BorrowVisuals
{
    static int logged;
    static readonly string Host = SpinnersHost.Host.ToString();

    static bool IsSpinners(string s) => s == "29" || s == "SpinnersOfMercy" || s.EndsWith("_29") || s.EndsWith(".29") || s.EndsWith(":29");
    static string Swap(string s) => s == "29" || s == "SpinnersOfMercy" ? Host : s.Substring(0, s.Length - 2) + Host;

    static void Fix(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray tags, string where)
    {
        if (tags == null) return;
        var all = "";
        var hit = false;
        for (int i = 0; i < tags.Length; i++)
        {
            var s = tags[i];
            all += s + ",";
            if (s != null && IsSpinners(s)) { tags[i] = Swap(s); hit = true; }
        }
        if (hit) Plugin.L.LogInfo("flavor " + where + " [" + all + "] -> borrowed " + Host);
        else if (logged++ < 150) Plugin.L.LogInfo("flavor " + where + " [" + all + "]");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(dwd.core.prefabs.implementations.byflavor.PrefabFlavorContext), MethodType.Constructor,
        new[] { typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray) })]
    static void FromArray(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray __0) => Fix(__0, "ctor[]");

    [HarmonyPrefix, HarmonyPatch(typeof(dwd.core.prefabs.implementations.byflavor.PrefabFlavorContext), nameof(dwd.core.prefabs.implementations.byflavor.PrefabFlavorContext.WithFlavors),
        new[] { typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray) })]
    static void WithArray(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray __0) => Fix(__0, "with[]");

    [HarmonyPrefix, HarmonyPatch(typeof(dwd.core.prefabs.implementations.byflavor.PrefabFlavorContext), MethodType.Constructor,
        new[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<string>) })]
    static void FromList(ref Il2CppSystem.Collections.Generic.IEnumerable<string> __0) => __0 = FixList(__0, "ctor{}");

    [HarmonyPrefix, HarmonyPatch(typeof(dwd.core.prefabs.implementations.byflavor.PrefabFlavorContext), nameof(dwd.core.prefabs.implementations.byflavor.PrefabFlavorContext.WithFlavors),
        new[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<string>) })]
    static void WithList(ref Il2CppSystem.Collections.Generic.IEnumerable<string> __0) => __0 = FixList(__0, "with{}");

    static Il2CppSystem.Collections.Generic.IEnumerable<string> FixList(Il2CppSystem.Collections.Generic.IEnumerable<string> src, string where)
    {
        if (src == null) return src;
        var list = new Il2CppSystem.Collections.Generic.List<string>(src);
        var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(list.Count);
        for (int i = 0; i < list.Count; i++) arr[i] = list[i];
        Fix(arr, where);
        var outList = new Il2CppSystem.Collections.Generic.List<string>();
        for (int i = 0; i < arr.Length; i++) outList.Add(arr[i]);
        return new Il2CppSystem.Collections.Generic.IEnumerable<string>(outList.Pointer);
    }
}

// Slice 2h: census of faction-keyed tables (R13 layer 4 and beyond). Sherlock: "how would be able to seamlessly stitch
// something in without it noticing". Answer: register Spinners in every table the game keys by faction. This lists them.
// Entry types = types with a property `faction`/`Faction` of type Factions. Tables = loaded Unity objects with an array or
// list property of such entries, plus static Dictionary<Factions, *> properties. For each: which factions it covers.
[HarmonyPatch]
static class Census
{
    static readonly HashSet<string> done = new();

    [HarmonyPostfix, HarmonyPatch(typeof(AvailableFactionsData), nameof(AvailableFactionsData.AvailableFactionsForPlayerType))]
    static void InMenu() { BoardViewer.InMatch = false; Stitch.All(); if (done.Add("menu")) Run("menu"); }

    // 0.13.0 census (match): FactionGameObjects.factionObjects and FactionSpriteData.factionSprites cover every faction
    // but 29, and PlayerUILoader.InitFromContext crashed right after. Stitch first, then take the census again.
    [HarmonyPrefix, HarmonyPatch(typeof(tuber.client.match.behaviours.PlayerUILoader), nameof(tuber.client.match.behaviours.PlayerUILoader.InitFromContext))]
    // 0.14.0 also ran Prefabs() here; the game hard-crashed inside it (no managed exception, no result line): it reads
    // PrefabLookupEntry<PrefabFlavorsMetaData>.Metadata, a generic value type through interop. Not called any more.
    static void InMatch() { BoardViewer.InMatch = true; Stitch.All(); if (done.Add("match")) Run("match"); }

    // The screen's prefab catalogue: which flavor tag sets each loader can serve (only sets naming a faction).
    static void Prefabs()
    {
        try
        {
            var lines = new List<string>();
            var names = new HashSet<string>(Enum.GetNames(typeof(Factions)));
            foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<dwd.core.prefabs.implementations.byflavor.PrefabByFlavorLoader>()))
            {
                var loader = o.Cast<dwd.core.prefabs.implementations.byflavor.PrefabByFlavorLoader>();
                var lookup = loader.byFlavorLookup;
                if (lookup == null) continue;
                var sets = new SortedSet<string>();
                foreach (var e in Il2Each.Of(lookup.baseEntries))
                {
                    var s = e.Metadata.ToString();
                    if (names.Any(n => s.Contains(n))) sets.Add(s);
                }
                if (sets.Count > 0) lines.Add(loader.name + " (" + loader.gameObject.name + "): " + string.Join(" | ", sets));
            }
            var path = System.IO.Path.Combine(Plugin.ArtDir() ?? ".", "prefab-flavors.txt");
            System.IO.File.WriteAllLines(path, lines);
            Plugin.L.LogInfo("prefab catalogue: " + lines.Count + " loaders with faction-tagged prefabs -> " + path);
        }
        catch (Exception e) { Plugin.L.LogWarning("prefab catalogue failed: " + e.Message); }
    }

    static void Run(string where)
    {
        try
        {
            var lines = new List<string>();
            var asms = new[] { typeof(tuber.client.match.data.FactionSpriteData).Assembly, typeof(FactionUtils).Assembly };
            var types = asms.SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } }).ToArray();
            var entryTypes = new HashSet<Type>(types.Where(ty => ty.GetProperty("faction") is { } pr && pr.PropertyType == typeof(Factions)
                                                              || ty.GetProperty("Faction") is { } pr2 && pr2.PropertyType == typeof(Factions)));
            lines.Add("entry types: " + string.Join(", ", entryTypes.Select(e => e.FullName)));
            var unityObject = typeof(UnityEngine.Object);
            foreach (var holder in types.Where(ty => unityObject.IsAssignableFrom(ty) && !ty.IsAbstract))
            {
                var props = holder.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    .Where(pr => pr.PropertyType.IsGenericType && pr.PropertyType.GetGenericArguments().Any(g => entryTypes.Contains(g))).ToArray();
                if (props.Length == 0) continue;
                UnityEngine.Object[] found;
                try { found = UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(holder)).ToArray(); }
                catch { continue; }
                foreach (var pr in props)
                {
                    var cover = new SortedSet<string>();
                    foreach (var o in found)
                    {
                        try
                        {
                            var typed = Activator.CreateInstance(holder, o.Pointer);
                            if (pr.GetValue(typed) is System.Collections.IEnumerable seq)
                                foreach (var entry in seq)
                                {
                                    if (entry == null) continue;
                                    var fp = entry.GetType().GetProperty("faction") ?? entry.GetType().GetProperty("Faction");
                                    if (fp?.GetValue(entry) is Factions f) cover.Add(f.ToString());
                                }
                        }
                        catch { }
                    }
                    lines.Add(holder.FullName + "." + pr.Name + "  instances=" + found.Length + "  factions: "
                              + (cover.Count == 0 ? "(none read)" : string.Join(" ", cover))
                              + (cover.Contains("MarquiseDeCat") && !cover.Contains("29") ? "   <-- HOLE for 29" : ""));
                }
            }
            foreach (var ty in types)
                foreach (var pr in ty.GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public))
                {
                    var pt = pr.PropertyType;
                    if (!pt.IsGenericType || !pt.Name.StartsWith("Dictionary") || pt.GetGenericArguments()[0] != typeof(Factions)) continue;
                    try
                    {
                        var d = pr.GetValue(null);
                        var keys = d?.GetType().GetProperty("Keys")?.GetValue(d) as System.Collections.IEnumerable;
                        var ks = new SortedSet<string>();
                        if (keys != null) foreach (var k in keys) ks.Add(k.ToString());
                        lines.Add("static " + ty.FullName + "." + pr.Name + "  keys: " + string.Join(" ", ks)
                                  + (ks.Contains("MarquiseDeCat") && !ks.Contains("29") ? "   <-- HOLE for 29" : ""));
                    }
                    catch { }
                }
            var dir = Plugin.ArtDir();
            var path = System.IO.Path.Combine(dir ?? ".", "census-" + where + ".txt");
            System.IO.File.WriteAllLines(path, lines);
            Plugin.L.LogInfo("census (" + where + "): " + lines.Count + " lines, " + lines.Count(l => l.Contains("HOLE")) + " holes -> " + path);
        }
        catch (Exception e) { Plugin.L.LogWarning("census " + where + " failed: " + e); }
    }
}

static class Il2Each
{
    internal static IEnumerable<T> Of<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> seq)
    {
        if (seq == null) yield break;
        var en = seq.GetEnumerator();
        var it = en.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (it.MoveNext()) yield return en.Current;
    }
}

// Slice 2i: register faction 29 in the client tables, the way the game's own factions are registered (a row each).
static class Stitch
{
    internal static void All()
    {
        var added = 0;
        try
        {
            foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<tuber.client.match.dataRenderers.FactionGameObjects>()))
            {
                var t = o.Cast<tuber.client.match.dataRenderers.FactionGameObjects>();
                var rows = t.factionObjects;
                if (rows == null) continue;
                tuber.client.match.dataRenderers.FactionGameObjects.FactionObjects host = null;
                var has = false;
                foreach (var r in rows) { if (r.faction == Plugin.Spinners) has = true; if (r.faction == SpinnersHost.Host) host = r; }
                if (has || host == null) continue;
                var row = new tuber.client.match.dataRenderers.FactionGameObjects.FactionObjects { faction = Plugin.Spinners, gameObjects = host.gameObjects };
                var grown = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<tuber.client.match.dataRenderers.FactionGameObjects.FactionObjects>(rows.Length + 1);
                for (int i = 0; i < rows.Length; i++) grown[i] = rows[i];
                grown[rows.Length] = row;
                t.factionObjects = grown;
                added++;
            }
            foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<FactionSpriteData>()))
            {
                var t = o.Cast<FactionSpriteData>();
                var rows = t.factionSprites;
                if (rows == null) continue;
                FactionSpriteData.FactionSprite host = null;
                var has = false;
                foreach (var r in rows) { if (r.faction == Plugin.Spinners) has = true; if (r.faction == SpinnersHost.Host) host = r; }
                if (has) continue;
                var row = new FactionSpriteData.FactionSprite { faction = Plugin.Spinners, sprite = Plugin.LoadIcon() ?? host?.sprite };
                var grown = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<FactionSpriteData.FactionSprite>(rows.Length + 1);
                for (int i = 0; i < rows.Length; i++) grown[i] = rows[i];
                grown[rows.Length] = row;
                t.factionSprites = grown;
                added++;
            }
        }
        catch (Exception e) { Plugin.L.LogWarning("stitch failed: " + e.Message); }
        if (added > 0) Plugin.L.LogInfo("stitched faction 29 into " + added + " client tables");
    }
}

// Slice A step 2 (0.16.0): Spinners' pieces and setup, on the Marquise class and the Marquise HUD (R14 mask).
// Pieces reuse what the Marquise HUD already draws, so nothing new needs art yet (TokenType 11 / BuildingType 13 come
// back with slice J): wood tokens are the webs, and the three Marquise building tracks are the three infirmary tracks
// (Sawmills = mouse, Workshops = rabbit, Recruiters = fox), 3 each. The keep is removed. Warriors 25 -> 20.
// Setup is Root's own Move action, composed in an AnonymousAction (both in Canis.actions), instead of the keep placement.
static class SpinnersSetup
{
    internal const int InfirmariesPerTrack = 3;
    internal const int WebsKept = 8;   // Marquise has 8 wood; the rule says 24. Extra tokens wait until the HUD's capacity is known.

    internal static void PatchAll(Harmony h)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        void One(System.Reflection.MethodBase target, string name, bool prefix)
        {
            try
            {
                var m = new HarmonyMethod(typeof(SpinnersSetup).GetMethod(name, flags));
                if (prefix) h.Patch(target, prefix: m); else h.Patch(target, postfix: m);
                Plugin.L.LogInfo("patched: " + target.DeclaringType.Name + "." + target.Name);
            }
            catch (Exception e) { Plugin.L.LogError("patch " + name + " failed (that part inactive): " + e.Message); }
        }
        One(AccessTools.Method(typeof(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer), "GetSetupActions", new[] { typeof(bool) }), nameof(Setup), true);
        One(AccessTools.Method(typeof(Canis.actions.Move), "execute"), nameof(MoveTrace), true);
        One(AccessTools.Method(typeof(tuber.canis.actions.SetupPlayers), "execute"), nameof(SetupLast), true);
        try
        {
            // One registration (prefix + postfix together): one Harmony owner for this method.
            h.Patch(AccessTools.Method(typeof(tuber.canis.TuberMatch), "NextSetupPlayerTurnNumForFaction"),
                prefix: new HarmonyMethod(typeof(SpinnersSetup).GetMethod(nameof(SetupSlot), flags)),
                postfix: new HarmonyMethod(typeof(SpinnersSetup).GetMethod(nameof(SetupSlotSeen), flags)));
            Plugin.L.LogInfo("patched: TuberMatch.NextSetupPlayerTurnNumForFaction");
        }
        catch (Exception e) { Plugin.L.LogError("patch setup slot failed: " + e.Message); }
        // 0.16.0: get_FactionSetupOrder is a field accessor ("can't be patched"), so Spinners sets up in Marquise's slot (first).
        One(AccessTools.PropertyGetter(typeof(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer), "KeepNode"), nameof(KeepNode), true);
    }

    internal static IEnumerable<Canis.entities.Entity> Kids(Canis.entities.Entity e)
    {
        var list = new List<Canis.entities.Entity>();
        if (e?.Children == null) return list;
        for (int i = 0; i < e.Children.Count; i++) list.Add(e.Children[i]);
        return list;
    }

    static int Trim(Canis.entities.Entity area, int keep)
    {
        var kids = Kids(area).ToList();
        int n = 0;
        for (int i = keep; i < kids.Count; i++) { kids[i].Destroy(); n++; }
        return n;
    }

    // Called from SpinnersPieces.AfterInit, after the host initialize(), for the Spinners player only.
    internal static void Reshape(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p)
    {
        try
        {
            var keep = p.Keep;
            if (keep != null) keep.Destroy();
            int s = Trim(p.Sawmills, InfirmariesPerTrack), w = Trim(p.Workshops, InfirmariesPerTrack), r = Trim(p.Recruiters, InfirmariesPerTrack);
            int war = Trim(p.WarriorArea, SpinnersPieces.Warriors);
            int wood = Kids(p.TokensArea).Count();
            // 0.30.0: 24 webs (fan board). Marquise has 8 wood; the 16 missing are made exactly as Root makes its own
            // wood in initialize() (0.9.0 trace: a plain TuberEntity named "Wood" in TokensArea with PieceType=Token,
            // TokenType=Wood, Faction=MarquiseDeCat), here in the same initialize() postfix, before the match state is
            // sent to the screen. They are ordinary Root wood: placed, moved, removed and scored like the first 8.
            int made = 0;
            for (int i = wood; i < SpinnersPieces.Webs; i++)
            {
                // 0.30.3: Root's own helper (TuberPlayerEntity.createPiece) instead of the bare constructor with no
                // archetype that 0.30.0-0.30.2 used (the first such web moved crashed Root's Move).
                var web = p.createPiece(p, p.TokensArea, "Wood");
                if (web == null) { Plugin.L.LogError("Spinners webs: createPiece returned null"); break; }
                if (web.Parent == null) p.TokensArea.AddChild(web);
                SpinnersPieces.Set(web, tuber.canis.TuberAttributes.PieceType, (int)PieceTypes.Token);
                SpinnersPieces.Set(web, tuber.canis.TuberAttributes.TokenType, (int)TokenTypes.Wood);
                SpinnersPieces.Set(web, tuber.canis.TuberAttributes.Faction, (int)SpinnersHost.Host);
                SpinnersPieces.MadeWebs.Add(web.EntityID?.ToString() ?? "");
                made++;
            }
            if (made > 0)
            {
                var last = Kids(p.TokensArea).Last().TryCast<tuber.canis.entities.TuberEntity>();
                Plugin.L.LogInfo("Spinners webs: " + made + " made like Root's wood (" + wood + " -> " + Kids(p.TokensArea).Count() + "); last reads "
                                 + last?.PieceType + "/" + last?.TokenType + "/" + last?.Faction + " id " + last?.EntityID);
                // 0.30.2 trace: does Root's entity registry know the made webs the way it knows its own wood?
                var all = Kids(p.TokensArea).ToList();
                Plugin.L.LogInfo("Spinners webs registry: Root wood " + all.Count(k => !SpinnersPieces.IsMade(k) && SpinnersPieces.Registered(k)) + "/" + all.Count(k => !SpinnersPieces.IsMade(k))
                                 + " registered, made webs " + all.Count(k => SpinnersPieces.IsMade(k) && SpinnersPieces.Registered(k)) + "/" + all.Count(SpinnersPieces.IsMade) + " registered");
                var rootWood = all.FirstOrDefault(k => !SpinnersPieces.IsMade(k));
                bool rootHasArch = rootWood?.GetArchetype() != null;
                var madeList = all.Where(SpinnersPieces.IsMade).ToList();
                // 0.30.4: 0.30.3 showed both Root's wood and the made webs registered with no archetype, so that check
                // proves nothing about the Move crash. Compare a Root wood and a made web property by property and
                // attribute by attribute; the made webs are used only if nothing but identity differs.
                var diffs = SpinnersPieces.Diff(rootWood, madeList.FirstOrDefault());
                Plugin.L.LogInfo("Spinners webs: Root wood vs made web differ in " + diffs.Count + " places" + (diffs.Count == 0 ? "" : ": " + string.Join(" | ", diffs.Take(40))));
                SpinnersPieces.MadeUsable = madeList.Count > 0 && diffs.Count == 0 && madeList.All(k => SpinnersPieces.Registered(k) && (k.GetArchetype() != null || !rootHasArch));
                Plugin.L.LogInfo("Spinners webs: Root wood archetype " + (rootHasArch ? rootWood.GetArchetype().GetIl2CppType().Name : "none") + ", made archetype "
                                 + (madeList.FirstOrDefault()?.GetArchetype() == null ? "none" : madeList[0].GetArchetype().GetIl2CppType().Name)
                                 + " -> made webs " + (SpinnersPieces.MadeUsable ? "USED (24 webs)" : "NOT used (Recruit keeps to Root's 8)"));
            }
            Plugin.L.LogInfo("Spinners pieces: keep removed=" + (keep != null) + "; infirmary tracks mouse/rabbit/fox = sawmills/workshops/recruiters "
                + Kids(p.Sawmills).Count() + "/" + Kids(p.Workshops).Count() + "/" + Kids(p.Recruiters).Count() + " (removed " + s + "/" + w + "/" + r
                + "); warriors " + Kids(p.WarriorArea).Count() + " (removed " + war + "); webs (wood tokens) " + Kids(p.TokensArea).Count() + " of 24");
        }
        catch (Exception e) { Plugin.L.LogError("Spinners reshape failed: " + e); }
    }

    // Setup order N (fan board: "Setup (N)", after the official factions; its corner rule needs the others' homes placed).
    // TuberMatch.FactionSetupOrder's getter cannot be patched (field accessor, 0.16.0), but the list it returns can be
    // edited: just before SetupPlayers runs, MarquiseDeCat (= Spinners under the mask) moves to the end.
    // Setup slot. 0.17.2: list edited yet Spinners' setup never ran: SetupPlayers pairs each faction with
    // TuberMatch.NextSetupPlayerTurnNumForFaction. 0.18.0 trace: Eyrie 20, Vagabond 41, Riverfolk 70 = slot letter x 10 + a
    // per-faction counter that the call itself advances ("Next"). 0.18.0 also called it for every Factions value to find
    // the maximum: that hard-crashed the game. So it is never called by the plugin, and the trace only reads the answer
    // Root got. Letters decode as A=1 (Eyrie B=20, Vagabond D=40, Riverfolk G=70); the fan board says "Setup (N)", N = 14.
    internal const int SpinnersSetupSlot = 140;
    static int slotLogs;
    static bool SetupSlot(Factions __0, ref int __result)
    {
        if (__0 != SpinnersHost.Host || SpinnersPieces.Players.Count == 0) return true;
        __result = SpinnersSetupSlot;
        return false;
    }
    static void SetupSlotSeen(Factions __0, int __result)
    {
        if (slotLogs++ < 30) Plugin.L.LogInfo("setup slot " + __0 + " = " + __result + (__0 == SpinnersHost.Host && SpinnersPieces.Players.Count > 0 ? " (Spinners)" : ""));
    }

    static void SetupLast(tuber.canis.actions.SetupPlayers __instance)
    {
        try
        {
            if (SpinnersPieces.Players.Count > 0 && __instance.playersCompletedSetup != null && slotLogs < 40)
            {
                var done = ""; for (int i = 0; i < __instance.playersCompletedSetup.Count; i++) done += __instance.playersCompletedSetup[i] + " ";
                Plugin.L.LogInfo("setup step: players completed [" + done + "]");
            }
            if (SpinnersPieces.Players.Count == 0) return;
            var order = __instance.tm?.FactionSetupOrder;
            if (order == null) { Plugin.L.LogWarning("setup order: no list"); return; }
            string Show() { var t = ""; for (int i = 0; i < order.Count; i++) t += order[i] + " "; return t; }
            var before = Show();
            // 0.17.0 put it at the very end, after "Invalid", and its setup never ran: Root stops at Invalid. So it goes
            // just before Invalid (after every real faction).
            if (order.Contains(SpinnersHost.Host))
            {
                order.Remove(SpinnersHost.Host);
                int stop = order.IndexOf(Factions.Invalid);
                if (stop < 0) order.Add(SpinnersHost.Host); else order.Insert(stop, SpinnersHost.Host);
            }
            Plugin.L.LogInfo("setup order: " + before + "-> " + Show() + "(Spinners = " + SpinnersHost.Host + ", last)");
        }
        catch (Exception e) { Plugin.L.LogError("setup order failed: " + e); }
    }

    static bool Setup(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer __instance, ref Canis.actions.Action __result)
    {
        if (!SpinnersPieces.IsSpinners(__instance)) return true;
        var p = __instance;
        Plugin.L.LogInfo("Spinners setup replaces the Marquise setup");
        Func<Canis.context.Context, Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>> body = ctx => Steps(p, ctx);
        var f = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<Canis.context.Context, Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>>>(body);
        __result = new Canis.actions.AnonymousAction(f, p.Match);
        return false;
    }

    // 0.16.0 found 0 clearings among the playmat's direct children: the nodes sit deeper. Walk the tree (bounded) and log
    // the path to the first node once, so the layout is known.
    static int walkLogs;
    internal static List<tuber.canis.entities.TuberBoardNode> Clearings(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p)
    {
        var found = new List<tuber.canis.entities.TuberBoardNode>();
        var seen = new HashSet<IntPtr>();
        string firstPath = null;
        int visited = 0;
        void Walk(Canis.entities.Entity e, string path, int depth)
        {
            if (e == null || depth > 6 || visited > 5000 || !seen.Add(e.Pointer)) return;
            visited++;
            var n = e.TryCast<tuber.canis.entities.TuberBoardNode>();
            if (n != null && n.IsClearing) { found.Add(n); firstPath ??= path; }
            foreach (var k in Kids(e)) Walk(k, path + "/" + k.GetIl2CppType().Name, depth + 1);
        }
        Walk(p.TuberMatch.TuberPlaymat, "TuberPlaymat", 0);
        if (found.Count == 0) Walk(p.Match.Playmat, "Playmat", 0);
        if (walkLogs++ < 2) Plugin.L.LogInfo("setup: walked " + visited + " entities; first clearing at " + (firstPath ?? "none"));
        return found;
    }

    // The other factions' setup (Lizard 0.16.0 run: NRE in LizardCultSetup.SetStartingLocation) reads the Marquise keep's
    // clearing to choose its corner. Spinners has no keep, so for the Spinners player KeepNode answers its home corner
    // (where its setup put the infirmary): the others set up opposite Spinners, as they would opposite a keep.
    internal static tuber.canis.entities.TuberBoardNode Home, Reserved;
    internal static bool SetupOver;   // set when the first player turn starts (TurnTrace: GetBirdsongActions)
    static int keepAsks;
    // Prefix: Root's own getter is not run for Spinners (its keep is gone, the getter may not expect that).
    static bool KeepNode(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer __instance, ref tuber.canis.entities.TuberBoardNode __result)
    {
        if (!SpinnersPieces.IsSpinners(__instance)) return true;
        // 0.23.1 run: after setup, a keep answer made Root offer Marquise Field Hospitals (send dead warriors to the keep);
        // the keep piece was destroyed in initialize, and the client froze on its unknown entity (KeyNotFoundException).
        // After setup, "no keep" (Root's state for a destroyed keep): no Field Hospitals.
        if (SetupOver) { __result = null; return false; }
        // Before Spinners has set up, the others (Eyrie, Lizard, Duchy set up first now) still ask where "the Marquise
        // keep" is, and the Lizard crashes on no answer (0.16.0). They get a free corner held for Spinners; they set up
        // opposite it, and Spinners' rule ("opposite an enemy's home") then leads back to it.
        if (Home == null && Reserved == null)
        {
            Reserved = Clearings(__instance).FirstOrDefault(n => n.IsCorner && !HasPlayers(n));
            Plugin.L.LogInfo("KeepNode before Spinners set up: corner " + Reserved?.Index + " held for Spinners");
        }
        __result = Home ?? Reserved;
        if (keepAsks++ < 5) Plugin.L.LogInfo("KeepNode asked for Spinners -> " + (__result == null ? "null" : "clearing " + __result.Index) + (Home == null ? " (held; Spinners not set up yet)" : ""));
        return false;
    }

    // Read-only: how Root's own Moves are filled in (first 20), to confirm which context field is the moving piece.
    static int movesTraced, spinnersMoves;
    static int birdsongDrops;
    static bool MoveTrace(Canis.actions.Move __instance, ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action> __result)
    {
        try
        {
            if (SpinnersBirdsong.InBirdsong)
            {
                var c1 = __instance.context;
                var piece = c1 != null && c1.HasTarget ? c1.Target?.TryCast<tuber.canis.entities.TuberEntity>() : null;
                if (piece != null && piece.PieceType == PieceTypes.Token && piece.TokenType == TokenTypes.Wood && piece.Faction == SpinnersHost.Host
                    && __instance.destination?.TryCast<tuber.canis.entities.TuberBoardNode>() != null)
                {
                    if (birdsongDrops++ < 10) Plugin.L.LogInfo("Birdsong: Marquise wood move to a clearing dropped (web stays in supply)");
                    __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>().Pointer);
                    return false;
                }
            }
        }
        catch (Exception e) { Plugin.L.LogWarning("birdsong wood: " + e.Message); }
        try
        {
            // 0.26.0 Skitter: count moves of Spinners warriors (Spinners and Marquise never share a game, so a moving
            // MarquiseDeCat warrior is Spinners').
            var c0 = __instance.context;
            var w = c0 != null && c0.HasTarget ? c0.Target?.TryCast<tuber.canis.entities.TuberEntity>() : null;
            if (w != null && w.PieceType == PieceTypes.Warrior && w.Faction == SpinnersHost.Host && SpinnersPieces.Players.Count > 0) AbilityTrace.SpinnersMoves++;
        }
        catch (Exception e) { Plugin.L.LogWarning("move count: " + e.Message); }
        try
        {
            // 0.30.2: name the exact piece of every Spinners move, so a crash inside Root's Move points at its cause.
            var c2 = __instance.context;
            var pc = c2 != null && c2.HasTarget ? c2.Target?.TryCast<tuber.canis.entities.TuberEntity>() : null;
            if (pc != null && pc.Faction == SpinnersHost.Host && SpinnersPieces.Players.Count > 0 && spinnersMoves++ < 60)
            {
                var dn = __instance.destination?.TryCast<tuber.canis.entities.TuberBoardNode>();
                Plugin.L.LogInfo("Spinners move: " + pc.PieceType + (pc.PieceType == PieceTypes.Token ? (SpinnersPieces.IsMade(pc) ? " (made web)" : " (Root wood)") : "")
                    + " id " + pc.EntityID + " registered=" + SpinnersPieces.Registered(pc) + " parent=" + (pc.Parent == null ? "NULL" : pc.Parent.GetIl2CppType().Name)
                    + " -> " + (dn != null ? "clearing " + dn.Index : __instance.destination == null ? "NULL" : __instance.destination.GetIl2CppType().Name)
                    + (SpinnersBirdsong.InBirdsong ? " [birdsong]" : ""));
            }
        }
        catch (Exception e) { Plugin.L.LogWarning("spinners move trace: " + e.Message); }
        if (movesTraced++ >= 20) return true;
        try
        {
            var c = __instance.context;
            string Name(Canis.entities.Entity e) => e == null ? "null" : e.GetIl2CppType().Name + (e.Parent == null ? "" : " in " + e.Parent.GetIl2CppType().Name);
            Plugin.L.LogInfo("Move #" + movesTraced + ": dest=" + Name(__instance.destination) + " gen=" + (__instance.destinationGen != null)
                + " ctx=" + (c == null ? "null" : "Source=" + Name(c.Source) + " Target=" + (c.HasTarget ? Name(c.Target) : "none") + " Match=" + (c.Match != null)));
        }
        catch (Exception e) { Plugin.L.LogWarning("move trace: " + e.Message); }
        return true;
    }

    static bool HasPlayers(tuber.canis.entities.TuberBoardNode n) => n.GetPlayersInClearing().Count > 0;

    static tuber.canis.entities.TuberBoardNode PickCorner(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, List<tuber.canis.entities.TuberBoardNode> clearings)
    {
        // Rule: the corner opposite an enemy's home corner (keep / roost / garden / tunnel); if none is free, any free corner;
        // then any free edge clearing. The choice among valid corners is the player's in the rules; this build picks the first.
        var corners = clearings.Where(n => n.IsCorner).ToList();
        foreach (var c in corners.Where(HasPlayers))
        {
            var o = c.OppositeCorner();
            if (o != null && !HasPlayers(o)) { Plugin.L.LogInfo("setup corner: " + o.Index + " (" + o.Suit + "), opposite enemy home " + c.Index); return o; }
        }
        var free = corners.FirstOrDefault(n => !HasPlayers(n));
        if (free != null) { Plugin.L.LogInfo("setup corner: " + free.Index + " (" + free.Suit + "), no enemy home corner had a free opposite"); return free; }
        var edge = clearings.FirstOrDefault(n => n.IsEdgeLocation && !HasPlayers(n));
        Plugin.L.LogInfo("setup: all corners taken, edge clearing " + edge?.Index);
        return edge;
    }

    static int movesBuilt;
    static Canis.actions.Action MoveTo(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Canis.context.Context setup, Canis.entities.Entity piece, tuber.canis.entities.TuberBoardNode node)
    {
        // 0.16.2 trace of Root's own Moves: Source = TuberPlaymat, Target = the piece. Ours arrived as Source = Target =
        // TuberPlaymat: the context we built was replaced on the way down. So each Move is made inside its own step, at
        // run time, from the context Root hands that step: WithTarget(piece) on it, then the Move inherits it. This holds
        // whether children keep their own context or take the parent's.
        Func<Canis.context.Context, Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>> make = ctx =>
        {
            // 0.16.3: WithTarget returns a copy (step context kept Target=TuberPlaymat) and the Move still ran with the
            // step's own context. So the step's own context is changed in place: its target becomes the piece.
            var c = ctx ?? setup;
            c._target = piece;
            var mv = new Canis.actions.Move(node, p.Match, node.Children.Count, false, false);
            mv.InheritContext(c);
            if (movesBuilt++ < 2)
                Plugin.L.LogInfo("Spinners move built at run time: Source=" + c.Source?.GetIl2CppType().Name + " Target=" + (c.HasTarget ? c.Target?.GetIl2CppType().Name : "none")
                                 + "; step context now Target=" + (ctx != null && ctx.HasTarget ? ctx.Target?.GetIl2CppType().Name : "none"));
            var one = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>();
            one.Add(mv);
            return new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(one.Pointer);
        };
        return new Canis.actions.AnonymousAction(
            DelegateSupport.ConvertDelegate<Il2CppSystem.Func<Canis.context.Context, Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>>>(make), p.Match);
    }

    static Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action> Steps(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Canis.context.Context setup)
    {
        var steps = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>();
        var originalTarget = setup?._target;   // MoveTo sets targets in place; put this back when setup ends
        try
        {
            var clearings = Clearings(p);
            Plugin.L.LogInfo("setup: " + clearings.Count + " clearings, " + clearings.Count(n => n.IsCorner) + " corners");
            var corner = PickCorner(p, clearings);
            Home = corner;
            if (corner == null) { Plugin.L.LogError("setup: no clearing found"); return new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(steps.Pointer); }

            var warriors = new Queue<Canis.entities.Entity>(Kids(p.WarriorArea));
            var webs = new Queue<Canis.entities.Entity>(Kids(p.TokensArea));
            var track = corner.Suit switch { Suits.Mouse => p.Sawmills, Suits.Rabbit => p.Workshops, _ => p.Recruiters };
            var infirmary = Kids(track).FirstOrDefault();
            var moved = new List<(Canis.entities.Entity piece, string what, tuber.canis.entities.TuberBoardNode to)>();
            void Put(Canis.entities.Entity piece, string what, tuber.canis.entities.TuberBoardNode to)
            {
                if (piece == null) { Plugin.L.LogWarning("setup: no " + what + " left in supply"); return; }
                steps.Add(MoveTo(p, setup, piece, to));
                moved.Add((piece, what, to));
            }
            Put(warriors.Count > 0 ? warriors.Dequeue() : null, "warrior", corner);
            Put(warriors.Count > 0 ? warriors.Dequeue() : null, "warrior", corner);
            Put(webs.Count > 0 ? webs.Dequeue() : null, "web", corner);
            Put(webs.Count > 0 ? webs.Dequeue() : null, "web", corner);
            Put(infirmary, "infirmary (" + corner.Suit + ")", corner);
            foreach (var n in Il2Each.Of(corner.ClearingNeighbors))
            {
                Put(warriors.Count > 0 ? warriors.Dequeue() : null, "warrior", n);
                Put(webs.Count > 0 ? webs.Dequeue() : null, "web", n);
            }
            // After the moves run: report where every piece really is (proves Move takes the context target).
            Action<Canis.context.Context> check = ctx =>
            {
                if (ctx != null) ctx._target = originalTarget;
                if (setup != null) setup._target = originalTarget;
                int ok = 0;
                foreach (var (piece, what, to) in moved)
                {
                    var at = piece.Parent;
                    if (at != null && at.Pointer == to.Pointer) ok++;
                    else Plugin.L.LogWarning("setup: " + what + " is in " + (at == null ? "nothing" : at.GetIl2CppType().Name) + ", expected clearing " + to.Index);
                }
                Plugin.L.LogInfo("setup done: " + ok + "/" + moved.Count + " pieces on their clearings");
            };
            steps.Add(new Canis.actions.AnonymousAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Canis.context.Context>>(check), p.Match));
            Plugin.L.LogInfo("setup: " + moved.Count + " moves queued (corner " + corner.Index + " + " + (moved.Count - 5) / 2 + " adjacent clearings)");
        }
        catch (Exception e) { Plugin.L.LogError("Spinners setup failed: " + e); }
        return new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(steps.Pointer);
    }
}

// R15 observe step for slice C (Daylight), read-only: how Root itself builds a turn phase, a choice prompt and a draw,
// before Spinners does any of it. Each method has no other Harmony owner in this plugin.
static class TurnTrace
{
    static readonly Dictionary<string, int> seen = new();
    static bool Once(string k, int max = 1) { seen.TryGetValue(k, out var n); seen[k] = n + 1; return n < max; }
    static string Short(object o, int max = 400) { var s = o?.ToString() ?? "null"; return s.Length > max ? s.Substring(0, max) + "…" : s; }
    static string Ent(Canis.entities.Entity e) => e == null ? "null" : e.GetIl2CppType().Name + (e.Parent == null ? "" : " in " + e.Parent.GetIl2CppType().Name + (e.Parent.Parent == null ? "" : " in " + e.Parent.Parent.GetIl2CppType().Name));

    internal static void PatchAll(Harmony h)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var phase = new HarmonyMethod(typeof(TurnTrace).GetMethod(nameof(Phase), flags));
        int n = 0;
        foreach (var type in typeof(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer).Assembly.GetTypes())
        {
            if (type.Namespace != "tuber.canis.entities.PlayableClasses" || !type.Name.EndsWith("Player")) continue;
            foreach (var name in new[] { "GetBirdsongActions", "GetDaylightActions", "GetEveningActions", "GetPossibleDaylightAbilities" })
            {
                if (type.Name == "MarquiseDeCatPlayer" && name == "GetEveningActions") continue;   // owned by Shelter
                var m = type.GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m == null) continue;
                try { h.Patch(m, postfix: phase); n++; } catch (Exception e) { Plugin.L.LogWarning("turn trace " + type.Name + "." + name + ": " + e.Message); }
            }
        }
        Plugin.L.LogInfo("turn trace: " + n + " phase methods");
        void One(System.Reflection.MethodBase m, string name)
        {
            try { h.Patch(m, prefix: new HarmonyMethod(typeof(TurnTrace).GetMethod(name, flags))); Plugin.L.LogInfo("patched: " + m.DeclaringType.Name + "." + m.Name); }
            catch (Exception e) { Plugin.L.LogWarning("trace " + name + ": " + e.Message); }
        }
        One(AccessTools.Method(typeof(Canis.actions.CustomChoiceActions), "execute"), nameof(Choice));
        One(AccessTools.Method(typeof(Canis.actions.DrawCard), "execute"), nameof(Draw));
        One(AccessTools.Method(typeof(tuber.canis.actions.Draw), "execute"), nameof(TuberDraw));
    }

    static void Phase(object __instance, object __result, System.Reflection.MethodBase __originalMethod)
    {
        try
        {
            var who = __instance.GetType().Name;
            if (__originalMethod.Name == "GetBirdsongActions" && !SpinnersSetup.SetupOver) { SpinnersSetup.SetupOver = true; Plugin.L.LogInfo("setup over: KeepNode now answers no keep (no Field Hospitals)"); }
            if (__originalMethod.Name == "GetBirdsongActions" && __instance is Il2CppSystem.Object me && SpinnersPieces.IsSpinners(me)) { Gather.UsedThisTurn = false; SpinnersRecruit.UsedThisTurn = false; Shelter.Reset(); }
            if (!Once(who + "." + __originalMethod.Name, 2)) return;
            var tag = __instance is Il2CppSystem.Object o && SpinnersPieces.IsSpinners(o) ? " [Spinners]" : "";
            string body;
            if (__result is Il2CppSystem.Collections.Generic.IEnumerable<Canis.utils.ids.EntityID> ids)
            {
                var names = new List<string>();
                var match = (__instance as Canis.entities.Entity)?.Match;
                foreach (var id in Il2Each.Of(ids)) { var e = match?.GetEntity(id); names.Add(e == null ? id.ToString() : e.GetIl2CppType().Name); }
                body = string.Join(", ", names);
            }
            else body = Short(__result);
            Plugin.L.LogInfo("turn " + who + tag + "." + __originalMethod.Name + " -> " + (__result?.GetType().Name ?? "null") + ": " + body);
        }
        catch (Exception e) { Plugin.L.LogWarning("turn trace: " + e.Message); }
    }

    static void Choice(Canis.actions.CustomChoiceActions __instance)
    {
        try
        {
            if (!Once("choice", 8)) return;
            var parts = new List<string>();
            var r = __instance.responses;
            if (r != null) for (int i = 0; i < r.Length; i++) parts.Add(r[i] == null ? "null" : r[i].GetIl2CppType().Name + "{" + Short(r[i], 120) + "}");
            var c = __instance.context;
            Plugin.L.LogInfo("choice prompt: " + parts.Count + " options [" + string.Join(" | ", parts) + "] ctx Source=" + Ent(c?.Source) + " Target=" + (c != null && c.HasTarget ? Ent(c.Target) : "none"));
        }
        catch (Exception e) { Plugin.L.LogWarning("choice trace: " + e.Message); }
    }

    // 0.20.0 crash: tuber.canis.actions.Draw reads Context.Target. Log what Root's own draws carry (Evening draws).
    static void TuberDraw(tuber.canis.actions.Draw __instance)
    {
        try
        {
            if (!Once("tuberdraw", 8)) return;
            var c = __instance.context;
            Plugin.L.LogInfo("Draw: ctx Source=" + Ent(c?.Source) + " Target=" + (c != null && c.HasTarget ? Ent(c.Target) : "none") + " target object=" + (c?._target == null ? "null" : c._target.GetIl2CppType().Name));
        }
        catch (Exception e) { Plugin.L.LogWarning("Draw trace: " + e.Message); }
    }

    static void Draw(Canis.actions.DrawCard __instance)
    {
        try
        {
            if (!Once("draw", 6)) return;
            var c = __instance.context;
            Plugin.L.LogInfo("draw: from " + Ent(__instance.from) + " to " + Ent(__instance.to) + " ctx Source=" + Ent(c?.Source) + " Target=" + (c != null && c.HasTarget ? Ent(c.Target) : "none"));
        }
        catch (Exception e) { Plugin.L.LogWarning("draw trace: " + e.Message); }
    }
}

// Slice H1 (Sherlock: "replace the board of the marquis … with the spinner theme plus the explanation and actions to be
// able to read the board"). Step 1: the fan faction board itself, on screen during a match (spider tab or F9; F10 flips
// front/back; click the board to close). Plain Unity UI on its own canvas: no Root object is touched. F11 writes a map of
// Root's HUD (names, components, sprites, texts) to ~/ret-work/root/spinners/hud-map.txt: the observe step for step 2,
// which reskins the Marquise HUD element by element.
public class BoardViewer : UnityEngine.MonoBehaviour
{
    public BoardViewer(IntPtr ptr) : base(ptr) { }
    internal static bool InMatch;
    static UnityEngine.GameObject canvasGo, tab, panel;
    static UnityEngine.UI.Image boardImage;
    static UnityEngine.Sprite front, back;
    static bool showingBack;

    static UnityEngine.Sprite LoadSprite(string file)
    {
        var dir = Plugin.ArtDir();
        var png = dir == null ? null : System.IO.Path.Combine(dir, "raw", file);
        if (png == null || !System.IO.File.Exists(png)) { Plugin.L.LogWarning("board art not found: " + png); return null; }
        var tex = new UnityEngine.Texture2D(2, 2);
        UnityEngine.ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(png));
        tex.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
        var sp = UnityEngine.Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new UnityEngine.Vector2(0.5f, 0.5f));
        sp.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
        Plugin.L.LogInfo("board art " + file + " loaded " + tex.width + "x" + tex.height);
        return sp;
    }

    static UnityEngine.RectTransform Rect(UnityEngine.GameObject go) => go.GetComponent<UnityEngine.RectTransform>() ?? go.AddComponent<UnityEngine.RectTransform>();

    static void Build()
    {
        if (canvasGo != null) return;
        canvasGo = new UnityEngine.GameObject("RetSpinnersBoardCanvas");
        UnityEngine.Object.DontDestroyOnLoad(canvasGo);
        canvasGo.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
        var canvas = canvasGo.AddComponent<UnityEngine.Canvas>();
        canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // The tab: the spider icon at the left edge, a little above the middle.
        tab = new UnityEngine.GameObject("RetSpinnersBoardTab");
        tab.transform.SetParent(canvasGo.transform, false);
        var tr = Rect(tab);
        tr.anchorMin = tr.anchorMax = new UnityEngine.Vector2(0f, 0.62f);
        tr.pivot = new UnityEngine.Vector2(0f, 0.5f);
        tr.anchoredPosition = new UnityEngine.Vector2(8f, 0f);
        tr.sizeDelta = new UnityEngine.Vector2(72f, 72f);
        var ti = tab.AddComponent<UnityEngine.UI.Image>();
        ti.sprite = Plugin.LoadIcon();
        ti.preserveAspect = true;
        var tb = tab.AddComponent<UnityEngine.UI.Button>();
        tb.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(Toggle)));

        // The board: dimmed backdrop, the board image fitted to the screen; clicking anywhere closes it.
        panel = new UnityEngine.GameObject("RetSpinnersBoardPanel");
        panel.transform.SetParent(canvasGo.transform, false);
        var pr = Rect(panel);
        pr.anchorMin = UnityEngine.Vector2.zero; pr.anchorMax = UnityEngine.Vector2.one;
        pr.offsetMin = pr.offsetMax = UnityEngine.Vector2.zero;
        var dim = panel.AddComponent<UnityEngine.UI.Image>();
        dim.color = new UnityEngine.Color(0f, 0f, 0f, 0.75f);
        var pb = panel.AddComponent<UnityEngine.UI.Button>();
        pb.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(Toggle)));
        var boardGo = new UnityEngine.GameObject("RetSpinnersBoardImage");
        boardGo.transform.SetParent(panel.transform, false);
        var br = Rect(boardGo);
        br.anchorMin = new UnityEngine.Vector2(0.02f, 0.02f); br.anchorMax = new UnityEngine.Vector2(0.98f, 0.98f);
        br.offsetMin = br.offsetMax = UnityEngine.Vector2.zero;
        boardImage = boardGo.AddComponent<UnityEngine.UI.Image>();
        boardImage.preserveAspect = true;
        boardImage.raycastTarget = false;
        front = LoadSprite("board.png");
        back = LoadSprite("board_back.png");
        boardImage.sprite = front;
        panel.SetActive(false);
        tab.SetActive(false);
        Plugin.L.LogInfo("Spinners board viewer ready (tab or F9 open/close, F10 flip, F11 HUD map)");
    }

    static void Toggle()
    {
        if (panel == null) return;
        panel.SetActive(!panel.activeSelf);
        Plugin.L.LogInfo("Spinners board " + (panel.activeSelf ? "opened (" + (showingBack ? "back" : "front") + ")" : "closed"));
    }

    static void Flip()
    {
        if (boardImage == null) return;
        showingBack = !showingBack;
        boardImage.sprite = showingBack ? back : front;
        if (!panel.activeSelf) panel.SetActive(true);
    }

    void Update()
    {
        try
        {
            Build();
            // 0.18.1: the tab stayed after the game ended (InMatch was only cleared at faction selection). Twice a second,
            // check that a match screen still exists (Root's PlayerUILoader, which builds the match HUD).
            if (InMatch && UnityEngine.Time.unscaledTime >= nextCheck)
            {
                nextCheck = UnityEngine.Time.unscaledTime + 0.5f;
                if (UnityEngine.Object.FindObjectOfType<tuber.client.match.behaviours.PlayerUILoader>() == null)
                {
                    InMatch = false;
                    Plugin.L.LogInfo("Spinners board: match screen gone, tab hidden");
                }
            }
            bool show = InMatch && SpinnersPieces.Players.Count > 0;
            if (tab.activeSelf != show) tab.SetActive(show);
            if (!show) { if (panel.activeSelf) panel.SetActive(false); return; }
            if (UnityEngine.Time.unscaledTime >= nextSkin)
            {
                nextSkin = UnityEngine.Time.unscaledTime + 1f;
                try { HudSkin.Pass(); } catch (Exception e) { if (skinErrors++ < 3) Plugin.L.LogError("HUD skin: " + e); }
            }
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9)) Toggle();
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10)) Flip();
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F11)) HudMap.Write();
        }
        catch (Exception e) { if (errors++ < 3) Plugin.L.LogError("board viewer: " + e); }
    }
    int errors;
    float nextCheck, nextSkin;
    int skinErrors;
}

// Read-only map of every on-screen UI canvas: object path, active state, components, image sprite, TextMeshPro text.
static class HudMap
{
    internal static void Write()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            int nodes = 0;
            void Walk(UnityEngine.Transform t, string indent, int depth)
            {
                if (nodes > 20000 || depth > 25) return;
                nodes++;
                var go = t.gameObject;
                var comps = new List<string>();
                foreach (var c in go.GetComponents<UnityEngine.Component>()) if (c != null) comps.Add(c.GetIl2CppType().Name);
                var extra = "";
                var img = go.GetComponent<UnityEngine.UI.Image>();
                if (img != null && img.sprite != null) extra += " sprite=" + img.sprite.name;
                var tmp = go.GetComponent<TMPro.TMP_Text>();
                if (tmp != null && !string.IsNullOrEmpty(tmp.text)) extra += " text=\"" + tmp.text.Replace("\n", "\\n") + "\"";
                sb.Append(indent).Append(go.name).Append(go.activeInHierarchy ? "" : " (off)").Append(" [").Append(string.Join(",", comps)).Append("]").Append(extra).Append('\n');
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), indent + "  ", depth + 1);
            }
            var canvases = UnityEngine.Object.FindObjectsOfType<UnityEngine.Canvas>();
            int roots = 0;
            foreach (var c in canvases)
            {
                if (c == null || !c.isRootCanvas || c.gameObject.name.StartsWith("RetSpinners")) continue;
                roots++;
                sb.Append("=== canvas ").Append(c.gameObject.name).Append('\n');
                Walk(c.transform, "", 0);
            }
            var path = System.IO.Path.Combine(Plugin.ArtDir() ?? ".", "hud-map.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            Plugin.L.LogInfo("HUD map: " + roots + " canvases, " + nodes + " objects -> " + path);
        }
        catch (Exception e) { Plugin.L.LogError("HUD map failed: " + e); }
    }
}

// Slice H2: the Marquise HUD wears Spinners art. hud-map.txt (F11, 0.18.1) gave the Marquise sprite names; under the mask
// no real Marquise can share the game, so every image showing one of them belongs to the Spinners player. Once a second
// in a Spinners match, any UI Image (including hidden/pooled ones) showing a mapped sprite gets the Spinners art cut from
// the fan boards by make_hud_art.py (~/ret-work/root/spinners/hud/). Texts naming the Marquise are renamed. Action buttons
// keep their Marquise art and names until each one runs its Spinners action (slices C-E).
static class HudSkin
{
    static readonly Dictionary<string, string> map = new()
    {
        ["S_ill_MarquiseDeCat"] = "portrait",          // player panel avatar
        ["Avatar_MarquiseDeCat"] = "portrait",         // battle overlay avatars
        ["T_Marq_64Pattern"] = "pattern",              // panel and player-list backers
        ["S_icon_Marq_HeadBlank"] = "warrior",         // strategic view warrior
        ["S_icon_Marq_Head"] = "warrior",              // battle warrior icon
        ["S_icon_Drawer_MarquiseWarrior"] = "warrior", // supply drawer
        ["S_icon_Marq_Wood_small"] = "web",            // strategic view wood = webs
        ["S_icon_Recruiter"] = "infirmary_fox",        // recruiters track = fox infirmaries
        ["S_btn_Marq_Overwork"] = "btn_recruit",       // 0.29.0: the Overwork button runs Recruit by day
        ["S_btn_Marq_Recruit"] = "btn_gather",         // 0.29.0: the Recruit button runs Gather
        ["S_btn_Marq_Build"] = "btn_establish",        // the Build button runs Establish (slice C2)
        ["RetSpinners_btn_shelter_swap"] = "btn_shelter", // Evening: the same button runs Shelter (slice D1)
        ["RetSpinners_btn_recruit_swap"] = "btn_recruit",
        ["S_icon_Sawmill"] = "infirmary_mouse",        // sawmills track = mouse infirmaries (name not yet seen)
        ["S_icon_Workshop"] = "infirmary_rabbit",      // workshops track = rabbit infirmaries (name not yet seen)
    };
    static readonly Dictionary<string, UnityEngine.Sprite> art = new();
    static readonly Dictionary<string, int> swapped = new();
    static bool loaded;
    static int passes;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        var dir = Plugin.ArtDir();
        foreach (var name in map.Values.Distinct())
        {
            var png = dir == null ? null : System.IO.Path.Combine(dir, "hud", name + ".png");
            if (png == null || !System.IO.File.Exists(png)) { Plugin.L.LogWarning("HUD art missing: " + png + " (run make_hud_art.py)"); continue; }
            var tex = new UnityEngine.Texture2D(2, 2);
            UnityEngine.ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(png));
            tex.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
            if (name == "pattern") tex.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            var sp = UnityEngine.Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new UnityEngine.Vector2(0.5f, 0.5f), 100f);
            sp.name = "RetSpinners_" + name;
            sp.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
            art[name] = sp;
        }
        Plugin.L.LogInfo("HUD art loaded: " + string.Join(", ", art.Keys));
    }

    internal static void Pass()
    {
        Load();
        int images = 0, texts = 0;
        foreach (var img in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>(true))
        {
            var sp = img?.sprite;
            if (sp == null) continue;
            var n = sp.name;
            if (n == "RetSpinners_btn_recruit" && Shelter.Active) n = "RetSpinners_btn_shelter_swap";
            else if (n == "RetSpinners_btn_shelter" && !Shelter.Active) n = "RetSpinners_btn_recruit_swap";
            if (!map.TryGetValue(n, out var key) || !art.TryGetValue(key, out var mine)) continue;
            img.sprite = mine;
            if (key != "pattern") img.preserveAspect = true;
            images++;
            swapped.TryGetValue(n, out var c); swapped[n] = c + 1;
        }
        foreach (var t in UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>(true))
        {
            var s = t?.text;
            if (string.IsNullOrEmpty(s) || !s.Contains("Marquise")) continue;
            t.text = s.Replace("Marquise de Cat", "Spinners of Mercy").Replace("Marquise", "Spinners");
            texts++;
        }
        if ((images > 0 || texts > 0) && passes++ < 10)
            Plugin.L.LogInfo("HUD skin: " + images + " images, " + texts + " texts this pass; totals " + string.Join(" ", swapped.Select(kv => kv.Key + "=" + kv.Value)));
    }
}

// R15 observe step for slice C, read-only: how the Spinners player's (Marquise-class) abilities really offer targets,
// cost and run, so Gather/Recruit/Establish can imitate a working one. Logs only for abilities owned by the Spinners
// player. Each method has no other Harmony owner in this plugin.
static class AbilityTrace
{
    static readonly Dictionary<string, int> seen = new();
    static bool Once(string k, int max) { seen.TryGetValue(k, out var n); seen[k] = n + 1; return n < max; }
    static bool Mine(object ability)
    {
        var a = (ability as Il2CppSystem.Object)?.TryCast<tuber.canis.abilities.TuberAbilityDefinition>();
        var owner = a?.OwningPlayer;
        return owner != null && SpinnersPieces.IsSpinners(owner);
    }

    internal static void PatchAll(Harmony h)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        HarmonyMethod Post(string n) => new HarmonyMethod(typeof(AbilityTrace).GetMethod(n, flags));
        int n = 0;
        foreach (var type in typeof(tuber.canis.abilities.MarquiseDeCatAbilities.RecruitAbility).Assembly.GetTypes())
        {
            if (type.Namespace != "tuber.canis.abilities.MarquiseDeCatAbilities") continue;
            if (type.Name == "OverworkAbility" || type.Name == "BuildAbility" || type.Name == "RecruitAbility") continue;   // owned by Gather / Establish / Spinners Recruit (one Harmony owner per method)
            foreach (var (name, post) in new[] { ("Targets", "TargetsSeen"), ("Cost", "CostSeen"), ("BeginExecution", "StepSeen"), ("ResumeExecution", "ResumeSeen") })
            {
                var m = type.GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
                if (m == null) continue;
                try { h.Patch(m, postfix: Post(post)); n++; } catch (Exception e) { Plugin.L.LogWarning("ability trace " + type.Name + "." + name + ": " + e.Message); }
            }
        }
        Plugin.L.LogInfo("ability trace: " + n + " Marquise ability methods");
        try { h.Patch(AccessTools.Method(typeof(tuber.canis.actions.ReccuringAbilitySelection), "execute"), prefix: Post(nameof(SelectionSeen))); Plugin.L.LogInfo("patched: ReccuringAbilitySelection.execute"); }
        catch (Exception e) { Plugin.L.LogWarning("selection trace: " + e.Message); }
    }

    static string Describe(Networking.selection.targetinformation.TargetInformation t, Canis.Match m)
    {
        if (t == null) return "null";
        var s = t.GetIl2CppType().Name + "(kind=" + t.GetKind() + " prompt=" + (t.TargetPrompt == null ? "null" : t.TargetPrompt.ToString()) + " override=" + t.OverrideKind;
        var el = t.TryCast<Networking.selection.targetinformation.EntityListTargetInformation>();
        if (el != null)
        {
            var ids = el.ValidTargets;
            var first = ids != null && ids.Length > 0 ? m?.GetEntity(ids[0]) : null;
            s += " valid=" + (ids?.Length ?? 0) + (first == null ? "" : " first=" + first.GetIl2CppType().Name) + " select=" + el.NumberToSelect + " min=" + el.MinimumToSelect + " forced=" + el.Forced;
        }
        var cc = t.TryCast<Networking.selection.targetinformation.CustomChoiceTargetInformation>();
        if (cc != null) s += " choices=" + (cc.Choices?.Length ?? 0);
        return s + ")";
    }

    static void TargetsSeen(object __instance, Canis.Match __0, ref Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation> __result)
    {
        try
        {
            if (!Mine(__instance)) return;
            if (__instance is tuber.canis.abilities.MarquiseDeCatAbilities.MarchAbility && __result != null)
            {
                // 0.32.0 Skitter: only warriors that can leave along a web
                var ml = new Il2CppSystem.Collections.Generic.List<Networking.selection.targetinformation.TargetInformation>(__result);
                if (ml.Count > 0) Skitter.FilterWarriors(ml[0].TryCast<Networking.selection.targetinformation.EntityListTargetInformation>(), __0);
                __result = new Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation>(ml.Pointer);
            }
            if (!Once("targets " + __instance.GetType().Name, 3)) return;
            // Read the sequence once into a list and hand Root that same list, so Root's target code runs once.
            var parts = new List<string>();
            if (__result != null)
            {
                var list = new Il2CppSystem.Collections.Generic.List<Networking.selection.targetinformation.TargetInformation>(__result);
                for (int i = 0; i < list.Count; i++) parts.Add(Describe(list[i], __0));
                __result = new Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation>(list.Pointer);
            }
            Plugin.L.LogInfo("ability " + __instance.GetType().Name + ".Targets -> " + parts.Count + " [" + string.Join(" | ", parts) + "]");
        }
        catch (Exception e) { Plugin.L.LogWarning("targets trace: " + e.Message); }
    }

    // Composite costs listed down to their parts (Establish needs Root's "discard a matching card" cost).
    internal static string CostTree(Canis.actions.CostAction c, int depth = 0)
    {
        if (c == null) return "null";
        var s = c.GetIl2CppType().Name;
        var comp = c.TryCast<Canis.actions.CompositeCostAction>();
        if (comp?.children != null && depth < 4)
        {
            var parts = new List<string>();
            for (int i = 0; i < comp.children.Length; i++) parts.Add(CostTree(comp.children[i], depth + 1));
            s += "{" + string.Join(", ", parts) + "}";
        }
        return s;
    }

    static void CostSeen(object __instance, Canis.actions.CostAction __result)
    {
        try { if (Mine(__instance) && Once("cost " + __instance.GetType().Name, 2)) Plugin.L.LogInfo("ability " + __instance.GetType().Name + ".Cost -> " + CostTree(__result)); }
        catch (Exception e) { Plugin.L.LogWarning("cost trace: " + e.Message); }
    }

    static void StepSeen(object __instance, System.Reflection.MethodBase __originalMethod)
    {
        try { if (Mine(__instance) && Once("step " + __instance.GetType().Name, 3)) Plugin.L.LogInfo("ability " + __instance.GetType().Name + "." + __originalMethod.Name + " ran"); }
        catch (Exception e) { Plugin.L.LogWarning("step trace: " + e.Message); }
    }

    // 0.26.0 Skitter step 1 ("any number of moves"): March's states are confirm 10, second 20, outrage 25, finish 30
    // (0.25.0 trace). A full March logs 10 -> 20 -> 10 -> 30; a skipped second move (empty clearing response) logs
    // 10 -> 20 -> 30. For Spinners, whenever March would finish (30) and a Spinners warrior moved since the last move step
    // began, the state goes back to the second-move step (20) instead: move again, or skip to finish. Movement rules are
    // still Root's (web paths and "ignoring rule" are step 2). AbilityTrace owns March's ResumeExecution (one owner).
    internal static int SpinnersMoves;
    static int movesAtStep = -1;
    static void ResumeSeen(object __instance, ref int __result)
    {
        try
        {
            bool march = __instance is tuber.canis.abilities.MarquiseDeCatAbilities.MarchAbility;
            if (!Mine(__instance)) return;
            if (march)
            {
                int second = tuber.canis.abilities.MarquiseDeCatAbilities.MarchAbility.SecondMarchState;
                int finish = tuber.canis.abilities.MarquiseDeCatAbilities.MarchAbility.FinishMarchState;
                if (__result == finish && movesAtStep >= 0 && SpinnersMoves > movesAtStep)
                {
                    Plugin.L.LogInfo("Skitter: moved (" + (SpinnersMoves - movesAtStep) + " moves this step), another move offered");
                    __result = second;
                }
                else if (__result == finish) { Plugin.L.LogInfo("Skitter: done (no move in the last step)"); movesAtStep = -1; }
                if (__result == second) movesAtStep = SpinnersMoves;
            }
            if (Once("resume " + __instance.GetType().Name, march ? 30 : 6))
                Plugin.L.LogInfo("ability " + __instance.GetType().Name + ".ResumeExecution -> state " + __result);
        }
        catch (Exception e) { Plugin.L.LogWarning("resume trace: " + e.Message); }
    }

    static void SelectionSeen(tuber.canis.actions.ReccuringAbilitySelection __instance)
    {
        try
        {
            var a = __instance.SelectedAbility;
            if (a == null || !Mine(a) || !Once("selection", 12)) return;
            Plugin.L.LogInfo("ability selection: " + a.GetIl2CppType().Name + " step " + __instance.currentStep + " (targeting " + tuber.canis.actions.ReccuringAbilitySelection.TargetingStep + ", cost " + tuber.canis.actions.ReccuringAbilitySelection.CostStep + ", resolution " + tuber.canis.actions.ReccuringAbilitySelection.ResolutionStep + ", done " + tuber.canis.actions.ReccuringAbilitySelection.DoneStep + ")");
        }
        catch (Exception e) { Plugin.L.LogWarning("selection trace: " + e.Message); }
    }
}

// Slice C1: Gather ("Once per turn, draw a card"), re-wired onto the Marquise Overwork ability for the Spinners player
// only. Shapes copied from 0.19.1's trace of Root's own abilities:
// - Targets: Overwork builds its EntityListTargetInformation as usual (account, prompt, flags). Gather keeps that object
//   and replaces its ValidTargets with the clearings holding Spinners pieces (empty once used this turn: button dark,
//   like Overwork with no card) and its prompt.
// - Cost: BoolCost(true), the free cost March uses.
// - BeginExecution: Root's own Draw(hand). ResumeExecution: the ability's own CompletedExecutionState.
// - Undo: nothing to undo (a drawn card is not taken back).
static class Gather
{
    internal static bool UsedThisTurn;
    internal const string PromptKey = "spinners.gather.prompt";

    internal static void PatchAll(Harmony h)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        HarmonyMethod M(string n) => new HarmonyMethod(typeof(Gather).GetMethod(n, flags));
        var t = typeof(tuber.canis.abilities.MarquiseDeCatAbilities.OverworkAbility);
        void One(string method, string patch, bool prefix)
        {
            try
            {
                var m = t.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
                if (prefix) h.Patch(m, prefix: M(patch)); else h.Patch(m, postfix: M(patch));
                Plugin.L.LogInfo("patched: OverworkAbility." + method + " (Gather)");
            }
            catch (Exception e) { Plugin.L.LogError("Gather patch " + method + " failed (Gather inactive): " + e.Message); }
        }
        One("Targets", nameof(Targets), false);
        One("Cost", nameof(Cost), true);
        One("BeginExecution", nameof(Begin), true);
        One("ResumeExecution", nameof(Resume), true);
        One("Undo", nameof(Undo), true);
    }

    static tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer Owner(tuber.canis.abilities.TuberAbilityDefinition a)
    {
        var p = a?.OwningPlayer?.TryCast<tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer>();
        return p != null && SpinnersPieces.IsSpinners(p) ? p : null;
    }

    static int targetLogs;
    static void Targets(tuber.canis.abilities.MarquiseDeCatAbilities.OverworkAbility __instance,
                        ref Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation> __result)
    {
        try
        {
            var p = Owner(__instance);
            if (p == null || __result == null) return;
            var list = new Il2CppSystem.Collections.Generic.List<Networking.selection.targetinformation.TargetInformation>(__result);
            var el = list.Count > 0 ? list[0].TryCast<Networking.selection.targetinformation.EntityListTargetInformation>() : null;
            if (el == null) { Plugin.L.LogWarning("Gather: Overwork offered no entity list (" + list.Count + " targets); left as Root made it"); __result = new Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation>(list.Pointer); return; }
            if (Shelter.Active) { Shelter.Targets(p, el); __result = new Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation>(list.Pointer); return; }
            SpinnersRecruit.Targets(p, el);   // 0.29.0: by day the Overwork button runs Recruit
            __result = new Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation>(list.Pointer);
        }
        catch (Exception e) { Plugin.L.LogError("Gather targets: " + e); }
    }

    static bool Cost(tuber.canis.abilities.MarquiseDeCatAbilities.OverworkAbility __instance, Canis.Match __0, ref Canis.actions.CostAction __result)
    {
        var owner = Owner(__instance);
        if (owner == null) return true;
        if (Shelter.Active) { __result = new tuber.canis.actions.CostActions.BoolCost(__0, Shelter.CanPay(owner)); return false; }
        // 0.20.1: a cost that cannot be paid greys the button (CostAction.CanBePaid). 0.29.0: Recruit by day.
        __result = new tuber.canis.actions.CostActions.BoolCost(__0, SpinnersRecruit.CanPay(owner));
        return false;
    }

    static bool Begin(tuber.canis.abilities.MarquiseDeCatAbilities.OverworkAbility __instance, Canis.Match __1, Canis.context.Context __2,
                      ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action> __result)
    {
        var p = Owner(__instance);
        if (p == null) return true;
        if (Shelter.Active)
        {
            Il2CppSystem.Collections.Generic.List<Canis.actions.Action> sh;
            try { sh = Shelter.Steps(p, __2, __1); } catch (Exception e) { Plugin.L.LogError("Shelter: " + e); sh = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>(); }
            __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(sh.Pointer);
            return false;
        }
        Il2CppSystem.Collections.Generic.List<Canis.actions.Action> rs;
        try { rs = SpinnersRecruit.Steps(p, __2, __1); } catch (Exception e) { Plugin.L.LogError("Recruit: " + e); rs = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>(); }
        __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(rs.Pointer);
        return false;
    }

    // Gather's draw (0.29.0: run from the Recruit button by SpinnersRecruit). 0.20.0: Draw needs the drawing player as
    // its context target, set in place by a wrapping step.
    internal static Il2CppSystem.Collections.Generic.List<Canis.actions.Action> DrawSteps(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Canis.Match match)
    {
        var steps = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>();
        if (UsedThisTurn) { Plugin.L.LogInfo("Gather: already used this turn, no draw"); return steps; }
        try
        {
            Func<Canis.context.Context, Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>> wrap = ctx =>
            {
                if (ctx != null) ctx._target = p;
                var one = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>();
                one.Add(new tuber.canis.actions.Draw(p.Hand, match));
                return new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(one.Pointer);
            };
            steps.Add(new Canis.actions.AnonymousAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Func<Canis.context.Context, Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>>>(wrap), match));
            UsedThisTurn = true;
            Plugin.L.LogInfo("Gather: drawing a card (hand " + p.Hand.Children.Count + " before)");
        }
        catch (Exception e) { Plugin.L.LogError("Gather draw: " + e); }
        return steps;
    }

    static bool Resume(tuber.canis.abilities.MarquiseDeCatAbilities.OverworkAbility __instance, ref int __result)
    {
        if (Owner(__instance) == null) return true;
        __result = tuber.canis.abilities.TuberAbilityStateMachine.CompletedExecutionState;
        Plugin.L.LogInfo((Shelter.Active ? "Shelter" : "Recruit") + ": complete (state " + __result + ")");
        return false;
    }

    static bool Undo(tuber.canis.abilities.MarquiseDeCatAbilities.OverworkAbility __instance, ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action> __result)
    {
        if (Owner(__instance) == null) return true;
        __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>().Pointer);
        return false;
    }
}

// Slice C2: Establish ("Discard a card to place an infirmary in a matching clearing with a web"), re-wired onto the
// Marquise Build ability for the Spinners player only. Build is a plain ability (Targets / Cost / Action).
// - Targets: Root's own list object, ValidTargets = clearings with a Spinners web, an open building slot, an infirmary of
//   that suit left on its track (Sawmills = mouse, Workshops = rabbit, Recruiters = fox) and a card of that suit or a bird
//   in hand. OverrideKind cleared so the client does not open the Marquise building picker.
// - Cost: BoolCost (Build's own cost type), payable while any clearing is valid.
// - Action: Root's DiscardCost(card), then Root's Move(infirmary -> clearing), each in a step whose context target is set
//   in place first (the rule from setup's Move and Gather's Draw). Card: first card of the clearing's suit, else a bird.
static class Establish
{
    internal const string PromptKey = "spinners.establish.prompt";

    internal static void PatchAll(Harmony h)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        HarmonyMethod M(string n) => new HarmonyMethod(typeof(Establish).GetMethod(n, flags));
        var t = typeof(tuber.canis.abilities.MarquiseDeCatAbilities.BuildAbility);
        void One(string method, string patch, bool prefix)
        {
            try
            {
                var m = t.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
                if (prefix) h.Patch(m, prefix: M(patch)); else h.Patch(m, postfix: M(patch));
                Plugin.L.LogInfo("patched: BuildAbility." + method + " (Establish)");
            }
            catch (Exception e) { Plugin.L.LogError("Establish patch " + method + " failed (Establish inactive): " + e.Message); }
        }
        One("Targets", nameof(Targets), false);
        One("Cost", nameof(Cost), true);
        One("Action", nameof(Act), true);
        One("Undo", nameof(Undo), true);
    }

    static tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer Owner(tuber.canis.abilities.TuberAbilityDefinition a)
    {
        var p = a?.OwningPlayer?.TryCast<tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer>();
        return p != null && SpinnersPieces.IsSpinners(p) ? p : null;
    }

    internal static Canis.entities.Entity Track(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Suits suit) =>
        suit == Suits.Mouse ? p.Sawmills : suit == Suits.Rabbit ? p.Workshops : suit == Suits.Fox ? p.Recruiters : null;

    static Canis.entities.Entity Infirmary(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Suits suit) =>
        SpinnersSetup.Kids(Track(p, suit)).FirstOrDefault();

    internal static Canis.entities.Entity Card(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Suits suit)
    {
        Canis.entities.Entity bird = null;
        foreach (var c in SpinnersSetup.Kids(p.Hand))
        {
            var te = c.TryCast<tuber.canis.entities.TuberEntity>();
            if (te == null) continue;
            if (te.Suit == suit) return c;
            if (te.Suit == Suits.Bird && bird == null) bird = c;
        }
        return bird;
    }

    internal static bool HasWeb(tuber.canis.entities.TuberBoardNode n)
    {
        foreach (var c in SpinnersSetup.Kids(n))
        {
            var te = c.TryCast<tuber.canis.entities.TuberEntity>();
            if (te != null && te.PieceType == PieceTypes.Token && te.TokenType == TokenTypes.Wood && te.Faction == SpinnersHost.Host) return true;
        }
        return false;
    }

    static bool Valid(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, tuber.canis.entities.TuberBoardNode n) =>
        n.OpenBuildSlots > 0 && HasWeb(n) && Infirmary(p, n.Suit) != null && Card(p, n.Suit) != null;

    static List<tuber.canis.entities.TuberBoardNode> ValidClearings(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p) =>
        SpinnersSetup.Clearings(p).Where(n => Valid(p, n)).ToList();

    // 0.21.0: at Action time the context target is a TargetResponseQueue (the player's clicks), not the clearing, and
    // GetTargetEntity/GetFirstSelection gave nothing: "no clearing selected", the turn went back to "choose an option".
    // Peek the first EntityListTargetResponse (not dequeued, so nothing else in Root loses it) and look up its entity.
    internal static Canis.entities.Entity Clicked(Canis.context.Context c, Canis.Match m)
    {
        if (c == null) return null;
        try
        {
            var q = c._target?.TryCast<Canis.context.targetpickers.TargetResponseQueue>()?.queue;
            if (q != null && q.Count > 0)
            {
                var r = q.Peek()?.TryCast<Networking.selection.targetresponse.EntityListTargetResponse>();
                if (r?.EntityList != null && r.EntityList.Length > 0) { var e = m.GetEntity(r.EntityList[0]); if (e != null) return e; }
            }
        }
        catch (Exception e) { Plugin.L.LogWarning("reading the clicked target: " + e.Message); }
        try { return c.GetTargetFromResponseQueue(); } catch { return null; }
    }

    static tuber.canis.entities.TuberBoardNode Selected(Canis.context.Context c, Canis.Match m) =>
        Clicked(c, m)?.TryCast<tuber.canis.entities.TuberBoardNode>();

    static int targetLogs;
    static void Targets(tuber.canis.abilities.MarquiseDeCatAbilities.BuildAbility __instance,
                        ref Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation> __result)
    {
        try
        {
            var p = Owner(__instance);
            if (p == null || __result == null) return;
            var list = new Il2CppSystem.Collections.Generic.List<Networking.selection.targetinformation.TargetInformation>(__result);
            var el = list.Count > 0 ? list[0].TryCast<Networking.selection.targetinformation.EntityListTargetInformation>() : null;
            if (el == null) { Plugin.L.LogWarning("Establish: Build offered no entity list; left as Root made it"); __result = new Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation>(list.Pointer); return; }
            var valid = ValidClearings(p);
            el.ValidTargets = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Canis.utils.ids.EntityID>(valid.Select(n => n.EntityID).ToArray());
            el.TargetPrompt = new Canis.utils.localization.LocalizableTextVariables(PromptKey);
            el.OverrideKind = "";
            __result = new Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation>(list.Pointer);
            if (targetLogs++ < 6) Plugin.L.LogInfo("Establish targets: " + valid.Count + " clearings [" + string.Join(" ", valid.Select(n => n.Index + ":" + n.Suit)) + "]");
        }
        catch (Exception e) { Plugin.L.LogError("Establish targets: " + e); }
    }

    static bool Cost(tuber.canis.abilities.MarquiseDeCatAbilities.BuildAbility __instance, Canis.Match __0, ref Canis.actions.CostAction __result)
    {
        var p = Owner(__instance);
        if (p == null) return true;
        bool any = false;
        try { any = ValidClearings(p).Count > 0; } catch (Exception e) { Plugin.L.LogError("Establish cost: " + e); }
        __result = new tuber.canis.actions.CostActions.BoolCost(__0, any);
        return false;
    }

    internal static Canis.actions.Action WithTarget(Canis.Match m, Canis.entities.Entity target, Func<Canis.actions.Action> make)
    {
        Func<Canis.context.Context, Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>> wrap = ctx =>
        {
            if (ctx != null) ctx._target = target;
            var one = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>();
            one.Add(make());
            return new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(one.Pointer);
        };
        return new Canis.actions.AnonymousAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Func<Canis.context.Context, Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>>>(wrap), m);
    }

    static bool Act(tuber.canis.abilities.MarquiseDeCatAbilities.BuildAbility __instance, Canis.Match __0, Canis.context.Context __1,
                    ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action> __result)
    {
        var p = Owner(__instance);
        if (p == null) return true;
        var steps = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>();
        try
        {
            var node = Selected(__1, __0);
            if (node == null) { Plugin.L.LogError("Establish: no clearing selected (target " + (__1?._target == null ? "null" : __1._target.GetIl2CppType().Name) + ")"); }
            else
            {
                var card = Card(p, node.Suit);
                var inf = Infirmary(p, node.Suit);
                if (card == null || inf == null) Plugin.L.LogError("Establish: clearing " + node.Index + " no longer valid (card " + (card != null) + ", infirmary " + (inf != null) + ")");
                else
                {
                    var m = __0;
                    var cardSuit = card.TryCast<tuber.canis.entities.TuberEntity>()?.Suit;
                    steps.Add(WithTarget(m, card, () => new tuber.canis.actions.CostActions.DiscardCost(m, card)));
                    steps.Add(WithTarget(m, inf, () => new Canis.actions.Move(node, m, node.Children.Count, false, false)));
                    Plugin.L.LogInfo("Establish: " + node.Suit + " infirmary to clearing " + node.Index + ", discarding a " + cardSuit + " card");
                }
            }
        }
        catch (Exception e) { Plugin.L.LogError("Establish: " + e); }
        __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(steps.Pointer);
        return false;
    }

    static bool Undo(tuber.canis.abilities.MarquiseDeCatAbilities.BuildAbility __instance, ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action> __result)
    {
        if (Owner(__instance) == null) return true;
        __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>().Pointer);
        return false;
    }
}

// Slice C3: Spinners Recruit ("Once per turn, discard a card to place a warrior and web in each matching clearing").
// 0.27.0-0.28.1 history: the client confirms Marquise's Recruit button at once with an EMPTY selection (Marquise
// recruits at every recruiter, nothing to pick), ChooseSuitAction showed no prompt, and an automatic suit took the
// choice away from the player (Sherlock: "if it does something for me ruins ones own strategy"). 0.29.0 swaps buttons
// to fit what the client sends: the Overwork button (clicks reach the ability; Gather/Shelter proved it) runs Recruit,
// and the Recruit button (no click) runs Gather, which needs none. This class owns RecruitAbility = Gather.
// Recruit itself: click any clearing; its suit is recruited. A bird counts as that clicked suit only (balance); short
// supply places what is left; nothing is discarded unless pieces are placed.
static class SpinnersRecruit
{
    internal static bool UsedThisTurn;
    internal const string PromptKey = "spinners.recruit.prompt";

    internal static void PatchAll(Harmony h)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        HarmonyMethod M(string n) => new HarmonyMethod(typeof(SpinnersRecruit).GetMethod(n, flags));
        var t = typeof(tuber.canis.abilities.MarquiseDeCatAbilities.RecruitAbility);
        void One(string method, string patch, bool prefix)
        {
            try
            {
                var m = t.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
                if (prefix) h.Patch(m, prefix: M(patch)); else h.Patch(m, postfix: M(patch));
                Plugin.L.LogInfo("patched: RecruitAbility." + method + " (Gather)");
            }
            catch (Exception e) { Plugin.L.LogError("Recruit-button patch " + method + " failed (Gather inactive): " + e.Message); }
        }
        One("Targets", nameof(GatherTargets), false);
        One("Cost", nameof(GatherCost), true);
        One("BeginExecution", nameof(GatherBegin), true);
        One("ResumeExecution", nameof(GatherResume), true);
        One("Undo", nameof(Undo), true);
    }

    static tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer Owner(tuber.canis.abilities.TuberAbilityDefinition a)
    {
        var p = a?.OwningPlayer?.TryCast<tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer>();
        return p != null && SpinnersPieces.IsSpinners(p) ? p : null;
    }

    // ---- Recruit (run from the Overwork button by Gather's patches) ----
    // 0.29.3: pairs need both (0.29.2 run: "supply 13/0", the click did nothing instead of the button greying out).
    static bool Supply(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p) =>
        SpinnersSetup.Kids(p.WarriorArea).Any() && SpinnersSetup.Kids(p.TokensArea).Any(SpinnersPieces.Usable);

    // 0.29.1 (Sherlock: the web is "connected like a train track", also for Recruit): a clearing can be recruited into
    // only if it already holds a Spinners web or is adjacent to one (the track grows from its ends; no hopping across
    // the map). Computed before placing, so one Recruit does not chain through the webs it places.
    internal static List<tuber.canis.entities.TuberBoardNode> Connected(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p)
    {
        var all = SpinnersSetup.Clearings(p);
        var webbed = all.Where(Establish.HasWeb).ToList();
        var ptrs = new HashSet<IntPtr>(webbed.Select(n => n.Pointer));
        // Sherlock: the neighbouring web must be "travelable and not just near": only clearings joined by a path you
        // can travel (Root's ClearingNeighbors), minus paths Root marks blocked (e.g. closed Mountain-map paths).
        foreach (var n in webbed)
        {
            var blocked = new HashSet<IntPtr>(Il2Each.Of(n.BlockedNeighbors).Select(b => b.Pointer));
            foreach (var nb in Il2Each.Of(n.ClearingNeighbors))
                if (!blocked.Contains(nb.Pointer)) ptrs.Add(nb.Pointer);
        }
        return all.Where(n => ptrs.Contains(n.Pointer)).ToList();
    }

    static List<tuber.canis.entities.TuberBoardNode> ValidClearings(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p) =>
        UsedThisTurn || !Supply(p) ? new List<tuber.canis.entities.TuberBoardNode>()
            : Connected(p).Where(n => Establish.Card(p, n.Suit) != null).ToList();

    static int targetLogs;
    internal static void Targets(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Networking.selection.targetinformation.EntityListTargetInformation el)
    {
        var valid = ValidClearings(p);
        el.ValidTargets = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Canis.utils.ids.EntityID>(valid.Select(n => n.EntityID).ToArray());
        el.TargetPrompt = new Canis.utils.localization.LocalizableTextVariables(PromptKey);
        if (targetLogs++ < 6) Plugin.L.LogInfo("Recruit targets: " + valid.Count + " clearings" + (UsedThisTurn ? " (already used this turn)" : ""));
    }

    internal static bool CanPay(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p) => ValidClearings(p).Count > 0;

    internal static Il2CppSystem.Collections.Generic.List<Canis.actions.Action> Steps(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Canis.context.Context ctx, Canis.Match m)
    {
        var steps = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>();
        var node = Establish.Clicked(ctx, m)?.TryCast<tuber.canis.entities.TuberBoardNode>();
        var suit = node?.Suit ?? Suits.Invalid;
        var card = node == null || UsedThisTurn ? null : Establish.Card(p, suit);
        var warriors = new Queue<Canis.entities.Entity>(SpinnersSetup.Kids(p.WarriorArea));
        // 0.30.2 safe mode: the 0.30.1 match ended on Spinners' turn 2 with a NullReferenceException inside Root's
        // Move during Recruit, the first turn after the 16 made webs could be reached. Until the traces show the made
        // webs are safe to move, Recruit places only Root's own 8 wood.
        var webs = new Queue<Canis.entities.Entity>(SpinnersSetup.Kids(p.TokensArea).Where(SpinnersPieces.Usable));
        var targets = node == null ? new List<tuber.canis.entities.TuberBoardNode>() : Connected(p).Where(c => c.Suit == suit).OrderBy(c => c.Index).ToList();
        if (card == null || targets.Count == 0 || warriors.Count == 0 || webs.Count == 0)
        {
            Plugin.L.LogInfo("Recruit: nothing to do (clearing " + (node?.Index.ToString() ?? "none") + ", suit " + suit + ", card " + (card != null) + ", used " + UsedThisTurn + ", supply " + warriors.Count + "/" + webs.Count + "); no card discarded");
            return steps;
        }
        steps.Add(Establish.WithTarget(m, card, () => new tuber.canis.actions.CostActions.DiscardCost(m, card)));
        int w = 0, wb = 0;
        foreach (var n in targets)
        {
            var dest = n;
            // 0.29.1: a warrior and a web always go together (0.29.0 left a lone warrior when the webs ran out).
            if (warriors.Count == 0 || webs.Count == 0) break;
            var wp = warriors.Dequeue(); var bp = webs.Dequeue();
            steps.Add(Establish.WithTarget(m, wp, () => new Canis.actions.Move(dest, m, dest.Children.Count, false, false))); w++;
            steps.Add(Establish.WithTarget(m, bp, () => new Canis.actions.Move(dest, m, dest.Children.Count, false, false))); wb++;
        }
        UsedThisTurn = true;
        Plugin.L.LogInfo("Recruit: " + suit + " (clicked clearing " + node.Index + ", discarding a " + card.TryCast<tuber.canis.entities.TuberEntity>()?.Suit + " card): " + w + " warriors, " + wb + " webs into clearings "
                         + string.Join(",", targets.Select(n => n.Index)) + (warriors.Count == 0 || webs.Count == 0 ? " (supply short or now empty)" : ""));
        return steps;
    }

    // ---- Gather on the Recruit button (no click needed; the client sends none) ----
    static void GatherTargets(tuber.canis.abilities.MarquiseDeCatAbilities.RecruitAbility __instance,
                              ref Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation> __result)
    {
        try
        {
            var p = Owner(__instance);
            if (p == null || __result == null) return;
            var list = new Il2CppSystem.Collections.Generic.List<Networking.selection.targetinformation.TargetInformation>(__result);
            var el = list.Count > 0 ? list[0].TryCast<Networking.selection.targetinformation.EntityListTargetInformation>() : null;
            if (el != null)
            {
                var ids = Gather.UsedThisTurn ? new List<Canis.utils.ids.EntityID>()
                    : SpinnersSetup.Clearings(p).Where(n => n.GetPlayersInClearing().Contains(p)).Select(n => n.EntityID).ToList();
                el.ValidTargets = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Canis.utils.ids.EntityID>(ids.ToArray());
                el.TargetPrompt = new Canis.utils.localization.LocalizableTextVariables(Gather.PromptKey);
                el.OverrideKind = "";
            }
            __result = new Il2CppSystem.Collections.Generic.IEnumerable<Networking.selection.targetinformation.TargetInformation>(list.Pointer);
        }
        catch (Exception e) { Plugin.L.LogError("Gather targets: " + e); }
    }

    static bool GatherCost(tuber.canis.abilities.MarquiseDeCatAbilities.RecruitAbility __instance, Canis.Match __0, ref Canis.actions.CostAction __result)
    {
        if (Owner(__instance) == null) return true;
        __result = new tuber.canis.actions.CostActions.BoolCost(__0, !Gather.UsedThisTurn);
        return false;
    }

    static bool GatherBegin(tuber.canis.abilities.MarquiseDeCatAbilities.RecruitAbility __instance, Canis.Match __1,
                            ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action> __result)
    {
        var p = Owner(__instance);
        if (p == null) return true;
        __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(Gather.DrawSteps(p, __1).Pointer);
        return false;
    }

    static bool GatherResume(tuber.canis.abilities.MarquiseDeCatAbilities.RecruitAbility __instance, ref int __result)
    {
        if (Owner(__instance) == null) return true;
        __result = tuber.canis.abilities.TuberAbilityStateMachine.CompletedExecutionState;
        return false;
    }

    static bool Undo(tuber.canis.abilities.MarquiseDeCatAbilities.RecruitAbility __instance, ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action> __result)
    {
        if (Owner(__instance) == null) return true;
        __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>().Pointer);
        return false;
    }
}

// 0.29.3: Marquise's Birdsong placed wood at every sawmill each turn; Spinners' webs ARE that wood and its mouse
// infirmaries ARE sawmills, so the webs drained into mouse clearings ("eating up my webs"). 0.29.3 skipped the whole
// MarquiseDeCatBirdsongAction: the match ended ("Possible infinite loop … PlayerTurnPhase"): that action also advances
// the turn to Daylight, so the turn restarted forever. 0.30.1: the action runs as Root wrote it; only its wood moves
// are dropped (Move.execute prefix in SpinnersSetup.MoveTrace skips a Spinners wood move to a clearing while InBirdsong).
// Spinners and Marquise never share a game, so every MarquiseDeCatBirdsongAction belongs to Spinners.
static class SpinnersBirdsong
{
    internal static bool InBirdsong;

    internal static void PatchAll(Harmony h)
    {
        try
        {
            h.Patch(AccessTools.Method(typeof(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatBirdsongAction), "execute"),
                    prefix: new HarmonyMethod(typeof(SpinnersBirdsong).GetMethod(nameof(Start), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            Plugin.L.LogInfo("patched: MarquiseDeCatBirdsongAction.execute (Spinners: wood moves dropped)");
        }
        catch (Exception e) { Plugin.L.LogError("Birdsong patch failed (Marquise wood still placed): " + e.Message); }
        // 0.31.1: the dropped wood still showed Marquise's "choose a clearing for wood" prompt (Sherlock: "the maquis wood
        // animation … where i had to click a clearing"). That prompt is the PlaceBirdsongWoodAtTarget sub-action; it is
        // skipped for Spinners (a sub-step, not the phase action, so the turn still advances).
        try
        {
            h.Patch(AccessTools.Method(typeof(tuber.canis.actions.MarquiseDeCatActions.PlaceBirdsongWoodAtTarget), "execute"),
                    prefix: new HarmonyMethod(typeof(SpinnersBirdsong).GetMethod(nameof(NoPrompt), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            Plugin.L.LogInfo("patched: PlaceBirdsongWoodAtTarget.execute (no wood prompt for Spinners)");
        }
        catch (Exception e) { Plugin.L.LogError("Birdsong prompt patch failed (prompt stays): " + e.Message); }
    }

    static int promptLogs;
    static bool NoPrompt(ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action> __result)
    {
        if (SpinnersPieces.Players.Count == 0) return true;
        if (promptLogs++ < 5) Plugin.L.LogInfo("Birdsong: Marquise wood prompt skipped for Spinners");
        __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.actions.Action>(new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>().Pointer);
        return false;
    }

    static int logs;
    static void Start()
    {
        if (SpinnersPieces.Players.Count == 0) return;
        if (!InBirdsong && logs++ < 5) Plugin.L.LogInfo("Birdsong: Marquise wood will be dropped for Spinners");
        InBirdsong = true;
    }
}

// 0.31.0 Evening draw (fan board: "Only Draw one card per [card icon] showing"; the card icon is the middle space of
// each infirmary track, showing once that space is empty, i.e. 2+ infirmaries of the suit are off the track). Marquise's
// Evening draws 1 + AdditionalDraws(); for Spinners AdditionalDraws answers (icons - 1), so the total is the icon
// count (0 with no icon). 0.30.5 full match: Spinners won in 8 turns with 30 VP from 15 Shelters on Marquise's draw.
static class SpinnersDraw
{
    internal static int Icons(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p) =>
        new[] { Suits.Mouse, Suits.Rabbit, Suits.Fox }.Count(su => SpinnersPieces.InfirmariesPerSuit - SpinnersSetup.Kids(Establish.Track(p, su)).Count() >= 2);

    internal static void PatchAll(Harmony h)
    {
        try
        {
            h.Patch(AccessTools.Method(typeof(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer), "AdditionalDraws"),
                    prefix: new HarmonyMethod(typeof(SpinnersDraw).GetMethod(nameof(Extra), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            Plugin.L.LogInfo("patched: MarquiseDeCatPlayer.AdditionalDraws (Spinners Evening draw = card icons showing)");
        }
        catch (Exception e) { Plugin.L.LogError("Evening draw patch failed (Marquise draw stays): " + e.Message); }
    }

    static int logs;
    static bool Extra(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer __instance, ref int __result)
    {
        if (!SpinnersPieces.IsSpinners(__instance)) return true;
        int icons = 0;
        try { icons = Icons(__instance); } catch (Exception e) { Plugin.L.LogError("Evening draw: " + e); }
        __result = icons - 1;
        if (logs++ < 12) Plugin.L.LogInfo("Evening draw: " + icons + " card icons showing -> draw " + icons + " (AdditionalDraws " + __result + ")");
        return false;
    }
}

// 0.32.0 Skitter step 2 (fan board: "Take any number of moves between clearings connected by web, ignoring rule";
// Sherlock 2026-10-19: "connected like a train track", no hopping over an empty clearing). Each move goes from a
// clearing with a Spinners web to a neighbouring clearing (open path, not blocked) that also has one; rule is ignored.
// Step 1 (0.26.0) already repeats March's move step, so a warrior can ride the track as far as it reaches.
// Root asks the player entity for destinations (TuberPlayerEntity.GetNodesToMoveTo / GetBoardNodesToMoveTo(from, n));
// for Spinners those answers are replaced. March's warrior choice keeps only warriors that can leave along a web.
static class Skitter
{
    internal static List<tuber.canis.entities.TuberBoardNode> Dests(tuber.canis.entities.TuberBoardNode from)
    {
        var r = new List<tuber.canis.entities.TuberBoardNode>();
        if (from == null || !Establish.HasWeb(from)) return r;
        var blocked = new HashSet<IntPtr>(Il2Each.Of(from.BlockedNeighbors).Select(b => b.Pointer));
        foreach (var nb in Il2Each.Of(from.ClearingNeighbors))
            if (!blocked.Contains(nb.Pointer) && Establish.HasWeb(nb)) r.Add(nb);
        return r;
    }

    internal static void PatchAll(Harmony h)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var t = typeof(tuber.canis.entities.TuberPlayerEntity);
        foreach (var (name, patch) in new[] { ("GetNodesToMoveTo", nameof(Ids)), ("GetBoardNodesToMoveTo", nameof(Nodes)) })
        {
            try
            {
                h.Patch(AccessTools.Method(t, name), postfix: new HarmonyMethod(typeof(Skitter).GetMethod(patch, flags)));
                Plugin.L.LogInfo("patched: TuberPlayerEntity." + name + " (Skitter web paths)");
            }
            catch (Exception e) { Plugin.L.LogError("Skitter patch " + name + " failed (Root's movement stays): " + e.Message); }
        }
    }

    static int logs;
    static void Log(string what, tuber.canis.entities.TuberBoardNode from, int before, List<tuber.canis.entities.TuberBoardNode> now)
    {
        if (logs++ < 12) Plugin.L.LogInfo("Skitter " + what + ": from clearing " + from?.Index + (from != null && Establish.HasWeb(from) ? " (web)" : " (no web)") + ", Root offered " + before
                                          + ", web track offers " + now.Count + " [" + string.Join(",", now.Select(n => n.Index)) + "]");
    }

    static void Ids(tuber.canis.entities.TuberPlayerEntity __instance, tuber.canis.entities.TuberBoardNode __0, ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.utils.ids.EntityID> __result)
    {
        try
        {
            if (!SpinnersPieces.IsSpinners(__instance)) return;
            int before = __result == null ? 0 : new Il2CppSystem.Collections.Generic.List<Canis.utils.ids.EntityID>(__result).Count;
            var d = Dests(__0);
            var list = new Il2CppSystem.Collections.Generic.List<Canis.utils.ids.EntityID>();
            foreach (var n in d) list.Add(n.EntityID);
            __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.utils.ids.EntityID>(list.Pointer);
            Log("ids", __0, before, d);
        }
        catch (Exception e) { Plugin.L.LogError("Skitter ids: " + e); }
    }

    static void Nodes(tuber.canis.entities.TuberPlayerEntity __instance, tuber.canis.entities.TuberBoardNode __0, ref Il2CppSystem.Collections.Generic.IEnumerable<tuber.canis.entities.TuberBoardNode> __result)
    {
        try
        {
            if (!SpinnersPieces.IsSpinners(__instance)) return;
            int before = __result == null ? 0 : new Il2CppSystem.Collections.Generic.List<tuber.canis.entities.TuberBoardNode>(__result).Count;
            var d = Dests(__0);
            var list = new Il2CppSystem.Collections.Generic.List<tuber.canis.entities.TuberBoardNode>();
            foreach (var n in d) list.Add(n);
            __result = new Il2CppSystem.Collections.Generic.IEnumerable<tuber.canis.entities.TuberBoardNode>(list.Pointer);
            Log("nodes", __0, before, d);
        }
        catch (Exception e) { Plugin.L.LogError("Skitter nodes: " + e); }
    }

    // March's first target list = warriors to move; keep those standing on a web with a webbed neighbour.
    internal static void FilterWarriors(Networking.selection.targetinformation.EntityListTargetInformation el, Canis.Match m)
    {
        if (el?.ValidTargets == null) return;
        var keep = new List<Canis.utils.ids.EntityID>();
        int before = el.ValidTargets.Length;
        foreach (var id in el.ValidTargets)
        {
            var e = m.GetEntity(id);
            var node = e?.Parent?.TryCast<tuber.canis.entities.TuberBoardNode>();
            if (node == null) { keep.Add(id); continue; }   // not a piece on a clearing: leave Root's choice alone
            if (Dests(node).Count > 0) keep.Add(id);
        }
        el.ValidTargets = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Canis.utils.ids.EntityID>(keep.ToArray());
        if (logs++ < 12) Plugin.L.LogInfo("Skitter warriors: Root offered " + before + ", on the web track " + keep.Count);
    }
}

// Slice D1: Shelter (Evening, "up to once per infirmary on the map: discard a card matching the infirmary to score the VP
// of the rightmost empty, matching Infirmary space"). Marquise has no Evening choices, so Spinners' Evening becomes
// [Shelter on, MarquiseDeCatChoicePhase offering only the Overwork ability, Shelter off, Marquise's own Evening (draw,
// discard to 5)]. While Shelter is on, the Overwork ability (Gather by day) runs Shelter, and its button shows the VP
// marker. Score = Root's GainVP with the scoring player as context target (as Draw), traced to confirm.
static class Shelter
{
    internal static bool Active;
    static readonly HashSet<IntPtr> sheltered = new();
    internal const string PromptKey = "spinners.shelter.prompt";
    static bool openedThisTurn, stopped;
    internal static void Reset() { sheltered.Clear(); Active = false; openedThisTurn = false; stopped = false; websRemoved = 0; }

    // 0.25.0: Shelter runs inside Root's real Daylight loop, so the loop starting no longer switches it off (Birdsong
    // resets it, Marquise's Evening action ends it). Kept as a read-only trace.
    static void LoopStart(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatDaylightAction __instance)
    {
        SpinnersBirdsong.InBirdsong = false;
        if (Active) Plugin.L.LogInfo("Shelter: Daylight loop started while Shelter on");
    }

    // Read-only: what comes back to the loop and the choice phase (to see how a pass looks).
    static int returnLogs;
    static void LoopReturn(object __instance, Canis.actions.Action __0)
    {
        try
        {
            if (returnLogs >= 40) return;
            var cp = __0?.TryCast<tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatChoicePhase>();
            if (cp != null && (cp.player == null || !SpinnersPieces.IsSpinners(cp.player))) return;
            returnLogs++;
            Plugin.L.LogInfo("return to " + __instance.GetType().Name + (Active ? " [Shelter]" : "") + ": " + (__0 == null ? "null" : __0.GetIl2CppType().Name) + (cp != null ? " Passed=" + cp.Passed : ""));
        }
        catch (Exception e) { Plugin.L.LogWarning("return trace: " + e.Message); }
    }

    static string Piece(Canis.entities.Entity e)
    {
        if (e == null) return "null";
        var te = e.TryCast<tuber.canis.entities.TuberEntity>();
        var kind = te == null ? "" : " " + te.PieceType + (te.PieceType == PieceTypes.Building ? "/" + te.BuildingType : te.PieceType == PieceTypes.Token ? "/" + te.TokenType : "");
        return e.GetIl2CppType().Name + kind + (e.Parent == null ? " (no parent)" : " in " + e.Parent.GetIl2CppType().Name);
    }
    static int removeLogs;
    // 0.30.0 Webs rule ("Each web token rewards one less point when removed"): a removed web scores its remover 0.
    // Root asks GetRemoveDestination for each removed Spinners piece, then pays the remover with GainVP(cause
    // "removedPieces"); webs counted here are taken off that payment (GainVP prefix below).
    internal static int websRemoved;
    static void RemoveSeen(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer __instance, Canis.entities.Entity __0, Canis.entities.Entity __result)
    {
        try
        {
            if (!SpinnersPieces.IsSpinners(__instance)) return;
            var te = __0?.TryCast<tuber.canis.entities.TuberEntity>();
            if (te != null && te.PieceType == PieceTypes.Token && te.TokenType == TokenTypes.Wood) websRemoved++;
            if (removeLogs++ < 30) Plugin.L.LogInfo("removed: " + Piece(__0) + " -> " + Piece(__result) + (websRemoved > 0 ? " (webs pending -1 VP: " + websRemoved + ")" : ""));
        }
        catch (Exception e) { Plugin.L.LogWarning("remove trace: " + e.Message); }
    }
    static void HomeSeen(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer __instance, Canis.entities.Entity __0, Canis.entities.Entity __result)
    {
        try { if (SpinnersPieces.IsSpinners(__instance) && removeLogs++ < 30) Plugin.L.LogInfo("building home: " + Piece(__0) + " -> " + Piece(__result)); }
        catch (Exception e) { Plugin.L.LogWarning("home trace: " + e.Message); }
    }

    static void EveningEnd()
    {
        if (!Active) return;
        Active = false;
        Plugin.L.LogInfo("Shelter: Evening choices over (Marquise Evening: draw, discard)");
    }

    internal static void PatchAll(Harmony h)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        HarmonyMethod M(string n) => new HarmonyMethod(typeof(Shelter).GetMethod(n, flags));
        void One(System.Reflection.MethodBase m, string patch, bool prefix)
        {
            try
            {
                if (prefix) h.Patch(m, prefix: M(patch)); else h.Patch(m, postfix: M(patch));
                Plugin.L.LogInfo("patched: " + m.DeclaringType.Name + "." + m.Name + " (Shelter)");
            }
            catch (Exception e) { Plugin.L.LogError("Shelter patch " + patch + " failed (Shelter inactive): " + e.Message); }
        }
        One(AccessTools.Method(typeof(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer), "GetEveningActions"), nameof(Evening), false);
        One(AccessTools.Method(typeof(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatChoicePhase), "GetAbilities"), nameof(Abilities), true);
        One(AccessTools.Method(typeof(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatChoicePhase), "execute"), nameof(ChoiceStart), true);
        One(AccessTools.Method(typeof(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatEveningAction), "execute"), nameof(EveningEnd), true);
        One(AccessTools.Method(typeof(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatDaylightAction), "execute"), nameof(LoopStart), true);
        // Read-only (0.23.0 freeze after a Vagabond battle: client KeyNotFoundException in TuberVoodooGroupCommand):
        // where Root sends each removed Spinners piece.
        try
        {
            var mt = typeof(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer);
            h.Patch(AccessTools.Method(mt, "GetRemoveDestination"), postfix: M(nameof(RemoveSeen)));
            h.Patch(AccessTools.Method(mt, "GetBuildingHome"), postfix: M(nameof(HomeSeen)));
            Plugin.L.LogInfo("patched: MarquiseDeCatPlayer.GetRemoveDestination / GetBuildingHome (trace)");
        }
        catch (Exception e) { Plugin.L.LogWarning("removal trace: " + e.Message); }
        One(AccessTools.Method(typeof(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatDaylightAction), "HandleReturningAction"), nameof(DaylightReturn), true);
        One(AccessTools.Method(typeof(tuber.canis.actions.EveningPhase), "execute"), nameof(EveningPhaseSeen), true);
        One(AccessTools.Method(typeof(tuber.canis.actions.EveningPhase), "HandleReturningAction"), nameof(EveningPhaseReturn), true);
        One(AccessTools.Method(typeof(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatChoicePhase), "HandleReturningAction"), nameof(LoopReturn), true);
        One(AccessTools.Method(typeof(tuber.canis.actions.GainVP), "execute"), nameof(VPSeen), true);
    }

    // 0.22.0 - 0.24.0 history (Evening chain, kept-alive ChoicePhases, stop clearings): docs/bug-journal.md. 0.24.0 log: Root asks
    // EVERY faction for its Evening actions twice per turn (Riverfolk, Vagabond too), so a plugin-made Evening runs twice.
    // 0.25.0: the Evening is Root's own again (no wrapper). Shelter runs at the end of Root's real Daylight loop instead
    // (MarquiseDeCatDaylightAction owns the pass there): when the Spinners Daylight choices end (passed or no actions
    // left), Shelter opens once per turn with RemainingActions = shelters possible and the pass cleared, so the same loop
    // offers Shelter; passing or running out ends Daylight as usual. Order kept: Shelter before the Evening draw.
    // Deviation from the board: Shelter happens before Evening-start effects (none in this base set).
    static void Evening(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer __instance, ref Canis.actions.Action __result)
    {
        try { if (SpinnersPieces.IsSpinners(__instance) && eveningLogs++ < 6) Plugin.L.LogInfo("Spinners Evening asked: " + (__result == null ? "null" : __result.GetIl2CppType().Name) + " (Root's own, unchanged)"); }
        catch (Exception e) { Plugin.L.LogWarning("evening trace: " + e.Message); }
    }
    static int eveningLogs;

    static void DaylightReturn(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatDaylightAction __instance, Canis.actions.Action __0)
    {
        LoopReturn(__instance, __0);
        try
        {
            var cp = __0?.TryCast<tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatChoicePhase>();
            if (cp == null || cp.player == null || !SpinnersPieces.IsSpinners(cp.player)) return;
            var p = cp.player;
            int left = SpinnersPieces.Get(p, tuber.canis.TuberAttributes.RemainingActions);
            if (Active)
            {
                Plugin.L.LogInfo("Shelter: choice back (Passed=" + cp.Passed + ", " + left + " left)" + (cp.Passed || left <= 0 ? ": Daylight and Shelter end" : ""));
                return;
            }
            if (openedThisTurn || !(cp.Passed || left <= 0)) return;
            openedThisTurn = true;
            stopped = false;
            int n = Valid(p).Count;
            Plugin.L.LogInfo("Shelter: Daylight choices over (Passed=" + cp.Passed + ", " + left + " left); " + Infirmaries(p).Count + " infirmaries, " + n + " shelters possible");
            if (n == 0) return;
            Active = true;
            cp.Passed = false;
            SpinnersPieces.Set(p, tuber.canis.TuberAttributes.RemainingActions, n);
        }
        catch (Exception e) { Active = false; Plugin.L.LogError("Shelter open: " + e); }
    }

    // Read-only (R15 trace): what Root's Evening phase runs and gets back.
    static int phaseLogs;
    static void EveningPhaseSeen() { if (phaseLogs++ < 30) Plugin.L.LogInfo("EveningPhase.execute"); }
    static void EveningPhaseReturn(Canis.actions.Action __0)
    {
        try { if (phaseLogs++ < 30) Plugin.L.LogInfo("return to EveningPhase: " + (__0 == null ? "null" : __0.GetIl2CppType().Name)); }
        catch (Exception e) { Plugin.L.LogWarning("evening phase trace: " + e.Message); }
    }

    // Every copy of the choice phase while Shelter is on recounts RemainingActions = shelters possible now (0 ends it).
    static void ChoiceStart(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatChoicePhase __instance)
    {
        try
        {
            var p = __instance.player;
            if (!Active || p == null || !SpinnersPieces.IsSpinners(p)) return;
            int n = stopped ? 0 : Valid(p).Count;
            SpinnersPieces.Set(p, tuber.canis.TuberAttributes.RemainingActions, n);
            Plugin.L.LogInfo("Shelter: " + n + " shelters possible now" + (n == 0 ? " (choices close)" : ""));
        }
        catch (Exception e) { Plugin.L.LogError("Shelter start: " + e); }
    }

    static bool Abilities(tuber.canis.actions.MarquiseDeCatActions.MarquiseDeCatChoicePhase __instance, ref Il2CppSystem.Collections.Generic.IEnumerable<Canis.entities.AbilityDefinition> __result)
    {
        if (!Active || __instance.player == null || !SpinnersPieces.IsSpinners(__instance.player)) return true;
        var list = new Il2CppSystem.Collections.Generic.List<Canis.entities.AbilityDefinition>();
        foreach (var e in SpinnersSetup.Kids(__instance.player.Abilities))
        {
            var ow = e.TryCast<tuber.canis.abilities.MarquiseDeCatAbilities.OverworkAbility>();
            if (ow != null) list.Add(ow.Cast<Canis.entities.AbilityDefinition>());
        }
        __result = new Il2CppSystem.Collections.Generic.IEnumerable<Canis.entities.AbilityDefinition>(list.Pointer);
        return false;
    }

    static bool IsInfirmary(Canis.entities.Entity e)
    {
        var te = e?.TryCast<tuber.canis.entities.TuberEntity>();
        return te != null && te.PieceType == PieceTypes.Building && te.Faction == SpinnersHost.Host
            && (te.BuildingType == BuildingTypes.Sawmill || te.BuildingType == BuildingTypes.Workshop || te.BuildingType == BuildingTypes.Recruiter);
    }

    static List<(tuber.canis.entities.TuberBoardNode node, Canis.entities.Entity inf)> Infirmaries(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p)
    {
        var all = new List<(tuber.canis.entities.TuberBoardNode, Canis.entities.Entity)>();
        foreach (var n in SpinnersSetup.Clearings(p))
            foreach (var c in SpinnersSetup.Kids(n))
                if (IsInfirmary(c)) all.Add((n, c));
        return all;
    }

    // Track spaces score +0 / +1 / +2 left to right and are emptied left to right, so the rightmost empty space of a suit
    // is (infirmaries of that suit off the track) - 1.
    internal static int Value(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Suits suit)
    {
        var track = Establish.Track(p, suit);
        if (track == null) return 0;
        int off = SpinnersPieces.InfirmariesPerSuit - SpinnersSetup.Kids(track).Count();
        return Math.Max(0, off - 1);
    }

    static List<tuber.canis.entities.TuberBoardNode> Valid(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p) =>
        Infirmaries(p).Where(x => !sheltered.Contains(x.inf.Pointer) && Value(p, x.node.Suit) > 0 && Establish.Card(p, x.node.Suit) != null).Select(x => x.node).GroupBy(n => n.Pointer).Select(g => g.First()).ToList();   // +0 never helps: not offered

    internal static void Targets(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Networking.selection.targetinformation.EntityListTargetInformation el)
    {
        var valid = Valid(p);
        // 0.25.0: no "stop" clearings (0.24.0); Shelter runs in Root's Daylight loop, whose pass button ends it.
        var stops = new List<tuber.canis.entities.TuberBoardNode>();
        el.ValidTargets = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Canis.utils.ids.EntityID>(valid.Select(n => n.EntityID).ToArray());
        el.TargetPrompt = new Canis.utils.localization.LocalizableTextVariables(PromptKey);
        Plugin.L.LogInfo("Shelter targets: " + valid.Count + " clearings [" + string.Join(" ", valid.Select(n => n.Index + ":" + n.Suit + "+" + Value(p, n.Suit))) + "], " + stops.Count + " stop clearings");
    }

    internal static bool CanPay(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p) => Valid(p).Count > 0;

    internal static Il2CppSystem.Collections.Generic.List<Canis.actions.Action> Steps(tuber.canis.entities.PlayableClasses.MarquiseDeCatPlayer p, Canis.context.Context ctx, Canis.Match m)
    {
        var steps = new Il2CppSystem.Collections.Generic.List<Canis.actions.Action>();
        var node = Establish.Clicked(ctx, m)?.TryCast<tuber.canis.entities.TuberBoardNode>();
        if (node == null) { Plugin.L.LogError("Shelter: no clearing selected"); return steps; }
        // 0.25.1: matched by the IL2CPP object, not the C# wrapper (the clicked clearing arrives as a new wrapper, so
        // List.Contains was always false and every click counted as "stop": 0.25.0 skipped Shelter).
        if (!Valid(p).Any(v => v.Pointer == node.Pointer)) { Plugin.L.LogError("Shelter: clicked clearing " + node.Index + " is not a valid shelter"); return steps; }
        var inf = SpinnersSetup.Kids(node).FirstOrDefault(c => IsInfirmary(c) && !sheltered.Contains(c.Pointer));
        var card = Establish.Card(p, node.Suit);
        if (inf == null || card == null) { Plugin.L.LogError("Shelter: clearing " + node.Index + " no longer valid"); return steps; }
        int vp = Value(p, node.Suit);
        sheltered.Add(inf.Pointer);
        steps.Add(Establish.WithTarget(m, card, () => new tuber.canis.actions.CostActions.DiscardCost(m, card)));
        if (vp > 0) steps.Add(Establish.WithTarget(m, p, () => new tuber.canis.actions.GainVP(vp, m, "Shelter", true, null)));
        Plugin.L.LogInfo("Shelter: " + node.Suit + " infirmary in clearing " + node.Index + ", discarding a " + card.TryCast<tuber.canis.entities.TuberEntity>()?.Suit + " card, +" + vp + " VP");
        return steps;
    }

    static int vpLogs;
    static void VPSeen(tuber.canis.actions.GainVP __instance)
    {
        try
        {
            if (websRemoved > 0 && __instance.cause == "removedPieces")
            {
                var who = __instance.context != null && __instance.context.HasTarget ? __instance.context.Target : null;
                if (who == null || !SpinnersPieces.IsSpinners(who))
                {
                    int cut = Math.Min(__instance.amount, websRemoved);
                    __instance.amount -= cut;
                    websRemoved -= cut;
                    Plugin.L.LogInfo("Webs rule: removal VP " + (__instance.amount + cut) + " -> " + __instance.amount + " (" + cut + " webs)");
                }
            }
            if (vpLogs++ >= 10) return;
            var c = __instance.context;
            string Ent(Canis.entities.Entity e) => e == null ? "null" : e.GetIl2CppType().Name;
            Plugin.L.LogInfo("GainVP " + __instance.amount + " cause=" + __instance.cause + " ctx Source=" + Ent(c?.Source) + " Target=" + (c != null && c.HasTarget ? Ent(c.Target) : "none"));
        }
        catch (Exception e) { Plugin.L.LogWarning("GainVP trace: " + e.Message); }
    }
}
