# 언어 패치

[English](language-patches.md) | 한국어

번역 웹에서 편집한 내용을 메뉴, 이미지, 폰트와 함께 `.ewmod`로 만들 수 있습니다.

## 번역 웹

[번역 웹](https://github.com/VriskaSerket51/eastward-translate/blob/master/README.ko.md)에서 대상 언어를 선택하고 번역을 저장한 뒤 `Export JSON`으로 내보냅니다.

```json
{"language":"ko","translations":[{"path":"config/strings/UIString.xls/menu","key":"exit","value":"나가기"}]}
```

JSON을 직접 편집할 때는 `path`와 `key`를 유지하고 `value`만 수정합니다. 형식 인수, 마크업, 줄바꿈도 유지해야 합니다.

## 모드 생성

1. EastwardModTool에서 게임 폴더 열기
2. `Mod > Create language patch...` 선택하기
3. 언어 ID, 표시 이름, 기준 언어 입력하기
4. 번역 웹에서 내보낸 `translations.json` 선택하기
5. 필요한 이미지와 폰트 지정하기
6. `Create editing drafts`로 초안 생성하기
7. 소스와 미리보기 확인 후 `Export mod...`로 `.ewmod` 저장하기
8. `Manage mods...`에서 로더 설치 또는 업데이트 후 모드 추가하고 활성화하기

언어 ID는 번역 웹과 같아야 합니다. 한국어라면 언어 ID는 `ko`, 표시 이름은 `한국어`, 기준 언어는 `en` 등으로 지정합니다.

새 언어의 기본 문구는 기준 언어에서 가져옵니다. 설정 메뉴와 언어 등록 스크립트도 패치에 포함됩니다. 최초 언어 선택 화면은 `Include the initial language selection menu`로 포함할 수 있습니다.

## 이미지

챕터 타이틀과 DLC 로고는 번역 테이블 대신 이미지를 사용합니다.

| 대상 | 원래 참조 |
| --- | --- |
| 챕터 시작 | `scene/design/ui/F_Overlay.scene/<그룹>_CH<번호>`, `<그룹>_Prologue` |
| 챕터 목록 아이콘 | `texture/ui/ChapterTitle/icon/<번호>.png` |
| DLC 타이틀 | `texture/title/DLC/DLCTitle*.png` |

씬과 proto에서 텍스처와 덱 참조를 확인한 뒤 소스를 내보내 수정합니다. 수정한 이미지는 언어 패치 창에서 해당 언어에 지정할 수 있습니다. 공통 이미지를 교체하면 다른 언어에도 적용됩니다.

## 폰트

새 폰트를 사용하려면 기존 `font_ttf`를 선택하고 TTF/OTF 파일을 지정합니다. 스타일시트와 교체할 원래 폰트도 함께 선택합니다.

비트맵 폰트는 소스와 글리프 페이지를 함께 수정합니다. 기존 폰트 소스 교체는 지원하지만 새 비트맵 폰트의 자동 등록은 지원하지 않습니다.

번역 문구가 있어도 글리프가 없으면 표시되지 않습니다. 아랍어처럼 글자 조합이나 문장 방향 처리가 필요한 언어는 게임에서 확인해야 합니다.

## 배포 파일

도구 코드의 라이선스와 게임 리소스, 폰트의 재배포 조건은 별개입니다. 패치에 포함할 이미지, 폰트, 스크립트, 번역의 이용 및 배포 조건을 확인해야 합니다.
