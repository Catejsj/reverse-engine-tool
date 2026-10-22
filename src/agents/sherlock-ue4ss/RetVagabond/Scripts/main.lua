-- RetVagabond v6: Root's Vagabond leaps into a Ball of Violence in Sherlock Holmes: The Awakened.
--
--   F6   strike: the nearest NPC in front of Holmes is swallowed by Root's fight cloud with the Vagabond landing in it,
--        and the NPC falls under Sherlock's own ragdoll physics
--   F10  undo: every NPC knocked down by this mod is stood back up (nothing is ever destroyed)
--
-- Why a screen overlay again (2026-10-14): v5 put the animation on a panel in the 3D world. The spawn worked
-- (World:SpawnActor), but nothing added to the world at runtime was ever drawn under UE4SS 3.0.1: a WidgetComponent
-- never ticked (draw size 0x0, no render target), and a textured plane mesh, even attached to Holmes, was never
-- rendered (WasRecentlyRendered false). The screen overlay is the one display that has worked in this game (v2-v4),
-- so v6 goes back to it and keeps what Sherlock asked for since:
--   * the strike sequence (experiments/root_vaga_strike.py): the Vagabond lands, the ball bursts, he is revealed
--     in his victory pose as it clears;
--   * sized by WORLD distance, not by the NPC's screen height and with no screen cap: one frame pixel = 1.25 cm at the
--     NPC's spot (the ball is ~2.6 m), so it shrinks with distance like a thing in the scene and covers the whole body;
--     the frame point PIVOT (the cloud's x, the Vagabond's ground line) is pinned to the NPC's feet every frame;
--   * the NPC is not hidden: its skeletal mesh ragdolls (gravity on: Frogwares ship the bodies with it off);
--   * near-instant: playback starts at the landing (START_FRAME), the NPC drops ~0.25 s after F6.
-- v5 (world panel + spawn probes) is kept in experiments/v5_world_panel.lua.
--
-- GC rules from v1/v2: the overlay is built once and stays in the viewport (collapsed when idle); every frame texture
-- is held by its own collapsed Image inside it, so the garbage collector always sees them in use.
--
-- @RET_WORK@ is replaced by deploy.sh with the Wine path of ~/ret-work (Z:/...).

local TAG = "[RetVagabond] "
local WORK = "@RET_WORK@"
local FRAMES_DIR = WORK .. "/root/extract/fx/VagabondStrike/frames/"
local EVENTS_DIR = WORK .. "/bridge/events/"
local FRAME_COUNT = 130
local FRAME_W, FRAME_H = 768, 384
local CLOUD_START = 45        -- frame where the Ball of Violence bursts (he lands)
local COVER_FRAME = 48        -- cloud covers the target: ragdoll it here
local START_FRAME = CLOUD_START - 4   -- set to 0 to bring back the full 1.5 s leap from the side
local FRAME_MS = 33           -- 30 fps
local PIVOT = {X = 528 / FRAME_W, Y = 330 / FRAME_H}   -- frame point that stands on the NPC's feet
local WORLD_SCALE = 1.25      -- cm per frame pixel at the NPC's distance
local REACH = 600             -- cm in front of Holmes
local NPC_HALF_HEIGHT = 95    -- capsule half height, used to find the NPC's feet
local SHOVE = 350             -- ragdoll impulse (velocity change, cm/s), away from Holmes

local overlay = nil           -- {widget, image, slot, textures}: built once, kept alive by the viewport
local busy = false
local downed = {}             -- {npc, saved}
local downed_addr = {}
local seq = 0

local function log(msg)
    print(TAG .. msg .. "\n")
end

local function event(name)
    -- One file per message, written then renamed, so the companion never reads half a file.
    seq = seq + 1
    local base = EVENTS_DIR .. string.format("%d-%d", os.time(), seq)
    local f = io.open(base .. ".tmp", "w")
    if not f then
        log("cannot write event to " .. EVENTS_DIR .. " (is `ret companion` set up?)")
        return
    end
    f:write('{"sound": "' .. name .. '"}')
    f:close()
    os.rename(base .. ".tmp", base .. ".json")
end

local function player_controller()
    local pc = FindFirstOf("PlayerController")
    if pc and pc:IsValid() and pc.Pawn and pc.Pawn:IsValid() then
        return pc
    end
    return nil
end

local function construct(class_path, outer, name)
    return StaticConstructObject(StaticFindObject(class_path), outer, FName(name))
end

local function build_overlay(pc)
    if overlay and overlay.widget:IsValid() then return true end
    local outer = FindFirstOf("GameInstance")
    local widget = construct("/Script/UMG.UserWidget", outer, "RetVagabondOverlay")
    local tree = construct("/Script/UMG.WidgetTree", widget, "RetTree")
    widget.WidgetTree = tree
    local canvas = construct("/Script/UMG.CanvasPanel", tree, "RetCanvas")
    tree.RootWidget = canvas
    local image = construct("/Script/UMG.Image", tree, "RetStrike")
    local slot = canvas:AddChildToCanvas(image)
    slot:SetAlignment(PIVOT)       -- SetPosition places the pivot (the NPC's feet), not the corner
    image:SetVisibility(1)         -- Collapsed until a strike

    local krl = StaticFindObject("/Script/Engine.Default__KismetRenderingLibrary")
    local textures = {}
    local t0 = os.clock()
    for i = 0, FRAME_COUNT - 1 do
        local tex = krl:ImportFileAsTexture2D(pc, FRAMES_DIR .. string.format("%04d.png", i))
        if not tex or not tex:IsValid() then
            log("could not load frame " .. i .. " from " .. FRAMES_DIR)
            return false
        end
        -- A collapsed Image per frame: its brush references the texture, so the GC sees it as in use.
        local keeper = construct("/Script/UMG.Image", tree, "RetKeep" .. i)
        canvas:AddChildToCanvas(keeper)
        keeper:SetBrushFromTexture(tex, false)
        keeper:SetVisibility(1)
        textures[i] = tex
    end
    widget:SetVisibility(3)        -- HitTestInvisible: the overlay can never block the game's UI or input
    widget:AddToViewport(100)
    overlay = {widget = widget, image = image, slot = slot, textures = textures}
    log(string.format("overlay built, %d strike frames loaded in %.2fs", FRAME_COUNT, os.clock() - t0))
    return true
end

local function to_screen(pc, p)
    local out = {}
    local ok = pc:ProjectWorldLocationToScreen(p, out, false)
    if not ok then return nil end
    return out.X, out.Y
end

-- Pin the frame to the NPC's feet at its real size: 1 m straight up from the feet, projected, gives screen pixels
-- per centimetre at that distance.
local function place(pc, feet, scale)
    local fx, fy = to_screen(pc, feet)
    local ux, uy = to_screen(pc, {X = feet.X, Y = feet.Y, Z = feet.Z + 100})
    if not (fx and ux) then return false end
    local px_per_cm = math.sqrt((ux - fx) ^ 2 + (uy - fy) ^ 2) / 100
    local k = px_per_cm * WORLD_SCALE / scale
    overlay.slot:SetSize({X = FRAME_W * k, Y = FRAME_H * k})
    overlay.slot:SetPosition({X = fx / scale, Y = fy / scale})
    return true
end

-- Walls (2026-10-15): the overlay is drawn on the screen, so on its own it shows through walls. One visibility trace
-- per frame from the camera toward the ball's centre (1 m above the feet), stopped STOP_SHORT cm before it so the
-- NPC's own body and ragdoll never count; any blocking hit on the way is a wall, and the frame is not drawn.
-- KismetSystemLibrary:LineTraceSingle, channel TraceTypeQuery1 (= Visibility), checked in the object dump.
local BALL_CENTRE = 100
local STOP_SHORT = 80
local trace_ok = true          -- false after a failed call: then the overlay is always drawn (v6 behaviour)
local blocked_frames = 0

local function wall_between(pc, feet)
    if not trace_ok then return false end
    local ksl = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
    local cam = pc.PlayerCameraManager:GetCameraLocation()
    local tx, ty, tz = feet.X, feet.Y, feet.Z + BALL_CENTRE
    local dx, dy, dz = tx - cam.X, ty - cam.Y, tz - cam.Z
    local len = math.sqrt(dx * dx + dy * dy + dz * dz)
    if len <= STOP_SHORT then return false end
    local k = (len - STOP_SHORT) / len
    local hit = {}
    local ok, blocked = pcall(function()
        return ksl:LineTraceSingle(pc, {X = cam.X, Y = cam.Y, Z = cam.Z},
            {X = cam.X + dx * k, Y = cam.Y + dy * k, Z = cam.Z + dz * k},
            0, false, {pc.Pawn}, 0, hit, true, {R = 1, G = 0, B = 0, A = 1}, {R = 0, G = 1, B = 0, A = 1}, 0)
    end)
    if not ok then
        trace_ok = false
        log("wall check off: LineTraceSingle failed (" .. type(blocked) .. "): " .. tostring(blocked))
        return false
    end
    return blocked == true
end

local function nearest_npc(pc)
    local me = pc.Pawn
    local here = me:K2_GetActorLocation()
    local fwd = me:GetActorForwardVector()
    local best, best_d = nil, REACH
    for _, c in ipairs(FindAllOf("Character") or {}) do
        if c:IsValid() and c:GetAddress() ~= me:GetAddress() and not downed_addr[c:GetAddress()] then
            local p = c:K2_GetActorLocation()
            local dx, dy, dz = p.X - here.X, p.Y - here.Y, p.Z - here.Z
            local d = math.sqrt(dx * dx + dy * dy + dz * dz)
            local facing = d > 0 and (dx * fwd.X + dy * fwd.Y) / d or 0
            if d < best_d and facing > 0.3 then
                best, best_d = c, d
            end
        end
    end
    return best, best_d
end

-- Sherlock's own physics: the NPC's skeletal mesh simulates, using the physics asset the game ships for it.
-- Frogwares ship the bodies with gravity off (logged: gravity=false before); without SetEnableGravity they float up.
local function knock_down(npc, push)
    local mesh, cap, cm = npc.Mesh, npc.CapsuleComponent, npc.CharacterMovement
    if not mesh or not mesh:IsValid() then return false end
    local rl, rr = mesh.RelativeLocation, mesh.RelativeRotation
    local saved = {
        loc = {X = rl.X, Y = rl.Y, Z = rl.Z},
        rot = {Pitch = rr.Pitch, Yaw = rr.Yaw, Roll = rr.Roll},
        profile = mesh:GetCollisionProfileName(),
        cap_coll = cap and cap:IsValid() and cap:GetCollisionEnabled() or nil,
    }
    local pa = mesh.PhysicsAssetOverride
    if not (pa and pa:IsValid()) and mesh.SkeletalMesh and mesh.SkeletalMesh:IsValid() then
        pa = mesh.SkeletalMesh.PhysicsAsset
    end
    if not (pa and pa:IsValid()) then
        log("no physics asset on " .. npc:GetFullName() .. ": cannot ragdoll it")
        return false
    end
    if cm and cm:IsValid() then cm:DisableMovement() end
    if cap and cap:IsValid() then cap:SetCollisionEnabled(0) end      -- NoCollision
    mesh:SetCollisionProfileName(FName("Ragdoll"), true)
    pcall(function() mesh:SetEnableGravity(true) end)
    mesh:SetAllBodiesSimulatePhysics(true)
    mesh:SetSimulatePhysics(true)
    mesh:AddImpulse({X = push.X * SHOVE, Y = push.Y * SHOVE, Z = SHOVE * 0.35}, FName("None"), true)
    table.insert(downed, {npc = npc, saved = saved})
    downed_addr[npc:GetAddress()] = true
    return true
end

local function stand_up(entry)
    local npc, s = entry.npc, entry.saved
    if not npc:IsValid() then return false end
    local mesh, cap, cm = npc.Mesh, npc.CapsuleComponent, npc.CharacterMovement
    mesh:SetSimulatePhysics(false)
    mesh:SetAllBodiesSimulatePhysics(false)
    mesh:K2_AttachToComponent(cap, FName("None"), 0, 0, 0, true)       -- KeepRelative
    mesh:K2_SetRelativeLocationAndRotation(s.loc, s.rot, false, {}, true)
    mesh:SetCollisionProfileName(s.profile, true)
    if cap and cap:IsValid() and s.cap_coll then cap:SetCollisionEnabled(s.cap_coll) end
    if cm and cm:IsValid() then cm:SetMovementMode(1, 0) end            -- Walking
    return true
end

local function strike()
    if busy then return end
    local pc = player_controller()
    if not pc then log("no player yet (load a save first)") return end
    local npc, dist = nearest_npc(pc)
    if not npc then log("no NPC within reach in front of Holmes") return end
    local ok, err = pcall(build_overlay, pc)
    if not ok or not err then
        log("overlay failed (" .. type(err) .. "): " .. tostring(err))
        return
    end
    busy = true

    local loc = npc:K2_GetActorLocation()
    local feet = {X = loc.X, Y = loc.Y, Z = loc.Z - NPC_HALF_HEIGHT}   -- fixed: the cloud stays where the NPC stood
    local name = npc:GetFullName()
    log(string.format("strike: %s at %.0f cm", name, dist))
    local scale = StaticFindObject("/Script/UMG.Default__WidgetLayoutLibrary"):GetViewportScale(pc)
    if not scale or scale <= 0 then scale = 1 end

    local me = pc.Pawn:K2_GetActorLocation()
    local dx, dy = loc.X - me.X, loc.Y - me.Y
    local len = math.sqrt(dx * dx + dy * dy)
    local push = len > 0 and {X = dx / len, Y = dy / len} or {X = 1, Y = 0}

    blocked_frames = 0
    local frame = START_FRAME
    LoopAsync(FRAME_MS, function()
        local done = frame >= FRAME_COUNT
        ExecuteInGameThread(function()
            if frame >= FRAME_COUNT or not overlay then return end
            local image = overlay.image
            local walled = wall_between(pc, feet)
            if walled then blocked_frames = blocked_frames + 1 end
            if not walled and place(pc, feet, scale) then
                image:SetBrushFromTexture(overlay.textures[frame], false)
                image:SetVisibility(3)   -- HitTestInvisible: drawn, never steals clicks
            else
                image:SetVisibility(1)   -- Collapsed: the spot is behind the camera
            end
            if frame == math.max(CLOUD_START, START_FRAME) then event("ballofviolence") end
            if frame == COVER_FRAME and npc:IsValid() then
                if knock_down(npc, push) then
                    event("death")
                    log("knocked down: " .. name)
                end
            end
            frame = frame + 1
            if frame >= FRAME_COUNT then
                image:SetVisibility(1)
                busy = false
                log("strike done; frames hidden behind a wall: " .. blocked_frames)
            end
        end)
        return done   -- true stops the loop
    end)
end

local function undo()
    ExecuteInGameThread(function()
        local n = 0
        for _, e in ipairs(downed) do
            local ok = pcall(stand_up, e)
            if ok then n = n + 1 end
        end
        downed, downed_addr = {}, {}
        log("stood up " .. n .. " NPC(s)")
    end)
end

RegisterKeyBind(Key.F6, function() ExecuteInGameThread(strike) end)
RegisterKeyBind(Key.F10, undo)
log("v6.1 loaded (wall check). F6 = strike, F10 = undo. Frames: " .. FRAMES_DIR)
