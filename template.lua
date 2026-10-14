--------------------------------------------------------------------
-- Copy to mods/hello.lua. Each file gets its own GameModule environment.
--------------------------------------------------------------------
function initMod(mod)
    mod.on('game.init', function(config)
        mod.log('Hello, Eastward!')
    end)

    mod.on('scene.open', function(scene)
        mod.log('Scene: ' .. tostring(scene.path))
    end)

    -- Use existing ew.* signals; do not register them again.
    mod.on('ew.item.get', function(...)
        mod.log('Item received')
    end)
end
