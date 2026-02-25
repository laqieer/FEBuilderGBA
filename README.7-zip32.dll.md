7-zip32.dll (Optional - Hybrid Mode)
===

**Note:** As of 2026, FEBuilderGBA uses a **hybrid approach** for archive handling:
- **If 7-zip32.dll exists**: Uses native DLL for maximum extraction speed
- **If 7-zip32.dll is missing**: Automatically falls back to [SharpCompress](https://github.com/adamhathcock/sharpcompress) (pure .NET)

The DLL is **optional** - the application works fine without it, just slower for large archives.

---

## Historical Information

7-zip32 was a dll to use 7z created by the common archiver project.
For details and source code, please visit this site:
http://www.madobe.net/archiver/lib/7-zip32.html

License: LGPL 2.1


7-zip32.dll (オプション - ハイブリッドモード)
===

**注意:** 2026年以降、FEBuilderGBAはアーカイブ処理に**ハイブリッドアプローチ**を使用しています：
- **7-zip32.dllが存在する場合**: ネイティブDLLを使用して最大の展開速度を実現
- **7-zip32.dllがない場合**: 自動的に[SharpCompress](https://github.com/adamhathcock/sharpcompress)（純粋な.NET）にフォールバック

DLLは**オプション**です - アプリケーションはDLLなしでも正常に動作しますが、大きなアーカイブの場合は遅くなります。

---

## 履歴情報

7-zip32は、統合アーカイバプロジェクトで作られた7zを利用するためのdllでした。
詳細や、ソースコードはこちらのサイトをご覧ください。
http://www.madobe.net/archiver/lib/7-zip32.html

License: LGPL 2.1

