# EastwardModLoader
Mod loader of video game 'Eastward'

## How to use
Inject 'main.lua' in to '{steam_dir}/Eastward/game/_system.g', using 'Injector.exe' or just make custom injector with [EastwardLib](https://github.com/VriskaSerket51/EastwardLib).

Generate your mods with 'template.lua' file, and move it to '{steam_dir}/Eastward/game/mods/'.

Then mod successfully injected!
Logs for injecting will be saved at 'ModLoader.log'.

Other game logs will be saved at '%appdata%/Pixpil/Eastward/{steam_id}/game.log' (Windows).

## Add custom mod
'template.lua' is just a template, you can edit it whatever you want!

Key idea is using Eastward's signal handler, such as `mock.connectGlobalSignalFunc("game.init", onGameInit)`.

Here is example code for skipping Logo scene.
```lua
mock.connectGlobalSignalFunc("scene.open", onSceneOpen)

function onSceneOpen(scene)
    local path = scene.path

    if (path == "scene/design/ui/Logo.scene") then
        game:openSceneByPath("scene/ui/TitleMenu.scene")
    end
end
```
