--------------------------------------------------------------------
-- EastwardModLoader: loaded before the original mock.init call.
--------------------------------------------------------------------
if rawget(_G, 'EastwardModLoader') then return EastwardModLoader end

local loader = { version = 2, mods = {}, scriptPatches = {}, loading = true }
_G.EastwardModLoader = loader

local function log(id, message)
    print('[EastwardModLoader:' .. id .. '] ' .. tostring(message))
end

local function invoke(id, callback, ...)
    local args = { n = select('#', ...), ... }
    local ok, result = xpcall(function()
        return callback(unpack(args, 1, args.n))
    end, debug.traceback)
    if not ok then log(id, result) end
    return ok, result
end

local pending = {}
local function connect(context, signal, callback)
    assert(type(signal) == 'string' and type(callback) == 'function', 'on requires a signal and callback')
    local guarded = function(...)
        if context.enabled then invoke(context.id, callback, ...) end
    end
    local ok = pcall(mock.connectGlobalSignalFunc, signal, guarded)
    if not ok then
        pending[#pending + 1] = { context = context, signal = signal, callback = guarded }
    end
end

-- Game-specific ew.* signals are registered while mock.init loads game modules.
mock.connectGlobalSignalFunc('game.init', function()
    for _, item in ipairs(pending) do
        local ok, reason = pcall(mock.connectGlobalSignalFunc, item.signal, item.callback)
        if not ok then log(item.context.id, 'Unknown signal ' .. item.signal .. ': ' .. tostring(reason)) end
    end
    pending = {}
end)

local function loadMod(file, configuredId)
    local id = configuredId or file:sub(1, -5)
    local path = 'mods/' .. file
    local directory = configuredId and path:match('^(.*)/[^/]+$') or 'mods/' .. id
    local moduleName = 'EastwardMods.' .. id
    local context = { id = id, enabled = true, path = path }
    function context.log(message) log(id, message) end
    function context.on(signal, callback) connect(context, signal, callback) end
    function context.disable() context.enabled = false end
    local patches = {}
    function context.patchScript(moduleName, relativePath)
        assert(loader.loading, 'Script patches must be declared during initMod')
        assert(type(moduleName) == 'string' and moduleName:match('^[%w_.]+$')
            and not moduleName:match('^%.') and not moduleName:match('%.$')
            and not moduleName:find('..', 1, true), 'Invalid module name')
        assert(type(relativePath) == 'string' and relativePath:match('^[%w_./-]+%.lua$')
            and not relativePath:match('^/') and not relativePath:find('..', 1, true), 'Invalid mod-relative Lua path')
        local path = directory .. '/' .. relativePath
        assert(MOAIFileSystem.checkFileExists(path), 'Script patch does not exist: ' .. path)
        local existing = rawget(_G, 'MOCK_SCRIPT_LIBRARY_PATCH') or {}
        assert(not existing[moduleName] and not loader.scriptPatches[moduleName], 'Script patch conflict: ' .. moduleName)
        patches[moduleName] = path
    end
    loader.mods[id] = context

    GameModule.addGameModuleMapping(moduleName, path, path)
    -- flushError=true prevents one failed mod from poisoning later game module loads.
    local ok, mod, errors = pcall(GameModule.loadGameModule, moduleName, true)
    if not ok or not mod then
        context.enabled = false
        context.error = ok and errors or mod
        log(id, 'Load failed: ' .. tostring(context.error))
        if type(errors) == 'table' then
            for _, error in ipairs(errors) do log(id, error.msg or error.errtype) end
        end
        return
    end
    context.module = mod
    if type(mod.initMod) == 'function' then
        local initialized, error = invoke(id, mod.initMod, context)
        if not initialized then
            context.enabled = false
            context.error = error
            return
        end
    end
    local scriptPatches = rawget(_G, 'MOCK_SCRIPT_LIBRARY_PATCH') or {}
    for moduleName, path in pairs(patches) do
        scriptPatches[moduleName] = path
        loader.scriptPatches[moduleName] = { mod = id, path = path }
    end
    _G.MOCK_SCRIPT_LIBRARY_PATCH = scriptPatches
    log(id, 'Loaded')
end

local function loadLegacy()
    local files = MOAIFileSystem.listFiles('mods') or {}
    table.sort(files)
    for _, file in ipairs(files) do
        if file:match('^[%w_-]+%.lua$') then
            local ok, reason = pcall(loadMod, file)
            if not ok then log(file, reason) end
        end
    end
end

if MOAIFileSystem.checkFileExists and MOAIFileSystem.checkFileExists('mods/loader.json') then
    local stream = MOAIFileStream.new()
    assert(stream:open('_system/__eastward_modloader/ewmod.lua', MOAIFileStream.READ) ~= false, 'Resource package module is missing')
    local source = stream:read()
    stream:close()
    local packages = assert(loadstring(assert(source), '@EastwardModLoader.ewmod'))()
    local configuration = packages.readConfiguration()
    local originalPreInit = MOCK_PRE_GAME_INIT
    MOCK_PRE_GAME_INIT = function(...)
        if originalPreInit then originalPreInit(...) end
        -- The game's original callback mounts DLC; our complete G overlays go after it.
        packages.activate(configuration)
        for _, mod in ipairs(configuration.mods) do
            if mod.enabled and mod.kind == 'lua' then
                local ok, reason = pcall(loadMod, mod.file, mod.id)
                if not ok then log(mod.id, reason) end
            end
        end
        if configuration.legacyLuaDiscovery == true then loadLegacy() end
        loader.loading = false
    end
else
    loadLegacy()
    loader.loading = false
end
return loader
