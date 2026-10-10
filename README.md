# Vox Machinae — Japanese Subtitles & YawVR (Yaw2) Motion MOD

[日本語](#日本語) | [English](#english)

A MelonLoader mod for **Vox Machinae** that adds hand-translated Japanese
subtitles and drives a **YawVR Yaw2** motion rig from 24 channels of game
telemetry.

[![Download](https://img.shields.io/github/v/release/tyoripoko/VoxMachinae-JP-YawVR-MOD?label=download&style=for-the-badge)](https://github.com/tyoripoko/VoxMachinae-JP-YawVR-MOD/releases/latest)

---

## English

### What it does

- **Japanese subtitles** — 12,358 hand-translated lines covering every
  character in the campaign and in matches. Displays inside the VR headset
  (OpenVR overlay) or on the desktop, chosen automatically from the game's
  own VR mode.
- **Yaw2 motion output** — walking bounce synced to footfalls, landing
  impacts scaled by fall speed, damage shake, lateral G while turning, jet
  thrust and weapon recoil, sent to YawVR GameLink over UDP on 24 channels.
- **Per-mech tuning** — nine JSON profiles (Catalyst, Decker, Dredge, Drill,
  Goldrush, Hopper, Overhaul, Rook, Scout) with commented parameters.
- **Ready-tuned GameLink profile** included, so GameLink works out of the box.

The two halves are independent: install the subtitles alone if you have no
Yaw2.

### Install

1. Download the latest release zip and extract it anywhere.
2. Run the installer (`インストール.bat`). It locates your Steam install
   from the registry and `libraryfolders.vdf`, backs up anything it
   overwrites, and switches its own messages to English on non-Japanese
   Windows.
3. Launch YawVR GameLink, load the **Vox Machinae 24ch** profile, press
   START, then launch the game from Steam.

**Requirements:** Windows 10/11 x64, Vox Machinae (Steam),
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
MelonLoader is bundled. Yaw2 and YawVR GameLink are only needed for motion.

The first launch takes a few minutes while MelonLoader analyses the game.

See `README_EN.txt` in the release for full documentation, the channel map,
tuning notes and troubleshooting.

### Building from source

```
dotnet build   -c Release -p:GamePath="<path to Vox Machinae>"    # VoxSubtitle, VoxTelemetry
dotnet publish -c Release -o publish_fd                          # overlays
```

The mod projects reference `MelonLoader\net6\MelonLoader.dll`, `0Harmony.dll`
and the IL2CPP-generated `Assembly-CSharp.dll` from your own game folder.
Those are not redistributed here.

### Licence

MIT for the author's own code — see [LICENSE](LICENSE). Bundled third-party
software keeps its own licence; see `THIRD-PARTY-NOTICES_EN.txt` in the
release.

Unofficial and unaffiliated with Space Bullet Dynamics Corporation or YawVR.
Please do not contact either company about this mod.

---

## 日本語

### 何をするMODか

- **日本語字幕** — キャンペーンと対戦中の台詞を手作業で翻訳した12,358行。
  全キャラクター対応。ゲーム本体のVRモードを読み取り、VR内（OpenVRオーバー
  レイ）とデスクトップを自動で切り替えて表示します。
- **Yaw2連動** — 歩行の接地に同期した縦揺れ、落下速度に比例する着地衝撃、
  被弾の揺れ、旋回の横G、ジェット噴射、武器の反動を24チャンネルに分けて
  YawVR GameLink へUDP送信します。
- **機体別の味付け** — 9機体ぶんのJSONプロファイル（Catalyst / Decker /
  Dredge / Drill / Goldrush / Hopper / Overhaul / Rook / Scout）。
  各値に日本語の説明コメント付き。
- **調整済みGameLinkプロファイル同梱** — 読み込むだけで動きます。

字幕と連動は独立しているので、Yaw2をお持ちでなければ字幕だけ使えます。

### 導入

1. 最新リリースのzipをダウンロードし、好きな場所に展開します。
2. `インストール.bat` を実行します。レジストリと `libraryfolders.vdf` から
   Steamの導入先を自動検出し、上書きするファイルをバックアップします。
3. YawVR GameLink を起動して **Vox Machinae 24ch** プロファイルを読み込み、
   START を押してから、Steamでゲームを起動します。

**必要なもの:** Windows 10/11 x64、Vox Machinae（Steam）、
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。
MelonLoaderは同梱済み。Yaw2とGameLinkは連動を使う場合のみ必要です。

初回起動はMelonLoaderの解析に数分かかります。

チャンネル表、調整方法、トラブルシュートはリリース内の `README.txt` に
すべて書いてあります。

### ライセンス

作者が書いたコードはMIT（[LICENSE](LICENSE)）。同梱している第三者
ソフトウェアはそれぞれのライセンスに従います。

Space Bullet Dynamics Corporation および YawVR とは無関係の非公式MODです。
不具合のお問い合わせを両社へ行わないようお願いします。

---

## サードパーティ素材について / Third-party assets

`VoxMachinaePlugin/Image/` の画像（`logo.png` / `recent.png` / `wide.png`）は、
YawVR GameLink のプラグイン一覧で対象ゲームを識別するためのアイコンとして使用しています。
これらは Vox Machinae の公式アートワークであり、著作権は
Space Bullet Dynamics Corporation に帰属します。
**本リポジトリの MIT ライセンスは、これらの画像には適用されません。**
権利者からの申し出があれば速やかに差し替え／削除します。

同梱している MelonLoader / XUnity.AutoTranslator / UniverseLib などの
第三者コンポーネントのライセンスは、配布パッケージ内の
`THIRD-PARTY-NOTICES.txt` を参照してください。

The images under `VoxMachinaePlugin/Image/` (`logo.png` / `recent.png` / `wide.png`)
are used only as plugin icons in the YawVR GameLink plugin list, to identify the
target game. They are official Vox Machinae artwork and remain the property of
Space Bullet Dynamics Corporation. **The MIT license of this repository does not
apply to these images.** They will be replaced or removed promptly upon request
from the rights holder.

For the licenses of bundled third-party components (MelonLoader,
XUnity.AutoTranslator, UniverseLib, etc.), see `THIRD-PARTY-NOTICES.txt`
in the distribution package.
