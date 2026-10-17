# Engine notes

English | [한국어](engine.ko.md)

## Startup

The loader is connected before the original `mock.init` call in `_system/main.lua` inside `_system.g`. The original startup code is kept separately.

The original `MOCK_PRE_GAME_INIT` callback mounts DLC before `.ewmod` packages are activated. Asset and texture indexes and game scripts are read afterwards. Game signals that are not registered yet are connected at `game.init`.

See the public [Game.lua](https://github.com/pixpil/mock/blob/master/mock/core/Game.lua) and [GameModule.lua](https://github.com/pixpil/mock/blob/master/GameModule.lua) sources.

## VFS mounting

Mounting another archive at the same path removes the previous mount. Files missing from the new archive do not fall back to the old archive. The third argument to `mountVirtualDirectory` is the format provider.

Mod ZIPs are therefore mounted at `__eastward_modloader_packages/<mod ID>`. Changed files resolve to `resources/<resource path>` inside that mount, while the original mounts stay in place.

## File reads

`overlay.lua` redirects these read paths:

- File existence checks and directory listings.
- Read modes of `io.open`, `loadfile` and `dofile`.
- Load methods for MOAI streams, buffers, images, textures, fonts and related objects.
- File paths passed to asynchronous loaders.
- Script paths in `GameModule.addGameModuleMapping`.

Write modes use the original paths.

This does not hook every C/C++ file access. Check file reads when integrating another native loader. Replacing or reverting assets during gameplay is not supported.
