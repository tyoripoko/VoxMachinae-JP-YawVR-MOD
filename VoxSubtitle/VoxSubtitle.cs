// VoxSubtitle.cs  v3.0
// MelonLoader mod for Vox Machinae - Japanese Subtitle System
// 外部オーバーレイEXEを自動起動 + UDP送信
//
// v2.8 変更点:
//   - 2D/VR オーバーレイの自動切換えを実装
//     VoxClient.CameraRig の vrMode / GetCurrentVrMode() / vrModel を参照し、
//     VrMode.None なら通常版、それ以外なら VR 版を起動する。
//   - オーバーレイ起動を OnInitializeMelon から初回シーンロード後に移動
//     （OnInitializeMelon 時点では CameraRig が未初期化で必ず None になるため）
//   - VR 版を起動した場合、数秒後にプロセス生存を確認し、
//     OpenVR 初期化失敗で自滅していたら通常版へ自動フォールバック
//     （Oculus ネイティブモードなど SteamVR 非経由の環境を救済）
//   - MelonPreferences で手動上書き可: [VoxSubtitle] OverlayMode = Auto / VR / Desktop / Off
//
// フック対象:
//   1. TextToSpeech.Speak()     - TTS音声（BLUE/REDなどAI）
//   2. SubtitleGooey.Display()  - 全キャラクター字幕（Donny, Frank, Neberu, Chef, Hans等）
//
// 字幕オプションOFFでも動作（メソッド呼び出し自体をフックするため）
// v2.5: Il2Cpp環境対応パッチ - AppDomain.GetAssemblies()を使用

using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using UnityEngine;

[assembly: MelonInfo(typeof(VoxSubtitle.VoxSubtitleMod), "VoxSubtitle", "4.7.2", "tyoripoko")]
[assembly: MelonGame("SpaceBulletDynamicsCorporation", "VoxMachinae")]

namespace VoxSubtitle
{
    public class VoxSubtitleMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static VoxSubtitleMod Instance;

        private const string UDP_IP = "127.0.0.1";
        private const int UDP_PORT = 20044;
        private static UdpClient _udpClient;

        private Dictionary<string, SubtitleEntry> _translations = new Dictionary<string, SubtitleEntry>();
        // key → 有効期限 (Time.time)。期限内は同じテキストの再送を防ぐ。
        // 固定2秒クリアではなく各エントリの表示秒数ベースで管理する。
        private Dictionary<string, float> _processedTexts = new Dictionary<string, float>();
        private float _lastSendTime = 0f;

        // ──────────────────────────────────────────────────────────────
        // オーバーレイ自動切換え用
        // ──────────────────────────────────────────────────────────────
        private const float VR_PROBE_INTERVAL = 0.25f;      // VRモード判定のポーリング間隔(秒)
        private const float VR_FALLBACK_CHECK_DELAY = 25.0f; // VR版の生存確認までの待機(秒)

        private static MelonPreferences_Category _cfg;
        private static MelonPreferences_Entry<string> _cfgOverlayMode;
        private static MelonPreferences_Entry<float> _cfgProbeTimeout;

        private Process _overlayProcess;
        private bool _overlayLaunched;
        private bool _probing;
        private float _probeDeadline;
        private float _nextProbeTime;
        private bool _vrFallbackPending;
        private float _vrFallbackCheckAt;

        private static bool _vrModeResolved;
        private static MethodInfo _vrModeMethod;
        private static PropertyInfo _vrModeProp;
        private static FieldInfo _vrModeField;
        private static PropertyInfo _vrModelProp;
        private static FieldInfo _vrModelField;

        // 話者名の英語→日本語マッピング
        private static readonly Dictionary<string, string> _speakerNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "BLUE", "ブルー" },
            { "RED", "レッド" },
            { "Donny", "ドニー" },
            { "Frank", "フランク" },
            { "Neberu", "ネベル" },
            { "Hans", "ハンス" },
            { "Chef", "シェフ" },
            { "Diesel", "ディーゼル" },
            { "Shahin", "シャヒン" },
            { "Joe", "ジョー" },
            { "Reggie", "レジー" },
            { "Tom", "トム" },
            { "Jess", "ジェス" },
            { "Neil", "ニール" },
            { "Blake", "ブレイク" },
            { "Dana", "ダナ" },
            { "Todd", "トッド" },
            { "Radar Staff", "レーダー職員" },
            { "Refinery Staff", "精製所職員" },
            { "Security Commander", "セキュリティ司令官" },
            { "Frenchie", "フレンチー" },
            { "Jones", "ジョーンズ" },
            { "Ralph", "ラルフ" },
            { "Chloe", "クロエ" },
            { "Anita", "アニタ" },
            { "Carl", "カール" },
            { "Botlock", "ボットロック" },
            { "Bottson", "ボットソン" },
            { "Botty", "ボッティー" },
            { "Robod", "ロボッド" },
            { "Trevor", "トレバー" },
            { "Jacques", "ジャック" },
            { "Ethan", "イーサン" },
            { "Sadie", "セイディ" },
            { "Rig Crew", "リグ・クルー" },
            { "Stephan", "ステファン" },
            { "WoBot", "ウォーボット" },
            { "Robob", "ロボブ" },
            { "Botbot", "ボットボット" },
        };

        private struct SubtitleEntry
        {
            public string Japanese;
            public string Speaker;
            public float Duration;
        }

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Instance = this;

            LoggerInstance.Msg("████████████████████████████████████████");
            LoggerInstance.Msg("██  VoxSubtitle v4.7.2  ████████████████");
            LoggerInstance.Msg("██  全キャラクター字幕対応  ████████████");
            LoggerInstance.Msg("██  2D/VR オーバーレイ自動切換え  ██████");
            LoggerInstance.Msg("████████████████████████████████████████");

            SetupPreferences();

            _udpClient = new UdpClient();
            LoadTranslations();

            // オーバーレイ起動はここでは行わない。
            // CameraRig.vrMode がまだ初期化されていないため、必ず None と判定されてしまう。
            // 初回シーンロード後に BeginOverlaySelection() で判定する。

            try
            {
                var harmony = new HarmonyLib.Harmony("com.tyoripoko.voxsubtitle");
                harmony.PatchAll();
                LoggerInstance.Msg("  Harmony: all patches applied");
            }
            catch (Exception e)
            {
                LoggerInstance.Msg($"  Harmony ERROR: {e.Message}");
            }
        }

        private void SetupPreferences()
        {
            try
            {
                _cfg = MelonPreferences.CreateCategory("VoxSubtitle", "VoxSubtitle 字幕設定");

                _cfgOverlayMode = _cfg.CreateEntry(
                    "OverlayMode", "Auto",
                    description: "字幕オーバーレイの選択。Auto=VR/2Dを自動判定 / VR=VR版固定 / Desktop=通常版固定 / Off=起動しない");

                _cfgProbeTimeout = _cfg.CreateEntry(
                    "VrDetectTimeoutSeconds", 8.0f,
                    description: "VR判定の待機秒数。この秒数までにVRモードを検出できなければ通常版を起動します");
            }
            catch (Exception e)
            {
                Log?.Msg($"[Config] MelonPreferences 初期化エラー: {e.Message}");
            }
        }

        public override void OnDeinitializeMelon()
        {
            try { _udpClient?.Close(); } catch { }
            try
            {
                if (_overlayProcess != null && !_overlayProcess.HasExited)
                {
                    _overlayProcess.Kill();
                }
            }
            catch { }
        }

        public override void OnUpdate()
        {
            // ── オーバーレイ判定フェーズ ──────────────────────────
            if (_probing && !_overlayLaunched && Time.time >= _nextProbeTime)
            {
                _nextProbeTime = Time.time + VR_PROBE_INTERVAL;
                int vr = TryGetVrMode();

                if (vr > 0)
                {
                    _probing = false;
                    Log?.Msg($"[Overlay] VRモード検出 (VrMode={vr}) → VR版を起動します");
                    LaunchOverlay(true);
                }
                else if (Time.time >= _probeDeadline)
                {
                    _probing = false;
                    Log?.Msg(vr == 0
                        ? "[Overlay] VrMode=None（2Dデスクトップ）→ 通常版を起動します"
                        : "[Overlay] VRモードを判定できませんでした → 通常版を起動します");
                    LaunchOverlay(false);
                }
            }

            // ── VR版の生存確認（OpenVR初期化失敗時のフォールバック）──
            if (_vrFallbackPending && Time.time >= _vrFallbackCheckAt)
            {
                _vrFallbackPending = false;

                bool dead;
                try { dead = (_overlayProcess == null) || _overlayProcess.HasExited; }
                catch { dead = false; } // 判定不能なら生存とみなし二重起動を避ける

                if (dead)
                {
                    Log?.Msg("[Overlay] VR版が終了しています（OpenVR初期化失敗と判断）→ 通常版へ切替えます");
                    _overlayProcess = null;
                    _overlayLaunched = false;
                    LaunchOverlay(false, noVrFallback: true);
                }
            }

            // 期限切れエントリの定期削除（メモリ節約。シーン切り替え時にも Clear する）
            if (Time.time - _lastSendTime > 60f && _processedTexts.Count > 0)
            {
                _processedTexts.Clear();
            }
        }

        // シーンがアンロードされた時（リスタート、ミッション切り替え等）
        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            try
            {
                Log?.Msg($"[Scene] Unloaded: {sceneName} (index: {buildIndex})");
                _processedTexts.Clear();
                _lastSendTime = 0f;
            }
            catch (Exception ex)
            {
                Log?.Error($"[Scene] Unload error: {ex.Message}");
            }
        }

        // シーンがロードされた時
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            try
            {
                Log?.Msg($"[Scene] Loaded: {sceneName} (index: {buildIndex})");
                _processedTexts.Clear();
                _lastSendTime = 0f;

                // 初回シーンロードでオーバーレイの選択を開始
                if (!_overlayLaunched && !_probing)
                {
                    BeginOverlaySelection();
                }
            }
            catch (Exception ex)
            {
                Log?.Error($"[Scene] Load error: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // オーバーレイ選択
        // ──────────────────────────────────────────────────────────────
        private void BeginOverlaySelection()
        {
            string mode = "Auto";
            try { if (_cfgOverlayMode != null && !string.IsNullOrEmpty(_cfgOverlayMode.Value)) mode = _cfgOverlayMode.Value.Trim(); }
            catch { }

            if (mode.Equals("Off", StringComparison.OrdinalIgnoreCase))
            {
                _overlayLaunched = true;
                Log?.Msg("[Overlay] OverlayMode=Off のためオーバーレイを起動しません");
                return;
            }

            if (mode.Equals("VR", StringComparison.OrdinalIgnoreCase))
            {
                Log?.Msg("[Overlay] OverlayMode=VR（手動指定）");
                LaunchOverlay(true);
                return;
            }

            if (mode.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
            {
                Log?.Msg("[Overlay] OverlayMode=Desktop（手動指定）");
                LaunchOverlay(false);
                return;
            }

            float timeout = 8f;
            try { if (_cfgProbeTimeout != null) timeout = _cfgProbeTimeout.Value; } catch { }
            if (timeout < 0.5f) timeout = 0.5f;

            _probing = true;
            _probeDeadline = Time.time + timeout;
            _nextProbeTime = 0f;
            Log?.Msg($"[Overlay] VR/2D 判定を開始します（最大 {timeout:F1} 秒）");
        }

        /// <summary>
        /// ゲーム本体の VR モードを取得する。
        /// 戻り値: -1 = 判定不能 / 0 = VrMode.None（2Dデスクトップ）/ 1以上 = VR稼働中
        /// </summary>
        private int TryGetVrMode()
        {
            try
            {
                if (!_vrModeResolved)
                {
                    _vrModeResolved = true;

                    var t = Il2CppHelper.FindType("VoxClient.CameraRig");
                    if (t == null)
                    {
                        Log?.Msg("[Overlay] VoxClient.CameraRig が見つかりません（VR判定不可）");
                        return -1;
                    }

                    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                    // Il2CppInterop はフィールドをプロパティ化する場合があるため3通り試す
                    try { _vrModeMethod = t.GetMethod("GetCurrentVrMode", F, null, Type.EmptyTypes, null); } catch { }
                    try { _vrModeProp = t.GetProperty("vrMode", F); } catch { }
                    try { _vrModeField = t.GetField("vrMode", F); } catch { }
                    try { _vrModelProp = t.GetProperty("vrModel", F); } catch { }
                    try { _vrModelField = t.GetField("vrModel", F); } catch { }

                    Log?.Msg($"[Overlay] VR判定ソース: method={_vrModeMethod != null}, prop={_vrModeProp != null}, field={_vrModeField != null}");
                }

                int result = -1;

                object v = null;
                if (_vrModeMethod != null) { try { v = _vrModeMethod.Invoke(null, null); } catch { } }
                if (v == null && _vrModeProp != null) { try { v = _vrModeProp.GetValue(null); } catch { } }
                if (v == null && _vrModeField != null) { try { v = _vrModeField.GetValue(null); } catch { } }
                if (v != null) result = VrModeToInt(v);

                // vrMode を読めなかった場合のみ vrModel（HMD機種名）で補う。
                // vrMode が None(0) を返したときはそれが確定情報なので上書きしない。
                // デスクトップモードでは vrModel が "Unknown" を返すため、
                // 中身のない値は VR の根拠として扱わない。
                if (result < 0)
                {
                    object m = null;
                    if (_vrModelProp != null) { try { m = _vrModelProp.GetValue(null); } catch { } }
                    if (m == null && _vrModelField != null) { try { m = _vrModelField.GetValue(null); } catch { } }
                    string model = m?.ToString();
                    if (!string.IsNullOrWhiteSpace(model) && !IsPlaceholderModel(model))
                    {
                        Log?.Msg($"[Overlay] vrModel=\"{model}\" を検出 → VR稼働と判断");
                        return 1;
                    }
                }

                return result;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>vrModel が実機種名ではなくプレースホルダかどうか。</summary>
        private static bool IsPlaceholderModel(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            s = s.Trim();
            return s.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                || s.Equals("None",    StringComparison.OrdinalIgnoreCase)
                || s.Equals("null",    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>VrMode 値を int に変換する。None=0、その他の名前=1以上。</summary>
        private static int VrModeToInt(object v)
        {
            if (v == null) return -1;

            try { return Convert.ToInt32(v); }
            catch { }

            string s = v.ToString();
            if (string.IsNullOrEmpty(s)) return -1;
            if (s.Equals("None", StringComparison.OrdinalIgnoreCase)) return 0;
            if (int.TryParse(s, out int n)) return n;

            // None 以外の列挙名（Auto / Oculus / OpenVR / PSVR / MockHMD / WMR）は VR 稼働とみなす
            return 1;
        }

        private void LaunchOverlay(bool preferVr, bool noVrFallback = false)
        {
            if (_overlayLaunched) return;

            try
            {
                string modsPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                // どちらのオーバーレイもサブフォルダに配置する。
                // Mods直下に .dll を置くと MelonLoader がMODとして読み込もうとし、
                // ゲームが起動しなくなるため（通常版はEXEと同じ場所に .dll が必要）。
                string vrExePath = Path.Combine(modsPath, "VROverlay", "VoxSubtitleVROverlay.exe");
                string normalExePath = Path.Combine(modsPath, "Overlay", "VoxSubtitleOverlay.exe");

                // 旧レイアウト（Mods直下に自己完結EXEを置いていた頃）との互換
                if (!File.Exists(normalExePath))
                {
                    string legacyExePath = Path.Combine(modsPath, "VoxSubtitleOverlay.exe");
                    if (File.Exists(legacyExePath)) normalExePath = legacyExePath;
                }

                string exePath = null;
                string exeName = null;
                bool launchedVr = false;

                bool vrAvailable = !noVrFallback && File.Exists(vrExePath);
                bool normalAvailable = File.Exists(normalExePath);

                if (preferVr && vrAvailable)
                {
                    exePath = vrExePath;
                    exeName = "VoxSubtitleVROverlay.exe (VR版)";
                    launchedVr = true;
                }
                else if (normalAvailable)
                {
                    exePath = normalExePath;
                    exeName = "VoxSubtitleOverlay.exe (通常版)";
                }
                else if (vrAvailable)
                {
                    // 通常版が同梱されていない環境ではVR版で代替する
                    exePath = vrExePath;
                    exeName = "VoxSubtitleVROverlay.exe (VR版 / 通常版が無いため代替)";
                    launchedVr = true;
                }

                if (exePath == null)
                {
                    Log?.Msg("[Overlay] オーバーレイEXEが見つかりません");
                    Log?.Msg("[Overlay] 字幕表示なしで動作します");
                    _overlayLaunched = true;
                    return;
                }

                _overlayProcess = new Process();
                _overlayProcess.StartInfo.FileName = exePath;
                _overlayProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                _overlayProcess.StartInfo.UseShellExecute = true;
                _overlayProcess.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                _overlayProcess.Start();

                _overlayLaunched = true;
                Log?.Msg($"[Overlay] {exeName} 起動完了");

                if (launchedVr)
                {
                    // OpenVR初期化に失敗するとVR版は自ら終了する。数秒後に生存を確認する。
                    _vrFallbackPending = true;
                    _vrFallbackCheckAt = Time.time + VR_FALLBACK_CHECK_DELAY;
                }
            }
            catch (Exception e)
            {
                Log?.Msg($"[Overlay] 起動エラー: {e.Message}");
            }
        }

        private void LoadTranslations()
        {
            try
            {
                string modsPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string filePath = Path.Combine(modsPath, "translations.txt");

                if (!File.Exists(filePath))
                {
                    Log?.Msg("[Subtitle] translations.txt not found");
                    return;
                }

                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
                        continue;

                    string[] parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        // キーを正規化：末尾スペース除去、改行除去
                        string key = parts[0].Trim().TrimEnd(' ', '\t', '\r', '\n');

                        // 末尾にスペース+ピリオドがある場合にも対応（例："standby. " → "standby."）
                        // 追加でスペース付きバージョンも登録
                        var entry = new SubtitleEntry
                        {
                            Japanese = parts[1].Trim(),
                            Speaker = parts.Length > 2 ? parts[2].Trim() : "",
                            Duration = parts.Length > 3 && float.TryParse(parts[3], out float d) ? d : 5f
                        };
                        _translations[key] = entry;

                        // スペース付きバージョンも登録（TTSの末尾スペース対策）
                        if (!key.EndsWith(" "))
                        {
                            _translations[key + " "] = entry;
                        }
                    }
                }
                Log?.Msg($"[Subtitle] Loaded {_translations.Count} translations");
            }
            catch (Exception e)
            {
                Log?.Msg($"[Subtitle] Load error: {e.Message}");
            }
        }

        // 話者名を日本語に変換
        private string TranslateSpeakerName(string englishName)
        {
            if (string.IsNullOrEmpty(englishName)) return "";

            if (_speakerNameMap.TryGetValue(englishName, out string japaneseName))
            {
                return japaneseName;
            }
            return englishName; // マッピングがなければ英語のまま
        }

        private void SendSubtitle(string text, string speaker, float duration)
        {
            try
            {
                string msg = $"{speaker}|{text}|{duration:F1}";
                byte[] data = Encoding.UTF8.GetBytes(msg);
                _udpClient?.Send(data, data.Length, UDP_IP, UDP_PORT);
                Log?.Msg($"[字幕送信] {speaker}: {text}");
            }
            catch { }
        }

        // VoxSound.Play / TextToSpeech.Speak フック用（既存のTTS/SOUND処理）
        internal void ProcessSubtitle(string type, string id, string text)
        {
            string key = "";

            if (type == "TTS" && !string.IsNullOrEmpty(text))
            {
                // 改行を空白に置換し、連続空白を1つに、末尾を除去
                key = text.Replace("\n", " ").Replace("\r", " ");
                while (key.Contains("  ")) key = key.Replace("  ", " ");
                key = key.Trim();
            }
            else if (type == "SOUND" && !string.IsNullOrEmpty(id))
            {
                key = id.Trim();
            }

            if (string.IsNullOrEmpty(key)) return;
            // 有効期限内なら重複とみなしてスキップ
            if (_processedTexts.TryGetValue(key, out float expiry) && Time.time < expiry) return;

            if (_translations.TryGetValue(key, out var entry))
            {
                // 日本語テキストが空または # 始まりの場合は表示しない（意図的非表示マーカー）
                if (string.IsNullOrEmpty(entry.Japanese) || entry.Japanese.StartsWith("#")) return;
                float duration = entry.Duration > 0 ? entry.Duration : 5f;
                _processedTexts[key] = Time.time + duration + 1f;
                _lastSendTime = Time.time;
                SendSubtitle(entry.Japanese, entry.Speaker, duration);
            }
            else
            {
                // TTS/SOUNDでも未翻訳ログを出力
                Log?.Msg($"[未翻訳:{type}] {key}");
            }
        }

        // SubtitleGooey.Display() フック用（全キャラクター対応）
        internal void ProcessSubtitleDisplay(string speaker, string text, float duration)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 有効期限内なら重複とみなしてスキップ
            string key = text;
            if (_processedTexts.TryGetValue(key, out float expiry) && Time.time < expiry) return;

            // 翻訳辞書を検索
            if (_translations.TryGetValue(key, out var entry))
            {
                // 日本語テキストが空または # 始まりの場合は表示しない
                if (string.IsNullOrEmpty(entry.Japanese) || entry.Japanese.StartsWith("#")) return;
                string actualSpeaker;
                if (!string.IsNullOrEmpty(entry.Speaker))
                {
                    actualSpeaker = entry.Speaker;
                }
                else
                {
                    actualSpeaker = TranslateSpeakerName(speaker);
                }

                float actualDuration = entry.Duration > 0 ? entry.Duration : duration;
                // 有効期限 = 表示秒数 + 1秒バッファ（VRモードでの繰り返し呼び出しをブロック）
                _processedTexts[key] = Time.time + actualDuration + 1f;
                _lastSendTime = Time.time;

                SendSubtitle(entry.Japanese, actualSpeaker, actualDuration);
            }
            else
            {
                // 翻訳がない場合はログに記録（translations.txtに追加するための参考用）
                // フォーマット: 英語テキスト|日本語訳|話者名|秒数
                string translatedSpeaker = TranslateSpeakerName(speaker);
                Log?.Msg($"[未翻訳:SUBTITLE] {speaker}: {text} ({duration:F1}s)");
                Log?.Msg($"  → 追加用: {text}|【日本語訳】|{translatedSpeaker}|{duration:F0}");
            }
        }
    }

    // ======================================================================
    // Il2Cpp環境用ヘルパー - アセンブリから型を検索
    // ======================================================================
    internal static class Il2CppHelper
    {
        private static Assembly _gameAssembly;

        public static Assembly GetGameAssembly()
        {
            if (_gameAssembly != null) return _gameAssembly;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "Assembly-CSharp")
                {
                    _gameAssembly = asm;
                    VoxSubtitleMod.Log?.Msg($"[Helper] Found Assembly-CSharp");
                    return _gameAssembly;
                }
            }
            VoxSubtitleMod.Log?.Msg("[Helper] Assembly-CSharp not found!");
            return null;
        }

        public static Type FindType(string fullName)
        {
            var asm = GetGameAssembly();
            if (asm == null) return null;

            var type = asm.GetType(fullName);
            if (type != null) return type;

            // フルネームで見つからない場合、名前だけで検索
            string shortName = fullName.Contains(".") ? fullName.Split('.').Last() : fullName;
            foreach (var t in asm.GetTypes())
            {
                if (t.Name == shortName)
                {
                    VoxSubtitleMod.Log?.Msg($"[Helper] Found type: {t.FullName}");
                    return t;
                }
            }
            return null;
        }
    }

    // ======================================================================
    // TextToSpeech.Speak() パッチ - TTS音声用（BLUE/RED AI）
    // ======================================================================
    [HarmonyPatch]
    public class TextToSpeech_Speak_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var results = new List<MethodBase>();
            try
            {
                var type = Il2CppHelper.FindType("VoxClient.TextToSpeech");
                if (type == null)
                {
                    VoxSubtitleMod.Log?.Msg("[TTS] Type not found");
                    return results;
                }

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                foreach (var m in type.GetMethods(flags))
                {
                    if (m.Name != "Speak") continue;
                    var p = m.GetParameters();
                    if (p.Length >= 1 && p[0].ParameterType == typeof(string))
                    {
                        results.Add(m);
                        VoxSubtitleMod.Log?.Msg($"[TTS] Found: Speak({string.Join(", ", p.Select(x => x.ParameterType.Name))})");
                    }
                }
                if (results.Count > 0)
                    VoxSubtitleMod.Log?.Msg($"[TTS] {results.Count} overload(s) patched");
            }
            catch (Exception e)
            {
                VoxSubtitleMod.Log?.Msg($"[TTS] Patch error: {e.Message}");
            }
            return results;
        }

        [HarmonyPostfix]
        static void Postfix(string __0)
        {
            if (string.IsNullOrEmpty(__0)) return;
            VoxSubtitleMod.Instance?.ProcessSubtitle("TTS", "", __0);
        }
    }

    // ======================================================================
    // SubtitleGooey.Display() パッチ - 全キャラクター字幕用
    // ゲームの字幕設定がOFFでもフック可能（メソッド呼び出し自体をフック）
    // ======================================================================
    [HarmonyPatch]
    public class SubtitleGooey_Display_Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var results = new List<MethodBase>();
            try
            {
                var type = Il2CppHelper.FindType("VoxClient.SubtitleGooey");
                if (type == null)
                {
                    VoxSubtitleMod.Log?.Msg("[SubtitleGooey] Type not found");
                    return results;
                }

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                foreach (var m in type.GetMethods(flags))
                {
                    if (m.Name != "Display") continue;
                    var p = m.GetParameters();
                    // Display(string nameValue, string textValue, float duration)
                    if (p.Length == 3 &&
                        p[0].ParameterType == typeof(string) &&
                        p[1].ParameterType == typeof(string) &&
                        p[2].ParameterType == typeof(float))
                    {
                        results.Add(m);
                        VoxSubtitleMod.Log?.Msg($"[SubtitleGooey] Found: Display(string, string, float)");
                    }
                }

                if (results.Count > 0)
                    VoxSubtitleMod.Log?.Msg($"[SubtitleGooey] {results.Count} method(s) patched");
                else
                    VoxSubtitleMod.Log?.Msg("[SubtitleGooey] Display method not found");
            }
            catch (Exception e)
            {
                VoxSubtitleMod.Log?.Msg($"[SubtitleGooey] Patch error: {e.Message}");
            }
            return results;
        }

        [HarmonyPostfix]
        static void Postfix(string __0, string __1, float __2)
        {
            // __0 = nameValue (話者名: "Donny", "BLUE", "Frank" など)
            // __1 = textValue (セリフテキスト)
            // __2 = duration  (表示秒数)

            if (string.IsNullOrEmpty(__1)) return;

            VoxSubtitleMod.Instance?.ProcessSubtitleDisplay(__0, __1, __2);
        }
    }
}
