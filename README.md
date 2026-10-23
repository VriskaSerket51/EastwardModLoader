# EastwardModLoader

English | [한국어](README.ko.md)

An unofficial Eastward mod loader for Lua scripts and resource replacement packages.

## How to use

Open the game directory in EastwardModTool and select **Mod > Manage mods...**.

Use **Install / update loader**, then add an `.ewmod`, a Lua file or a directory containing `main.lua`. Select the mods to use and press **Apply enabled mods**.

The loader adds a call before the existing `mock.init` call in the startup Lua code inside `_system.g`. The archive is backed up before installation. **Uninstall loader** restores the backup.

The loader mounts `.ewmod` packages as ZIPs and reads their resources directly. Activating mods does not rebuild resource `.g` archives. See the [engine notes](docs/engine.md) for details.

## Creating mods

Start with `template.lua`. Connect to game signals in `initMod(mod)`:

```lua
function initMod(mod)
    mod.on('game.init', function(config)
        mod.log('Hello, Eastward!')
    end)
end
```

To replace a Lua module, put `Common.lua` in the mod directory:

```lua
function initMod(mod)
    mod.patchScript('script.Common', 'Common.lua')
end
```

Replacements are applied before game startup. Lua mods replacing the same module cannot be enabled together.

See the [language patch guide](docs/language-patches.md) to change translations, menus, images and fonts together.

## How to build

Requires .NET 10.

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModLoader.git
cd EastwardModLoader
dotnet build EastwardModLoader.sln -c Release
```

Installation and mod management code is in `EastwardModLoader.Core`. Other applications can use `LoaderModManager`.
