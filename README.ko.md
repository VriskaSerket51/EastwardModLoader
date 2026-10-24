# EastwardModLoader

[English](README.md) | 한국어

사용자가 만든 Lua 스크립트와 커스텀 리소스를 Eastward에 불러오는 비공식 모드 로더입니다.

## 사용 방법

1. [EastwardModTool](https://github.com/VriskaSerket51/EastwardModTool)에서 게임 폴더를 열기
2. `Mod > Manage mods...`에서 `Install / update loader`를 선택하기
3. `.ewmod`, Lua 파일 또는 `main.lua`가 있는 폴더를 추가하기
4. 사용할 모드를 체크한 뒤 `Apply enabled mods`를 선택하기

> 설치 시 `_system.g`의 시작 스크립트를 수정하고 원본 아카이브를 백업합니다.
> `Uninstall loader`로 백업을 복원할 수 있습니다.

## 모드 생성

`template.lua`를 복사해서 수정합니다.

```lua
function initMod(mod)
    mod.on('game.init', function(config)
        mod.log('Hello, Eastward!')
    end)
end
```

Lua 모듈을 교체하려면 모드 폴더에 `Common.lua`를 넣고 `initMod` 안에서 아래 함수를 호출합니다.

```lua
mod.patchScript('script.Common', 'Common.lua')
```

> 교체는 게임 시작 전에 적용됩니다. 같은 Lua 모듈을 바꾸는 모드는 함께 사용할 수 없습니다.

## 관련 문서

언어 패치 제작 방법은 [언어 패치 가이드](docs/language-patches.ko.md)를 참고하면 됩니다.

## 빌드 방법

.NET 10이 필요합니다.

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModLoader.git
cd EastwardModLoader
dotnet build EastwardModLoader.sln -c Release
```
