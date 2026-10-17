# EastwardModLoader

[English](README.md) | 한국어

게임 'Eastward'의 모드 로더입니다.

## 사용 방법

EastwardModTool에서 게임 폴더를 열고 **Mod > Manage mods...** 메뉴를 선택합니다.

**Install / update loader**로 설치한 뒤 `.ewmod`, Lua 파일 또는 `main.lua`가 있는 폴더를 추가하세요. 사용할 모드를 선택하고 **Apply enabled mods**를 누르면 됩니다.

로더 설치, 업데이트 때 `_system.g`를 수정하고 원본은 백업합니다. **Uninstall loader**로 제거하면 원본을 복원합니다.

`.ewmod`는 ZIP으로 마운트해 안의 리소스를 바로 읽습니다. 모드 활성화 때 리소스 `.g`를 다시 만들지 않습니다. 자세한 구조는 [여기](docs/engine.ko.md)에 있습니다.

## 모드 만들기

`template.lua`를 수정해 사용할 수 있습니다. `initMod(mod)`에서 게임 시그널에 연결합니다.

```lua
function initMod(mod)
    mod.on('game.init', function(config)
        mod.log('Hello, Eastward!')
    end)
end
```

Lua 모듈을 교체하는 예제입니다. 모드 폴더에 `Common.lua`를 넣습니다.

```lua
function initMod(mod)
    mod.patchScript('script.Common', 'Common.lua')
end
```

교체는 게임 시작 전에 적용됩니다. 같은 모듈을 바꾸는 Lua 모드는 함께 사용할 수 없습니다.

번역과 메뉴, 이미지, 폰트를 함께 바꾸려면 [언어 패치 가이드](docs/language-patches.ko.md)를 참고하세요.

## 빌드 방법

.NET 10이 필요합니다.

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModLoader.git
cd EastwardModLoader
dotnet build EastwardModLoader.sln -c Release
```

설치와 모드 관리 코드는 `EastwardModLoader.Core`에 있습니다. 다른 앱에서는 `LoaderModManager`를 사용하면 됩니다.
