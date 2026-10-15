# Vox Machinae — YawVR (Yaw2) Motion MOD

A MelonLoader mod that drives a **YawVR Yaw2** motion rig from
[Vox Machinae](https://store.steampowered.com/app/334680/Vox_Machinae/),
the VR game where you pilot a 20-metre walking mech.

It hooks the game's own sound events and state, and sends **24 channels** of
telemetry to YawVR GameLink over UDP — so the chair moves with the footfalls,
the landing impacts, the recoil of each weapon and the lateral G of a turn.

Hand-translated **Japanese subtitles** are included as an optional extra.

*日本語の説明は下にあります。*

**[Download the latest release](https://github.com/tyoripoko/VoxMachinae-JP-YawVR-MOD/releases/latest)**

---

## What it does

### Motion output (installed by default)

Twenty-four channels, grouped into five outputs in the bundled GameLink profile:

| Group | Channels |
|---|---|
| Body attitude | Pitch / Roll / Yaw (Yaw follows the minimap heading) |
| Cockpit sway | Cockpit_Pitch / Roll / Yaw |
| Blast shield | Shield_Pitch / Roll / Yaw — opening, closing, ejecting |
| Landing | Landing_Pitch / Roll / Yaw — scaled by fall speed |
| Walking | Walk (vertical bounce, synced to real footfalls) |
| Turning | Turning (lateral G, up to ~7°/s) |
| Vibration | Foot / Landing / Damage / Turning / Jet / Weapon, each with amplitude and frequency |

Per-weapon recoil is tuned individually — the railgun's charge ramps from a fast
fine buzz to a heavy low thud, the drill holds a continuous grind, missiles and
cannons each kick differently. Story-mode cutscenes have their own choreography,
including the zero-g asteroid mission.

This half is language-independent. It works the same whatever language you play in.

### Japanese subtitles (optional)

12,358 lines of dialogue, translated by hand, covering the campaign and in-match
chatter. The overlay renders inside the VR headset through OpenVR, or on the
desktop, and picks the right one automatically by reading the game's own VR mode.

Menus and HUD text stay in English. This mod only subtitles spoken dialogue.

---

## Install

1. Download and extract the release zip.
2. Run **`Install.bat`** (or `インストール.bat` — the same file). It finds your
   Steam install, backs up anything it overwrites, and records what it copied.
3. In YawVR GameLink, load the bundled **"Vox Machinae 24ch"** profile and press START.
4. Launch Vox Machinae from Steam.

For Japanese subtitles, also run **`Add-Japanese-Subtitles.bat`**.
To remove everything, run **`Uninstall.bat`**.

The first launch takes a few minutes while MelonLoader analyses the game.

### Requirements

- Windows 10 / 11 x64
- Yaw2 + YawVR GameLink — for the motion half
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — only if you add the subtitles

MelonLoader v0.6.0 is bundled. Vox Machinae and GameLink are not.

---

## Tuning

Each of the nine mechs has a JSON file under `Mods\Grinder\`, with commented
parameters for walking bounce, landing impact, damage shake and turning G.
Scale the `mult_` values between 0.5x and 2x to taste.

Per-weapon vibration lives in `Mods\Weapon_Vib.txt`, and cutscene choreography
in `Mods\CutScene.txt`. Both reload about two seconds after you save them.

**The per-mech values are not fully tuned.** Only the mechs the author flies
regularly have been dialled in. The campaign-only mech **Scout** shakes quite
hard — scale its `mult_` values down if it is too much.

---

## Building from source

```
VoxTelemetry/         MelonLoader mod — reads game state, sends 24ch UDP
VoxSubtitle/          MelonLoader mod — extracts dialogue, feeds the overlay
VoxSubtitleOverlay/   WPF desktop subtitle window
VoxSubtitleVR/        OpenVR in-headset subtitle overlay
VoxMachinaePlugin/    YawVR GameLink game plugin
data/                 translations, cutscene and per-mech tuning files
```

```bash
dotnet build VoxTelemetry/VoxTelemetry.csproj -c Release
```

The mod projects reference MelonLoader and the game's Il2Cpp assemblies from
your own Vox Machinae install. Those are not redistributed here — point the
`HintPath` entries at your copy. `VoxMachinaePlugin` needs `YawGLAPI.dll` from
your GameLink folder.

---

## Licence

The author's code and the Japanese subtitle data are **MIT** licensed.
See [LICENSE](LICENSE).

Bundled third-party software keeps its own licence; the release zip carries the
full texts in `licenses\` and a per-file breakdown in `THIRD-PARTY-NOTICES.txt`.

### Third-party assets

The images under `VoxMachinaePlugin/Image/` are used only as plugin icons in the
GameLink plugin list, to identify the target game. They are official Vox Machinae
artwork and remain the property of Space Bullet Dynamics Corporation.
**The MIT licence does not apply to them.** They will be replaced or removed
promptly on request from the rights holder.

This is an unofficial, personally made mod with no connection to Space Bullet
Dynamics Corporation or to YawVR. Please do not contact either of them about it.

---

# 日本語

全高20mのロボットに乗るVRゲーム **Vox Machinae** を、モーションリグ
**YawVR Yaw2** と連動させる MelonLoader MOD です。

ゲーム内のサウンドと状態をフックし、**24チャンネル**のテレメトリを
YawVR GameLink へ UDP で送ります。歩行の接地、着地の衝撃、武器ごとの反動、
旋回の横G — 椅子がゲームの動きそのままに動きます。

手作業で翻訳した**日本語字幕**を、任意で追加できます。

**[最新版をダウンロード](https://github.com/tyoripoko/VoxMachinae-JP-YawVR-MOD/releases/latest)**

## 導入

1. リリースのZIPをダウンロードして展開します。
2. **`インストール.bat`** を実行します。Steam の導入先を自動で探し、
   上書きするファイルをバックアップし、入れたファイルを記録します。
3. YawVR GameLink で同梱の **「Vox Machinae 24ch」** プロファイルを読み込み、START。
4. Steam から Vox Machinae を起動します。

日本語字幕が欲しい場合は、続けて **`日本語字幕を追加.bat`** を実行してください。
削除は **`アンインストール.bat`** で、字幕を含めてまとめて消せます。

初回起動は MelonLoader の解析に数分かかります。

### 必要なもの

- Windows 10 / 11 (64bit)
- Yaw2 + YawVR GameLink ※連動を使う場合
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) ※日本語字幕を追加する場合のみ

MelonLoader v0.6.0 は同梱しています。

## 調整

9機体それぞれに `Mods\Grinder\（機体名）.json` があり、歩行・着地・被弾・旋回の
強さを日本語コメント付きで調整できます。`mult_` で始まる値を 0.5〜2倍の範囲で。

武器ごとの振動は `Mods\Weapon_Vib.txt`、カットシーン演出は `Mods\CutScene.txt` です。
どちらも保存すると約2秒で自動リロードされます。

**機体別の振動設定は未調整です。** 作者が主に使う機体以外は詰め切れていません。
とくにキャンペーン専用機体 **Scout** は振動が激しめなので、きついと感じたら
`Scout.json` の `mult_` の値を下げてください。

## ライセンス

作者のコードと日本語字幕データは **MIT** です。同梱物のライセンス全文は
配布ZIPの `licenses\` に、対応表は `THIRD-PARTY-NOTICES.txt` に入っています。

`VoxMachinaePlugin/Image/` の画像は GameLink のプラグイン一覧で対象ゲームを
識別するアイコンとしてのみ使用しており、Vox Machinae の公式アートワーク
（Space Bullet Dynamics Corporation 著作）です。**MITは適用されません。**
権利者からの申し出があれば速やかに差し替え／削除します。

本MODは非公式の個人制作物です。Space Bullet Dynamics Corporation および
YawVR とは一切関係ありません。不具合のお問い合わせを両社へ行わないでください。
