# 언어 패치

[English](language-patches.md) | 한국어

번역 웹에서 편집한 내용을 메뉴, 이미지, 폰트와 함께 `.ewmod`로 만들 수 있습니다.

## 번역 웹

[번역 웹](https://github.com/VriskaSerket51/eastward-kr/blob/master/README.ko.md)에서 대상 언어를 선택하고 번역을 저장한 뒤 **Export JSON**을 누릅니다.

```json
{"language":"ko","translations":[{"path":"config/strings/UIString.xls/menu","key":"exit","value":"나가기"}]}
```

JSON을 직접 편집한다면 `path`와 `key`는 유지하고 `value`만 수정하세요. 형식 인수와 마크업, 줄바꿈도 유지합니다.

## 모드 만들기

1. EastwardModTool에서 게임 폴더를 엽니다.
2. **Mod > Create language patch...** 메뉴를 선택합니다.
3. 언어 ID, 표시 이름과 기준 언어를 입력합니다. 예: `ko`, `한국어`, `en`.
4. 번역 JSON 파일로 웹에서 내보낸 `translations.json`을 선택합니다.
5. 필요한 이미지, 폰트를 지정합니다.
6. **Create editing drafts**로 초안을 만듭니다.
7. 소스와 미리보기를 확인하고 **Export mod...** 메뉴에서 `.ewmod`를 저장합니다.
8. **Manage mods...** 창에서 로더를 설치, 업데이트하고 패키지를 추가한 뒤 활성화합니다.

웹에서 선택한 언어 ID를 모드툴에도 똑같이 입력하세요. 새 언어의 기본 문구는 선택한 기존 언어에서 가져옵니다. 설정 메뉴와 언어 등록 스크립트도 패치에 포함합니다. 최초 언어 선택 화면은 **Include the initial language selection menu**로 선택할 수 있습니다.

## 이미지

챕터 타이틀과 DLC 로고는 번역 테이블 대신 이미지를 사용합니다.

| 대상 | 원래 참조 |
| --- | --- |
| 챕터 시작 | `scene/design/ui/F_Overlay.scene/<그룹>_CH<번호>`, `<그룹>_Prologue` |
| 챕터 목록 아이콘 | `texture/ui/ChapterTitle/icon/<번호>.png` |
| DLC 타이틀 | `texture/title/DLC/DLCTitle*.png` |

씬, proto에서 텍스처와 덱 참조를 확인한 뒤 소스를 내보내 수정합니다. 언어 패치 창에서 해당 언어에 사용할 이미지를 지정하세요. 공통 이미지를 교체하면 다른 언어에도 적용됩니다.

## 폰트

새 폰트는 기존 `font_ttf`를 선택하고 TTF/OTF 파일을 지정합니다. 스타일시트와 교체할 원래 폰트도 선택하세요.

비트맵 폰트는 소스와 글리프 페이지를 함께 수정합니다. 기존 폰트 소스 교체는 지원하지만 새 비트맵 폰트의 자동 등록은 제공하지 않습니다.

번역 문구가 있어도 글리프가 없으면 표시되지 않습니다. 아랍어 등 글자 조합, 문장 방향이 필요한 언어는 게임에서 확인해야 합니다.

## 패치에 포함하는 파일

도구 코드의 라이선스와 게임 리소스·폰트의 이용 및 재배포 조건은 별개입니다. 패치를 배포할 때는 포함할 이미지, 폰트, 스크립트와 번역의 권한 및 배포 조건을 확인하세요.
