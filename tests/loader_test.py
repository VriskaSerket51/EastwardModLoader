"""Optional LuaJIT test host: python -m pip install lupa; python tests/loader_test.py.
No game executable or actual game scripts are executed.
"""
from pathlib import Path
from lupa.luajit21 import LuaRuntime

lua = LuaRuntime(unpack_returned_tuples=True)
lua.execute(r"""
local signals = { ['game.init'] = {}, ['scene.open'] = {} }
local logs, order = {}, {}
print = function(message) logs[#logs + 1] = message end
mock = {
    connectGlobalSignalFunc = function(signal, callback)
        assert(signals[signal], 'signal not found')
        table.insert(signals[signal], callback)
    end
}
MOAIFileSystem = { checkFileExists = function() return true end, listFiles = function() return {
    'z-good.lua', 'a-bad.lua', 'b-init-failure.lua', 'x-callback-failure.lua',
    'disabled.lua.disabled', '../unsafe.lua', 'two.dots.lua'
} end }
local mappings = {}
GameModule = {
    addGameModuleMapping = function(name, path) mappings[name] = path end,
    loadGameModule = function(name, flush)
        assert(flush == true)
        order[#order + 1] = name
        if name == 'EastwardMods.a-bad' then return false, {{msg = 'syntax error'}} end
        if name == 'EastwardMods.b-init-failure' then
            return { initMod = function(mod)
                mod.patchScript('script.Aborted', 'aborted.lua')
                mod.on('scene.open', function() error('disabled callback should not run') end)
                error('init failure')
            end }, {}
        end
        if name == 'EastwardMods.x-callback-failure' then
            return { initMod = function(mod)
                mod.on('scene.open', function() error('callback failure') end)
            end }, {}
        end
        return { initMod = function(mod)
            mod.patchScript('script.Common', 'Common.lua')
            mod.on('game.init', function(config) assert(config == 'configuration') end)
            mod.on('ew.item.get', function(value, missing, trailing)
                assert(value == 42 and missing == nil and trailing == 'ok')
                goodCalled = true
            end)
            mod.on('scene.open', function(scene) assert(scene.path == 'scene/test'); sceneCalled = true end)
        end }, {}
    end
}
function simulate()
    assert(#order == 4)
    assert(order[1] == 'EastwardMods.a-bad' and order[4] == 'EastwardMods.z-good')
    signals['ew.item.get'] = {}
    for _, callback in ipairs(signals['game.init']) do callback('configuration') end
    for _, callback in ipairs(signals['ew.item.get']) do callback(42, nil, 'ok') end
    for _, callback in ipairs(signals['scene.open']) do callback({path = 'scene/test'}) end
    assert(goodCalled and sceneCalled)
    assert(MOCK_SCRIPT_LIBRARY_PATCH['script.Common'] == 'mods/z-good/Common.lua')
    assert(MOCK_SCRIPT_LIBRARY_PATCH['script.Aborted'] == nil)
    assert(not pcall(EastwardModLoader.mods['z-good'].patchScript, 'script.Late', 'late.lua'))
    assert(not EastwardModLoader.mods['a-bad'].enabled)
    assert(not EastwardModLoader.mods['b-init-failure'].enabled)
    assert(EastwardModLoader.mods['z-good'].enabled)
    assert(#logs >= 4)
end
function loadedCount() return #order end
""")
source = (Path(__file__).resolve().parents[1] / 'main.lua').read_text()
lua.execute(source)
lua.globals().simulate()
lua.execute(source)
assert lua.globals().loadedCount() == 4
print('LuaJIT host tests passed: order, failed mod isolation, deferred signals, callback errors, nil arguments, duplicate bootstrap.')

# Optionally verify against an existing checkout of the public mock engine.
import sys
if len(sys.argv) > 1:
    engine = LuaRuntime(unpack_returned_tuples=True)
    engine.execute(r"""
    _stat, _warn = function() end, function() end
    local files = {
        ['mods/a-broken.lua'] = 'not valid lua !!!',
        ['mods/b-first.lua'] = "counter = 1; function initMod(mod) mod.on('scene.open', function() firstSeen = counter end) end",
        ['mods/c-second.lua'] = "counter = 2; function initMod(mod) mod.on('scene.open', function() secondSeen = counter end) end"
    }
    local callbacks = { ['game.init'] = {}, ['scene.open'] = {} }
    mock = { connectGlobalSignalFunc = function(name, callback)
        assert(callbacks[name]); table.insert(callbacks[name], callback)
    end }
    MOAIFileSystem = {
        checkFileExists = function(path) return files[path] ~= nil end,
        listFiles = function() return {'c-second.lua', 'a-broken.lua', 'b-first.lua'} end
    }
    MOAIFileStream = { READ = 1, new = function() return {
        open = function(self, path) self.path = path end,
        read = function(self) return files[self.path], #files[self.path] end,
        close = function() end
    } end }
    function verifyModules()
        assert(GameModule.getErrorInfo() == nil, 'mod errors leaked to game startup')
        local first = EastwardModLoader.mods['b-first'].module
        local second = EastwardModLoader.mods['c-second'].module
        assert(first.counter == 1 and second.counter == 2 and rawget(_G, 'counter') == nil)
        for _, callback in ipairs(callbacks['scene.open']) do callback({}) end
        assert(first.firstSeen == 1 and second.secondSeen == 2)
    end
    """)
    engine.execute(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    engine.execute(source)
    engine.globals().verifyModules()
    print('Public GameModule compatibility passed: separate environments and failure flushing.')
