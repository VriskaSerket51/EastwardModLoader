# 엔진 참고

[English](engine.md) | 한국어

## 시작 코드

`_system.g`의 `_system/main.lua`에서 원래 `mock.init` 앞에 로더를 연결합니다. 원본 시작 코드는 따로 보관합니다.

`MOCK_PRE_GAME_INIT`의 원래 콜백이 DLC를 마운트한 뒤 `.ewmod`를 활성화합니다. 이후 에셋, 텍스처 인덱스와 게임 스크립트를 읽습니다. 아직 등록되지 않은 게임 시그널은 `game.init`에서 연결합니다.

공개 소스는 [Game.lua](https://github.com/pixpil/mock/blob/master/mock/core/Game.lua), [GameModule.lua](https://github.com/pixpil/mock/blob/master/GameModule.lua)를 참고하세요.

## VFS 마운트

같은 경로에 아카이브를 다시 마운트하면 이전 마운트가 제거됩니다. 새 아카이브에 없는 파일을 이전 아카이브에서 찾지는 않습니다. `mountVirtualDirectory`의 세 번째 인수는 형식 제공자입니다.

따라서 모드 ZIP은 `__eastward_modloader_packages/<모드 ID>`에 마운트합니다. 변경한 파일을 그 안의 `resources/<리소스 경로>`로 연결하고 원래 마운트는 유지합니다.

## 파일 읽기

`overlay.lua`에서 다음 읽기 경로를 연결합니다.

- 파일 존재, 목록 조회.
- `io.open` 읽기 모드, `loadfile`, `dofile`.
- MOAI 스트림, 버퍼, 이미지, 텍스처, 폰트 등의 로드 메서드.
- 비동기 로드에 넘기는 파일 경로.
- `GameModule.addGameModuleMapping`의 스크립트 경로.

쓰기 모드는 원래 경로를 사용합니다.

모든 C/C++ 파일 접근을 가로채는 후크는 아닙니다. 다른 네이티브 로더를 연결할 때는 해당 파일 읽기를 확인해야 합니다. 실행 중 에셋 교체, 되돌리기는 지원하지 않습니다.
