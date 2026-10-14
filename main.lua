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

local function loadMod(file)
    local id = file:sub(1, -5)
    local path = 'mods/' .. file
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
        local path = 'mods/' .. id .. '/' .. relativePath
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

local files = MOAIFileSystem.listFiles('mods') or {}
table.sort(files)
for _, file in ipairs(files) do
    -- Filenames form isolated GameModule names; reject separators and ambiguous dots.
    if file:match('^[%w_-]+%.lua$') then
        local ok, error = pcall(loadMod, file)
        if not ok then log(file, error) end
    end
end

loader.loading = false
return loader
