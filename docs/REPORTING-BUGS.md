# Reporting Bugs

## The fastest way: the in-app Report a Bug tool

1. In the **Avalonia** GUI, open **Help → Report a Bug…**. In the **WinForms** GUI, open **File → Problem Report Tool** and click **Report a Bug on GitHub**.
2. The app captures a screenshot of the editor window you're working in (falling back to the main window if none is open) and opens a pre-filled GitHub issue form — with your exact release version, ROM version, editor and platform — in your browser.
3. Attach that screenshot: Avalonia saves it to your temp folder and reveals it in your file browser (drag it into the **Screenshot(s)** box); WinForms copies it to the clipboard (paste it with **Ctrl+V**).
4. Fill in **What's wrong?** and any missing details, then submit.

## Manual bug report

If Help → Report a Bug… is not available (CLI crash, startup failure), go to:
https://github.com/laqieer/FEBuilderGBA/issues/new/choose

Select the appropriate template and fill in all fields.

## What to include

- **App version** — shown in Help → About, or from `--version` on CLI
- **ROM version** — shown in the bottom status bar (e.g., FE8U)
- **Platform** — Windows/Linux/macOS/Android and architecture
- **Screenshot** — drag-and-drop, do not link external services
- **report.7z** (optional) — use File → Problem Report Tool; contains version + CRC32, **no ROM data**

## What NOT to include

- Your ROM file (.gba) — never attach it. The report.7z does NOT contain ROM data.
- Personal information.
