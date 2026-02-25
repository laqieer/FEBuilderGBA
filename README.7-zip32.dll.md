7-zip32.dll (Optional - Hybrid Mode)
===

**Note:** As of 2025, FEBuilderGBA uses a **hybrid approach** for archive handling:
- **If 7-zip32.dll exists**: Uses native DLL for maximum extraction speed
- **If 7-zip32.dll is missing**: Automatically falls back to [SharpCompress](https://github.com/adamhathcock/sharpcompress) (pure .NET)

The DLL is **optional** - the application works fine without it, just slower for large archives.

---

## Historical Information

7-zip32 was a dll to use 7z created by the common archiver project.
For details and source code, please visit this site:
http://www.madobe.net/archiver/lib/7-zip32.html

License: LGPL 2.1


7-zip32.dll (廃止 - 使用されていません)
===

**注意:** 2025年以降、FEBuilderGBAはネイティブの7-zip32.dllを使用しなくなりました。アーカイブ処理は[SharpCompress](https://github.com/adamhathcock/sharpcompress)という純粋な.NETライブラリに移行され、ネイティブ依存関係がなくなりました。

このファイルは履歴参照のためにのみ保持されています。

---

## 履歴情報

7-zip32は、統合アーカイバプロジェクトで作られた7zを利用するためのdllでした。
詳細や、ソースコードはこちらのサイトをご覧ください。
http://www.madobe.net/archiver/lib/7-zip32.html

License: LGPL 2.1

