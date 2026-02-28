README
===

[![MSBuild](https://github.com/laqieer/FEBuilderGBA/actions/workflows/msbuild.yml/badge.svg)](https://github.com/laqieer/FEBuilderGBA/actions/workflows/msbuild.yml)
[![GitHub Release](https://img.shields.io/github/v/release/laqieer/FEBuilderGBA)](https://github.com/laqieer/FEBuilderGBA/releases/latest)
[<img src="https://raw.githubusercontent.com/oprypin/nightly.link/master/logo.svg" height="16" style="height: 16px; vertical-align: sub">Nightly Build](https://nightly.link/laqieer/FEBuilderGBA/workflows/msbuild/master)
[![codecov](https://codecov.io/gh/laqieer/FEBuilderGBA/branch/master/graph/badge.svg)](https://codecov.io/gh/laqieer/FEBuilderGBA)

Mirrors for Chinese mainland users (面向中国大陆用户的镜像发布地址): [![Gitee Release](https://gitee-badge.vercel.app/svg/release/laqieer/FEBuilderGBA?style=flat)](https://gitee.com/laqieer/FEBuilderGBA/releases/latest) [<img src="[https://raw.githubusercontent.com/oprypin/nightly.link/master/logo.svg](https://gitee.com/laqieer/FEBuilderGBA/widgets/widget_5.svg)" height="16" style="height: 16px; vertical-align: sub">Gitee Go Build](https://gitee.com/laqieer/FEBuilderGBA/gitee_go/pipelines?tab=release)

## 🚀 Getting Started

### Cloning the Repository

This repository uses **git submodules** for patch management. Clone with:

```bash
git clone --recursive https://github.com/laqieer/FEBuilderGBA.git
```

Or if you already cloned without `--recursive`:

```bash
git submodule update --init --recursive
```

**Note:** The patch repository ([FEBuilderGBA-patch2](https://github.com/laqieer/FEBuilderGBA-patch2)) is maintained separately for independent versioning and faster updates.

## Testing & Coverage

- ✅ **384 tests** passing (0 skipped)
- 📊 [View Full Coverage Report on Codecov](https://codecov.io/gh/laqieer/FEBuilderGBA)
- 🔍 Latest test results and coverage reports available as [GitHub Actions artifacts](https://github.com/laqieer/FEBuilderGBA/actions)
- 🧪 **Test Coverage:**
  - Unit tests for core utilities (RegexCache, LZ77, U, TextEscape)
  - UpdateInfo version tracking and comparison
  - Core package download logic
  - Integration tests for update system

## 🔄 Update System

FEBuilderGBA uses a two-track update model that keeps the application and patch data independent:

### How It Works

| Component | What it contains | How it updates |
|-----------|-----------------|----------------|
| **Core** | FEBuilderGBA.exe, DLLs, config data | Download `FEBuilderGBA_YYYYMMDD.HH.zip` from GitHub Releases or nightly.link |
| **Patch2** | ~44,000 patch files in `config/patch2/` | `git fetch` + `git reset --hard` via the built-in Git updater |

When you check for updates the app compares the remote version against the local assembly build date and shows only the relevant update button(s).

### Updating Patch2 via Git

Patch2 is a [git submodule](https://github.com/laqieer/FEBuilderGBA-patch2) updated independently of core releases.

- **In-app:** Tools → Check for Updates → "Gitでパッチデータを更新します"
- **Manual:** `cd config/patch2 && git pull`
- **First run:** The app detects missing patch2 directories and offers to clone them automatically. If Git is not installed, empty directories are created so the app still starts.

### Benefits

- ✅ **Incremental patch updates** — only changed patch files are transferred via git
- ✅ **Faster patch updates** — no ZIP download or extraction required
- ✅ **Offline-friendly** — patch2 can be updated separately from the core app
- ✅ **Git history** — full audit trail of every patch data change

### Version Information

- **Core version:** Help → About
- **Patch2 version:** `git -C config/patch2 log -1 --format="%h %s"`

[This fork](https://github.com/laqieer/FEBuilderGBA/) is an integration of several forks of FEBuilderGBA and continues development based on it.

README for Korean character table
===

It is from an [unofficial build](https://github.com/delvier/FEBuilderGBA) of FEBuilderGBA that supports Korean character table.

The character table used is **Johab**, only for the Hangul Syllables part. If you want to use another character table like Wansung or Windows-949, you may replace __FE\[678\].tbl__ in __./config/translate/ko_tbl__.

Since this fork is incomplete, there might be some issues that raw code points appear can be occurred, e.g. '@61A0' rather than '마' (0xA061) appears. This is likely because the upper bytes from 0xA0 to 0xDF are used for single-byte representation in Shift JIS and Windows-932.

You should change "Text Encoding in ROM" in Options manually every time the ROM is loaded.

Original README
===

FE_Builder_GBA
===
This is a ROM hacking suite for the Trilogy of Fire Emblem games for the Game Boy Advance.
The editor supports
 * FE6 (The Binding Blade)
 * FE7J/FE7U (The Blazing Blade)
 * FE8J/FE8U (The Sacred Stones)
Essentially, both Japanese and North American releases of all games (with the exception of FE6 being Japan-only) are supported.

Starting from the main screen, FEBuilder supports a wide range of functions from image displaying, importing and export of most data, map remodeling, table editing, community patch management, music insertion, and much more. 

This suite was made at first to help make my Kaitou patch easier to create!

The origin of the name is from 某LAND.  
However, the development language is C#. (We're in this together...)  

Of course, it's open source.
The license of the source code is GPL3.  
Please use it freely with no limitations.

Much of this project's functions are thanks to the data collected by various communities and people.
We would like to thank our hacking predecessors who have publicly shared any analyzed data. 

Details (There is a commentary at the bottom of the page, and the wiki provides other instructions)  
https://dw.ngmansion.xyz/doku.php?id=en:guide:febuildergba:index

Some poorly designed anti-virus software may misidentify FEBuilderGBA as a virus.
This is because FEBuilderGBA uses the WindowsDebugAPI to communicate with the emulator.
Please configure your anti-virus to exclude the FEBuilderGBA directory.
FEBuilderGBA is NOT virus. 
The source code is all available on github, so you can build it yourself if you are worried.


This software has no association with the official products.  
We do not need any donations as we are making this software non-commercial. 

If you really want to donate to someone, donate to the charitable organization supporting the freedom of speech on the Internet, **Freedom of Expression**, including the **EFF Electronic Frontier Foundation**. 

Of course, you are free to write articles about FEBuilderGBA.  
In some cases, you may earn some pocket money through affiliates. :)  
However, please do it at your own risk. :(  

If you have something you do not understand through hacking or the editor, please read "Manual" in "Help".  
If you find a bug that you can not solve by any means, please create report.7z from 'File' -> 'Menu' -> 'Create Report Issue' and consult with the community.
https://discordapp.com/invite/Yzztqqa
Do NOT send your ROM (.gba) directly.

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe


FE_Builder_GBA
===
FE GBA 3部作のROMエディターです。  
FE8J FE7J FE6 FE8U FE7U に対応しています。  

Project_FE_GBA の画面を参考に、  
新規に判明した部分を追加しました。  
画像表示やインポートエクスポート、マップ改造まで幅広い機能をサポートします。  

怪盗パッチを作っているときに思った、こんな機能が欲しい!!という機能をすべて入れ込みました。  

名前の由来は、 某LANDのアレからです。  
ただし、開発言語はC# です。 (中の人達は一緒だしね・・・)  
C#でありますが、特にパフォーマンスに注意しているので、サクサク動くかと思います。  

当然、オープンソース。ソースコードのライセンスは GPL3 です。  
ご自由にご利用ください。  

これを作るのに、いろいろいなデータ、コミニティを参考にしました。  
解析したデータを公開してくれた先人にお礼を申し上げます。  


詳細 (ページ下部に解説集があるよ)  
https://dw.ngmansion.xyz/doku.php?id=guide:febuildergba:index

一部の出来の悪いアンチウイルスソフトが、FEBuilderGBAをウイルスと誤認することがあるようです。
これは、FEBuilderGBAがエミュレータと通信するためにWindowsDebugAPIを利用しているからだと思います。
もしそうなったら、アンチウイルスの設定で、FEBuilderGBAディレクトリを除外してください。
FEBuilderGBAはウイルスではありません。
ソースコードはすべてgithubで公開しているので、心配な場合は自分でビルドしてください。


このソフトウェアは、公式とは一切関係ありません。  
私達は非営利でこのソフトウェアを作っているので、寄付を必要としません。  
どうしても寄付したい方は、EFF 電子フロンティア財団を始めとする、インターネットでの言論の自由、表現の自由を支援している慈善団体にでも寄付してください。  

もちろん、あなたがFEBuilderGBAに関する記事を書くのは自由です。  
場合によっては、アフェリエイトでお小遣いを稼ぐこともできるでしょう。 :)  
ただし、あなたの責任において実施してください。 :(  

もし、hackromでわからないことがあれば、「ヘルプ」の「マニュアル」を読んでください。  
どうしても解決しないバグが発生した場合は、「メニュー」の「ファイル」->「問題報告ツール」から、report.7zを作成して、コミニティに相談してください。
https://discordapp.com/invite/Yzztqqa
(ROMは送信しないでください。)  

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe


FE_Builder_GBA
===
它是FE GBA三部曲的ROM编辑器。  
它对应于 FE8J FE7J FE6 FE8U FE7U.  

参考Project_FE_GBA的屏幕，  
我添加了一个新发现的部分。  
我们支持图像显示，导入导出，地图重构等功能。  

当我制作一个kaitou补丁时，我想要这样的功能  

这个名字的起源是来自 某LAND。  
但是，开发语言是C＃。 （里面的人在一起...）  
它是C＃，但我担心性能，所以我认为它会工作很好。  

当然，开源。源代码的许可证是GPL3。  
请自由使用。  

我参考了各种数据和社区来做到这一点。  
我要感谢发布分析数据的前辈。  


详细信息（页面底部有评论）  
https://dw.ngmansion.xyz/doku.php?id=zh:guide:febuildergba:index

Some poorly designed anti-virus software may misidentify FEBuilderGBA as a virus.
This is because FEBuilderGBA uses the WindowsDebugAPI to communicate with the emulator.
Please configure your anti-virus to exclude the FEBuilderGBA directory.
FEBuilderGBA is NOT virus. 
The source code is all available on github, so you can build it yourself if you are worried.


这个软件与官方无关。  
我们不需要捐赠，因为我们正在制作该软件的非营利。  
如果你真的想捐赠，  
捐赠给支持言论自由的慈善组织，包括EFF电子前沿基金会在内的言论自由  

当然，您可以自由撰写关于FEBuilderGBA的文章。  
在某些情况下，您可以通过会员赚取零用钱。 :)  
但是，请自行承担风险。 :(  

如果你有一些你从hackrom不能理解的东西，请阅读“帮助”中的“手册”。  
如果您发现无法解决的错误，请在'菜单'的'文件' -> '问题报告工具'中创建report.7z，并咨询社区。
https://discordapp.com/invite/Yzztqqa
（请不要发送ROM。）  

SourceCode:
https://github.com/FEBuilderGBA/FEBuilderGBA

Installer:
https://github.com/FEBuilderGBA/FEBuilderGBA_Installer/releases/download/ver_20200130.17.1/FEBuilderGBA_Downloader.exe
