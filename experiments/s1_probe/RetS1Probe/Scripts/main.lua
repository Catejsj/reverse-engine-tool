-- RET spike S1: does UE4SS run inside SHTA.exe under Proton, with the Steam wrapper untouched?
-- Each result is written to UE4SS.log:
--   1. Lua runs at all                      -> "[RetS1Probe] loaded"            (passed, attempt 2)
--   2. we can READ the game's objects       -> pawn class + values              (passed, attempt 2)
--   3. we can make one deliberate WRITE that shows on screen
--
-- Attempt 2 wrote CharacterMovement.MaxWalkSpeed and read 0.0: Frogwares drive Holmes through their
-- own MainCharacter class (object dump: MainCharacter:MaxMovementSpeed, MainCharMovementComponent).
--   F7  MainCharacter.MaxMovementSpeed x2       (the game's own speed cap)
--   F9  pawn CustomTimeDilation = 2             (engine-level: the pawn ticks at double time)
--   F8  restore both
-- Nothing here touches files, saves, or the network.

local TAG = "[RetS1Probe] "
local saved = {}

local function log(msg)
    print(TAG .. msg .. "\n")
end

local function player()
    local pc = FindFirstOf("PlayerController")
    if not pc or not pc:IsValid() then return nil, "no PlayerController yet (load a save first)" end
    local pawn = pc.Pawn
    if not pawn or not pawn:IsValid() then return nil, "PlayerController has no pawn" end
    return pawn, nil
end

local function scale(pawn, prop, value)
    local before = pawn[prop]
    if saved[prop] == nil then saved[prop] = before end
    pawn[prop] = value
    log(string.format("WRITE %s %.2f -> %.2f", prop, before, pawn[prop]))
end

RegisterKeyBind(Key.F7, function()
    ExecuteInGameThread(function()
        local pawn, err = player()
        if not pawn then log(err) return end
        log("pawn: " .. pawn:GetFullName())
        scale(pawn, "MaxMovementSpeed", (saved.MaxMovementSpeed or pawn.MaxMovementSpeed) * 2)
    end)
end)

RegisterKeyBind(Key.F9, function()
    ExecuteInGameThread(function()
        local pawn, err = player()
        if not pawn then log(err) return end
        scale(pawn, "CustomTimeDilation", 2.0)
    end)
end)

RegisterKeyBind(Key.F8, function()
    ExecuteInGameThread(function()
        local pawn, err = player()
        if not pawn then log(err) return end
        for prop, value in pairs(saved) do
            pawn[prop] = value
            log(string.format("RESTORE %s -> %.2f", prop, pawn[prop]))
        end
        saved = {}
    end)
end)

log("loaded")
