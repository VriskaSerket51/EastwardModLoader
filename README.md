# EastwardModLoader

English | [한국어](README.ko.md)

Unofficial mod loader for loading user-created Lua scripts and custom resources into Eastward.

## How to use

1. Open the game folder in [EastwardModTool](https://github.com/VriskaSerket51/EastwardModTool).
2. Open `Mod > Manage mods...` and select `Install / update loader`.
3. Add an `.ewmod`, Lua file or folder containing `main.lua`.
4. Enable the mods and select `Apply enabled mods`.

Installation updates the startup script in `_system.g` and backs up the archive. `Uninstall loader` restores that backup.

## Mod creation

Start with `template.lua`:

```lua
function initMod(mod)
    mod.on('game.init', function(config)
        mod.log('Hello, Eastward!')
    end)
end
```

To replace a Lua module, put `Common.lua` in the mod folder and call:

```lua
mod.patchScript('script.Common', 'Common.lua')
```

Call it inside `initMod`, before game startup. Mods replacing the same Lua module cannot be enabled together.

## Documentation

See the [language patch guide](docs/language-patches.md) for creating language mods.

## How to build

.NET 10 required.

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModLoader.git
cd EastwardModLoader
dotnet build EastwardModLoader.sln -c Release
```
