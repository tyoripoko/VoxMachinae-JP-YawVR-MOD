// VoxTelemetry.cs  v6.11
// MelonLoader mod for Vox Machinae
//
// ════════════════════════════════════════════════════════════════
// v5.42 → v5.43  フック直接発火方式に変更（ステージ問わず自動タイミング）
//
// 【設計】
//   各サウンドフックで直接エフェクトを発火。
//   固定タイムラインではないので別ステージでもタイミングが自動で合致。
//   状態管理: _launchActive / _returnActive / _returnLiftDone の3変数のみ。
//   シェイク遅延: 各イベントから固定秒数後（0.3s/0.9s/0.4s）はステージ共通。
//
// 【出撃フロー】
//   InternalEngine     → _launchActive=true
//   Play_Launch_Belt_01 → VibON + Belt shake
//   LargeHorz_Open     → 0.9s後に VibOFF + Stop shake
//   Play_Launch_Lift   → VibON + 0.3s後に Lift shake
//   Play_Launch_Detach → 0.4s後に VibOFF + End shake + 終了
//
// 【帰還フロー】
//   BlastShieldClose   → _returnActive=true + VibON
//   LargeHorz_Open     → VibOFF（Lift前のみ）
//   Play_Launch_Lift   → VibON + 0.3s後に Lift shake + _returnLiftDone=true
//   LargeHorz_Close    → VibOFF（Lift後のみ）
//   Play_Launch_Belt_01 → VibON + Belt shake
//   Stop_Launch_Belt_01 → VibOFF + Stop shake
//   Play_Launch_Detach → Stop shake + 終了
//
// v5.41 → v5.42  タイムライン駆動（固定時刻）← 別ステージでずれるため廃止
//
// 【設計変更】
//   フックは InternalEngine（出撃）と BlastShieldClose（帰還）の2点のみ。
//   以降は static readonly TLEvt[] 配列を OnUpdate でタイマー駆動。
//   全フラグ・全状態変数を廃止し、コードを大幅簡素化。
//
// 【出撃タイムライン（InternalEngine=0秒）】
//   +3.14s  VibON + Belt shake
//   +25.16s VibOFF + Stop shake
//   +29.76s VibON
//   +30.06s Lift shake
//   +49.71s VibOFF + End shake
//   +52.00s Done
//
// 【帰還タイムライン（BlastShieldClose=0秒）】
//   +0.00s  VibON
//   +0.15s  VibOFF
//   +2.05s  VibON
//   +2.35s  Lift shake
//   +12.08s VibOFF
//   +30.07s VibON + Belt shake
//   +44.39s VibOFF + Stop shake
//   +47.00s Done
//
// ※ステージによりタイミングが異なる場合はLaunchTL/ReturnTL配列を調整。
//
// v5.40 → v5.41  Lift下降中ガタッ消失バグ修正（_returnLiftDone追加）
//
// 【原因】
//   Lift下降中(+4s〜+10s)に Play_LaunchDoor_LargeHorz_Close が発火し
//   _returnLiftStarted=true の条件でそのまま微振動を停止していた。
//   Lift下降中の Close は無視すべきだった。
//
// 【修正】
//   _returnLiftDone フラグを追加。
//   Play_Launch_Belt_02（Lift完了のサイン）受信後のみ
//   Play_LaunchDoor_LargeHorz_Close で停止を許可。
//   Lift下降中の Close はすべて無視される。
//
// v5.39 → v5.40  帰還カットシーン終了後暴走・一時停止バグ修正
//
// 【修正①】カットシーン終了後も帰還エフェクトが暴走
//   原因: Play_Launch_Detach後5秒でBlastShieldCloseが再発火して
//         _returnTransporting=true に再セットされていた。
//   修正: Play_Launch_Detach受信後 _returnCooldown=15秒 をセット。
//         BlastShieldCloseの帰還開始にクールダウンチェックを追加。
//
// 【修正②】Lift下降後の一時停止中も振動が続く
//   原因: Lift後の一時停止は Play_LaunchDoor_LargeHorz_Close が
//         トリガーだが未フック。
//   修正: Play_LaunchDoor_LargeHorz_Close フックを追加。
//         _returnLiftStarted=true 後に発火したときのみ停止。
//         Belt_01で再開（既存）。
//
// v5.38 → v5.39  帰還カットシーン2バグ修正（_returnLiftStarted追加等）
//
// 【修正①】Lift下降中にガタッ・微振動が止まる
//   原因: Lift下降中に2回目のPlay_LaunchDoor_LargeHorz_Openが発火し
//         _returnPaused=true になっていた。
//   修正: _returnLiftStarted フラグを追加。Lift開始後は
//         LargeHorz_Openの停止処理を無効化。
//
// 【修正②】帰還カットシーン終了後もガタッが暴走
//   原因: 2回目以降の帰還でPlay_Launch_Detachが発火せず
//         _returnTransportingがリセットされない。
//   修正: MechRoot検出時に _returnTransporting も強制リセット。
//
// v5.37 → v5.38  各シェイク開始タイミング0.3秒早める
//
//   停止ジョルト遅延: 1.2秒 → 0.9秒
//   終了ジョルト遅延: 0.7秒 → 0.4秒
//   Lift開始シェイク: 即時発火 → 0.3秒後に発火（_cutsceneLiftDelayTimer追加）
//
// v5.36 → v5.37  停止/終了ジョルト開始1秒早める
//
//   停止ジョルト遅延: 2.2秒 → 1.2秒
//   終了ジョルト遅延: 1.7秒 → 0.7秒
//   持続時間: 1.5秒（変更なし）
//
// v5.35 → v5.36  停止/終了ジョルト遅延+0.7秒、Lift持続1秒に変更
//
//   停止ジョルト遅延: 1.5秒 → 2.2秒（+0.7秒）
//   終了ジョルト遅延: 1.0秒 → 1.7秒（+0.7秒）
//   Lift開始シェイク持続: 1.5秒 → 1.0秒
//
// v5.34 → v5.35  カットシーンPitchシェイクを _landP.Trigger() に刷新
//
// 【根本原因の確定】
//   ch0/1/2（Pitch/Roll/Yaw）はスムージング（指数移動平均 SMOOTHING=0.2）を通る。
//   カットシーン中は MechRoot==null で pitch=0 が毎フレーム計算され、
//   _smoothPitch ≈ 0 に収束するため、加算値が吸収されて全く動かなかった。
//
// 【解決策】
//   ガタッで動作確認済みの _landP.Trigger() を使い、
//   Landing_Pitch ch8 で 1.5秒の地震シェイクを発火する。
//   ShakeAxis は dur 後に自然に 0 に戻るため原点復帰も保証。
//
//   Belt開始/停止/Lift開始/終了: _landP.Trigger(AMP=150, HZ 2〜4, 1.5秒)
//   帰還の同等タイミングも同じパラメータ。
//
// v5.33 → v5.34  帰還カットシーン振動パターン追加
//
// 【帰還タイムライン（Play_Launch_Detach基準）】
//   -44.75s  BlastShieldClose → 帰還開始、微振動+ガタッ開始
//   -42.63s  Play_Launch_Lift → 下降開始ジョルト pitch=-30度、微振動継続
//   -38.35s  Play_LaunchDoor_LargeHorz_Open → 微振動一時停止
//   -14.61s  Play_Launch_Belt_01 → 微振動再開、+30度加速ジョルト
//    -0.50s  Stop_Launch_Belt_01 → 停止ジョルト pitch=-30度
//     0.00s  Play_Launch_Detach → 到着、エフェクト終了
//
// v5.32 → v5.33  Play_Launch_Detach未発火時のFoot_Vib永続バグ修正
//                Foot_Vib 永続振動バグを修正
//
// 【原因】Play_Launch_Detach が発火しないセッションでは
//   _cutsceneTransporting が true のまま永遠にリセットされず
//   ガタッパルス・Foot_Vib が続き続けた。
//
// 【修正】MechRoot 検出時（= ゲーム操作開始）に
//   _cutsceneTransporting / _cutsceneVibAmp / _cutsceneJoltTimer を
//   強制ゼロリセットするフォールバックを追加。
//
// v5.31 → v5.32  カットシーンジョルトをステップ関数に完全刷新
//
// 【根本原因の解明】
//   PITCH_NORM=30 → pitch=1.0 がちょうど30度。
//   以前は -144.0 等を送っていたが、値が ±1.0 を超えると
//   GameLink/YawVR が物理的なストッパーで同じ最大角になり
//   値を増やしても変化しなかった。
//
// 【新実装: ステップ関数】
//   CUTSCENE_HOLD_DUR=0.5秒間、固定値 ±1.0 を送信
//   タイマー終了後 即座に pitch=0 スナップ → 原点復帰
//
//   Belt開始   → pitch=+1.0(+30度) 0.5秒
//   停止ジョルト → pitch=-1.0(-30度) 0.5秒
//   Lift開始   → pitch=+1.0(+30度) 0.5秒
//   終了ジョルト → pitch=-1.0(-30度) 0.5秒
//
// v5.30 → v5.31  Pitch 8倍化試み（無効だったため刷新）
//
//   CUTSCENE_STOP_P:  -18.0 → -144.0
//   CUTSCENE_ACCEL_P:   2.4 →   19.2 / 1.5秒→1秒
//   CUTSCENE_END_P:   -18.0 → -144.0 / 2.0秒→1秒
//   Belt_01開始時に前進加速ジョルト追加（FlagCutsceneBeltShake）
//
// v5.29 → v5.30  カットシーンPitch揺れ3倍強化
//
// v5.27 → v5.28  Pitch軸が弱い根本原因を修正（差し戻し）
//
// 【原因】
//   MULT_DAMAGE_PITCH = 1.0  × GameLink Damage_Pitch=3 → 実効 3.0
//   MULT_DAMAGE_ROLL  = 3.0  × GameLink Damage_Roll=3  → 実効 9.0
//   → Damage チャンネルで Roll/Yaw が Pitch の3倍の実効出力になっていた。
//
// 【修正】
//   MULT_DAMAGE_PITCH: 1.0 → 3.0
//   → 全Damageチャンネルの実効出力を統一（Pitch=Roll=Yaw=9.0）
//
// v5.26 → v5.27  カットシーンジョルトを ch0/1/2 に変更（原点復帰修正）
//
// 【根本原因】
//   Damage/Landing/Escape チャンネルはオーバーレイ（揺れ振幅を指示する）。
//   値が0になっても YawVR が自動でセンターに戻る保証がない。
//
// 【修正】ch0/1/2（Pitch/Roll/Yaw）は絶対位置コマンド。
//   0を送れば YawVR が自力でセンターに駆動する。
//   これがすべての揺れパターンに共通する原点復帰の正しい実装。
//
//   ジョルト中: pitch += _cutsceneJoltP * decay（線形減衰）
//   タイマー終了後: _cutsceneJoltP=0 → pitch=0 → センターへ駆動
//
// v5.25 → v5.26  揺れ後の斜め停止バグ修正 + 揺れ幅2倍
//
// 【原因】タイマー終了直前のフレームで decay が微小な非ゼロ値になり
//   YawVRがその値で停止・保持してしまっていた。
// 【修正】_cutsceneDmgForceZero フラグを追加。タイマー終了時にセット、
//   次フレームの SendTelemetry で dmgP/R/Y を強制的に 0 送信してから解除。
// 【揺れ幅2倍】
//   CUTSCENE_STOP_P: -3.0 → -6.0
//   CUTSCENE_STOP_RY: 1.2 → 2.4
//   CUTSCENE_END_P: -3.0 → -6.0
//   CUTSCENE_END_RY: 3.0 → 6.0
//
// v5.24 → v5.25  停止・終了ジョルトを3倍に強化
//
// v5.23 → v5.24  カットシーンジョルトを Damage_Pitch/Roll/Yaw に変更
//
// 【変更】
//   停止/発進/終了ジョルトを _escP から Damage_Pitch/Roll/Yaw (ch5/6/7) に変更。
//   ShakeAxis（sin波）ではなく線形減衰の単一方向フラットな「G感」で表現。
//
//   停止（ノーズダウン）: Damage_Pitch=-1.0, Roll/Yaw=+0.4, 2秒減衰
//   発進（ノーズアップ）: Damage_Pitch=+0.8, Roll/Yaw=+0.25, 1.5秒減衰
//   終了（全軸最大）:    Damage_Pitch=-1.0, Roll/Yaw=+1.0, 2秒減衰
//
//   実効出力: Pitch×1.0, Roll×3.0, Yaw×3.0
//
// v5.22 → v5.23  カットシーンジョルトを _escP に変更
//
// 【原因】_landP は MULT_LANDING=1.5 で最大出力±1.5。
//   amp=0.90→1.80 にしても clamp後の変化は 1.35→1.50 のみ（11%増）。
//   感じ取れない理由はチャンネル選択が間違っていた。
//
// 【修正】停止/加速/終了ジョルトを _escP/_escR/_escY に変更。
//   MULT_ESCAPE=4.0 → 同じ amp で出力±4.0（2.7倍）
//
// 【追加】加速ジョルト（Play_Launch_Lift → FlagCutsceneAccelShake）
//   エレベーター開始の急加速G再現。
//
// 【終了ジョルト強化】全軸 amp=1.0 → 出力±4.0（Roll/Yaw含む）
//
// v5.21 → v5.22  2回目以降の出撃シーンで一時停止しないバグを修正
//
// 【原因】_cutsceneLiftStarted が1回目の Play_Launch_Lift で true に
//   なった後リセットされず、2回目の InternalEngine 開始時に残っていた。
//   その結果 Play_LaunchDoor_LargeHorz_Open の停止ガードが常に
//   !_cutsceneLiftStarted = false になり一時停止が発火しなかった。
// 【修正】InternalEngine フックで _cutsceneLiftStarted と
//   _cutscenePaused を必ずリセットするよう変更。
//
// v5.20 → v5.21  カットシーン揺れ演出強化
//
// 【変更】
//   ①ガタッを Foot_Vib → _landP（Landing_Pitch ch8）に変更
//     方向性のある Pitch 軸の揺れのみで表現。
//   ②一時停止ジョルト追加（LargeHorz_Open後1.5秒 → 微振動停止と同時に発火）
//     _landP=0.90 / _landR=_landY=0.35 / HZ 1.5〜2.5 / 2秒
//   ③エレベーター終了ジョルト追加（Detach後1秒 → 微振動停止と同時に発火）
//     ②と同じパラメータ
//
// v5.19 → v5.20  ガタッ半減、微振動延長
//
// 【変更】
//   ガタッ AMP: 300 → 150
//   ガタッ 持続: 0.35秒 → 0.175秒
//   一時停止前の微振動: 即時停止 → LargeHorz_Open から1.5秒後に停止
//   エレベーター終了後の微振動: Detach から1秒後に停止
//
// v5.18 → v5.19  エレベーター中の微振動停止バグを修正
//
// 【原因】Play_LaunchDoor_LargeHorz_Open がエレベーター稼働中にも
//   再発火し、誤って _cutscenePaused=true にしていた。
// 【修正】Play_Launch_Lift 以降は _cutsceneLiftStarted=true にして
//   LargeHorz_Open の停止処理を完全に無効化。
//
// v5.17 → v5.18  ガタッ間隔を 1.5秒 に変更
//
// v5.16 → v5.17  カットシーン一時停止対応 + ガタッ大幅強化
//
// 【変更】
//   一時停止フェーズ制御:
//     Play_LaunchDoor_LargeHorz_Open (+27.73s) → 微振動OFF（_cutscenePaused=true）
//     Play_Launch_Lift                (+28.73s) → 微振動再開
//
//   ガタッパルス強化:
//     AMP: 80 → 300
//     HZ:  20 → 5（低HZ=指向性のある大きな揺れ）
//     持続: 0.15s → 0.35s
//
//   ガタッ直前に微振動をゼロにしてからパルスを発火、
//   パルス終了後に連続微振動値へ復帰する。
//
// v5.15 → v5.16  格納庫カットシーン搬送エフェクト フェーズ制御追加
//
// 【変更】
//   フェーズ分析（InternalEngine=0秒基準）:
//     0.00s  InternalEngine → 搬送エフェクト開始（微振動なし・停止中）
//     0.00s  CockpitShake   → 停止ジョルト（_landP ch8、Pitch下方向）
//     2.23s  Play_Launch_Belt_01 → 微振動(AMP=25/HZ=8)＋ガタッ開始
//    28.73s  Play_Launch_Lift    → そのまま継続
//    48.26s  Play_Launch_Detach  → 全エフェクト終了
//
//   ①停止中は微振動ゼロ（InternalEngine後Belt_01まで振動しない）
//   ②停止ジョルトを _landP（Landing_Pitch ch8）に変更
//     → BlastShieldOpen が _escP を上書きする問題を回避
//     amp=0.8 / HZ 1.5〜2.5 / 1.8秒（前方停止の衝撃=Pitch下揺れ）
//   ③ガタッ発火ログ追加（[Cutscene] ガタッパルス発火）
//
// v5.14 → v5.15  格納庫カットシーン搬送エフェクト完全再設計
//
// 【原因（v5.14が効かなかった理由）】
//   ① 停止衝撃: CockpitShake で _escP(amp=0.75) を設定するが、
//     0.08s後に BlastShieldOpen が _escP を amp=0.30 で上書き → 無効化。
//   ② 搬送エフェクト: _walkCalibrated が +2.94s で真になり終了。
//     実際のカットシーン映像は MechRoot 検出まで 53秒 続く。
//
// 【修正】
//   - _escP を一切使わず Foot_Vib 1チャンネルに集約。
//   - 終了トリガー: Play_Launch_Detach (+48.27s) に変更。
//   - 効果:
//       停止ジョルト: CockpitShake → Foot_Vib AMP=100/HZ=5/1.5秒
//       連続微振動:   搬送中        → Foot_Vib AMP=25/HZ=8 を常時出力
//       ガタッ:       0.45秒ごと    → Foot_Vib AMP=80/HZ=20/0.15秒パルス
//
// v5.13 → v5.14  格納庫カットシーン搬送エフェクト拡張
//
// 【変更】
//   ①停止衝撃 3倍: CockpitShake の Pitch amp 0.25 → 0.75
//   ②レール搬送中の連続微振動:
//     InternalEngine (MechRoot未検出) で開始。
//     Foot_Vib ch14/15 に AMP=25/HZ=8 の微弱振動を継続送信。
//     Walk キャリブレーション完了で自動終了。
//   ③定期的ガタッパルス:
//     搬送中 0.45秒ごとに Pitch amp=0.18/HZ 8〜12/0.12秒 のパルスを発火。
//
// v5.12 → v5.13  格納庫カットシーン Pitch シェイク追加
//
// 【変更】
//   MechRoot未検出時（格納庫カットシーン等）に CockpitShake/CockpitRattle が
//   発火した際、Pitchチャンネルを1回だけ小さく揺らす。
//   HZ 2〜3 / amp 0.25 / 1.5秒。3秒クールダウンで重複防止。
//   ゲーム中（MechRoot検出後）の動作は変更なし。
//
// v5.11 → v5.12  BlastShieldOpen/Close の振動時間を個別設定
//
// 【変更】
//   BlastShieldOpen:
//     通常振動 2.3秒（HZ 3〜6）→ ファイナルシェイク 0.3秒（HZ 8〜15）
//   BlastShieldClose:
//     通常振動 1.5秒（HZ 3〜6）→ ファイナルシェイク 0.3秒（HZ 8〜15）
//   ESC_DUR_SHIELD_OPEN / ESC_DUR_SHIELD_CLOSE 定数を追加。
//   FlagShieldDur で呼び出し側からOpen/Closeを区別。
//
// v5.10 → v5.11  BlastShield 振動調整 + 2.6秒後に強い揺れを追加
//
// 【変更】
//   ESC_FREQ_MIN_SHIELD: 8  → 3
//   ESC_FREQ_MAX_SHIELD: 15 → 6
//   ESC_DUR_SHIELD:      2.7 → 2.6秒
//   ファイナルシェイク追加:
//     2.6秒後に amp=0.90 / HZ=8〜15 / 0.3秒 の強い揺れを発火
//
// v5.09 → v5.10  BlastShield 振動速度・時間調整
//
// 【変更】
//   ESC_FREQ_MIN_SHIELD: 1  → 8
//   ESC_FREQ_MAX_SHIELD: 1.5 → 15
//   ESC_DUR_SHIELD:      2.5 → 2.7秒
//
// v5.08 → v5.09  敵撃破時の EjectJet 誤発動を修正
//
// 【原因】
//   敵ロボットが撃破されるとパイロットの脱出で EjectJet が発火する。
//   コードは自機/敵機を区別せず全 EjectJet に TriggerEscape していた。
//   ログより、敵撃破ごとに EjectJet×4 → Escape継続(0.70)×4 が確認された。
//
// 【修正】
//   EjectJet に WEAPON_DIST_SELF(30m) 距離チェックを追加。
//   敵機の EjectJet は遠距離 → スキップ。
//   自機の EjectJet はコクピット近傍 → 通過。
//
// v5.07 → v5.08  Damage ch5-7 に加速度デッドゾーンを追加
//
// 【原因特定】
//   Damage_Vib (ch18-19) はキャリブレーション後に一切発火していない。
//   ユーザーが感じていた「Damage反応」は ch5-7 (Damage_Pitch/Roll/Yaw)
//   であり、UpdateDamageAccel() が近くの爆発によるカメラシェイクを
//   MULT_DAMAGE_ROLL/YAW=3.0 で3倍増幅して出力していた。
//
// 【修正】
//   DMG_DEADZONE = 0.1f を追加（正規化後の値、≒5m/s²）。
//   この閾値未満の加速度はゼロ出力とし、近接爆発等のノイズを除去する。
//   実際の被弾時はより大きい加速度が生じるため影響しない。
//   デッドゾーン値はコード定数なので必要に応じて調整可能。
//
// v5.06 → v5.07  BlastShield Jet_Vib誤発動修正 + 距離チェック診断ログ追加
//
// 【Bug修正】BlastShieldOpen/Close で Jet_Vib が 3.5秒発動していた
//   原因: FlagEscape処理内の `_vibJetTimer = VIB_ESCAPE_DUR` が
//         isEject 関係なく無条件実行されていた。
//   修正: 当該行を削除。Jet_VibはFlagVibEscape(isEject時のみセット)が処理する。
//
// 【診断ログ追加】FootImpact/CockpitShake/Rattle/MechGetsHit
//   - comp=null の場合（VoxSoundがComponentでない）→ログ出力
//   - comp!=null の場合 → 実際の dist を [Diag] でログ出力
//   - 次ビルドで原因特定後に距離チェックまたは別方式に修正予定
//
// v5.05 → v5.06  BlastShield 振動をきっかり 2.5秒で停止
//
// 【原因】
//   BlastShieldOpen/Close は同一フレームに4回連続発火する。
//   毎回 TriggerEscape が呼ばれてタイマーがリセットされるため
//   実効継続時間が不定になっていた。
//
// 【修正】
//   ESC_DUR_SHIELD: 1.5f → 2.5f
//   _shieldEscapeCooldown を追加。最初の1発のみ処理し、
//   2発目以降は ESC_DUR_SHIELD 秒が経過するまで無視する。
//
// v5.04 → v5.05  Walk チャンネルのデフォルト値を2倍に
//
// 【変更】
//   MULT_WALK: 1.5 → 3.0
//
// 【距離チェック未実装チャンネルの調査結果】
//   Escape系（BlastShieldOpen/Close・AlertEject・EjectJet）は
//   自機コクピット固有のサウンド名のため距離チェック不要。
//   距離チェックが必要な全チャンネルへの対応は v5.04 で完了。
//
// v5.03 → v5.04  Walk_Vib / Damage_Vib を自機のみに限定
//
// 【原因】
//   FootImpact / CockpitShake / CockpitRattle / MechGetsHit は
//   敵ロボットのサウンドにも無条件で発火していた。
//
// 【修正】
//   武器系と同様に WEAPON_DIST_SELF(30m) 以内のサウンドソースのみ
//   反応するよう距離チェックを追加。
//
// v5.02 → v5.03  GameLink側 Multiplier設定をコードに統合
//
// 【変更】
//   以下の GameLink Multiplier値を逆算してコード定数に統合。
//   GameLink側はすべて Multiplier=1 で運用可能。
//
//   Damage_Roll=3    → MULT_DAMAGE_ROLL  : 1.0 → 3.0
//   Damage_Yaw=3     → MULT_DAMAGE_YAW   : 1.0 → 3.0
//   Escape_Pitch=2   → MULT_ESCAPE_PITCH : 2.0 → 4.0
//   Escape_Roll=2    → MULT_ESCAPE_ROLL  : 2.0 → 4.0
//   Escape_Yaw=2     → MULT_ESCAPE_YAW   : 2.0 → 4.0
////   Foot_Vib_Amp=3   → FOOT_WALK/LAND_AMP (→ ch14: Foot_Vib_Amp): 100 → 300
//   Foot_Vib_Hz=1.2  → FOOT_WALK_HZ       (→ ch15: Foot_Vib_Hz) :  15 →  18
//                    → FOOT_LAND_HZ       (→ ch15: Foot_Vib_Hz) :  50 →  60
//
// v5.01 → v5.02  武器振動デフォルト値を Weapon_Vib.txt 調整済み値に統合
//
// 【変更】
//   - SetDefaultWeaponVibParams を Weapon_Vib.txt 最終調整値に更新
//     （GameLink Multiplier=1 前提、実効レンジ 0-100）
//   - launcher_guided を削除（ゲーム未使用）
//   - RAIL_START_AMP/END_AMP/START_HZ/END_HZ/CURVE をtxt値に合わせ更新
//   - DRILL_AMP/HZ をtxt値に合わせ更新
//   - Weapon_Vib_Amp=3.0 前提のコメントを Multiplier=1 に修正
//
// v5.00 → v5.01  Railgunチャージ序盤の無変化問題を修正
//
// 【原因】
//   SmoothStep は t=0 での傾きが数学的にゼロになる設計のため、
//   RAIL_CURVE 値に関わらずチャージ開始から約1秒間 amp/hz がほぼ
//   変化しなかった。
//
// 【修正】
//   SmoothStep を削除し Pow のみに変更。
//   t=0 から滑らかに変化し始めるようになった。
//   RAIL_CURVE の意味も更新（コメント参照）。
//
// v4.99 → v5.00  Railgunチャージ振動のカーブを滑らかに
//
// 【変更】
//   - チャージ増大を線形(Lerp)→ SmoothStep + 指数カーブ に変更
//   - RAIL_CURVE 定数を追加（デフォルト 1.5）
//     1.0: なだらかなS字カーブ
//     1.5: 序盤は微動、後半に向けて加速（デフォルト）
//     2.0: さらに後半集中
//   - Weapon_Vib.txt の RAIL_CURVE|値|CONST で実行中に変更可能
//   - 最小値・最大値・チャージ時間は変更なし
//
// v4.98 → v4.99  レーザー振動パラメータを Weapon_Vib.txt で完全設定可能に
//
// 【変更】
//   - LASER_VIB_TIMEOUT を const → static に変更
//   - Weapon_Vib.txt の CONST セクションで LASER_VIB_TIMEOUT を上書き可能に
//     例: LASER_VIB_TIMEOUT|3.5|CONST
//   - LargeLaserMuzzle / SmallLaserMuzzle の AMP/HZ/タイムアウト は
//     SOUND エントリの 秒フィールド で個別指定可能
//     例: LargeLaserMuzzle|30|20|2.5|SOUND
//         （amp=30, hz=20, タイムアウト=2.5秒）
//
// v4.97 → v4.98  レーザー振動を照射中ずっと一定に維持
//
// 【変更】
//   - レーザー用独立ステート (_laserVibTimer / _laserVibAmp / _laserVibHz) 追加
//   - LargeLaserMuzzle / SmallLaserMuzzle 検出時にタイマーをリセットして
//     減衰なしの一定振動を継続（デフォルト timeout=3.5秒）
//   - 照射を止めると最後の Muzzle イベントから LASER_VIB_TIMEOUT 秒後に自動停止
//   - WeaponVibTable の LargeLaserMuzzle / SmallLaserMuzzle の dur が
//     タイムアウト秒数として使われる（外部ファイルで変更可能）
//   - isMuzzle ブロックからレーザーを除外して二重処理を防止
//
// v4.96 → v4.97  バグ修正
//
// 【修正】
//   - [致命的] ステージ遷移後に _respawnFrozen が解除されず
//     BlastShieldOpen なしの遷移（チュートリアル等）で全チャンネルが
//     ゼロ送信のまま固着する問題を修正（→ TryScan でMechRoot再検出時に解除）
//   - [Walk_Vib] "FootImpact" の大文字小文字不一致で Walk_Vib が
//     未発火だった問題を修正（"foot" の Contains が case-sensitive）
//   - [コード品質] Weapon_Fire_Patch 内の未使用ローカル WeaponVibParams
//     辞書（デッドコード）を削除
//
// v4.93 → v4.94  Weapon_Vib Multiplier 3倍対応
//
// 【変更】
//   - 武器振動パラメータを1/3に調整
//   - GameLink Weapon_Vib Multiplier=3 前提（実効レンジ 0-300）
//   - Laser/Pulsar: 高周波微振動追加
//
// 【対応武器】
//   cannon_large/small/flak_mine/flak, minigun/minigun_alt, gatling,
//   launcher_8pack/3pack/cruise,
//   mallet, railgun, riot, shotgun, grenade_launcher
//
// 【除外】launcher_guided（ゲーム未使用）
//
// 【機能】
//   - Yaw/Turning: blips.world.yから取得（HMD独立）
//   - Turning: デッドゾーン10°/s + 2乗カーブ
//   - 安定化待機: 5秒（レーダーアニメーション対策）
//   - Escape: EjectJet継続対応
//   - Smoothing: 0.2（Yaw/Pitch/Roll/Walk/Turning）
//
// 【GameLink Multiplier設定】
//   Pitch=1.0, Roll=1.0, Yaw=1.0
//   Walk=1.5, Turning=5.0
//   Damage_P/R/Y=1.0, Landing_P/R/Y=1.5, Escape_P/R/Y=2.0
//   Foot_Vib=1.0, Turning_Vib=1.0, Damage_Vib=1.0, Jet_Vib=1.0
//   Weapon_Vib_Amp=1.0, Weapon_Vib_Hz=1.0（実効0-100）
//
// 【24チャンネル構成】
//   ch0:  Yaw            ch1:  Pitch         ch2:  Roll
//   ch3:  Walk           ch4:  Turning
//   ch5:  Damage_Pitch   ch6:  Damage_Roll   ch7:  Damage_Yaw
//   ch8:  Landing_Pitch  ch9:  Landing_Roll  ch10: Landing_Yaw
//   ch11: Escape_Pitch   ch12: Escape_Roll   ch13: Escape_Yaw
//   ch14: Foot_Vib_Amp   ch15: Foot_Vib_Hz   (Walk+Landing統合)
//   ch16: Turning_Vib_Amp ch17: Turning_Vib_Hz
//   ch18: Damage_Vib_Amp ch19: Damage_Vib_Hz
//   ch20: Jet_Vib_Amp    ch21: Jet_Vib_Hz
//   ch22: Weapon_Vib_Amp ch23: Weapon_Vib_Hz
// ════════════════════════════════════════════════════════════════

using HarmonyLib;
using MelonLoader;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using UnityEngine;

[assembly: MelonInfo(typeof(VoxTelemetry.VoxTelemetryMod), "VoxTelemetry", "6.11.0", "tyoripoko")]
[assembly: MelonGame("SpaceBulletDynamicsCorporation", "VoxMachinae")]

namespace VoxTelemetry
{
    struct ShakeAxis
    {
        float amp, freq, dur, t, phase;

        public float Value
        {
            get
            {
                if (dur <= 0f || t >= dur) return 0f;
                float progress = t / dur;
                float env = Mathf.Cos(progress * Mathf.PI * 0.5f);
                return amp * env * Mathf.Sin(2f * Mathf.PI * freq * t + phase);
            }
        }

        public void Trigger(float a, float freqMin, float freqMax, float durMin, float durMax)
        {
            amp = a;
            freq = freqMin + UnityEngine.Random.value * (freqMax - freqMin);
            dur = durMin + UnityEngine.Random.value * (durMax - durMin);
            t = 0f;
            phase = UnityEngine.Random.value * 2f * Mathf.PI;
        }

        public void Step(float dt) { if (t < dur) t += dt; }
        public void Reset() { amp = 0f; dur = 0f; t = 0f; }  // 残留値を即ゼロ
    }

    public class VoxTelemetryMod : MelonMod
    {
        private const string PLUGIN_IP   = "127.0.0.1";
        private const int    PLUGIN_PORT = 20042;

        // ═══════════════ ↓↓↓ ここから追加 ↓↓↓ ═══════════════
        // ── 字幕送信用 ─────────────────────────────────────────────────
        private const int SUBTITLE_PORT = 20043;
        internal static UdpClient SubtitleUdp;

        // ────────────────────────────────────────────────────────────────
        // Multiplier設定（GameLinkデフォルト値）
        // ────────────────────────────────────────────────────────────────
        private static float MULT_PITCH        = 1.0f;
        private static float MULT_ROLL         = 1.0f;
        internal static float MULT_YAW          = 1.0f;
        private static float MULT_WALK         = 3.0f;
        private static float MULT_TURNING      = 5.0f;
        private static float MULT_DAMAGE_PITCH = 1.0f;
        private static float MULT_DAMAGE_ROLL  = 3.0f;
        private static float MULT_DAMAGE_YAW   = 3.0f;
        private static float MULT_LANDING_PITCH= 1.5f;
        private static float MULT_LANDING_ROLL = 1.5f;
        private static float MULT_LANDING_YAW  = 1.5f;
        private static float MULT_ESCAPE_PITCH = 4.0f;
        private static float MULT_ESCAPE_ROLL  = 4.0f;
        private static float MULT_ESCAPE_YAW   = 4.0f;

        // ────────────────────────────────────────────────────────────────
        // スムージング設定（GameLink Smoothing 0.2 相当）
        // ────────────────────────────────────────────────────────────────
        private static float SMOOTHING = 0.2f;
        internal float _smoothYaw = 0f;
        private float _smoothPitch = 0f;
        private float _smoothRoll = 0f;
        private float _smoothWalk = 0f;
        private float _smoothTurning = 0f;
        private bool _smoothInitialized = false;
        // ═══════════════ ↑↑↑ ここまで追加 ↑↑↑ ═══════════════

        internal static UdpClient            Udp;
        internal static MelonLogger.Instance Log;
        internal static VoxTelemetryMod      Instance;

        // ── Euler angle 停止 ─────────────────────────────────────────
        internal bool  _eulerPaused    = false;
        internal float _eulerPauseTimer = 0f;
        private const float EULER_PAUSE_SEC = 6.0f;

        // ── リスポーン待機フリーズ ────────────────────────────────────
        internal bool  _respawnFrozen          = false;
        internal bool  _respawnFreezeArmed     = false;
        internal bool  _isRespawnSpawn          = false;  // リスポーン後のMechRoot検出でgamePausedをセットしないためのフラグ
        private  float _respawnFreezeCountdown = 0f;
        internal static volatile bool FlagRespawnResume = false;

        // ════════════════════════════════════════════════════════════════
        // Damage ch5-7: X/Y/Z座標加速度ベース
        // ════════════════════════════════════════════════════════════════
        private static float DMG_ACCEL_SCALE = 50f;
        private const int DMG_SMOOTH_SIZE = 6;
        // デッドゾーン: この値未満の加速度はノイズ（近くの爆発シェイク等）として無視する。
        // 実際の被弾時はこれより大きい加速度が生じる。0.0〜1.0 で指定（正規化後の値）。
        // 値を大きくするほど鈍感になる（0.1 = DMG_ACCEL_SCALE の 10% ＝ 5m/s²）。
        private static float DMG_DEADZONE = 0.1f;

        private float _dmgPrevX = 0f, _dmgPrevY = 0f, _dmgPrevZ = 0f;
        private float _dmgPrevVelX = 0f, _dmgPrevVelY = 0f, _dmgPrevVelZ = 0f;

        private float[] _dmgAccelXBuf = new float[DMG_SMOOTH_SIZE];
        private int _dmgAccelXIdx = 0;
        private float _dmgAccelXSum = 0f;

        private float[] _dmgAccelYBuf = new float[DMG_SMOOTH_SIZE];
        private int _dmgAccelYIdx = 0;
        private float _dmgAccelYSum = 0f;

        private float[] _dmgAccelZBuf = new float[DMG_SMOOTH_SIZE];
        private int _dmgAccelZIdx = 0;
        private float _dmgAccelZSum = 0f;

        private bool  _dmgCalibrated = false;
        private float _dmgCalibrateTime = 0f;
        private float _dmgPrevTime = 0f;

        internal float _dmgPitch = 0f, _dmgRoll = 0f, _dmgYaw = 0f;

        // ────────────────────────────────────────────────────────────
        // Landing 3軸 (ch8-10)
        // ────────────────────────────────────────────────────────────
        internal ShakeAxis _landP, _landR, _landY;
        private static float LAND_FREQ_MIN = 2.5f, LAND_FREQ_MAX = 3.0f;

        // ────────────────────────────────────────────────────────────
        // Escape 3軸 (ch11-13)
        // ────────────────────────────────────────────────────────────
        private ShakeAxis _escP, _escR, _escY;
        private const float ESC_FREQ_MIN_SHIELD = 3f, ESC_FREQ_MAX_SHIELD = 6f;
        private const float ESC_FREQ_MIN_EJECT = 2f, ESC_FREQ_MAX_EJECT = 3.5f;
        internal const float ESC_DUR_SHIELD      = 2.6f;   // クールダウン用（Open側の合計時間）
        internal const float ESC_DUR_SHIELD_OPEN  = 2.3f;   // BlastShieldOpen 通常振動
        internal const float ESC_DUR_SHIELD_CLOSE = 1.5f;   // BlastShieldClose 通常振動
        // 呼び出し前にOpen/Closeで上書きする
        internal static float FlagShieldDur = ESC_DUR_SHIELD_OPEN;
        // 2.6秒後に追加する強い揺れ（0.3秒）
        private const float ESC_FINAL_DELAY    = 2.6f;
        private const float ESC_FINAL_DUR      = 0.3f;
        private const float ESC_FINAL_AMP      = 0.90f;
        private const float ESC_FREQ_MIN_FINAL = 8f;
        private const float ESC_FREQ_MAX_FINAL = 15f;
        // ファイナルシェイク用タイマー（負 = 未アーム）
        private float _shieldFinalShakeTimer = -1f;
        private const float ESC_DUR_MIN_EJECT = 3.0f, ESC_DUR_MAX_EJECT = 3.5f;
        internal const float ESC_AMP_SHIELD = 0.30f;
        internal const float ESC_AMP_EJECT = 0.90f;
        internal static volatile bool FlagEscape = false;
        // ────────────────────────────────────────────────────────────
        // ────────────────────────────────────────────────────────────
        // 格納庫カットシーン搬送エフェクト（Foot_Vib 1チャンネルに集約）
        //
        // タイムライン（InternalEngine=0秒基準）:
        //   0.00s  CockpitShake → 停止衝撃ジョルト
        //   0.08s  BlastShieldOpen（_escP を使うが本エフェクトとは独立）
        //   2.24s  Play_Launch_Belt_01 → 連続微振動＋ガタッ開始
        //  48.27s  Play_Launch_Detach → 搬送終了
        //
        // Foot_Vib 出力:
        //   ジョルト:   AMP=100 / HZ=5  / 1.5秒
        //   連続微振動: AMP=25  / HZ=8
        // ────────────────────────────────────────────────────────────
        // 出撃・帰還カットシーン搬送エフェクト
        //   各サウンドフックで直接エフェクトを発火 → ステージ問わず自動でタイミング合致
        //   状態管理は最小限の4変数のみ
        // ────────────────────────────────────────────────────────────
        internal bool  _launchActive      = false;
        internal bool  _launchLiftStarted = false;
        internal bool  _returnActive    = false;
        internal bool  _returnLiftDone   = false;
        internal float _returnLiftTimer  = 0f;   // Lift発火からの経過秒数（VibOFF判定用）
        internal float _returnLiftStopTimer = 0f; // 前半終了タイマー（Lift+23秒でVibOFF）
        internal float _returnMaxTimer  = 0f;  // mechRoot消失後の経過時間（強制終了用）
        internal float _returnCooldown = 0f;
        internal float _returnTimeout  = 0f;  // 帰還開始から一定時間でLiftが来なければ強制終了
        internal float _returnStopDelay = 0f;  // LargeHorz_Open後のStop shake遅延タイマー
        internal float _returnHeatCancelTimer = 0f;  // 熱暴走キャンセル用タイマー（mechRoot生存時）
        // ストーリーモードミッション判定フラグ
        // InternalEngine（格納庫搬送 = ストーリーモード確定）でtrue、[Scan]resetでfalse
        internal bool  _storyMissionActive = false;
        internal bool  _gamePaused         = false;  // MainMonitorOpen/Closeで制御
        internal bool  _waitingForCutscene  = false;  // CutScene.txt待機中（MainMonitorCloseを無視）
        internal bool  _playerPaused        = false;  // プレイヤーが手動でポーズした（MainMonitorOpen時にgamePaused=falseだった場合）
        private float  _launchGraceTimer    = 0f;  // End shake出力のための出撃完了後猶予
        internal float  _returnGraceTimer    = 0f;  // Stop shake出力のための帰還完了後猶予

        // SendTelemetry参照用（変更不可）
        internal float _cutsceneVibAmp = 0f;
        internal float _cutsceneVibHz  = 0f;
        internal float _returnVibAmp   = 0f;
        internal float _returnVibHz    = 0f;

        // ガタッ内部タイマー
        internal float _cutsceneRatchetTimer = 0f;
        private  float _cutsceneRatchetPulse = 0f;
        internal float _returnRatchetTimer   = 0f;
        private  float _returnRatchetPulse   = 0f;

        // 微振動開始中フラグ（VibOFF後にガタッが復帰しないよう管理）
        internal bool _launchVibActive  = false;
        internal bool _returnVibActive  = false;

        // シェイク遅延タイマー（サウンドイベントから少し遅らせるだけ、ステージ共通）
        internal float _launchStopDelay  = 0f;  // HorzOpen後 → VibOFF (Stop shakeは即発火)
        internal float _launchResumeTimer = 0f;  // CsVibOFF後4秒 → ResumeShake + CsVibOn
        internal float _launchLiftDelay  = 0f;  // Lift後 → LiftShake（現在は_launchResumeTimerに移行）
        internal float _launchEndDelay   = 0f;  // Detach後 → VibOFF+EndShake
        internal float _returnLiftDelay  = 0f;  // Lift後 → LiftShake

        internal const float CUTSCENE_RATCHET_INT      = 1.5f;
        internal const float CUTSCENE_VIB_AMP_INTERNAL = 25f;
        internal const float CUTSCENE_VIB_HZ_INTERNAL  = 8f;
        private  const float CUTSCENE_VIB_AMP          = 25f;
        private  const float CUTSCENE_VIB_HZ           = 8f;
        private  const float CUTSCENE_PULSE_AMP         = 150f;
        private  const float CUTSCENE_PULSE_HZ_MIN      = 3f;
        private  const float CUTSCENE_PULSE_HZ_MAX      = 5f;
        private  const float CUTSCENE_PULSE_DUR         = 0.175f;
        internal const float CUTSCENE_QUAKE_AMP         = 150f;
        internal const float CUTSCENE_QUAKE_HZ_MIN      = 2f;
        internal const float CUTSCENE_QUAKE_HZ_MAX      = 4f;
        internal const float CUTSCENE_QUAKE_DUR         = 1.5f;
        internal const float CUTSCENE_QUAKE_DUR_LIFT    = 1.0f;

        internal static volatile bool FlagCutsceneJolt = false;  // CockpitShake起動ジョルト
        internal static float FlagEscapeAmp = 0f;
        // BlastShieldOpen/Close は同一フレームに4回発火するため、
        // 最初の1発のみ受け付けるクールダウンタイマー
        internal static float _shieldEscapeCooldown = 0f;
        internal static float FlagEscapeFreqMin = 1f;
        internal static float FlagEscapeFreqMax = 1.5f;
        internal static float FlagEscapeDurMin = 1.5f;
        internal static float FlagEscapeDurMax = 1.5f;
        internal static volatile bool FlagEulerPause = false;

        // ────────────────────────────────────────────────────────────
        // Foot_Vib (ch14-15) — Walk + Landing 統合
        // ────────────────────────────────────────────────────────────
        private static float FOOT_WALK_AMP = 300f;
        private static float FOOT_WALK_HZ = 18f;
        private static float FOOT_LAND_AMP = 300f;
        private static float FOOT_LAND_HZ = 60f;
        private static float VIB_WALK_DUR = 0.5f;
        private static float VIB_LAND_DUR = 2.0f;
        internal float _vibWalkTimer = 0f;
        internal float _vibLandTimer = 0f;
        internal static volatile bool FlagVibWalk = false;
        internal static float FlagVibWalkDur = 0.3f;

        // ────────────────────────────────────────────────────────────
        // Turning_Vib (ch16-17)
        // ────────────────────────────────────────────────────────────
        private static float TURNING_VIB_AMP_SCALE = 100f;  // _turnVibValue への乗数
        private static float TURNING_VIB_HZ = 70f;

        // ────────────────────────────────────────────────────────────
        // Damage_Vib (ch18-19)
        // ────────────────────────────────────────────────────────────
        private float _vibDmgTimer = 0f;
        private static float DAMAGE_VIB_AMP = 100f;
        private static float DAMAGE_VIB_HZ = 50f;
        internal static volatile bool FlagVibDmg = false;
        internal static float FlagVibDmgDur = 1.5f;

        // ────────────────────────────────────────────────────────────
        // Jet_Vib (ch20-21)
        // ────────────────────────────────────────────────────────────
        internal float _vibJetTimer = 0f;
        private static float JET_VIB_AMP = 100f;
        private static float JET_VIB_HZ = 10f;
        private const float VIB_ESCAPE_DUR = 3.5f;
        internal static volatile bool FlagVibEscape = false;
        internal bool _jetEffectActive = false;

        // ────────────────────────────────────────────────────────────
        // Weapon_Vib (ch22-23) ※Multiplier=1前提（実効0-100）
        // ────────────────────────────────────────────────────────────
        internal float _weaponVibAmp = 0f;
        private float _weaponVibHz = 0f;
        private float _vibWeaponDur = 0.5f;
        internal const float WEAPON_DIST_SELF = 30f;
        internal const float WEAPON_DIST_NEAR = 50f;
        internal static volatile bool FlagVibWeapon = false;
        internal static float FlagVibWeaponAmp = 0f;
        internal static float FlagVibWeaponHz = 0f;
        internal static float FlagVibWeaponDur = 0.5f;

        // 連続振動用（レールガンチャージ、ドリル）
        internal bool _railCharging = false;
        internal float _railChargeTimer = 0f;
        // ※外部ファイル(Weapon_Vib.txt)から読み込み可能
        // Railgun: 高HZ（細かい）→ 低HZ（重い）へチャージ中に変化
        internal static float RAIL_CHARGE_TIME = 2.7f;
        internal static float RAIL_START_AMP = 45f;
        internal static float RAIL_END_AMP = 100f;
        internal static float RAIL_START_HZ = 60f;
        internal static float RAIL_END_HZ = 12f;
        // チャージ増大カーブの指数（Pow適用。t=0から変化し始める）
        // 0.5 = 序盤で素早く立ち上がり、後半はなだらか
        // 1.0 = 一定ペースで線形増加
        // 1.5 = 序盤は緩やか、後半に加速
        // 2.0 = さらに後半集中
        // 3.0 = チャージ完了直前まで緩やか、最後に急上昇
        // Weapon_Vib.txt の RAIL_CURVE|値|CONST で変更可能
        internal static float RAIL_CURVE = 0.4f;

        internal bool _drillActive = false;
        // Drill: 中間的な連続振動
        internal static float DRILL_AMP = 100f;
        internal static float DRILL_HZ = 10f;

        // ────────────────────────────────────────────────────────────
        // Laser_Vib — 照射中ずっと一定振動（減衰なし・タイムアウト方式）
        // LargeLaserMuzzle / SmallLaserMuzzle が来るたびにタイマーリセット。
        // 最後のイベントから LASER_VIB_TIMEOUT 秒経過で自動停止。
        // ────────────────────────────────────────────────────────────
        private float _laserVibTimer = 0f;
        private float _laserVibAmp   = 0f;
        private float _laserVibHz    = 0f;
        internal static float LASER_VIB_TIMEOUT = 3.5f;  // 最短再発火間隔(~3s)より少し長く。Weapon_Vib.txtのCONSTで変更可能
        internal static volatile bool FlagLaserVib     = false;
        internal static float         FlagLaserVibAmp  = 0f;
        internal static float         FlagLaserVibHz   = 0f;
        internal static float         FlagLaserVibTimeout = LASER_VIB_TIMEOUT;
        
        // 武器振動デフォルト値（未登録武器用）
        internal static float DEFAULT_WEAPON_AMP = 35f;
        internal static float DEFAULT_WEAPON_HZ = 8f;
        internal static float DEFAULT_WEAPON_DUR = 0.4f;

        // ════════════════════════════════════════════════════════════════
        // Walk (ch3) — Y加速度ベース + ジェット抑制
        // ════════════════════════════════════════════════════════════════
        private static float ACCEL_SCALE = 50f;
        private static float JET_VELOCITY_THRESHOLD = 2.0f;
        private const float JET_COOLDOWN = 0.5f;

        // ── ロボットプロファイル ──────────────────────────────────────
        internal int  _currentTypeId = -1;  // 現在のロボ typeId（未検出=-1）
        private static readonly Dictionary<int, string> TypeIdToName = new()
        {
            { 1, "Catalyst" }, { 2, "Dredge"   }, { 3, "Hopper" }, { 4, "Goldrush" },
            { 5, "Overhaul" }, { 6, "Drill"    }, { 8, "Rook"   }, { 11, "Decker"  }, { 20, "Scout"   },
        };

        internal Transform _cameraEyelevel;
        private bool  _walkCalibrated = false;
        private float _walkCalibrateTime = 0f;
        private float _walkPrevY = 0f, _walkPrevVelY = 0f, _walkPrevTime = 0f;

        private const int VEL_SMOOTH_SIZE = 10;
        private float[] _velBuffer = new float[VEL_SMOOTH_SIZE];
        private int _velIndex = 0;
        private float _velSum = 0f;

        private const int ACCEL_SMOOTH_SIZE = 6;
        private float[] _accelBuffer = new float[ACCEL_SMOOTH_SIZE];
        private int _accelIndex = 0;
        private float _accelSum = 0f;

        private bool  _isJetting = false;
        private float _jetCooldownTimer = 0f;
        private float _walkValue = 0f;

        private static float PITCH_NORM = 30f, ROLL_NORM = 30f;

        // ── スキャン ─────────────────────────────────────────────────
        internal Transform _mechRoot;
        private Transform  _cockpitRef;
        private GameObject _cockpitObj;
        private float      _scanTimer = 0f;
        private bool       _cockpitFound = false;

        // ── 物理演算 ─────────────────────────────────────────────────
        private Vector3 _prevPos;
        private float   _smoothLat = 0f, _smoothSpd = 0f;
        private const float SPEED_MAX = 500f, TAU_VEL = 0.08f, TAU_SPEED = 0.12f;
        private Vector3 _filtVel = Vector3.zero;
        private float _prevYaw = 0f;
        private float _rawTurnRate = 0f;
        private static float MAX_YAW_RATE = 60.0f, TAU_TURN_RISE = 0.40f, TAU_TURN_FALL = 0.20f;
        internal static float YAW_RATE_FILTER     = 75.0f;   // blips角速度フィルタ閾値（通常）
        internal static float YAW_RATE_FILTER_GAS = 150.0f;  // blips角速度フィルタ閾値（Gas and Go）

        // ── Turning操作検出 ──────────────────────────────────────────
        internal float _turnVibValue = 0f;
        private bool _isTurningByPlayer = false;

        // ── 着地検出 ──────────────────────────────────────────
        private float _mechPrevY, _fallStartY, _landCooldown;
        private float _prevFallVel;
        private float _landingAccelPeak;
        private bool _falling;
        private static float FALL_VEL_THRESH = -5.0f;
        private static float MIN_FALL_DIST = 10f;
        private static float LAND_ACCEL_MIN = 40f;
        private static float LAND_ACCEL_MAX = 600f;
        internal static volatile bool FlagLanding = false;
        internal static float FlagLandingAmp = 0f;
        internal static float FlagLandingDurMin = 0.8f;
        internal static float FlagLandingDurMax = 1.5f;

        // ── UDP送信 ─────────────────────────────────────────────────
        private float _udpTimer = 0f;
        private const float UDP_HZ = 0.033f;

        private static float FastExp(float x)
        {
            if (x < -2f) { float h = FastExp(x * 0.5f); return h * h; }
            float x2 = x * x, x3 = x2 * x, x4 = x3 * x, x5 = x4 * x;
            float r = 1f + x + x2 * 0.5f + x3 * 0.166667f + x4 * 0.041667f + x5 * 0.008333f;
            return r < 0f ? 0f : r;
        }

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance; Instance = this;
            LoggerInstance.Msg("████████████████████████████████████████");
            LoggerInstance.Msg("██  VoxTelemetry v6.11  ████████████████");
            LoggerInstance.Msg("████████████████████████████████████████");
            LoggerInstance.Msg("  Weapon_Vib外部ファイル対応");
            Udp = new UdpClient();

            // ═══════════════ ↓↓↓ ここから追加 ↓↓↓ ═══════════════
            SubtitleUdp = new UdpClient();
            LoggerInstance.Msg("  Subtitle UDP: port " + SUBTITLE_PORT);
            // ═══════════════ ↑↑↑ ここまで追加 ↑↑↑ ═══════════════
            
            // 武器振動設定ファイル読み込み
            LoadWeaponVibConfig();
            LoadCutSceneConfig();

            try
            {
                var harmony = new HarmonyLib.Harmony("com.tyoripoko.voxtelemetry");
                harmony.PatchAll();
                LoggerInstance.Msg("  Harmony: all patches applied");
            } catch (System.Exception e) { LoggerInstance.Msg($"  Harmony ERROR: {e.Message}"); }
        }
        
        // ════════════════════════════════════════════════════════════════
        // 武器振動設定ファイル読み込み
        // ════════════════════════════════════════════════════════════════
        private static readonly string WeaponVibConfigPath = 
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Weapon_Vib.txt");

        // ════════════════════════════════════════════════════════════════
        // カットシーン振動設定（CutScene.txt）
        // ════════════════════════════════════════════════════════════════
        private static readonly string CutSceneConfigPath =
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "CutScene.txt");

        internal struct CsVibEntry { public float Start, End, Amp, Hz; }

        internal class CutSceneData
        {
            public string Name         = "";
            public string TriggerSound = "";
            public string StageSound   = "";   // このサウンドが鳴ったステージでのみ有効
            public float  Duration     = 0f;
            public float  CradleAmp    = 0f;   // 後方互換用（全軸共通）
            public float  CradlePitchAmp = -1f; // -1=CradleAmpを使用
            public float  CradleRollAmp  = -1f;
            public float  CradleYawAmp   = -1f;
            public float  CradlePeriod = 8f;
            public float  CradlePitchPhase = 0f;
            public float  CradleRollPhase  = 120f;
            public float  CradleYawPhase   = 240f;
            public bool   PostDisable  = false;
            public bool   BlastOnStart = false;  // 開始時にBlastShieldOpenと同じEscape振動を発火
            public System.Collections.Generic.List<CsVibEntry> Vibs = new();
        }

        // サウンド名 → CutSceneData のルックアップ（複数シーン対応）
        internal static System.Collections.Generic.Dictionary<string, CutSceneData> CutSceneTable
            = new System.Collections.Generic.Dictionary<string, CutSceneData>();
        // ステージ識別サウンド → CutSceneData
        internal static System.Collections.Generic.Dictionary<string, CutSceneData> StageSoundTable
            = new System.Collections.Generic.Dictionary<string, CutSceneData>();

        // 実行時状態
        internal CutSceneData? _activeCsScene    = null;
        internal CutSceneData? _stageConfirmedCs = null;  // ステージサウンドで確認済みシーン
        internal float         _csTimer       = -1f;   // -1=非アクティブ
        internal float         _csDmgValue    = 0f;    // 現フレームのVIB出力値(-1～+1)
        internal float         _csCradlePitch = 0f;
        internal float         _csCradleRoll  = 0f;
        internal float         _csCradleYaw   = 0f;
        internal bool          _csPostDisable = false; // カットシーン後の全チャンネル無効フラグ
        
        // Weapon.Fire()用パラメータ
        internal static Dictionary<string, (float amp, float hz, float dur)> WeaponVibParams = new Dictionary<string, (float, float, float)>();
        
        // VoxSound.Play用パラメータ
        internal static Dictionary<string, (float amp, float hz, float dur)> WeaponVibTable = new Dictionary<string, (float, float, float)>();
        
        private void LoadWeaponVibConfig()
        {
            // デフォルト値を設定
            SetDefaultWeaponVibParams();
            
            if (!System.IO.File.Exists(WeaponVibConfigPath))
            {
                Log?.Msg($"[Config] Weapon_Vib.txt not found, using defaults");
                Log?.Msg($"[Config] Path: {WeaponVibConfigPath}");
                return;
            }
            
            try
            {
                var lines = System.IO.File.ReadAllLines(WeaponVibConfigPath);
                int fireCount = 0, soundCount = 0, constCount = 0;
                
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    
                    // 行末コメントを削除
                    int commentIdx = line.IndexOf('#');
                    if (commentIdx >= 0) line = line.Substring(0, commentIdx).Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    
                    var parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    
                    string name = parts[0].Trim();
                    
                    // 定数の場合
                    if (parts.Length >= 3 && parts[2].Trim().ToUpper() == "CONST")
                    {
                        if (!float.TryParse(parts[1].Trim(), out float value)) continue;
                        
                        switch (name.ToUpper())
                        {
                            case "RAIL_CHARGE_TIME": RAIL_CHARGE_TIME = value; break;
                            case "RAIL_START_AMP": RAIL_START_AMP = value; break;
                            case "RAIL_END_AMP": RAIL_END_AMP = value; break;
                            case "RAIL_START_HZ": RAIL_START_HZ = value; break;
                            case "RAIL_END_HZ": RAIL_END_HZ = value; break;
                            case "RAIL_CURVE": RAIL_CURVE = value; break;
                            case "DRILL_AMP": DRILL_AMP = value; break;
                            case "DRILL_HZ": DRILL_HZ = value; break;
                            case "DEFAULT_AMP": DEFAULT_WEAPON_AMP = value; break;
                            case "DEFAULT_HZ": DEFAULT_WEAPON_HZ = value; break;
                            case "DEFAULT_DUR": DEFAULT_WEAPON_DUR = value; break;
                            case "LASER_VIB_TIMEOUT": LASER_VIB_TIMEOUT = value; break;
                        }
                        constCount++;
                        continue;
                    }
                    
                    // FIRE/SOUNDの場合
                    if (parts.Length < 5) continue;
                    
                    if (!float.TryParse(parts[1].Trim(), out float amp)) continue;
                    if (!float.TryParse(parts[2].Trim(), out float hz)) continue;
                    if (!float.TryParse(parts[3].Trim(), out float dur)) continue;
                    string type = parts[4].Trim().ToUpper();
                    
                    if (type == "FIRE")
                    {
                        WeaponVibParams[name] = (amp, hz, dur);
                        fireCount++;
                    }
                    else if (type == "SOUND")
                    {
                        WeaponVibTable[name] = (amp, hz, dur);
                        soundCount++;
                    }
                }
                
                Log?.Msg($"[Config] Weapon_Vib.txt loaded: FIRE={fireCount}, SOUND={soundCount}, CONST={constCount}");
                
                // 自動リロード用に更新日時を記録
                _lastConfigModified = System.IO.File.GetLastWriteTime(WeaponVibConfigPath);
            }
            catch (System.Exception e)
            {
                Log?.Msg($"[Config] Error loading Weapon_Vib.txt: {e.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // ロボットプロファイル読み込み
        //   Mods/Grinder/{RobotName}.json を読み込み、各 static 定数を上書きする。
        //   typeId が未登録・ファイルなしの場合はデフォルト値のまま継続。
        // ════════════════════════════════════════════════════════════════
        internal void LoadRobotProfile(int typeId)
        {
            if (typeId == _currentTypeId) return;  // 同じロボの再読み込みは不要
            _currentTypeId = typeId;

            if (!TypeIdToName.TryGetValue(typeId, out var robotName))
            {
                Log?.Msg($"[Profile] typeId={typeId} は未登録（プロファイルなし）");
                return;
            }

            var modsDir   = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            var profilePath = Path.Combine(modsDir, "Grinder", robotName + ".json");

            if (!File.Exists(profilePath))
            {
                Log?.Msg($"[Profile] {robotName}.json が見つかりません（{profilePath}）");
                return;
            }

            try
            {
                var json = File.ReadAllText(profilePath);
                var p    = SimpleJson.Parse(json);

                // ── yaw ────────────────────────────────────────────
                p.TryFloat("yaw.yaw_rate_filter",       ref YAW_RATE_FILTER);   // blipsフィルタ閾値（旋回最大速度より大きく設定）
                p.TryFloat("yaw.blips_stabilize_delay", ref BLIPS_STABILIZE_DELAY);
                p.TryFloat("yaw.turning_deadzone",      ref TURNING_DEADZONE);
                p.TryFloat("yaw.turning_deadzone_max",  ref MAX_YAW_RATE);      // Turning演出の正規化上限
                p.TryFloat("yaw.turning_curve",         ref TURNING_CURVE);
                p.TryFloat("yaw.mult_yaw",              ref MULT_YAW);
                p.TryFloat("yaw.mult_turning",          ref MULT_TURNING);
                p.TryFloat("yaw.smoothing",             ref SMOOTHING);

                // ── pitch_roll ──────────────────────────────────────
                p.TryFloat("pitch_roll.mult_pitch",  ref MULT_PITCH);
                p.TryFloat("pitch_roll.mult_roll",   ref MULT_ROLL);
                p.TryFloat("pitch_roll.pitch_norm",  ref PITCH_NORM);
                p.TryFloat("pitch_roll.roll_norm",   ref ROLL_NORM);

                // ── walk ────────────────────────────────────────────
                p.TryFloat("walk.mult_walk",     ref MULT_WALK);
                p.TryFloat("walk.accel_scale",   ref ACCEL_SCALE);
                p.TryFloat("walk.vib_walk_dur",  ref VIB_WALK_DUR);
                p.TryFloat("walk.foot_walk_amp", ref FOOT_WALK_AMP);
                p.TryFloat("walk.foot_walk_hz",  ref FOOT_WALK_HZ);

                // ── landing ─────────────────────────────────────────
                p.TryFloat("landing.mult_landing_pitch", ref MULT_LANDING_PITCH);
                p.TryFloat("landing.mult_landing_roll",  ref MULT_LANDING_ROLL);
                p.TryFloat("landing.mult_landing_yaw",   ref MULT_LANDING_YAW);
                p.TryFloat("landing.vib_land_dur",       ref VIB_LAND_DUR);
                p.TryFloat("landing.foot_land_amp",      ref FOOT_LAND_AMP);
                p.TryFloat("landing.foot_land_hz",       ref FOOT_LAND_HZ);
                p.TryFloat("landing.fall_vel_thresh",    ref FALL_VEL_THRESH);
                p.TryFloat("landing.min_fall_dist",      ref MIN_FALL_DIST);
                p.TryFloat("landing.land_accel_min",     ref LAND_ACCEL_MIN);
                p.TryFloat("landing.land_accel_max",     ref LAND_ACCEL_MAX);
                p.TryFloat("landing.land_freq_min",      ref LAND_FREQ_MIN);
                p.TryFloat("landing.land_freq_max",      ref LAND_FREQ_MAX);

                // ── damage ──────────────────────────────────────────
                p.TryFloat("damage.mult_damage_pitch", ref MULT_DAMAGE_PITCH);
                p.TryFloat("damage.mult_damage_roll",  ref MULT_DAMAGE_ROLL);
                p.TryFloat("damage.mult_damage_yaw",   ref MULT_DAMAGE_YAW);
                p.TryFloat("damage.dmg_accel_scale",   ref DMG_ACCEL_SCALE);
                p.TryFloat("damage.dmg_deadzone",      ref DMG_DEADZONE);
                p.TryFloat("damage.damage_vib_amp",    ref DAMAGE_VIB_AMP);
                p.TryFloat("damage.damage_vib_hz",     ref DAMAGE_VIB_HZ);

                // ── escape ──────────────────────────────────────────
                p.TryFloat("escape.mult_escape_pitch", ref MULT_ESCAPE_PITCH);
                p.TryFloat("escape.mult_escape_roll",  ref MULT_ESCAPE_ROLL);
                p.TryFloat("escape.mult_escape_yaw",   ref MULT_ESCAPE_YAW);

                // ── turning_vib ─────────────────────────────────────
                p.TryFloat("turning_vib.turning_vib_amp_scale", ref TURNING_VIB_AMP_SCALE);
                p.TryFloat("turning_vib.turning_vib_hz",        ref TURNING_VIB_HZ);

                // ── jet ─────────────────────────────────────────────
                p.TryFloat("jet.jet_vib_amp",            ref JET_VIB_AMP);
                p.TryFloat("jet.jet_vib_hz",             ref JET_VIB_HZ);
                p.TryFloat("jet.jet_velocity_threshold", ref JET_VELOCITY_THRESHOLD);

                // ── rail ────────────────────────────────────────────
                p.TryFloat("rail.rail_charge_time", ref RAIL_CHARGE_TIME);
                p.TryFloat("rail.rail_start_amp",   ref RAIL_START_AMP);
                p.TryFloat("rail.rail_end_amp",     ref RAIL_END_AMP);
                p.TryFloat("rail.rail_start_hz",    ref RAIL_START_HZ);
                p.TryFloat("rail.rail_end_hz",      ref RAIL_END_HZ);
                p.TryFloat("rail.rail_curve",       ref RAIL_CURVE);

                // ── drill ───────────────────────────────────────────
                p.TryFloat("drill.drill_amp", ref DRILL_AMP);
                p.TryFloat("drill.drill_hz",  ref DRILL_HZ);

                Log?.Msg($"[Profile] {robotName}.json 読み込み完了 (typeId={typeId})");
            }
            catch (System.Exception e)
            {
                Log?.Msg($"[Profile] {robotName}.json 読み込みエラー: {e.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════
        // CutScene.txt 読み込み
        // ════════════════════════════════════════════════════════════════
        private void LoadCutSceneConfig()
        {
            CutSceneTable.Clear();
            int sceneCount = 0, vibCount = 0;
            try
            {
                if (!System.IO.File.Exists(CutSceneConfigPath))
                {
                    Log?.Msg("[CutScene] CutScene.txt が見つかりません（スキップ）");
                    return;
                }
                var scenes = new System.Collections.Generic.Dictionary<string, CutSceneData>();
                CutSceneTable.Clear();
                StageSoundTable.Clear();
                foreach (var rawLine in System.IO.File.ReadAllLines(CutSceneConfigPath))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    var parts = line.Split("|");
                    if (parts.Length < 2) continue;
                    string directive = parts[0].ToUpperInvariant();

                    if (directive == "SCENE" && parts.Length >= 4)
                    {
                        var d = new CutSceneData
                        {
                            Name         = parts[1],
                            TriggerSound = parts[2],
                            Duration     = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)
                        };
                        scenes[parts[1]] = d;
                        CutSceneTable[parts[2]] = d;
                        sceneCount++;
                    }
                    else if (directive == "CRADLE" && parts.Length >= 6 && scenes.TryGetValue(parts[1], out var cs))
                    {
                        // 新フォーマット: CRADLE|scene|pitchAmp|rollAmp|yawAmp|period|pitchPhase|rollPhase|yawPhase
                        // 旧フォーマット: CRADLE|scene|amp|period|pitchPhase|rollPhase|yawPhase
                        // 新フォーマット(9要素): CRADLE|scene|pitchAmp|rollAmp|yawAmp|period|pPhase|rPhase|yPhase
                        // 旧フォーマット(7要素): CRADLE|scene|amp|period|pPhase|rPhase|yPhase
                        bool newFormat = parts.Length >= 9;
                        if (newFormat)
                        {
                            cs.CradlePitchAmp   = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                            cs.CradleRollAmp    = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                            cs.CradleYawAmp     = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
                            cs.CradlePeriod     = float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture);
                            cs.CradlePitchPhase = parts.Length > 6 ? float.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                            cs.CradleRollPhase  = parts.Length > 7 ? float.Parse(parts[7], System.Globalization.CultureInfo.InvariantCulture) : 120f;
                            cs.CradleYawPhase   = parts.Length > 8 ? float.Parse(parts[8], System.Globalization.CultureInfo.InvariantCulture) : 240f;
                        }
                        else
                        {
                            cs.CradleAmp        = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                            cs.CradlePeriod     = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                            cs.CradlePitchPhase = parts.Length > 4 ? float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                            cs.CradleRollPhase  = parts.Length > 5 ? float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture) : 120f;
                            cs.CradleYawPhase   = parts.Length > 6 ? float.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture) : 240f;
                        }
                    }
                    else if (directive == "VIB" && parts.Length >= 6 && scenes.TryGetValue(parts[1], out var csv))
                    {
                        csv.Vibs.Add(new CsVibEntry
                        {
                            Start = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                            End   = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                            Amp   = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                            Hz    = float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
                        });
                        vibCount++;
                    }
                    else if (directive == "DISABLE" && parts.Length >= 3 && scenes.TryGetValue(parts[1], out var csd))
                    {
                        csd.PostDisable = parts[2].Trim() == "1";
                    }
                    else if (directive == "BLAST" && parts.Length >= 3 && scenes.TryGetValue(parts[1], out var csb))
                    {
                        csb.BlastOnStart = parts[2].Trim() == "1";
                    }
                    else if (directive == "STAGE" && parts.Length >= 3 && scenes.TryGetValue(parts[1], out var css))
                    {
                        css.StageSound = parts[2].Trim();
                        StageSoundTable[parts[2].Trim()] = css;  // ステージ識別サウンド → シーン
                        css.BlastOnStart = true;  // ★ STAGEが設定されたシーン（Gas and Go等）は自動的にBlastShieldOpen振動を発火
                    }
                }
                Log?.Msg($"[CutScene] CutScene.txt 読み込み完了: {sceneCount}シーン, {vibCount}VIBエントリ");
            }
            catch (System.Exception e)
            {
                Log?.Msg($"[CutScene] Error loading CutScene.txt: {e.Message}");
            }
        }

        private void SetDefaultWeaponVibParams()
        {
            // Weapon.Fire() 用デフォルト値（GameLink Multiplier=1前提）
            // 低HZ=指向性のある大きな揺れ、高HZ=細かい振動
            WeaponVibParams.Clear();
            // キャノン系
            WeaponVibParams["cannon_large"]    = (100f, 5.1f,  1.0f);
            WeaponVibParams["cannon_small"]    = (100f, 8.1f,  0.7f);
            WeaponVibParams["cannon_flak_mine"]= ( 90f, 9.0f,  1.0f);
            WeaponVibParams["cannon_flak"]     = ( 90f, 9.0f,  1.0f);
            // ガトリング系
            WeaponVibParams["minigun"]         = ( 75f, 20f,   0.1f);
            WeaponVibParams["minigun_alt"]     = ( 75f, 15f,   0.1f);
            WeaponVibParams["gatling"]         = ( 75f, 15f,   0.1f);
            // ミサイル系（launcher_guided はゲームで未使用のため除外）
            WeaponVibParams["launcher_8pack"]  = (100f, 18f,   0.8f);
            WeaponVibParams["launcher_3pack"]  = (100f, 18f,   0.8f);
            WeaponVibParams["launcher_cruise"] = (100f, 5.4f,  2.0f);
            // 特殊武器
            WeaponVibParams["mallet"]          = (100f, 5.4f,  2.0f);
            WeaponVibParams["riot"]            = ( 60f, 20f,   0.3f);
            WeaponVibParams["shotgun"]         = (100f, 10f,   0.8f);
            WeaponVibParams["grenade_launcher"]= (100f, 8.1f,  1.0f);
            WeaponVibParams["railgun"]         = (100f, 5.0f,  1.0f);

            // VoxSound.Play 用デフォルト値（GameLink Multiplier=1前提）
            WeaponVibTable.Clear();
            WeaponVibTable["DrillWeaponImpact"] = (100f, 20f,  1.0f);
            // レーザー: dur はタイムアウト秒数として使用（照射停止後この秒数で振動停止）
            WeaponVibTable["LargeLaserMuzzle"]  = ( 90f, 21f,  1.7f);
            WeaponVibTable["SmallLaserMuzzle"]  = ( 90f, 30f,  1.1f);
        }

        private void CheckAndReloadConfig()
        {
            try
            {
                if (System.IO.File.Exists(WeaponVibConfigPath))
                {
                    var lastWrite = System.IO.File.GetLastWriteTime(WeaponVibConfigPath);
                    if (lastWrite > _lastConfigModified)
                    {
                        _lastConfigModified = lastWrite;
                        LoadWeaponVibConfig();
                        Log?.Msg("[Config] Weapon_Vib.txt 変更検出 → 自動リロード完了");
                    }
                }
            }
            catch { }
            try
            {
                if (System.IO.File.Exists(CutSceneConfigPath))
                {
                    var lastWrite = System.IO.File.GetLastWriteTime(CutSceneConfigPath);
                    if (lastWrite > _lastCutSceneConfigModified)
                    {
                        _lastCutSceneConfigModified = lastWrite;
                        LoadCutSceneConfig();
                        Log?.Msg("[Config] CutScene.txt 変更検出 → 自動リロード完了");
                    }
                }
            }
            catch { }
        }

        // 設定ファイル自動リロード用
        private System.DateTime _lastConfigModified         = System.DateTime.MinValue;
        private System.DateTime _lastCutSceneConfigModified = System.DateTime.MinValue;

        public override void OnDeinitializeMelon()
        {
            // CutSceneタイマー完全停止
            _activeCsScene = null; _csTimer = -1f;
            _csDmgValue = 0f; _csCradlePitch = _csCradleRoll = _csCradleYaw = 0f;
            _csPostDisable = false;
            try { Udp?.Close(); } catch { }
        }

        // 設定ファイル自動リロード用
        private float _configCheckTimer = 0f;
        private const float CONFIG_CHECK_INTERVAL = 2.0f;  // 2秒ごとにチェック

        // 微振動ON（ガタッ含む）
        internal void CsVibOn(bool isLaunch)
        {
            if (isLaunch) { _launchVibActive=true; _cutsceneVibAmp=CUTSCENE_VIB_AMP; _cutsceneVibHz=CUTSCENE_VIB_HZ; _cutsceneRatchetTimer=CUTSCENE_RATCHET_INT; _cutsceneRatchetPulse=0f; }
            else          { _returnVibActive=true;  _returnVibAmp=CUTSCENE_VIB_AMP;  _returnVibHz=CUTSCENE_VIB_HZ;   _returnRatchetTimer=CUTSCENE_RATCHET_INT;   _returnRatchetPulse=0f; }
        }
        // 微振動OFF
        internal void CsVibOff(bool isLaunch)
        {
            if (isLaunch) { _launchVibActive=false; _cutsceneVibAmp=0f; _cutsceneVibHz=0f; _cutsceneRatchetPulse=0f; }
            else          { _returnVibActive=false;  _returnVibAmp=0f;  _returnVibHz=0f;   _returnRatchetPulse=0f; }
        }
        // シェイク発火
        internal void CsShake(float dur)    => _landP.Trigger(CUTSCENE_QUAKE_AMP, CUTSCENE_QUAKE_HZ_MIN, CUTSCENE_QUAKE_HZ_MAX, dur, dur);  // Pitch DOWN（停止/終了）
        internal void CsShakeUp(float dur)  => _escP.Trigger(CUTSCENE_QUAKE_AMP, CUTSCENE_QUAKE_HZ_MIN, CUTSCENE_QUAKE_HZ_MAX, dur, dur);   // Pitch UP（再起動）→ _landPを使わないのでガタッパルスに上書きされない

        public override void OnUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            
            // Weapon_Vib.txt 変更検出（2秒ごと）
            _configCheckTimer += dt;
            if (_configCheckTimer >= CONFIG_CHECK_INTERVAL)
            {
                _configCheckTimer = 0f;
                CheckAndReloadConfig();
            }
            // BlastShield重複発火ガードのカウントダウン
            if (_shieldEscapeCooldown > 0f) _shieldEscapeCooldown -= dt;

            // Euler pause
            if (_eulerPaused)
            {
                _eulerPauseTimer -= dt;
                if (_eulerPauseTimer <= 0f)
                {
                    _eulerPaused = false;
                    // ★ 一時停止解除時にaccumulatedYawを同期（スナップ防止）
                    if (MULT_YAW != 0f) _accumulatedYaw = _smoothYaw / MULT_YAW;
                    Log?.Msg("[Euler] 再開");
                }
            }

            // Respawn
            if (FlagRespawnResume)
            {
                FlagRespawnResume = false; _respawnFrozen = false; _respawnFreezeArmed = false;
                _isRespawnSpawn = true;  // ★ リスポーン後のMechRoot検出でgamePaused=trueを防ぐ
                _respawnFreezeCountdown = 0f; _eulerPaused = false;
                _walkCalibrated = false; _dmgCalibrated = false; _cameraEyelevel = null;
                _blipsTransform = null; _smoothInitialized = false;
                _blipsYawInitialized = false; _blipsYawOffset = 0f;
                _blipsStabilizing = false; _blipsStabilizeTimer = 0f;
                // ★ Respawn後に_mechRootを再検出させる（武器発射判定用）
                _mechRoot = null; _cockpitRef = null; _cockpitObj = null; _cockpitFound = false;
                Log?.Msg("[Respawn] フリーズ解除 + MechRoot再検出予約");
            }
            if (_respawnFreezeArmed)
            {
                _respawnFreezeCountdown -= dt;
                if (_respawnFreezeCountdown <= 0f)
                {
                    _respawnFreezeArmed = false; _respawnFrozen = true;
                    // ★ フリーズ開始時にもMechRootをリセット（チュートリアル対応）
                    _mechRoot = null; _cockpitRef = null; _cockpitObj = null; _cockpitFound = false;
                    Log?.Msg("[Respawn] フリーズ開始 + MechRoot再検出予約");
                }
            }

            // Landing flag
            if (FlagLanding)
            {
                FlagLanding = false;
                float a = FlagLandingAmp; FlagLandingAmp = 0f;
                float durMin = FlagLandingDurMin;
                float durMax = FlagLandingDurMax;
                FlagLandingDurMin = 0.8f; FlagLandingDurMax = 1.5f;

                _landP.Trigger(a, LAND_FREQ_MIN, LAND_FREQ_MAX, durMin, durMax);
                _landR.Trigger(a, LAND_FREQ_MIN, LAND_FREQ_MAX, durMin, durMax);
                _landY.Trigger(a, LAND_FREQ_MIN, LAND_FREQ_MAX, durMin, durMax);
                _vibLandTimer = VIB_LAND_DUR;
            }

            // Escape flag
            if (FlagEscape)
            {
                FlagEscape = false; float escAmp = FlagEscapeAmp; FlagEscapeAmp = 0f;
                float freqMin = FlagEscapeFreqMin;
                float freqMax = FlagEscapeFreqMax;
                float durMin = FlagEscapeDurMin;
                float durMax = FlagEscapeDurMax;
                _escP.Trigger(escAmp, freqMin, freqMax, durMin, durMax);
                _escR.Trigger(escAmp, freqMin, freqMax, durMin, durMax);
                _escY.Trigger(escAmp, freqMin, freqMax, durMin, durMax);
                // ★ _vibJetTimer はここでは設定しない。
                //    Jet_Vib は FlagVibEscape（isEject=true の場合のみセット）で処理する。
                //    以前は無条件に VIB_ESCAPE_DUR=3.5f をセットしていたため、
                //    BlastShieldOpen/Close でも Jet_Vib が 3.5 秒間発動していた。
                // BlastShield（isEjectでない）の場合のみ、振動秒数後に強い揺れをセット
                if (!FlagEulerPause)
                {
                    _shieldFinalShakeTimer = durMin;  // Open=2.3秒後、Close=1.5秒後
                }
                if (FlagEulerPause)
                {
                    FlagEulerPause = false; _eulerPaused = true; _eulerPauseTimer = EULER_PAUSE_SEC;
                    _respawnFreezeArmed = true; _respawnFreezeCountdown = durMax; _respawnFrozen = false;
                }
            }

            // ────────────────────────────────────────────────────────────
            // 出撃・帰還カットシーン搬送エフェクト（フック直接発火方式）
            // ────────────────────────────────────────────────────────────
            if (FlagCutsceneJolt)
            {
                FlagCutsceneJolt = false;
                _csPostDisable = false;  // Gas and Go等CutSceneジョルト時にPOST_DISABLEを確実クリア
                _landP.Trigger(2.4f, 1.5f, 2.5f, 1.8f, 1.8f);
                Log?.Msg("[Launch] CockpitShake → 起動ジョルト");
            }

            // 出撃: シェイク遅延タイマー
            // CsVibOff + Stop shake を同時発火（ガタッパルス停止と一時停止期間を同期）
            if (_launchStopDelay > 0f && !_launchLiftStarted)
            {
                _launchStopDelay -= dt;
                if (_launchStopDelay <= 0f)
                {
                    CsVibOff(true);                  // ガタッパルス停止（Stop shakeと同時→停止期間同期）
                    CsShake(CUTSCENE_QUAKE_DUR);     // Stop shake（Pitch DOWN）
                    _launchResumeTimer = 5.0f;        // 5.0秒後にResume shake
                    Log?.Msg("[Launch] VibOFF + Stop shake → Resume 5.0秒タイマー開始");
                }
            }
            else if (_launchStopDelay > 0f && _launchLiftStarted) { _launchStopDelay = 0f; }  // Lift後は即キャンセル
            // Resume タイマー: CsVibOFF後5秒 → Resume shake + CsVibOn（ガタッパルス同時再開）
            if (_launchResumeTimer > 0f)
            {
                _launchResumeTimer -= dt;
                if (_launchResumeTimer <= 0f)
                {
                    CsShakeUp(CUTSCENE_QUAKE_DUR_LIFT);  // Resume shake Pitch UP（_escP使用でガタッパルスに上書きされない）
                    CsVibOn(true);                        // ガタッパルス再開
                    _cutsceneRatchetTimer = 0f;            // 即座に初回ガタッパルスを発火
                    Log?.Msg("[Launch] Resume shake + ガタッパルス再開");
                }
            }
            if (_launchLiftDelay > 0f) { _launchLiftDelay -= dt; if (_launchLiftDelay <= 0f) { Log?.Msg("[Launch] Lift shake（旧パス・無効）"); } }  // 旧パス無効化
            if (_launchEndDelay  > 0f) { _launchEndDelay  -= dt; if (_launchEndDelay  <= 0f) { CsVibOff(true); CsShake(CUTSCENE_QUAKE_DUR); _launchGraceTimer = CUTSCENE_QUAKE_DUR; _launchActive=false; _waitingForCutscene=false; Log?.Msg("[Launch] End shake → 終了"); } }

            // 帰還: シェイク遅延タイマー
            if (_returnLiftDelay > 0f) { _returnLiftDelay -= dt; if (_returnLiftDelay <= 0f) { CsShake(CUTSCENE_QUAKE_DUR_LIFT); Log?.Msg("[Return] Lift shake"); } }
            if (_returnStopDelay > 0f && _returnActive && !_returnLiftDone)
            {
                _returnStopDelay -= dt;
                if (_returnStopDelay <= 0f) { CsShake(CUTSCENE_QUAKE_DUR); Log?.Msg("[Return] Stop shake"); }
            }

            // 帰還タイムアウト: BlastShieldCloseからLiftが来ないまま60秒経過したら強制終了
            if (_returnActive && !_returnLiftDone && _returnTimeout > 0f)
            {
                _returnTimeout -= dt;
                if (_returnTimeout <= 0f)
                {
                    _returnActive = false; _returnVibActive = false;
                    _returnLiftDone = false; _returnLiftTimer = 0f; _returnLiftStopTimer = 0f;
                    CsVibOff(false);
                    Log?.Msg("[Return] タイムアウト（60秒以内にLift未検出）→ 強制終了");
                }
            }

            // 熱暴走キャンセル: _mechRootが生きたまま3秒経過したら帰還を中断
            // 熱暴走（強制シールド閉鎖）はゲームプレイが継続するため_mechRootが消えない
            // 本物の帰還はBlastShieldClose後に_mechRootが消える（Scan reset）
            if (_returnActive && !_returnLiftDone && _mechRoot != null)
            {
                _returnHeatCancelTimer += dt;
                if (_returnHeatCancelTimer >= 3f)
                {
                    _returnActive = false; _returnVibActive = false;
                    _returnHeatCancelTimer = 0f;
                    CsVibOff(false);
                    _returnCooldown = 5f;  // 5秒クールダウン（すぐに再発火しないよう）
                    Log?.Msg("[Return] 熱暴走検出（mechRoot生存3秒）→ 帰還キャンセル");
                }
            }
            else { _returnHeatCancelTimer = 0f; }
            if (_returnLiftDone) _returnLiftTimer += dt;  // Lift発火後の経過時間を計測

            // 帰還カットシーン安全弁：mechRootが消えたら5秒後に強制終了（メニュー遷移対策）
            if (_returnActive)
            {
                if (_mechRoot == null)
                {
                    _returnMaxTimer += dt;
                    if (_returnMaxTimer > 5f)
                    {
                        _returnActive = false; _returnVibActive = false; _returnLiftDone = false;
                        _returnVibAmp = 0f; _returnVibHz = 0f;
                        _returnLiftTimer = 0f; _returnLiftStopTimer = 0f; _returnMaxTimer = 0f;
                        CsVibOff(false);
                        Log?.Msg("[Return] mechRoot消失5秒 → 帰還強制終了");
                    }
                }
                else { _returnMaxTimer = 0f; }
            }
            else { _returnMaxTimer = 0f; }

            // 前半終了タイマー：Lift+23秒でVibOFF + Stop shake
            if (_returnLiftStopTimer > 0f)
            {
                _returnLiftStopTimer -= dt;
                if (_returnLiftStopTimer <= 0f)
                {
                    CsVibOff(false);
                    CsShake(CUTSCENE_QUAKE_DUR);
                    Log?.Msg("[Return] 前半終了（23秒経過）→ VibOFF + Stop shake");
                }
            }

            // End shake猶予タイマー（_launchActive=false後もShakeAxis出力を維持）
            if (_launchGraceTimer > 0f) _launchGraceTimer -= dt;
            if (_returnGraceTimer > 0f) _returnGraceTimer -= dt;

            // 出撃ガタッ
            if (_launchActive && _launchVibActive && !_playerPaused)
            {
                _cutsceneRatchetTimer -= dt;
                if (_cutsceneRatchetTimer <= 0f)
                {
                    _cutsceneRatchetTimer = CUTSCENE_RATCHET_INT;
                    _cutsceneVibAmp = 0f; _cutsceneVibHz = 0f;
                    _cutsceneRatchetPulse = CUTSCENE_PULSE_DUR;
                    Log?.Msg("[Launch] ガタッパルス");
                }
                if (_cutsceneRatchetPulse > 0f)
                {
                    _cutsceneRatchetPulse -= dt;
                    if (_cutsceneRatchetPulse > 0f)
                        _landP.Trigger(CUTSCENE_PULSE_AMP, CUTSCENE_PULSE_HZ_MIN, CUTSCENE_PULSE_HZ_MAX, CUTSCENE_PULSE_DUR, CUTSCENE_PULSE_DUR);
                    else if (_launchVibActive)
                    { _cutsceneVibAmp = CUTSCENE_VIB_AMP; _cutsceneVibHz = CUTSCENE_VIB_HZ; }
                }
            }

            // 帰還ガタッ
            if (_returnActive && _returnVibActive && !_gamePaused)
            {
                _returnRatchetTimer -= dt;
                if (_returnRatchetTimer <= 0f)
                {
                    _returnRatchetTimer = CUTSCENE_RATCHET_INT;
                    _returnVibAmp = 0f; _returnVibHz = 0f;
                    _returnRatchetPulse = CUTSCENE_PULSE_DUR;
                    Log?.Msg("[Return] ガタッパルス");
                }
                if (_returnRatchetPulse > 0f)
                {
                    _returnRatchetPulse -= dt;
                    if (_returnRatchetPulse > 0f)
                        _escP.Trigger(CUTSCENE_PULSE_AMP, CUTSCENE_PULSE_HZ_MIN, CUTSCENE_PULSE_HZ_MAX, CUTSCENE_PULSE_DUR, CUTSCENE_PULSE_DUR);
                    else if (_returnVibActive)
                    { _returnVibAmp = CUTSCENE_VIB_AMP; _returnVibHz = CUTSCENE_VIB_HZ; }
                }
            }

            // 帰還クールダウン
            if (_returnCooldown > 0f) _returnCooldown -= dt;

            // ─────────────────────────────────────────────────────────
            // CutScene.txt タイムライン処理（Gas and Go 等）
            // Time.timeScale==0（メニュー等でポーズ中）はタイマーを進めない
            // ─────────────────────────────────────────────────────────
            if (_csTimer >= 0f && _activeCsScene != null && Time.timeScale > 0f && !_gamePaused)
            {
                _csTimer += dt;
                var scene = _activeCsScene;
                float t = _csTimer;

                // VIB: アクティブなウィンドウのsin波を加算
                _csDmgValue = 0f;
                foreach (var vib in scene.Vibs)
                {
                    if (t >= vib.Start && t <= vib.End)
                    {
                        _csDmgValue += (vib.Amp / 100f) * UnityEngine.Mathf.Sin(2f * UnityEngine.Mathf.PI * vib.Hz * t);
                    }
                }

                // CRADLE: ゆっくりした揺り籠（ch1-3 Pitch/Roll/Yaw）
                // CRADLE
                if (scene.CradlePeriod > 0f && (scene.CradleAmp > 0f || scene.CradlePitchAmp >= 0f))
                {
                    float w  = 2f * UnityEngine.Mathf.PI / scene.CradlePeriod;
                    float pA = scene.CradlePitchAmp >= 0f ? scene.CradlePitchAmp : scene.CradleAmp;
                    float rA = scene.CradleRollAmp  >= 0f ? scene.CradleRollAmp  : scene.CradleAmp;
                    float yA = scene.CradleYawAmp   >= 0f ? scene.CradleYawAmp   : scene.CradleAmp;
                    _csCradlePitch = pA * UnityEngine.Mathf.Sin(w * t + scene.CradlePitchPhase * UnityEngine.Mathf.PI / 180f);
                    _csCradleRoll  = rA * UnityEngine.Mathf.Sin(w * t + scene.CradleRollPhase  * UnityEngine.Mathf.PI / 180f);
                    _csCradleYaw   = yA * UnityEngine.Mathf.Sin(w * t + scene.CradleYawPhase   * UnityEngine.Mathf.PI / 180f);
                    // 5秒ごとにデバッグログ
                    if ((int)(t / 5f) != (int)((t - dt) / 5f))
                        Log?.Msg($"[CutScene] CRADLE t={t:F1}s pitch={_csCradlePitch:F3} roll={_csCradleRoll:F3}");
                }

                // シーン終了
                if (t > scene.Duration)
                {
                    _csTimer = -1f; _activeCsScene = null;
                    _csDmgValue = 0f;
                    _csCradlePitch = _csCradleRoll = _csCradleYaw = 0f;
                    if (scene.PostDisable)
                    {
                        _csPostDisable = true;
                        // BlipsYawはリセットしない：カットシーン中に強制初期化済みの基準角度をそのまま使用
                        // （リセットすると5秒の再安定化待機中にYaw=0固定になる）
                        Log?.Msg($"[CutScene] {scene.Name} 終了 → POST_DISABLE ON");
                    }
                    else                   { Log?.Msg($"[CutScene] {scene.Name} 終了"); }
                }
            }

            // Walk vib flag
            if (FlagVibWalk) { FlagVibWalk = false; _vibWalkTimer = FlagVibWalkDur; FlagVibWalkDur = 0.3f; }

            // BlastShield ファイナルシェイク（2.6秒後に0.3秒の強い揺れ）
            if (_shieldFinalShakeTimer >= 0f)
            {
                _shieldFinalShakeTimer -= dt;
                if (_shieldFinalShakeTimer < 0f)
                {
                    _escP.Trigger(ESC_FINAL_AMP, ESC_FREQ_MIN_FINAL, ESC_FREQ_MAX_FINAL, ESC_FINAL_DUR, ESC_FINAL_DUR);
                    _escR.Trigger(ESC_FINAL_AMP, ESC_FREQ_MIN_FINAL, ESC_FREQ_MAX_FINAL, ESC_FINAL_DUR, ESC_FINAL_DUR);
                    _escY.Trigger(ESC_FINAL_AMP, ESC_FREQ_MIN_FINAL, ESC_FREQ_MAX_FINAL, ESC_FINAL_DUR, ESC_FINAL_DUR);
                    Log?.Msg("[Esc] BlastShield ファイナルシェイク発火");
                }
            }

            // Railgun charge timer
            if (_railCharging)
            {
                _railChargeTimer += dt;
                if (_railChargeTimer > RAIL_CHARGE_TIME) _railChargeTimer = RAIL_CHARGE_TIME;
            }

            // Damage vib flag
            if (FlagVibDmg) { FlagVibDmg = false; _vibDmgTimer = FlagVibDmgDur; FlagVibDmgDur = 1.5f; }

            // Escape vib flag
            if (FlagVibEscape) { FlagVibEscape = false; _vibJetTimer = VIB_ESCAPE_DUR; }

            // Weapon vib flag
            if (FlagVibWeapon)
            {
                FlagVibWeapon = false;
                _weaponVibAmp = Mathf.Max(_weaponVibAmp, FlagVibWeaponAmp);
                _weaponVibHz = Mathf.Max(_weaponVibHz, FlagVibWeaponHz);
                _vibWeaponDur = FlagVibWeaponDur;
                FlagVibWeaponAmp = 0f; FlagVibWeaponHz = 0f; FlagVibWeaponDur = 0.5f;
            }

            // Laser vib flag（照射中ずっと一定維持。タイマーリセット方式）
            if (FlagLaserVib)
            {
                FlagLaserVib = false;
                _laserVibAmp     = FlagLaserVibAmp;
                _laserVibHz      = FlagLaserVibHz;
                _laserVibTimer   = FlagLaserVibTimeout;
                FlagLaserVibAmp  = 0f;
                FlagLaserVibHz   = 0f;
                FlagLaserVibTimeout = LASER_VIB_TIMEOUT;
            }

            _landP.Step(dt); _landR.Step(dt); _landY.Step(dt);
            _escP.Step(dt); _escR.Step(dt); _escY.Step(dt);
            _scanTimer -= dt;
            if (_scanTimer <= 0f) { _scanTimer = 1f; TryScan(); }

            if (_mechRoot != null) UpdatePhysics(dt);
            if (_mechRoot != null) UpdateLanding(dt);
            UpdateWalkAccel(dt);
            UpdateDamageAccel(dt);
            UpdateVibrationDecay(dt);
            UpdateWeaponMonitor();  // 武器発射検出
            _udpTimer += dt;
            if (_udpTimer >= UDP_HZ) { _udpTimer = 0f; SendTelemetry(); }
        }

        private void UpdateWalkAccel(float dt)
        {
            if (_cameraEyelevel == null && _cockpitObj != null) {
                _cameraEyelevel = FindChildRecursive(_cockpitObj.transform, "camera_POS_eyelevel");
                if (_cameraEyelevel != null) {
                    Log?.Msg("[Walk] camera_POS_eyelevel 発見");
                    _walkCalibrated = false; _walkCalibrateTime = 0f;
                    _dmgCalibrated = false; _dmgCalibrateTime = 0f;
                }
            }
            if (_cameraEyelevel == null) { _walkValue = 0f; return; }

            if (!_walkCalibrated) {
                _walkCalibrateTime += dt;
                if (_walkCalibrateTime >= 2.0f) {
                    _walkPrevY = _cameraEyelevel.position.y;
                    _walkPrevVelY = 0f; _walkPrevTime = Time.time;
                    for (int i = 0; i < ACCEL_SMOOTH_SIZE; i++) _accelBuffer[i] = 0f;
                    _accelSum = 0f; _accelIndex = 0;
                    for (int i = 0; i < VEL_SMOOTH_SIZE; i++) _velBuffer[i] = 0f;
                    _velSum = 0f; _velIndex = 0;
                    _walkCalibrated = true;
                    Log?.Msg("[Walk] キャリブレーション完了");
                }
                _walkValue = 0f; return;
            }

            float currentTime = Time.time;
            float timeDelta = currentTime - _walkPrevTime;
            if (timeDelta < 0.001f) timeDelta = 0.016f;

            float camY = _cameraEyelevel.position.y;
            float velY = (camY - _walkPrevY) / timeDelta;

            _velSum -= _velBuffer[_velIndex];
            _velSum += velY;
            _velBuffer[_velIndex] = velY;
            _velIndex = (_velIndex + 1) % VEL_SMOOTH_SIZE;
            float smoothedVel = _velSum / VEL_SMOOTH_SIZE;

            float accelY = (velY - _walkPrevVelY) / timeDelta;
            _accelSum -= _accelBuffer[_accelIndex];
            _accelSum += accelY;
            _accelBuffer[_accelIndex] = accelY;
            _accelIndex = (_accelIndex + 1) % ACCEL_SMOOTH_SIZE;
            float smoothedAccel = _accelSum / ACCEL_SMOOTH_SIZE;

            if (smoothedVel > JET_VELOCITY_THRESHOLD) {
                _isJetting = true; _jetCooldownTimer = JET_COOLDOWN;
            } else if (_jetCooldownTimer > 0) {
                _jetCooldownTimer -= timeDelta; _isJetting = true;
            } else {
                _isJetting = false;
            }

            if (!_isJetting)
                _walkValue = Mathf.Clamp(smoothedAccel / ACCEL_SCALE, -1f, 1f);
            else
                _walkValue = 0f;

            _walkPrevY = camY; _walkPrevVelY = velY; _walkPrevTime = currentTime;
        }

        private void UpdateDamageAccel(float dt)
        {
            if (_cameraEyelevel == null) {
                _dmgPitch = _dmgRoll = _dmgYaw = 0f;
                return;
            }

            if (!_dmgCalibrated) {
                _dmgCalibrateTime += dt;
                if (_dmgCalibrateTime >= 2.0f) {
                    Vector3 initPos = _cameraEyelevel.position;
                    _dmgPrevX = initPos.x; _dmgPrevY = initPos.y; _dmgPrevZ = initPos.z;
                    _dmgPrevVelX = _dmgPrevVelY = _dmgPrevVelZ = 0f;
                    _dmgPrevTime = Time.time;

                    for (int i = 0; i < DMG_SMOOTH_SIZE; i++) {
                        _dmgAccelXBuf[i] = 0f; _dmgAccelYBuf[i] = 0f; _dmgAccelZBuf[i] = 0f;
                    }
                    _dmgAccelXSum = _dmgAccelYSum = _dmgAccelZSum = 0f;
                    _dmgAccelXIdx = _dmgAccelYIdx = _dmgAccelZIdx = 0;

                    _dmgCalibrated = true;
                    Log?.Msg("[Damage] キャリブレーション完了");
                }
                _dmgPitch = _dmgRoll = _dmgYaw = 0f;
                return;
            }

            float currentTime = Time.time;
            float timeDelta = currentTime - _dmgPrevTime;
            if (timeDelta < 0.001f) timeDelta = 0.016f;

            Vector3 pos = _cameraEyelevel.position;
            float velX = (pos.x - _dmgPrevX) / timeDelta;
            float velY = (pos.y - _dmgPrevY) / timeDelta;
            float velZ = (pos.z - _dmgPrevZ) / timeDelta;

            float accelX = (velX - _dmgPrevVelX) / timeDelta;
            float accelY = (velY - _dmgPrevVelY) / timeDelta;
            float accelZ = (velZ - _dmgPrevVelZ) / timeDelta;

            _dmgAccelXSum -= _dmgAccelXBuf[_dmgAccelXIdx];
            _dmgAccelXSum += accelX;
            _dmgAccelXBuf[_dmgAccelXIdx] = accelX;
            _dmgAccelXIdx = (_dmgAccelXIdx + 1) % DMG_SMOOTH_SIZE;

            _dmgAccelYSum -= _dmgAccelYBuf[_dmgAccelYIdx];
            _dmgAccelYSum += accelY;
            _dmgAccelYBuf[_dmgAccelYIdx] = accelY;
            _dmgAccelYIdx = (_dmgAccelYIdx + 1) % DMG_SMOOTH_SIZE;

            _dmgAccelZSum -= _dmgAccelZBuf[_dmgAccelZIdx];
            _dmgAccelZSum += accelZ;
            _dmgAccelZBuf[_dmgAccelZIdx] = accelZ;
            _dmgAccelZIdx = (_dmgAccelZIdx + 1) % DMG_SMOOTH_SIZE;

            // デッドゾーン適用: 閾値未満はゼロ（近くの爆発・振動などのノイズを除去）
            float rawP = _dmgAccelYSum / DMG_SMOOTH_SIZE / DMG_ACCEL_SCALE;
            float rawR = _dmgAccelZSum / DMG_SMOOTH_SIZE / DMG_ACCEL_SCALE;
            float rawY = _dmgAccelXSum / DMG_SMOOTH_SIZE / DMG_ACCEL_SCALE;
            _dmgPitch = Mathf.Abs(rawP) >= DMG_DEADZONE ? Mathf.Clamp(rawP, -1f, 1f) : 0f;
            _dmgRoll  = Mathf.Abs(rawR) >= DMG_DEADZONE ? Mathf.Clamp(rawR, -1f, 1f) : 0f;
            _dmgYaw   = Mathf.Abs(rawY) >= DMG_DEADZONE ? Mathf.Clamp(rawY, -1f, 1f) : 0f;

            _dmgPrevX = pos.x; _dmgPrevY = pos.y; _dmgPrevZ = pos.z;
            _dmgPrevVelX = velX; _dmgPrevVelY = velY; _dmgPrevVelZ = velZ;
            _dmgPrevTime = currentTime;
        }

        private Transform FindChildRecursive(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++) {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
            }
            for (int i = 0; i < parent.childCount; i++) {
                var found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private void UpdateVibrationDecay(float dt)
        {
            // Walk timer
            _vibWalkTimer = Mathf.Max(0f, _vibWalkTimer - dt);
            // Land timer
            _vibLandTimer = Mathf.Max(0f, _vibLandTimer - dt);
            // Damage timer
            _vibDmgTimer = Mathf.Max(0f, _vibDmgTimer - dt);
            // Jet timer
            _vibJetTimer = Mathf.Max(0f, _vibJetTimer - dt);
            // Laser vib: 減衰なし、タイマーが切れたら停止
            _laserVibTimer = Mathf.Max(0f, _laserVibTimer - dt);
            if (_laserVibTimer <= 0f) { _laserVibAmp = 0f; _laserVibHz = 0f; }
            // Weapon decay - AMPのみ減衰、HZはAMPが0になるまで維持
            if (_vibWeaponDur > 0f && _weaponVibAmp > 0f)
            {
                float decay = dt / _vibWeaponDur;
                _weaponVibAmp = Mathf.Max(0f, _weaponVibAmp - decay * 100f);
                // AMPが0になったらHZもリセット
                if (_weaponVibAmp <= 0f)
                {
                    _weaponVibHz = 0f;
                }
            }
        }

        private void UpdatePhysics(float dt)
        {
            Vector3 pos = _mechRoot.position;
            Vector3 rawVel = (pos - _prevPos) / dt;
            if (rawVel.magnitude > SPEED_MAX) { _prevPos = pos; return; }

            float aVel = 1f - FastExp(-dt / TAU_VEL);
            _filtVel = Vector3.Lerp(_filtVel, rawVel, aVel);
            float aS = 1f - FastExp(-dt / TAU_SPEED);
            _smoothSpd = Mathf.Lerp(_smoothSpd, _filtVel.magnitude, aS);

            // ════════════════════════════════════════════════════════
            // Yaw角速度計算：blipsから取得（HMD独立）
            // ════════════════════════════════════════════════════════
            float yawRate = 0f;
            if (_blipsTransform != null && _blipsYawInitialized)
            {
                float rawBlipsYaw = _blipsTransform.eulerAngles.y;
                float yawDelta = Mathf.DeltaAngle(_prevBlipsYawPhysics, rawBlipsYaw);
                yawRate = yawDelta / dt;
                _prevBlipsYawPhysics = rawBlipsYaw;
            }
            else
            {
                float curYaw = _mechRoot.eulerAngles.y;
                float yawDelta = Mathf.DeltaAngle(_prevYaw, curYaw);
                yawRate = yawDelta / dt;
                _prevYaw = curYaw;
            }
            
            float target = Mathf.Clamp(yawRate / MAX_YAW_RATE, -1f, 1f);
            _rawTurnRate = target;
            bool rising = Mathf.Abs(target) > Mathf.Abs(_smoothLat);
            float tau = rising ? TAU_TURN_RISE : TAU_TURN_FALL;
            float aT = 1f - FastExp(-dt / tau);
            _smoothLat = Mathf.Lerp(_smoothLat, target, aT);

            bool noShakeEvent = !FlagVibDmg && _vibDmgTimer <= 0f
                             && !FlagLanding && _landP.Value == 0f
                             && !FlagEscape && _escP.Value == 0f
                             && !_eulerPaused;

            // Turning_Vibもデッドゾーンを適用
            float absRate = Mathf.Abs(yawRate);
            if (absRate > TURNING_DEADZONE && noShakeEvent)
            {
                _isTurningByPlayer = true;
                _turnVibValue = Mathf.Abs(target);
            }
            else
            {
                _isTurningByPlayer = false;
                _turnVibValue = 0f;
            }

            _prevPos = pos;
        }

        private void UpdateLanding(float dt)
        {
            if (_eulerPaused || _respawnFreezeArmed || _respawnFrozen) return;

            float y = _mechRoot.position.y;
            float vel = (y - _mechPrevY) / dt;
            float accel = (vel - _prevFallVel) / dt;
            _landCooldown -= dt;

            if (vel < FALL_VEL_THRESH)
            {
                if (!_falling)
                {
                    _falling = true;
                    _fallStartY = y;
                    _landingAccelPeak = 0f;
                }
            }
            else if (_falling && vel > -2.0f && _landCooldown <= 0f)
            {
                float fallDist = _fallStartY - y;
                if (accel > _landingAccelPeak) _landingAccelPeak = accel;
                float fallSpeed = Mathf.Abs(_prevFallVel);

                if (fallDist >= MIN_FALL_DIST)
                {
                    float ratio = Mathf.Clamp01((fallSpeed - 5f) / 20f);
                    float baseAmp = 1.0f;
                    float randAmp = baseAmp + (UnityEngine.Random.value * 0.6f - 0.3f);
                    float baseDurMin = 0.8f + ratio * 2.7f;
                    float baseDurMax = 1.0f + ratio * 3.0f;

                    FlagLanding = true;
                    FlagLandingAmp = Mathf.Clamp(randAmp, 0.7f, 1.3f);
                    FlagLandingDurMin = baseDurMin;
                    FlagLandingDurMax = baseDurMax;

                    Log?.Msg($"[Land] dist={fallDist:F1}m vel={fallSpeed:F1}m/s amp={randAmp:F2} dur={baseDurMin:F1}-{baseDurMax:F1}");
                }

                _falling = false; _landingAccelPeak = 0f; _landCooldown = 1f;
            }
            else if (vel > 3.0f)
            {
                _falling = false; _landingAccelPeak = 0f;
            }

            _prevFallVel = vel;
            _mechPrevY = y;
        }

        private void TryScan()
        {
            var newCockpit = GameObject.Find("MainCockpit(Clone)");
            if (newCockpit == null && _mechRoot != null && (!_returnActive || !_storyMissionActive)) {
                _mechRoot = null; _cockpitRef = null; _cockpitObj = null; _cockpitFound = false;
                _cameraEyelevel = null; _walkCalibrated = false; _dmgCalibrated = false;
                _blipsTransform = null;
                // ★ _smoothInitialized/_blipsYawOffset/_accumulatedYaw はリセットしない
                //    リセットするとYawが0にスナップする危険な動作になるため
                _blipsYawInitialized = false;
                _blipsStabilizing = false; _blipsStabilizeTimer = 0f;
                Log?.Msg("[Scan] reset");
                UpdateJumpJet_Patch._profileLoaded = false;  // 次セッションでプロファイル再読み込みを許可
                _currentTypeId = -1;
                // ★ シーン終了時に全エフェクトを完全停止
                _launchActive = false; _launchVibActive = false; _launchLiftStarted = false;
                _launchStopDelay = 0f; _launchResumeTimer = 0f; _launchLiftDelay = 0f; _launchEndDelay = 0f;
                _launchGraceTimer = 0f; _returnGraceTimer = 0f;
                _cutsceneVibAmp = 0f; _cutsceneVibHz = 0f;
                _returnActive = false; _returnVibActive = false; _returnLiftDone = false;
                _returnVibAmp = 0f; _returnVibHz = 0f;
                _returnCooldown = 30f; _returnLiftTimer = 0f; _returnLiftStopTimer = 0f; _returnTimeout = 0f;
                _returnHeatCancelTimer = 0f;
                _storyMissionActive = false;
                _waitingForCutscene = false;
                _gamePaused = false;
                _playerPaused = false;
                // ★ ゲームプレイ中の持続状態変数を全クリア（次ミッション開始時の誤出力防止）
                _jetEffectActive = false;
                _vibJetTimer     = 0f;
                _vibWalkTimer    = 0f;
                _vibLandTimer    = 0f;
                _vibDmgTimer     = 0f;
                _weaponVibAmp    = 0f;
                _weaponVibHz     = 0f;
                _laserVibTimer   = 0f;
                _drillActive     = false;
                _railCharging    = false;
                _turnVibValue    = 0f;
                _dmgPitch = 0f; _dmgRoll = 0f; _dmgYaw = 0f;
                _walkValue  = 0f;
                _smoothLat  = 0f;
                // ShakeAxis 残留値をゼロリセット
                _landP.Reset(); _landR.Reset(); _landY.Reset();
                _escP.Reset();  _escR.Reset();  _escY.Reset();
                // CutSceneリセット
                _activeCsScene = null; _csTimer = -1f;
                _csDmgValue = 0f; _csCradlePitch = _csCradleRoll = _csCradleYaw = 0f;
                _csPostDisable = false;
                _stageConfirmedCs = null;
                return;
            }
            if (newCockpit == null) return;
            if (_cockpitObj != newCockpit) {
                _cockpitObj = newCockpit; _cockpitFound = false;
                _cameraEyelevel = null; _walkCalibrated = false; _dmgCalibrated = false;
                _blipsTransform = null;
                // _smoothInitialized/_blipsYawOffset はYawスナップ防止のためリセットしない
                _blipsYawInitialized = false;
                _blipsStabilizing = false; _blipsStabilizeTimer = 0f;
            }

            if (_mechRoot == null) {
                float best = float.MaxValue; GameObject closest = null;
                foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) {
                    if (r.name != "EntityInstance") continue;
                    float d = Vector3.Distance(r.transform.position, _cockpitObj.transform.position);
                    if (d < best) { best = d; closest = r; }
                }
                if (closest != null && best < 20f) {
                    _mechRoot = closest.transform;
                    _prevPos = _mechRoot.position;
                    _mechPrevY = _mechRoot.position.y;
                    _prevYaw = _mechRoot.eulerAngles.y;
                    if (_eulerPaused) { _eulerPaused = false; Log?.Msg("[Euler] 再開 (MechRoot 再検出)"); }
                    // ★ チュートリアル等、BlastShieldOpen なしでステージ遷移する場合
                    //    _respawnFrozen が解除されないまま残る問題を修正。
                    //    MechRoot 再検出でフリーズを解除して送信を再開する。
                    if (_respawnFrozen || _respawnFreezeArmed)
                    {
                        _respawnFrozen = false;
                        _respawnFreezeArmed = false;
                        _respawnFreezeCountdown = 0f;
                        Log?.Msg("[Scan] _respawnFrozen 解除 (BlastShieldOpenなし遷移 → MechRoot再検出で自動解除)");
                    }
                    Log?.Msg("[Scan] MechRoot: " + closest.name);

                    // ── ロボットプロファイルをMechRoot検出時点で即座に読み込む ──
                    // （JetPatch依存を廃止。ジェット未使用でも起動直後に確定）
                    try
                    {
                        var flags2 = System.Reflection.BindingFlags.Public |
                                     System.Reflection.BindingFlags.NonPublic |
                                     System.Reflection.BindingFlags.Instance;
                        var ei = closest.GetComponent<Il2CppVoxClient.EntityInstance>();
                        if (ei != null)
                        {
                            var pTypeId = typeof(Il2CppVoxClient.EntityInstance).GetProperty("typeId", flags2);
                            if (pTypeId?.GetValue(ei) is int typeId)
                            {
                                Log?.Msg($"[Profile] typeId={typeId} 検出 → プロファイル読み込み");
                                LoadRobotProfile(typeId);
                                UpdateJumpJet_Patch._profileLoaded = true;  // JetPatch側の二重読み込みを防ぐ
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log?.Msg($"[Profile] MechRoot時typeId取得エラー: {ex.Message}");
                    }
                    // ★ MechRoot検出 = ゲーム操作可能 → _gamePaused を強制解除
                    // ただし帰還カットシーン中はスキップ（帰還中のMechRoot再検出で誤解除防止）
                    if (_gamePaused && !_returnActive)
                    {
                        _gamePaused = false;
                        _waitingForCutscene = false;
                        Log?.Msg("[Pause] MechRoot検出 → _gamePaused 強制解除");
                    }
                    // 出撃・帰還カットシーンを強制終了
                    if (_launchActive)
                    {
                        _launchActive = false; _launchVibActive = false; _launchLiftStarted = false;
                        _cutsceneVibAmp = 0f; _cutsceneVibHz = 0f;
                        _launchStopDelay = _launchResumeTimer = _launchLiftDelay = _launchEndDelay = 0f;
                        // CutScene.txtのカットシーン未開始ならポーズ状態を維持（Pitch/Roll漏れ防止）
                        // ただし CutScene.txt に登録なし（_csTimer=-1f）のステージや
                        // リスポーン後はgamePausedをセットしない（MainMonitorCloseが来ないため永続する）
                        if (_csTimer < 0f)
                        {
                            if (!_isRespawnSpawn && _storyMissionActive && _csTimer < -0.5f)
                            {
                                // _csTimer が -1f のまま（CutScene.txtにエントリなし）は
                                // MainMonitorOpen/Close によるポーズ解除を待つ通常フロー
                                // → gamePaused=true をセットせず、そのまま操作可能にする
                                Log?.Msg("[Launch] MechRoot検出 → 強制終了（CutScene未登録ステージ → gamePausedスキップ）");
                            }
                            else if (!_isRespawnSpawn)
                            {
                                _gamePaused = true;
                                _waitingForCutscene = false;
                                Log?.Msg("[Launch] MechRoot検出 → 強制終了（gamePaused=true）");
                            }
                            else
                            {
                                Log?.Msg("[Launch] MechRoot検出 → 強制終了（リスポーン → gamePausedスキップ）");
                            }
                            _isRespawnSpawn = false;  // フラグをクリア
                        }
                        else Log?.Msg("[Launch] MechRoot検出 → 強制終了");
                    }
                    // 帰還カットシーンはMechRoot検出で終了しない（格納庫搬送中にMechRootが来ることがある）
                    // 帰還はPlay_Launch_DetachまたはScan resetで終了する
                }
            }
            if (!_cockpitFound && _cockpitObj != null) {
                Transform refTr = null;
                foreach (var rb in _cockpitObj.GetComponentsInChildren<Rigidbody>())
                    if (rb.name.ToLower().Contains("leg")) { refTr = rb.transform; break; }
                if (refTr == null) {
                    var rbs = _cockpitObj.GetComponentsInChildren<Rigidbody>();
                    if (rbs.Length > 0) refTr = rbs[0].transform;
                }
                if (refTr == null) refTr = _cockpitObj.transform;
                _cockpitRef = refTr; _cockpitFound = true;
                Log?.Msg("[Scan] CockpitRef: " + refTr.name);

                // ════════════════════════════════════════════════════════
                // レーダー/コンパス調査用コード
                // ════════════════════════════════════════════════════════
                ScanForBlipsObject();
            }
        }

        // ════════════════════════════════════════════════════════════════
        // blips Yaw 関連（HMD独立・相対角度）
        // ════════════════════════════════════════════════════════════════
        internal Transform _blipsTransform;           // blipsオブジェクト（Yawソース）
        internal float _blipsYawOffset = 0f;          // 基準角度（開始時の向き）
        internal float _accumulatedYaw = 0f;          // delta累積Yaw（モニターアニメを除外した純粋な回転量）
        internal bool _blipsYawInitialized = false;
        internal float _prevBlipsYaw = 0f;            // Turning計算用（前フレームのYaw）
        internal float _prevBlipsYawPhysics = 0f;     // UpdatePhysics用（毎フレーム更新）
        private float _blipsYawRate = 0f;            // 角速度（度/秒）
        
        // 安定化待機（レーダーアニメーション対策）
        private float _blipsStabilizeTimer = 0f;
        private static float BLIPS_STABILIZE_DELAY = 5.0f;
        internal bool _blipsStabilizing = false;
        
        // Turningデッドゾーン（HMD連動の微小な動きを抑制）
        private static float TURNING_DEADZONE = 10.0f;
        private static float TURNING_CURVE = 2.0f;
        
        // ════════════════════════════════════════════════════════════════
        // 武器インジケーター監視
        // ════════════════════════════════════════════════════════════════
        private Transform[] _weaponTransform = new Transform[6];  // weapon0-5本体
        private Renderer[] _weaponRenderer = new Renderer[6];     // weapon0-5のRenderer
        private Color[] _prevWeaponColor = new Color[6];          // 前フレームのカラー
        private bool _weaponIndicatorsFound = false;

        private void ScanForBlipsObject()
        {
            // MainCockpit配下からblipsオブジェクトを検索
            if (_cockpitObj != null)
            {
                var allTransforms = _cockpitObj.GetComponentsInChildren<Transform>(true);
                foreach (var t in allTransforms)
                {
                    if (t.name == "blips")
                    {
                        _blipsTransform = t;
                        Log?.Msg("[Yaw] blips検出完了");
                        break;
                    }
                }
            }

            if (_blipsTransform == null)
            {
                Log?.Msg("[Yaw] WARNING: blips未検出 → mechRoot使用");
                // ★ 診断: blipsが見つからない場合にコクピット配下の全Transform名をダンプ
                // Scoutのblips代替オブジェクトを特定するため
                if (_cockpitObj != null)
                {
                    Log?.Msg("[Yaw-D] --- cockpit children dump ---");
                    var allT = _cockpitObj.GetComponentsInChildren<Transform>(true);
                    foreach (var t in allT)
                        Log?.Msg($"[Yaw-D]  {t.name}");
                }
            }
            
            // ★ 武器インジケーター調査
            ScanForWeaponIndicators();
        }
        
        // ════════════════════════════════════════════════════════════════
        // 武器インジケーター調査（右上の武器UI）
        // ════════════════════════════════════════════════════════════════
        private void ScanForWeaponIndicators()
        {
            // Weapon.Fire()フックで発射検出するため、UI監視は不要
            _weaponIndicatorsFound = true;
        }
        
        private float _weaponMonitorLogTimer = 0f;
        // FootImpact 診断ログカウンター（スパム防止）
        internal static int _diagFootLogCount = 0;
        
        private void UpdateWeaponMonitor()
        {
            // Weapon.Fire()フックで発射検出するため、ここでは何もしない
        }

        internal static void TriggerDamageVib(float dur = 1.5f)
        {
            FlagVibDmgDur = dur; FlagVibDmg = true;
        }

        internal static void TriggerWalkVib()
        {
            FlagVibWalkDur = 0.1f + UnityEngine.Random.value * 0.4f;
            FlagVibWalk = true;
        }

        internal static void TriggerEscape(float amp, bool isEject = false)
        {
            // 帰還カットシーン中はEscapeトリガーをスキップ（ガタッパルスの上書き防止）
            if (Instance != null && Instance._returnActive) return;
            FlagEscapeAmp = amp;
            if (isEject)
            {
                FlagEscapeFreqMin = ESC_FREQ_MIN_EJECT;
                FlagEscapeFreqMax = ESC_FREQ_MAX_EJECT;
                FlagEscapeDurMin = ESC_DUR_MIN_EJECT;
                FlagEscapeDurMax = ESC_DUR_MAX_EJECT;
                FlagVibEscape = true;
                FlagEulerPause = true;
            }
            else
            {
                FlagEscapeFreqMin = ESC_FREQ_MIN_SHIELD;
                FlagEscapeFreqMax = ESC_FREQ_MAX_SHIELD;
                FlagEscapeDurMin = FlagShieldDur;
                FlagEscapeDurMax = FlagShieldDur;
            }
            FlagEscape = true;
        }

        internal static void TriggerWeaponVib(float amp, float hz, float dur)
        {
            FlagVibWeaponAmp = Mathf.Max(FlagVibWeaponAmp, amp);
            FlagVibWeaponHz = Mathf.Max(FlagVibWeaponHz, hz);
            FlagVibWeaponDur = Mathf.Max(FlagVibWeaponDur, dur);
            FlagVibWeapon = true;
        }
        
        // 自分の機体ルートを取得（武器発射判定用）
        internal static Transform GetMechRoot()
        {
            var inst = Instance;
            return inst?._mechRoot;
        }

        private void SendTelemetry()
        {
            // 帰還カットシーン中はrespawnFrozenによるゼロ送信をスキップ
            if (_respawnFrozen && !_returnActive)
            {
                string zero = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F2},0.000,0.000,0.000,0.000," +
                    "0.000,0.000,0.000," +
                    "0.000,0.000,0.000," +
                    "0.000,0.000,0.000," +
                    "0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000",
                    _smoothYaw);
                byte[] zd = Encoding.ASCII.GetBytes(zero);
                try { Udp?.Send(zd, zd.Length, PLUGIN_IP, PLUGIN_PORT); } catch { }
                return;
            }

            float pitch = 0f, roll = 0f, yaw = _smoothYaw;  // ← yawは常に最終既知値で初期化（スナップ防止）
            float turning = 0f;

            // ════════════════════════════════════════════════════════
            // BlipsYaw 安定化（_mechRoot到達前でも実行）
            // カットシーン中にblipsが検出済みで未初期化の場合に安定化タイマーを進める
            // ════════════════════════════════════════════════════════
            if (_blipsTransform != null && !_blipsYawInitialized && _csTimer < 0f)
            {
                float rawBlipsYaw = _blipsTransform.eulerAngles.y;
                if (!_blipsStabilizing)
                {
                    if (_currentTypeId >= 0 || _launchActive)  // プロファイルロード済みOR出撃カットシーン中
                    {
                        _blipsStabilizing = true;
                        _blipsStabilizeTimer = BLIPS_STABILIZE_DELAY;
                        _prevBlipsYaw = rawBlipsYaw;
                        _prevBlipsYawPhysics = rawBlipsYaw;
                        Log?.Msg($"[BlipsYaw] 安定化待機開始（{BLIPS_STABILIZE_DELAY}秒）...");
                    }
                }
                else
                {
                    _blipsStabilizeTimer -= Time.deltaTime;
                    if (_blipsStabilizeTimer <= 0f)
                    {
                        _blipsYawOffset = rawBlipsYaw;
                        _accumulatedYaw = (MULT_YAW != 0f) ? (_smoothYaw / MULT_YAW) : 0f;
                        _prevBlipsYaw = rawBlipsYaw;
                        _prevBlipsYawPhysics = rawBlipsYaw;
                        _blipsYawInitialized = true;
                        _blipsStabilizing = false;
                        Log?.Msg($"[BlipsYaw] ★ 基準角度設定: {_blipsYawOffset:F1}°（安定化完了・開始Yaw:{_smoothYaw:F1}°）");
                        Log?.Msg($"[BlipsYaw] フィルタ閾値: {YAW_RATE_FILTER:F1}°/s（yaw_rate_filter）");
                    }
                }
            }

            if (_mechRoot != null && !_eulerPaused)
            {
                Vector3 e = _mechRoot.eulerAngles;
                float ex = e.x; if (ex > 180f) ex -= 360f;
                float ez = e.z; if (ez > 180f) ez -= 360f;
                pitch = Mathf.Clamp(ex / PITCH_NORM, -1f, 1f) * MULT_PITCH;
                roll = Mathf.Clamp(ez / ROLL_NORM, -1f, 1f) * MULT_ROLL;

                // ════════════════════════════════════════════════════════
                // Yaw & Turning: blips.world.yから取得（HMD独立・相対角度）
                // ════════════════════════════════════════════════════════
                if (_blipsTransform != null)
                {
                    float rawBlipsYaw = _blipsTransform.eulerAngles.y;

                    // 安定化完了後の通常処理
                    if (_blipsYawInitialized)
                    {
                        // 角速度を計算（Turning用）
                        float yawDelta = Mathf.DeltaAngle(_prevBlipsYaw, rawBlipsYaw);
                        float dt = Time.deltaTime;

                        // ★ 角速度フィルタ: モニター開閉アニメを除外
                        // dt≤0.001fの場合はfloat.MaxValueとして扱い必ずフィルタする（dt=0の誤通過防止）
                        bool inGasAndGo = (_csTimer >= 0f) || _csPostDisable;
                        float rateMax = inGasAndGo ? YAW_RATE_FILTER_GAS : YAW_RATE_FILTER;
                        float yawRateAbs = dt > 0.001f ? Mathf.Abs(yawDelta) / dt : float.MaxValue;
                        if (yawRateAbs > rateMax)
                        {
                            _prevBlipsYaw = rawBlipsYaw;
                            Log?.Msg($"[Yaw] 角速度フィルタ: {yawRateAbs:F0}°/s → 無視");
                        }
                        else
                        {
                            if (dt > 0.001f) _blipsYawRate = yawDelta / dt;
                            _prevBlipsYaw    = rawBlipsYaw;
                            _accumulatedYaw += yawDelta;
                            // ★ Yawはデルタを直接_smoothYawに加算（スムージングバイパス）
                            // _smoothYaw経由でスムージングすると SMOOTHING=0.2 で実速度の1/5になるため
                            _smoothYaw += yawDelta * MULT_YAW;
                            if (_smoothYaw >  180f) _smoothYaw -= 360f;
                            if (_smoothYaw < -180f) _smoothYaw += 360f;
                            yaw = _smoothYaw;  // スムージングブロックでyawDiff=0になり再適用されない
                        }
                        
                        // ════════════════════════════════════════════════════════
                        // Turning: デッドゾーン＋非線形マッピング
                        // HMD連動の微小な動きを抑制し、実際の旋回を強調
                        // ════════════════════════════════════════════════════════
                        float absRate = Mathf.Abs(_blipsYawRate);
                        float sign = Mathf.Sign(_blipsYawRate);
                        
                        // デッドゾーン適用（TURNING_DEADZONE以下は0）
                        if (absRate < TURNING_DEADZONE)
                        {
                            turning = 0f;
                        }
                        else
                        {
                            // デッドゾーン後の値を0〜1に正規化
                            float normalizedRate = (absRate - TURNING_DEADZONE) / (MAX_YAW_RATE - TURNING_DEADZONE);
                            normalizedRate = Mathf.Clamp01(normalizedRate);
                            
                            // 非線形マッピング（2乗カーブで小さな値を抑制）
                            float curvedRate = Mathf.Pow(normalizedRate, TURNING_CURVE);
                            
                            turning = sign * curvedRate * MULT_TURNING;
                        }
                    }
                }
            }
            else if (_mechRoot != null && _eulerPaused)
            {
                // EulerPause中もblipsベースのTurningを使用
                if (_blipsTransform != null && _blipsYawInitialized)
                {
                    float rawBlipsYaw = _blipsTransform.eulerAngles.y;
                    float yawDelta = Mathf.DeltaAngle(_prevBlipsYaw, rawBlipsYaw);
                    float dt = Time.deltaTime;
                    if (dt > 0.001f) _blipsYawRate = yawDelta / dt;
                    _prevBlipsYaw = rawBlipsYaw;
                    
                    // デッドゾーン＋非線形マッピング
                    float absRate = Mathf.Abs(_blipsYawRate);
                    float sign = Mathf.Sign(_blipsYawRate);
                    if (absRate < TURNING_DEADZONE)
                    {
                        turning = 0f;
                    }
                    else
                    {
                        float normalizedRate = (absRate - TURNING_DEADZONE) / (MAX_YAW_RATE - TURNING_DEADZONE);
                        normalizedRate = Mathf.Clamp01(normalizedRate);
                        float curvedRate = Mathf.Pow(normalizedRate, TURNING_CURVE);
                        turning = sign * curvedRate * MULT_TURNING;
                    }
                }
                else
                {
                    turning = Mathf.Clamp(_smoothLat, -1f, 1f) * MULT_TURNING;
                }
            }

            float walk = Mathf.Clamp(_walkValue, -1f, 1f) * MULT_WALK;

            // ════════════════════════════════════════════════════════
            // スムージング適用（指数移動平均）
            // ════════════════════════════════════════════════════════
            if (!_smoothInitialized)
            {
                // 初回は現在値で初期化
                _smoothYaw = yaw;
                _smoothPitch = pitch;
                _smoothRoll = roll;
                _smoothWalk = walk;
                _smoothTurning = turning;
                _smoothInitialized = true;
            }
            else
            {
                // Yawは角度のラップアラウンドを考慮
                float yawDiff = yaw - _smoothYaw;
                if (yawDiff > 180f) yawDiff -= 360f;
                if (yawDiff < -180f) yawDiff += 360f;
                _smoothYaw += SMOOTHING * yawDiff;
                if (_smoothYaw > 180f) _smoothYaw -= 360f;
                if (_smoothYaw < -180f) _smoothYaw += 360f;
                
                _smoothPitch += SMOOTHING * (pitch - _smoothPitch);
                _smoothRoll += SMOOTHING * (roll - _smoothRoll);
                _smoothWalk += SMOOTHING * (walk - _smoothWalk);
                _smoothTurning += SMOOTHING * (turning - _smoothTurning);
            }
            
            // スムージング後の値を使用
            yaw = _smoothYaw;
            pitch = _smoothPitch;
            roll = _smoothRoll;
            walk = _smoothWalk;
            turning = _smoothTurning;

            float dmgP = _dmgPitch * MULT_DAMAGE_PITCH;
            float dmgR = _dmgRoll * MULT_DAMAGE_ROLL;
            float dmgY = _dmgYaw * MULT_DAMAGE_YAW;

            // ── CutScene.txt: CRADLE（揺り籠）を ch1-3 に加算 ──
            pitch += _csCradlePitch;
            roll  += _csCradleRoll;
            yaw   += _csCradleYaw;

            // ── CutScene.txt: VIB（微振動）を Damage_Pitch/Roll/Yaw に加算 ──
            if (_csDmgValue != 0f)
            {
                dmgP += _csDmgValue * MULT_DAMAGE_PITCH;
                dmgR += _csDmgValue * MULT_DAMAGE_ROLL;
                dmgY += _csDmgValue * MULT_DAMAGE_YAW;
            }

            float landP = Mathf.Clamp(_landP.Value, -1f, 1f) * MULT_LANDING_PITCH;
            float landR = Mathf.Clamp(_landR.Value, -1f, 1f) * MULT_LANDING_ROLL;
            float landY = Mathf.Clamp(_landY.Value, -1f, 1f) * MULT_LANDING_YAW;

            float escP = Mathf.Clamp(_escP.Value, -1f, 1f) * MULT_ESCAPE_PITCH;
            float escR = Mathf.Clamp(_escR.Value, -1f, 1f) * MULT_ESCAPE_ROLL;
            float escY = Mathf.Clamp(_escY.Value, -1f, 1f) * MULT_ESCAPE_YAW;

            // Foot_Vib (ch14-15): Walk + Landing 統合
            float footAmp = 0f, footHz = 0f;
            if (_vibWalkTimer > 0f) { footAmp = Mathf.Max(footAmp, FOOT_WALK_AMP); footHz = Mathf.Max(footHz, FOOT_WALK_HZ); }
            if (_vibLandTimer > 0f) { footAmp = Mathf.Max(footAmp, FOOT_LAND_AMP); footHz = Mathf.Max(footHz, FOOT_LAND_HZ); }
            // カットシーン搬送エフェクト（ジョルト・連続微振動・ガタッを1チャンネルで制御）
            if (_cutsceneVibAmp > 0f) { footAmp = Mathf.Max(footAmp, _cutsceneVibAmp); footHz = Mathf.Max(footHz, _cutsceneVibHz); }
            // 帰還カットシーン微振動
            if (_returnVibAmp > 0f) { footAmp = Mathf.Max(footAmp, _returnVibAmp); footHz = Mathf.Max(footHz, _returnVibHz); }

            // Turning_Vib (ch16-17)
            float turningAmp = _turnVibValue * TURNING_VIB_AMP_SCALE;
            float turningHz = _turnVibValue > 0f ? TURNING_VIB_HZ : 0f;

            // Damage_Vib (ch18-19)
            float damageAmp = _vibDmgTimer > 0f ? DAMAGE_VIB_AMP : 0f;
            float damageHz = _vibDmgTimer > 0f ? DAMAGE_VIB_HZ : 0f;

            // Jet_Vib (ch20-21)
            float jetAmp = 0f, jetHz = 0f;
            if (_jetEffectActive) { jetAmp = JET_VIB_AMP; jetHz = JET_VIB_HZ; }
            if (_vibJetTimer > 0f) { jetAmp = Mathf.Max(jetAmp, JET_VIB_AMP); jetHz = Mathf.Max(jetHz, JET_VIB_HZ); }

            // Weapon_Vib (ch22-23): 連続振動を合成
            float weaponAmp = _weaponVibAmp;
            float weaponHz = _weaponVibHz;
            // Laser vib（減衰なし一定振動）を合成
            if (_laserVibTimer > 0f)
            {
                weaponAmp = Mathf.Max(weaponAmp, _laserVibAmp);
                weaponHz  = Mathf.Max(weaponHz,  _laserVibHz);
            }
            if (_railCharging)
            {
                float progress = Mathf.Clamp01(_railChargeTimer / RAIL_CHARGE_TIME);
                // SmoothStepはt=0での傾きがゼロになる設計のため、
                // チャージ序盤1秒ほどが完全に無変化になる問題があった。
                // Powのみに変更することで t=0 から滑らかに変化し始める。
                // RAIL_CURVE=0.5: 序盤で素早く立ち上がり、後半なだらか
                // RAIL_CURVE=1.0: 一定ペースで線形増加
                // RAIL_CURVE=1.5: 序盤は緩やか、後半に加速（デフォルト）
                // RAIL_CURVE=2.0: さらに後半集中
                // RAIL_CURVE=3.0: チャージ完了直前まで緩やか、最後に急上昇
                float easedProgress = (progress > 0f) ? Mathf.Pow(progress, RAIL_CURVE) : 0f;
                float railAmp = Mathf.Lerp(RAIL_START_AMP, RAIL_END_AMP, easedProgress);
                float railHz  = Mathf.Lerp(RAIL_START_HZ,  RAIL_END_HZ,  easedProgress);
                weaponAmp = Mathf.Max(weaponAmp, railAmp);
                weaponHz = Mathf.Max(weaponHz, railHz);
            }
            if (_drillActive)
            {
                weaponAmp = Mathf.Max(weaponAmp, DRILL_AMP);
                weaponHz = Mathf.Max(weaponHz, DRILL_HZ);
            }

            // ── CutScene POST_DISABLE: Weapon/Damage 以外を全無効（無重力ステージ等）──
            if (_csPostDisable)
            {
                pitch = _csCradlePitch; roll = _csCradleRoll;
                // yaw はCRADLEに関係なく実際のメカの向きを維持
                walk = 0f; turning = 0f;
                landP = 0f; landR = 0f; landY = 0f;
                escP  = 0f; escR  = 0f; escY  = 0f;
                footAmp = 0f; footHz = 0f;
                turningAmp = 0f; turningHz = 0f;
                damageAmp  = 0f; damageHz  = 0f;
                jetAmp = 0f; jetHz = 0f;
                // weaponAmp/weaponHz, dmgP/R/Y はそのまま
            }

            // ── MechRootなし（メニュー等）は全チャンネルをゼロ出力 ──
            // ══════════════════════════════════════════════════════════
            // 出力有効判定: 以下のいずれかのみ全チャンネルを送信
            //   ① 出撃カットシーン中     (_launchActive)
            //   ② 帰還カットシーン中     (_returnActive)
            //   ③ CutScene.txt アクティブ(_csTimer >= 0)
            //   ④ ストーリーミッション中ゲームプレイ
            //      (_storyMissionActive && _mechRoot!=null && !_gamePaused && !_waitingForCutscene)
            // ══════════════════════════════════════════════════════════
            bool outputActive = (_launchActive && _storyMissionActive && !_playerPaused)
                             || (_returnActive && _storyMissionActive && !_playerPaused)
                             || (_launchGraceTimer > 0f && _storyMissionActive && !_playerPaused)
                             || (_returnGraceTimer > 0f && _storyMissionActive && !_playerPaused)
                             || (_csTimer >= 0f && !_gamePaused)
                             || (_storyMissionActive && _mechRoot != null && !_gamePaused && !_waitingForCutscene);

            if (!outputActive)
            {
                // Escape振動（ch11-13）は gamePaused / outputActive に関わらず常に出力。
                // 理由: ミッション開始直後のBlastShieldOpenはMechRoot検出前に発火するため、
                //       MechRoot検出で_launchActive=falseになった後もEscape振動を途切れさせない。
                float escPout = Mathf.Clamp(_escP.Value, -1f, 1f) * MULT_ESCAPE_PITCH;
                float escRout = Mathf.Clamp(_escR.Value, -1f, 1f) * MULT_ESCAPE_ROLL;
                float escYout = Mathf.Clamp(_escY.Value, -1f, 1f) * MULT_ESCAPE_YAW;
                string zeroMsg;
                if (escPout == 0f && escRout == 0f && escYout == 0f)
                {
                    // Escape振動もなければ完全ゼロ（ch0=Yawのみ復帰値）
                    zeroMsg = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0:F2},0.000,0.000,0.000,0.000," +
                        "0.000,0.000,0.000," +
                        "0.000,0.000,0.000," +
                        "0.000,0.000,0.000," +
                        "0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000",
                        _smoothYaw);
                }
                else
                {
                    // Escape振動のみ出力（Walk/Land/Damage/Jet/Weapon等は全ゼロ）
                    zeroMsg = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0:F2},0.000,0.000,0.000,0.000," +
                        "0.000,0.000,0.000," +
                        "0.000,0.000,0.000," +
                        "{1:F3},{2:F3},{3:F3}," +
                        "0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000",
                        _smoothYaw, escPout, escRout, escYout);
                }
                byte[] zeroData = Encoding.ASCII.GetBytes(zeroMsg);
                try { Udp?.Send(zeroData, zeroData.Length, PLUGIN_IP, PLUGIN_PORT); } catch { }
                return;
            }

            string msg = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2},{1:F3},{2:F3},{3:F3},{4:F3}," +
                "{5:F3},{6:F3},{7:F3}," +
                "{8:F3},{9:F3},{10:F3}," +
                "{11:F3},{12:F3},{13:F3}," +
                "{14:F3},{15:F3},{16:F3},{17:F3},{18:F3},{19:F3},{20:F3},{21:F3},{22:F3},{23:F3}",
                yaw, pitch, roll, walk, turning,
                dmgP, dmgR, dmgY,
                landP, landR, landY,
                escP, escR, escY,
                footAmp, footHz, turningAmp, turningHz, damageAmp, damageHz, jetAmp, jetHz, weaponAmp, weaponHz);

            byte[] data = Encoding.ASCII.GetBytes(msg);
            try { Udp?.Send(data, data.Length, PLUGIN_IP, PLUGIN_PORT); } catch { }
        }

        // ═══════════════ ↓↓↓ ここから追加 ↓↓↓ ═══════════════
        // ════════════════════════════════════════════════════════════════
        // 字幕送信
        // ════════════════════════════════════════════════════════════════
        internal static void SendSubtitle(string type, string id, string text = "")
        {
            // フォーマット: TYPE|ID|TEXT
            // 例: "SOUND|Briefing1_Shahin_001|" または "TTS||Greetings Pilot"
            string msg = $"{type}|{id}|{text}";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            try { SubtitleUdp?.Send(data, data.Length, PLUGIN_IP, SUBTITLE_PORT); }
            catch { }
            Log?.Msg($"[Subtitle] {msg}");
        }
        // ═══════════════ ↑↑↑ ここまで追加 ↑↑↑ ═══════════════

    }

    // ════════════════════════════════════════════════════════════════
    // VoxSound.Play パッチ
    // ════════════════════════════════════════════════════════════════
    [HarmonyPatch]
    public class VoxSound_Play_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var results = new List<MethodBase>();
            try
            {
                var asm = Assembly.Load("Assembly-CSharp");
                var type = asm?.GetType("VoxSound") ?? asm?.GetType("Il2CppVoxClient.VoxSound");
                if (type == null) { VoxTelemetryMod.Log?.Msg("[VoxSound] type not found"); return results; }
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                foreach (var m in type.GetMethods(flags))
                {
                    if (m.Name != "Play") continue;
                    var p = m.GetParameters();
                    if (p.Length >= 1 && p[0].ParameterType == typeof(string))
                    {
                        results.Add(m);
                        VoxTelemetryMod.Log?.Msg($"[VoxSound] Patching Play({string.Join(",", System.Array.ConvertAll(p, x => x.ParameterType.Name))})");
                    }
                }
                if (results.Count == 0) VoxTelemetryMod.Log?.Msg("[VoxSound] Play not found");
                else VoxTelemetryMod.Log?.Msg($"[VoxSound] {results.Count} overload(s) patched OK");
            }
            catch (System.Exception e) { VoxTelemetryMod.Log?.Msg($"[VoxSound] ERR: {e.Message}"); }
            return results;
        }

        // WeaponVibTableはVoxTelemetryMod.WeaponVibTableを参照

        [HarmonyPostfix]
        static void Postfix(object __instance, string __0)
        {
            if (string.IsNullOrEmpty(__0)) return;

            // ═══════════════ ↓↓↓ ここから追加 ↓↓↓ ═══════════════
            // 字幕送信（全サウンド名を送信）
            VoxTelemetryMod.SendSubtitle("SOUND", __0);
            // ═══════════════ ↑↑↑ ここまで追加 ↑↑↑ ═══════════════


            // ════════════════════════════════════════════════════════
            // 連続振動：レールガンチャージ（距離チェック付き）
            // ════════════════════════════════════════════════════════
            if (__0 == "Railgun" || __0 == "RailgunReady" || __0 == "RailgunStop" || __0 == "RailgunCancel" || __0 == "RailgunMuzzle")
            {
                try
                {
                    var inst = VoxTelemetryMod.Instance;
                    if (inst != null && inst._mechRoot != null)
                    {
                        var comp = __instance as UnityEngine.Component;
                        if (comp != null)
                        {
                            Vector3 soundPos = comp.transform.position;
                            Vector3 mechPos = inst._cameraEyelevel != null
                                ? inst._cameraEyelevel.position
                                : inst._mechRoot.position;
                            float dist = Vector3.Distance(soundPos, mechPos);
                            
                            // 自分の武器のみ処理（距離30m以内）
                            if (dist < VoxTelemetryMod.WEAPON_DIST_SELF)
                            {
                                if (__0 == "Railgun" && !inst._railCharging)
                                {
                                    inst._railCharging = true;
                                    inst._railChargeTimer = 0f;
                                }
                                else if (__0 == "RailgunReady")
                                {
                                    inst._railChargeTimer = VoxTelemetryMod.RAIL_CHARGE_TIME;
                                }
                                else if (__0 == "RailgunStop" || __0 == "RailgunCancel")
                                {
                                    inst._railCharging = false;
                                    inst._railChargeTimer = 0f;
                                }
                                else if (__0 == "RailgunMuzzle")
                                {
                                    // チャージ終了のみ（発射振動はWeapon.Fire()で処理）
                                    inst._railCharging = false;
                                    inst._railChargeTimer = 0f;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // ════════════════════════════════════════════════════════
            // 連続振動：ドリル（距離チェック付き）
            // ════════════════════════════════════════════════════════
            if (__0 == "DrillWeapon" || __0 == "DrillWeapon_Stop" || __0 == "DrillWeaponImpact")
            {
                try
                {
                    var inst = VoxTelemetryMod.Instance;
                    if (inst != null && inst._mechRoot != null)
                    {
                        var comp = __instance as UnityEngine.Component;
                        if (comp != null)
                        {
                            Vector3 soundPos = comp.transform.position;
                            Vector3 mechPos = inst._cameraEyelevel != null
                                ? inst._cameraEyelevel.position
                                : inst._mechRoot.position;
                            float dist = Vector3.Distance(soundPos, mechPos);
                            
                            if (dist < VoxTelemetryMod.WEAPON_DIST_SELF)
                            {
                                if (__0 == "DrillWeapon")
                                {
                                    inst._drillActive = true;
                                }
                                else if (__0 == "DrillWeapon_Stop")
                                {
                                    inst._drillActive = false;
                                }
                                else if (__0 == "DrillWeaponImpact")
                                {
                                    if (VoxTelemetryMod.WeaponVibTable.TryGetValue("DrillWeaponImpact", out var drillData))
                                    {
                                        VoxTelemetryMod.TriggerWeaponVib(drillData.amp, drillData.hz, drillData.dur);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // ════════════════════════════════════════════════════════
            // レーザー振動（照射中ずっと一定。タイムアウトリセット方式）
            // LargeLaserMuzzle / SmallLaserMuzzle は isMuzzle より先に処理し
            // 専用ステートに転送して return する。
            // ════════════════════════════════════════════════════════
            if (__0 == "LargeLaserMuzzle" || __0 == "SmallLaserMuzzle")
            {
                try
                {
                    var inst = VoxTelemetryMod.Instance;
                    if (inst != null && inst._mechRoot != null)
                    {
                        var comp = __instance as UnityEngine.Component;
                        if (comp != null)
                        {
                            Vector3 soundPos = comp.transform.position;
                            Vector3 mechPos  = inst._cameraEyelevel != null
                                ? inst._cameraEyelevel.position
                                : inst._mechRoot.position;
                            float dist = Vector3.Distance(soundPos, mechPos);
                            if (dist < VoxTelemetryMod.WEAPON_DIST_SELF)
                            {
                                float amp     = __0 == "LargeLaserMuzzle" ? 20f : 15f;
                                float hz      = __0 == "LargeLaserMuzzle" ? 30f : 35f;
                                float timeout = VoxTelemetryMod.LASER_VIB_TIMEOUT;
                                // 外部ファイル設定があれば上書き
                                if (VoxTelemetryMod.WeaponVibTable.TryGetValue(__0, out var ld))
                                {
                                    amp = ld.amp; hz = ld.hz; timeout = ld.dur;
                                }
                                VoxTelemetryMod.FlagLaserVibAmp     = amp;
                                VoxTelemetryMod.FlagLaserVibHz      = hz;
                                VoxTelemetryMod.FlagLaserVibTimeout = timeout;
                                VoxTelemetryMod.FlagLaserVib        = true;
                                VoxTelemetryMod.Log?.Msg($"[Laser] {__0} → LaserVib amp={amp} hz={hz} timeout={timeout}s");
                            }
                        }
                    }
                }
                catch { }
                return;  // isMuzzle ブロックへの落下を防ぐ
            }

            // ════════════════════════════════════════════════════════
            // 武器発射判定（距離ベース）
            // ════════════════════════════════════════════════════════
            bool isMuzzle = __0.Contains("Muzzle") || __0.Contains("_Fire") || __0.Contains("Launch");

            if (isMuzzle)
            {
                try
                {
                    var inst = VoxTelemetryMod.Instance;
                    if (inst != null && inst._mechRoot != null && inst._storyMissionActive)
                    {
                        var comp = __instance as UnityEngine.Component;
                        if (comp != null)
                        {
                            Vector3 soundPos = comp.transform.position;
                            Vector3 mechPos = inst._cameraEyelevel != null
                                ? inst._cameraEyelevel.position
                                : inst._mechRoot.position;
                            float dist = Vector3.Distance(soundPos, mechPos);

                            if (dist < VoxTelemetryMod.WEAPON_DIST_SELF)
                            {
                                // VoxSound.Play経由の武器振動（Laser, Drill等）
                                if (VoxTelemetryMod.WeaponVibTable.TryGetValue(__0, out var weaponData))
                                {
                                    VoxTelemetryMod.TriggerWeaponVib(weaponData.amp, weaponData.hz, weaponData.dur);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // ════════════════════════════════════════════════════════
            // 既存処理
            // ════════════════════════════════════════════════════════
            // ════════════════════════════════════════════════════════
            // InternalEngine: 格納庫カットシーン搬送開始
            // MechRoot未検出時のみ発火 → レール搬送エフェクト開始
            // ════════════════════════════════════════════════════════
            // ════════════════════════════════════════════════════════
            // 出撃カットシーン: 各サウンドで直接エフェクト発火
            // ════════════════════════════════════════════════════════
            // ── 【調査用】カットシーン中の全サウンドをログ出力 ──
            {
                var instLog = VoxTelemetryMod.Instance;
                if (instLog != null && instLog._csTimer >= 0f)
                    VoxTelemetryMod.Log?.Msg($"[CsSound] +{instLog._csTimer:F2}s  {__0}");
            }

            // ── LeftMonitor/RightMonitor Open/Close: BlipsYaw変化量をゼロにして完全スルー ──
            if (__0 == "LeftMonitorOpen"  || __0 == "LeftMonitorClose" ||
                __0 == "RightMonitorOpen" || __0 == "RightMonitorClose")
            {
                // モニター開閉アニメ中はblips追跡を一時停止（アニメによる偽回転をフィルタ）
                // Scout等の高速ロボはモニター開閉で400°/s超のblips回転が発生するため必須
                var instM = VoxTelemetryMod.Instance;
                if (instM != null && instM._blipsYawInitialized)
                {
                    instM._eulerPaused    = true;
                    instM._eulerPauseTimer = 2.0f;  // 2秒後に自動解除（アニメ完了後）
                    VoxTelemetryMod.Log?.Msg($"[Yaw] {__0} → blips追跡一時停止(2秒)");
                }
            }

            // ── ポーズメニュー開閉検出 ──
            if (__0 == "MainMonitorOpen")
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst != null && inst._mechRoot != null)
                {
                    // ★ _gamePaused=false（ゲームプレイ中）にポーズした場合のみ_playerPaused=true
                    // 起動時の一時停止（MechRoot検出→gamePaused=trueの直後）は_playerPauseしない
                    if (!inst._gamePaused) inst._playerPaused = true;
                    inst._gamePaused = true;
                    VoxTelemetryMod.Log?.Msg($"[Pause] MainMonitorOpen → 処理一時停止 (playerPaused:{inst._playerPaused})");
                }
                // _mechRoot=nullかつ_returnActive=trueはメニュー遷移による強制終了
                if (inst != null && inst._mechRoot == null && inst._returnActive)
                {
                    inst._returnActive = false; inst._returnVibActive = false;
                    inst._returnLiftDone = false; inst._returnLiftTimer = 0f;
                    inst._returnLiftStopTimer = 0f;
                    inst.CsVibOff(false);
                    VoxTelemetryMod.Log?.Msg("[Return] MainMonitorOpen(_mechRoot=null) → 帰還強制終了");
                }
            }
            if (__0 == "MainMonitorClose")
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst != null && inst._mechRoot != null && !inst._waitingForCutscene)
                {
                    inst._gamePaused = false;
                    inst._playerPaused = false;
                    // ★ Yawスナップ防止: 操作可能状態への切り替わり時にaccumulatedYawを同期
                    // blips追跡中は_smoothYawから逆算してyaw出力を連続にする
                    if (inst._blipsYawInitialized && VoxTelemetryMod.MULT_YAW != 0f)
                        inst._accumulatedYaw = inst._smoothYaw / VoxTelemetryMod.MULT_YAW;
                    VoxTelemetryMod.Log?.Msg("[Pause] MainMonitorClose → 処理再開");
                }
            }

            // ── CutScene.txt ステージ識別サウンド ──
            if (VoxTelemetryMod.StageSoundTable.TryGetValue(__0, out var csStage))
            {
                var instSt = VoxTelemetryMod.Instance;
                if (instSt != null && instSt._storyMissionActive)
                {
                    instSt._stageConfirmedCs = csStage;
                    VoxTelemetryMod.Log?.Msg($"[CutScene] ステージ確認: {csStage.Name} (sound: {__0})");
                }
            }

            // ── CutScene.txt トリガー（ステージ確認済みのシーンのみ）──
            if (VoxTelemetryMod.CutSceneTable.TryGetValue(__0, out var csData))
            {
                var instCs = VoxTelemetryMod.Instance;
                // StageSound未設定 or 確認済みの場合のみ起動
                bool stageOk = string.IsNullOrEmpty(csData.StageSound)
                               || instCs?._stageConfirmedCs == csData;
                if (instCs != null && instCs._storyMissionActive && instCs._csTimer < 0f && stageOk)
                {
                    instCs._activeCsScene       = csData;
                    instCs._csTimer             = 0f;
                    instCs._csDmgValue          = 0f;
                    instCs._csCradlePitch        = instCs._csCradleRoll = instCs._csCradleYaw = 0f;
                    instCs._gamePaused           = false;
                    instCs._waitingForCutscene   = false;
                    instCs._csPostDisable        = false;
                    // 出撃カットシーンが残留している場合は強制終了
                    instCs._launchActive     = false;
                    instCs._launchVibActive  = false;
                    instCs._cutsceneVibAmp   = 0f;
                    instCs._cutsceneVibHz    = 0f;
                    instCs._launchEndDelay   = 0f;
                    instCs._launchLiftDelay  = 0f;
                    instCs._launchStopDelay  = 0f;
                    // BlipsYaw未初期化の場合は即時強制完了（安定化待機を飛ばす）
                    if (!instCs._blipsYawInitialized && instCs._blipsTransform != null)
                    {
                        float rawYaw = instCs._blipsTransform.eulerAngles.y;
                        instCs._blipsYawOffset      = rawYaw;
                        // ★ 0f→逆算: _smoothYawの現在値を維持することでスナップを防止
                        instCs._accumulatedYaw      = (VoxTelemetryMod.MULT_YAW != 0f) ? (instCs._smoothYaw / VoxTelemetryMod.MULT_YAW) : 0f;
                        instCs._prevBlipsYaw        = rawYaw;
                        instCs._prevBlipsYawPhysics = rawYaw;
                        instCs._blipsYawInitialized = true;
                        instCs._blipsStabilizing    = false;
                        VoxTelemetryMod.Log?.Msg($"[BlipsYaw] カットシーン開始→強制初期化: {rawYaw:F1}°（開始Yaw:{instCs._smoothYaw:F1}°）");
                    }
                    VoxTelemetryMod.Log?.Msg($"[CutScene] {csData.Name} 開始 (trigger: {__0}, dur: {csData.Duration}s)");
                    VoxTelemetryMod.Log?.Msg($"[CutScene] CRADLE: pAmp={csData.CradlePitchAmp} rAmp={csData.CradleRollAmp} yAmp={csData.CradleYawAmp} period={csData.CradlePeriod}s (amp={csData.CradleAmp})");
                    if (csData.BlastOnStart)
                    {
                        // Gas and Go ステージ限定: ブラストドアオープンと同一の振動パターンを発火
                        // BlastShieldOpen と同じ: TriggerEscape(ESC_AMP_SHIELD) + FlagShieldDur=ESC_DUR_SHIELD_OPEN
                        VoxTelemetryMod.FlagShieldDur = VoxTelemetryMod.ESC_DUR_SHIELD_OPEN;  // 2.3秒
                        VoxTelemetryMod._shieldEscapeCooldown = VoxTelemetryMod.ESC_DUR_SHIELD;
                        VoxTelemetryMod.TriggerEscape(VoxTelemetryMod.ESC_AMP_SHIELD, isEject: false);
                        VoxTelemetryMod.Log?.Msg($"[CutScene] {csData.Name} → BlastShieldOpen振動発火");
                    }
                }
            }

            if (__0 == "InternalEngine")
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst != null && inst._mechRoot == null && !inst._launchActive)
                {
                    inst._launchActive    = false;
                    inst._launchVibActive = false;
                    inst._cutsceneVibAmp  = 0f; inst._cutsceneVibHz = 0f;
                    inst._launchStopDelay = inst._launchLiftDelay = inst._launchEndDelay = 0f;
                    inst._storyMissionActive = true;
                    inst._gamePaused = true;
                    inst._waitingForCutscene = false;  // 前セッションの残存クリア
                    VoxTelemetryMod.Log?.Msg("[Launch] InternalEngine → ストーリーミッション開始・出撃カットシーン");
                    inst._launchActive      = true;
                    inst._launchLiftStarted = false;
                }
                return;
            }

            if (__0 == "Play_Launch_Belt_01")
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst != null && inst._launchActive)
                {
                    inst.CsVibOn(true);
                    inst.CsShake(VoxTelemetryMod.CUTSCENE_QUAKE_DUR);
                    VoxTelemetryMod.Log?.Msg("[Launch] Belt_01 → VibON + Belt shake");
                }
                if (inst != null && inst._returnActive)
                {
                    inst.CsVibOn(false);
                    inst.CsShake(VoxTelemetryMod.CUTSCENE_QUAKE_DUR);
                    VoxTelemetryMod.Log?.Msg("[Return] Belt_01 → VibON + Belt shake");
                }
                return;
            }

            if (__0 == "Play_LaunchDoor_LargeHorz_Open")
            {
                var inst = VoxTelemetryMod.Instance;
                // 出撃: Lift前の初回のみ。Stop shakeとCsVibOffを同時発火し5秒タイマー開始
                // CsVibOff先行でガタッパルスによるShake上書きを防止
                if (inst != null && inst._launchActive && !inst._launchLiftStarted
                    && inst._launchStopDelay <= 0f && inst._launchResumeTimer <= 0f)
                {
                    inst._launchStopDelay = 0.8f;  // 0.8秒後にCsVibOff + Stop shake（停止期間と同期）
                    VoxTelemetryMod.Log?.Msg("[Launch] LargeHorz_Open → 0.8s後VibOFF + Stop shake");
                }
                // 帰還: Lift前のみ → 即VibOFF + 0.5秒後にStop shake
                if (inst != null && inst._returnActive && !inst._returnLiftDone)
                {
                    inst.CsVibOff(false);                   // ガタッパルス即停止（Shake上書き防止）
                    inst._returnStopDelay = 2.0f;           // 2.0秒後にStop shake発火
                    VoxTelemetryMod.Log?.Msg("[Return] LargeHorz_Open → VibOFF + 2.0s後Stop shake");
                }
                return;
            }

            if (__0 == "Play_Launch_Lift")
            {
                var inst = VoxTelemetryMod.Instance;
                // 出撃: VibON + 0.3秒後にLift shake
                if (inst != null && inst._launchActive)
                {
                    inst._launchStopDelay   = 0f;  // 停止遅延キャンセル
                    inst._launchLiftStarted = true;  // 以降LargeHorz_Openを無視
                    // Resume はLargeHorz_Openからの4秒タイマー（_launchResumeTimer）が管理
                    // Liftサウンドはリフト開始の検知のみ。CsVibOn/Shakeはタイマー側で行う
                    VoxTelemetryMod.Log?.Msg("[Launch] Lift検出（Resume はタイマー管理）");
                }
                // 帰還: _returnLiftDone=true + VibON + 0.3秒後にLift shake
                if (inst != null && inst._returnActive)
                {
                    inst._returnLiftDone      = true;
                    inst._returnLiftTimer     = 0f;
                    inst._returnLiftStopTimer = 23f;
                    inst._returnTimeout       = 0f;  // Lift検出でタイムアウトキャンセル
                    inst.CsVibOn(false);
                    inst._returnLiftDelay = 0.3f;
                    VoxTelemetryMod.Log?.Msg("[Return] Lift → VibON + 0.3s後Lift shake");
                }
                return;
            }

            if (__0 == "Play_LaunchDoor_LargeHorz_Close")
            {
                var inst = VoxTelemetryMod.Instance;
                // 帰還: Lift後のLargeHorz_Closeはタイマー方式に移行したため処理なし
                // (VibOFFは_returnLiftStopTimerが担当)
                if (inst != null && inst._returnActive && inst._returnLiftDone)
                {
                    VoxTelemetryMod.Log?.Msg($"[Return] LargeHorz_Close → スキップ（タイマー方式）");
                }
                return;
            }

            if (__0 == "Play_Launch_Detach")
            {
                var inst = VoxTelemetryMod.Instance;
                // 出撃: 0.4秒後にVibOFF + End shake + 終了
                if (inst != null && inst._launchActive)
                {
                    inst._launchEndDelay = 0.4f;
                    VoxTelemetryMod.Log?.Msg("[Launch] Detach → 0.4s後にEnd shake");
                }
                // 帰還: 終了処理（Stop shakeはStop_Belt_01で発火済みのため省略）
                if (inst != null && inst._returnActive)
                {
                    inst.CsVibOff(false);
                    inst._returnGraceTimer = 0f;
                    inst._returnActive   = false;
                    inst._returnLiftDone = false;
                    inst._returnCooldown = 15f;
                    inst._gamePaused     = true;  // Scan resetまでの出力を停止
                    VoxTelemetryMod.Log?.Msg("[Return] Detach → 終了（gamePaused=true）");
                }
            }

            if (__0 == "Stop_Launch_Belt_01")
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst != null && inst._returnActive)
                {
                    inst.CsVibOff(false);
                    inst.CsShakeUp(VoxTelemetryMod.CUTSCENE_QUAKE_DUR);  // _escP使用：Detach後も出力継続
                    VoxTelemetryMod.Log?.Msg("[Return] Stop_Belt_01 → VibOFF + Stop shake");
                }
            }

            if (__0 == "BlastShieldOpen")
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst != null && (inst._respawnFrozen || inst._respawnFreezeArmed))
                {
                    VoxTelemetryMod.Log?.Msg("[Esc] BlastShieldOpen → RespawnResume");
                    VoxTelemetryMod.FlagRespawnResume = true;
                    return;
                }
                if (VoxTelemetryMod._shieldEscapeCooldown > 0f) return;
                VoxTelemetryMod._shieldEscapeCooldown = VoxTelemetryMod.ESC_DUR_SHIELD;
                VoxTelemetryMod.FlagShieldDur = VoxTelemetryMod.ESC_DUR_SHIELD_OPEN;   // 2.3秒
                VoxTelemetryMod.Log?.Msg("[Esc] BlastShieldOpen → Escape(0.30)");
                VoxTelemetryMod.TriggerEscape(VoxTelemetryMod.ESC_AMP_SHIELD, isEject: false);
                return;
            }
            if (__0 == "BlastShieldClose")
            {
                if (VoxTelemetryMod._shieldEscapeCooldown > 0f) return;
                VoxTelemetryMod._shieldEscapeCooldown = VoxTelemetryMod.ESC_DUR_SHIELD;
                VoxTelemetryMod.FlagShieldDur = VoxTelemetryMod.ESC_DUR_SHIELD_CLOSE;
                VoxTelemetryMod.Log?.Msg("[Esc] BlastShieldClose → Escape(0.30)");
                VoxTelemetryMod.TriggerEscape(VoxTelemetryMod.ESC_AMP_SHIELD, isEject: false);
                // 帰還カットシーン開始: _mechRoot != null（ゲームプレイ中）かつクールダウン終了後
                var instBC = VoxTelemetryMod.Instance;
                if (instBC != null && instBC._mechRoot != null && !instBC._returnActive && instBC._returnCooldown <= 0f && instBC._storyMissionActive)
                {
                    // ★ 帰還カットシーン開始時に全エフェクトを停止
                    instBC._jetEffectActive = false;
                    instBC._vibJetTimer     = 0f;
                    instBC._vibWalkTimer    = 0f;
                    instBC._vibLandTimer    = 0f;
                    instBC._weaponVibAmp    = 0f;
                    instBC._turnVibValue    = 0f;
                    instBC._dmgPitch = instBC._dmgRoll = instBC._dmgYaw = 0f;
                    // ★ POST_DISABLEをクリア（Gas and Go後の帰還でfootAmp/Yawが0上書きされるのを防止）
                    instBC._csPostDisable = false;
                    instBC._csTimer       = -1f;
                    instBC._returnActive   = true;
                    instBC._returnTimeout  = 60f;  // 60秒以内にLiftが来なければ強制終了（本来はScan resetで終了）
                    instBC._returnMaxTimer = 0f;   // mechRoot消失タイマーをリセット
                    instBC._returnLiftDone = false;
                    instBC.CsVibOn(false);
                    VoxTelemetryMod.Log?.Msg("[Return] BlastShieldClose → 全エフェクト停止・帰還カットシーン開始");
                }
                return;
            }
            if (__0 == "AlertEject")
            {
                VoxTelemetryMod.Log?.Msg("[Esc] AlertEject → Escape(0.90, EulerPause)");
                VoxTelemetryMod.TriggerEscape(VoxTelemetryMod.ESC_AMP_EJECT, isEject: true);
                return;
            }
            // EjectJet: 脱出中の継続的なジェット音 → 揺れを継続
            // ★ 敵撃破時も敵パイロットの脱出で EjectJet が発火するため距離チェックを追加。
            //    自機コクピットは mechRoot から数m以内にあるので確実に通過する。
            if (__0 == "EjectJet")
            {
                try
                {
                    var inst = VoxTelemetryMod.Instance;
                    if (inst != null && inst._mechRoot != null)
                    {
                        var comp = __instance as UnityEngine.Component;
                        if (comp != null)
                        {
                            float dist = Vector3.Distance(comp.transform.position,
                                inst._cameraEyelevel != null
                                    ? inst._cameraEyelevel.position
                                    : inst._mechRoot.position);
                            if (dist >= VoxTelemetryMod.WEAPON_DIST_SELF)
                            {
                                VoxTelemetryMod.Log?.Msg($"[Esc] EjectJet 敵の脱出 dist={dist:F1}m → スキップ");
                                return;
                            }
                        }
                    }
                }
                catch { }
                VoxTelemetryMod.Log?.Msg("[Esc] EjectJet → Escape継続(0.70)");
                VoxTelemetryMod.TriggerEscape(0.70f, isEject: true);
                return;
            }

            // ════════════════════════════════════════════════════════
            // Damage_Vib — 自機のみ（距離チェック）
            // CockpitShake/Rattle/MechGetsHit は敵ロボットにも発火するため
            // WEAPON_DIST_SELF(30m)以内のサウンドソースのみ反応する。
            // ※[Diag] ログで距離を確認中。comp=null の場合はVoxSoundが
            //   UnityEngine.Component でないためフィルタ不能。
            // ════════════════════════════════════════════════════════
            if (__0 == "MechGetsHit" || __0 == "CockpitShake" || __0 == "CockpitRattle")
            {
                try
                {
                    var inst = VoxTelemetryMod.Instance;
                    if (inst != null && inst._mechRoot != null)
                    {
                        var comp = __instance as UnityEngine.Component;                        if (comp == null)
                        {
                            // comp=null → VoxSound は Component でない → 距離チェック不能
                            VoxTelemetryMod.Log?.Msg($"[Diag] {__0} comp=null → 距離チェック不能");
                            if      (__0 == "MechGetsHit")   { VoxTelemetryMod.Log?.Msg("[Hit] MechGetsHit → DmgVib 2.0s");   VoxTelemetryMod.TriggerDamageVib(2.0f); }
                            else if (__0 == "CockpitShake")  { VoxTelemetryMod.Log?.Msg("[Hit] CockpitShake → DmgVib 1.5s");  VoxTelemetryMod.TriggerDamageVib(1.5f); }
                            else if (__0 == "CockpitRattle") { VoxTelemetryMod.Log?.Msg("[Hit] CockpitRattle → DmgVib 0.8s"); VoxTelemetryMod.TriggerDamageVib(0.8f); }
                        }
                        else
                        {
                            Vector3 soundPos = comp.transform.position;
                            Vector3 mechPos  = inst._cameraEyelevel != null
                                ? inst._cameraEyelevel.position
                                : inst._mechRoot.position;
                            float dist = Vector3.Distance(soundPos, mechPos);
                            VoxTelemetryMod.Log?.Msg($"[Diag] {__0} dist={dist:F1}m soundPos={soundPos} mechPos={mechPos}");
                            if (dist < VoxTelemetryMod.WEAPON_DIST_SELF)
                            {
                                if      (__0 == "MechGetsHit")   { VoxTelemetryMod.Log?.Msg("[Hit] MechGetsHit → DmgVib 2.0s");   VoxTelemetryMod.TriggerDamageVib(2.0f); }
                                else if (__0 == "CockpitShake")  { VoxTelemetryMod.Log?.Msg("[Hit] CockpitShake → DmgVib 1.5s");  VoxTelemetryMod.TriggerDamageVib(1.5f); }
                                else if (__0 == "CockpitRattle") { VoxTelemetryMod.Log?.Msg("[Hit] CockpitRattle → DmgVib 0.8s"); VoxTelemetryMod.TriggerDamageVib(0.8f); }
                            }
                        }
                    }
                    else if (inst != null && inst._mechRoot == null)
                    {
                        // ★ MechRoot未検出 = 格納庫カットシーン中
                        // CockpitShake/Rattle → ワンショット Pitch シェイク
                        if (__0 == "CockpitShake" || __0 == "CockpitRattle")
                        {
                            VoxTelemetryMod.FlagCutsceneJolt = true;
                        }
                    }                }
                catch { }
            }

            // ════════════════════════════════════════════════════════
            // Walk_Vib — 自機のみ（距離チェック）
            // FootImpact は敵ロボットにも発火するため
            // WEAPON_DIST_SELF(30m)以内のサウンドソースのみ反応する。
            // ※[Diag] ログで距離を確認中。
            // ════════════════════════════════════════════════════════
            if (__0 == "FootImpact" || __0.Contains("Footstep") || __0.Contains("Step") || __0.Contains("foot"))
            {
                try
                {
                    var inst = VoxTelemetryMod.Instance;
                    if (inst != null && inst._mechRoot != null)
                    {
                        var comp = __instance as UnityEngine.Component;
                        if (comp == null)
                        {
                            // comp=null → 距離チェック不能のためスキップ（NPC足音の誤発動防止）
                            return;
                        }
                        Vector3 soundPos = comp.transform.position;
                        Vector3 mechPos  = inst._cameraEyelevel != null
                            ? inst._cameraEyelevel.position
                            : inst._mechRoot.position;
                        float dist = Vector3.Distance(soundPos, mechPos);
                        if (VoxTelemetryMod._diagFootLogCount < 5)
                        {
                            VoxTelemetryMod.Log?.Msg($"[Diag] {__0} dist={dist:F1}m soundPos={soundPos} mechPos={mechPos}");
                            VoxTelemetryMod._diagFootLogCount++;
                        }
                        if (dist < VoxTelemetryMod.WEAPON_DIST_SELF)
                        {
                            VoxTelemetryMod.TriggerWalkVib();
                        }
                    }
                }
                catch { }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // EntityInstance.UpdateJumpJet パッチ — Jet_Vib (ch20-21)
    // ════════════════════════════════════════════════════════════════
    [HarmonyPatch]
    public class UpdateJumpJet_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var results = new List<MethodBase>();
            try
            {
                var asm = typeof(Il2CppVoxClient.EntityInstance).Assembly;
                var type = asm.GetType("Il2CppVoxClient.EntityInstance");
                if (type == null) { VoxTelemetryMod.Log?.Msg("[JetPatch] type not found"); return results; }
                var m = type.GetMethod("UpdateJumpJet", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m != null) { results.Add(m); VoxTelemetryMod.Log?.Msg("[JetPatch] Patch OK"); }
                else { VoxTelemetryMod.Log?.Msg("[JetPatch] UpdateJumpJet not found"); }
            }
            catch (System.Exception e) { VoxTelemetryMod.Log?.Msg($"[JetPatch] ERR: {e.Message}"); }
            return results;
        }

        [HarmonyPostfix]
        static void Postfix(Il2CppVoxClient.EntityInstance __instance, bool __0)
        {
            try
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst == null || inst._mechRoot == null) return;

                float dist = Vector3.Distance(__instance.transform.position, inst._mechRoot.position);
                if (dist >= 5f) return;

                inst._jetEffectActive = __0;

                // ── typeId からロボットプロファイルを読み込む（初回・切替時のみ）──
                if (!_profileLoaded)
                {
                    _profileLoaded = true;
                    try
                    {
                        var flags2 = System.Reflection.BindingFlags.Public |
                                     System.Reflection.BindingFlags.NonPublic |
                                     System.Reflection.BindingFlags.Instance;
                        var pTypeId = __instance.GetType().GetProperty("typeId", flags2);
                        if (pTypeId?.GetValue(__instance) is int typeId)
                        {
                            VoxTelemetryMod.Log?.Msg($"[Profile] typeId={typeId} 検出 → プロファイル読み込み");
                            inst.LoadRobotProfile(typeId);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        VoxTelemetryMod.Log?.Msg($"[Profile] typeId取得エラー: {ex.Message}");
                    }
                }
            }
            catch { }
        }

        // Scan resetと連動してリセット（TryScan内の reset ログ行の直後で _profileLoaded=false）
        internal static bool _profileLoaded = false;
    }

    // ════════════════════════════════════════════════════════════════
    // 軽量JSONパーサー（System.Text.Json非依存・dotpath対応）
    //   "section.key" 形式で float 値を取得する最小実装。
    //   _note / _xxx キーはスキップ。値が存在しない場合は ref を変更しない。
    // ════════════════════════════════════════════════════════════════
    internal static class SimpleJson
    {
        // JSONテキストをパースして dotpath → float を返す辞書を返す
        public static SimpleJsonDoc Parse(string json) => new SimpleJsonDoc(json);
    }

    internal class SimpleJsonDoc
    {
        private readonly Dictionary<string, float> _values = new();

        public SimpleJsonDoc(string json)
        {
            // 現在のセクション名スタック
            var sectionStack = new System.Collections.Generic.Stack<string>();
            foreach (var rawLine in json.Split('\n'))
            {
                var line = rawLine.Trim().TrimEnd(',');
                if (line.StartsWith("//") || line.StartsWith("_")) continue;

                // セクション開始: "key": {
                if (line.EndsWith("{") && line.Contains('"'))
                {
                    var key = ExtractKey(line);
                    if (key != null && !key.StartsWith("_"))
                        sectionStack.Push(key);
                    continue;
                }
                // セクション終了
                if (line == "}" || line == "},")
                {
                    if (sectionStack.Count > 0) sectionStack.Pop();
                    continue;
                }
                // キー: 値
                if (line.Contains(':'))
                {
                    var key = ExtractKey(line);
                    if (key == null || key.StartsWith("_")) continue;
                    var valStr = ExtractValue(line);
                    if (valStr == null) continue;
                    if (!float.TryParse(valStr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float fval)) continue;

                    var fullKey = sectionStack.Count > 0
                        ? string.Join(".", System.Linq.Enumerable.Reverse(sectionStack)) + "." + key
                        : key;
                    _values[fullKey] = fval;
                }
            }
        }

        public void TryFloat(string dotPath, ref float target)
        {
            if (_values.TryGetValue(dotPath, out float v)) target = v;
        }

        private static string? ExtractKey(string line)
        {
            var i1 = line.IndexOf('"');
            if (i1 < 0) return null;
            var i2 = line.IndexOf('"', i1 + 1);
            if (i2 < 0) return null;
            return line.Substring(i1 + 1, i2 - i1 - 1);
        }

        private static string? ExtractValue(string line)
        {
            var ci = line.IndexOf(':');
            if (ci < 0) return null;
            var v = line.Substring(ci + 1).Trim().TrimEnd(',').Trim();
            // 文字列値・null・bool はスキップ（floatのみ対象）
            if (v.StartsWith("\"") || v == "null" || v == "true" || v == "false") return null;
            return v;
        }
    }

    // ═══════════════ ↓↓↓ ここから追加 ↓↓↓ ═══════════════
    // ════════════════════════════════════════════════════════════════
    // TextToSpeech.Speak パッチ（ロボット音声の字幕）
    // ════════════════════════════════════════════════════════════════
    [HarmonyPatch]
    public class TextToSpeech_Speak_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var results = new List<MethodBase>();
            try
            {
                var asm = Assembly.Load("Assembly-CSharp");
                var type = asm?.GetType("TextToSpeech") ?? asm?.GetType("Il2CppVoxClient.TextToSpeech");
                if (type == null) { VoxTelemetryMod.Log?.Msg("[TTS] type not found"); return results; }

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                foreach (var m in type.GetMethods(flags))
                {
                    if (m.Name != "Speak") continue;
                    var p = m.GetParameters();
                    if (p.Length >= 1 && p[0].ParameterType == typeof(string))
                    {
                        results.Add(m);
                        VoxTelemetryMod.Log?.Msg($"[TTS] Patching Speak({string.Join(",", System.Array.ConvertAll(p, x => x.ParameterType.Name))})");
                    }
                }
                if (results.Count == 0) VoxTelemetryMod.Log?.Msg("[TTS] Speak not found");
                else VoxTelemetryMod.Log?.Msg($"[TTS] {results.Count} overload(s) patched OK");
            }
            catch (System.Exception e) { VoxTelemetryMod.Log?.Msg($"[TTS] ERR: {e.Message}"); }
            return results;
        }

        [HarmonyPostfix]
        static void Postfix(string __0)
        {
            if (string.IsNullOrEmpty(__0)) return;
            VoxTelemetryMod.SendSubtitle("TTS", "", __0);
        }
    }
    // ═══════════════ ↑↑↑ ここまで追加 ↑↑↑ ═══════════════

    // ════════════════════════════════════════════════════════════════
    // Weapon.Fire() フック - 自分の武器発射を検出
    // ════════════════════════════════════════════════════════════════
    [HarmonyPatch]
    public class Weapon_Fire_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var results = new List<MethodBase>();
            try
            {
                var asm = Assembly.Load("Assembly-CSharp");
                
                // Il2CppVoxClient.Weapon を検索
                var type = asm?.GetType("Il2CppVoxClient.Weapon");
                if (type == null)
                {
                    // 別の名前で試す
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == "Weapon" && t.FullName.Contains("VoxClient"))
                        {
                            type = t;
                            break;
                        }
                    }
                }
                
                if (type == null)
                {
                    VoxTelemetryMod.Log?.Msg("[WeaponFire] Weapon type not found");
                    return results;
                }
                
                VoxTelemetryMod.Log?.Msg($"[WeaponFire] Found type: {type.FullName}");
                
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                foreach (var m in type.GetMethods(flags))
                {
                    if (m.Name == "Fire")
                    {
                        results.Add(m);
                        var paramTypes = string.Join(",", System.Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name));
                        VoxTelemetryMod.Log?.Msg($"[WeaponFire] Patching Fire({paramTypes})");
                    }
                }
                
                if (results.Count == 0)
                {
                    VoxTelemetryMod.Log?.Msg("[WeaponFire] Fire method not found");
                }
                else
                {
                    VoxTelemetryMod.Log?.Msg($"[WeaponFire] {results.Count} Fire method(s) patched OK");
                }
            }
            catch (System.Exception e)
            {
                VoxTelemetryMod.Log?.Msg($"[WeaponFire] ERR: {e.Message}");
            }
            return results;
        }

        // 武器種別の振動パラメータは VoxTelemetryMod.WeaponVibParams（外部ファイル読み込み）を使用。
        // ※ここにローカル辞書は不要（削除済み）。


        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            if (__instance == null) return;
            
            try
            {
                // リフレクションで武器名を取得
                var type = __instance.GetType();
                var nameProp = type.GetProperty("name");
                string weaponName = nameProp?.GetValue(__instance)?.ToString() ?? "unknown";
                
                // transformを取得
                var transformProp = type.GetProperty("transform");
                var weaponTransform = transformProp?.GetValue(__instance) as Transform;
                if (weaponTransform == null) return;
                
                // 自分の機体ルートを取得
                Transform mechRoot = VoxTelemetryMod.GetMechRoot();
                if (mechRoot == null) return;
                // ストーリーモードミッション中のみ発動（テストレンジ・対戦・チュートリアル等では無効）
                var instW = VoxTelemetryMod.Instance;
                if (instW == null || !instW._storyMissionActive) return;
                
                // 武器の親を辿って自分の機体かどうか判定
                Transform current = weaponTransform;
                bool isMyWeapon = false;
                int depth = 0;
                while (current != null && depth < 20)
                {
                    if (current == mechRoot)
                    {
                        isMyWeapon = true;
                        break;
                    }
                    current = current.parent;
                    depth++;
                }
                
                if (!isMyWeapon) return;
                
                // 武器名に基づいて振動パラメータを決定
                // ※最も長いキーにマッチさせる（cannon_flak_mine > cannon_flak）
                float amp = VoxTelemetryMod.DEFAULT_WEAPON_AMP;
                float hz = VoxTelemetryMod.DEFAULT_WEAPON_HZ;
                float dur = VoxTelemetryMod.DEFAULT_WEAPON_DUR;
                
                string bestMatch = null;
                int bestLen = 0;
                foreach (var kvp in VoxTelemetryMod.WeaponVibParams)
                {
                    if (weaponName.Contains(kvp.Key) && kvp.Key.Length > bestLen)
                    {
                        bestMatch = kvp.Key;
                        bestLen = kvp.Key.Length;
                    }
                }
                
                if (bestMatch != null && VoxTelemetryMod.WeaponVibParams.TryGetValue(bestMatch, out var param))
                {
                    amp = param.amp;
                    hz = param.hz;
                    dur = param.dur;
                }
                
                VoxTelemetryMod.TriggerWeaponVib(amp, hz, dur);
            }
            catch { }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 【調査用】Gas and Go カットシーン中のコクピット振動アニメーション特定
    // ※ csproj に UnityEngine.AnimationModule.dll の参照追加が必要
    // ════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(UnityEngine.Animator), "Play",
        new[] { typeof(string), typeof(int), typeof(float) })]
    public class Animator_Play_Diag
    {
        static void Prefix(UnityEngine.Animator __instance, string stateName)
        {
            try
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst == null || inst._csTimer < 0f) return;
                string obj    = __instance.gameObject.name;
                string parent = __instance.transform.parent?.name ?? "null";
                VoxTelemetryMod.Log?.Msg($"[AnimDiag] Play(\"{stateName}\") obj={obj} parent={parent} t={inst._csTimer:F2}s");
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.Animator), "SetTrigger",
        new[] { typeof(string) })]
    public class Animator_SetTrigger_Diag
    {
        static void Prefix(UnityEngine.Animator __instance, string name)
        {
            try
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst == null || inst._csTimer < 0f) return;
                string obj    = __instance.gameObject.name;
                string parent = __instance.transform.parent?.name ?? "null";
                VoxTelemetryMod.Log?.Msg($"[AnimDiag] SetTrigger(\"{name}\") obj={obj} parent={parent} t={inst._csTimer:F2}s");
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.Animator), "SetBool",
        new[] { typeof(string), typeof(bool) })]
    public class Animator_SetBool_Diag
    {
        static void Prefix(UnityEngine.Animator __instance, string name, bool value)
        {
            try
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst == null || inst._csTimer < 0f) return;
                string obj = __instance.gameObject.name;
                VoxTelemetryMod.Log?.Msg($"[AnimDiag] SetBool(\"{name}\"={value}) obj={obj} t={inst._csTimer:F2}s");
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.Animator), "SetFloat",
        new[] { typeof(string), typeof(float) })]
    public class Animator_SetFloat_Diag
    {
        static void Prefix(UnityEngine.Animator __instance, string name, float value)
        {
            try
            {
                var inst = VoxTelemetryMod.Instance;
                if (inst == null || inst._csTimer < 0f) return;
                string obj = __instance.gameObject.name;
                VoxTelemetryMod.Log?.Msg($"[AnimDiag] SetFloat(\"{name}\"={value:F3}) obj={obj} t={inst._csTimer:F2}s");
            }
            catch { }
        }
    }


}
