# Language patches

English | [한국어](language-patches.ko.md)

Package translations from the web editor with menus, images and fonts in an `.ewmod`.

## Translation editor

Select the target language in the [translation editor](https://github.com/VriskaSerket51/eastward-kr/blob/master/README.md), save your translations and press **Export JSON**.

```json
{"language":"ko","translations":[{"path":"config/strings/UIString.xls/menu","key":"exit","value":"나가기"}]}
```

When editing JSON directly, keep `path` and `key` unchanged and edit only `value`. Preserve format arguments, markup and line breaks.

## Creating a mod

1. Open the game directory in EastwardModTool.
2. Select **Mod > Create language patch...**.
3. Enter the language ID, display name and base language. For example: `ko`, `한국어`, `en`.
4. Select the exported `translations.json` as the translation JSON file.
5. Choose any images and fonts you need.
6. Press **Create editing drafts**.
7. Check the sources and previews, then save an `.ewmod` with **Export mod...**.
8. In **Manage mods...**, install or update the loader, add the package and enable it.

Use the same language ID in the web editor and the mod tool. Default text for a new language comes from the selected base language. The patch includes the settings menu and language registration scripts. Use **Include the initial language selection menu** to include the first-run language selection screen.

## Images

Chapter titles and DLC logos use images rather than translation tables.

| Target | Original reference |
| --- | --- |
| Chapter opening | `scene/design/ui/F_Overlay.scene/<group>_CH<number>`, `<group>_Prologue` |
| Chapter list icons | `texture/ui/ChapterTitle/icon/<number>.png` |
| DLC title | `texture/title/DLC/DLCTitle*.png` |

Find the texture and deck references in scenes or proto assets, then export and edit their sources. Choose the images for that language in the language patch dialog. Replacing a shared image also affects other languages.

## Fonts

For a new font, select an existing `font_ttf` and choose a TTF/OTF file. Also select the stylesheet and original font to replace.

Edit bitmap font sources and glyph pages together. Existing font sources can be replaced, but new bitmap fonts are not registered automatically.

Text cannot appear without the required glyphs. Languages such as Arabic also need in-game checks for text shaping and direction.

## Files included in a patch

The tool code license does not grant rights to use or redistribute game resources or fonts. Before distributing a patch, check the permissions and distribution terms for the images, fonts, scripts and translations it includes.
