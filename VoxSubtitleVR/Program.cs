// VoxSubtitleOverlay VR版
// OpenVR APIを使用してVR空間内に字幕を表示

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Valve.VR;

namespace VoxSubtitleOverlay
{
    class Program
    {
        private const int UDP_PORT = 20044;
        private const string OVERLAY_KEY = "voxsubtitle.overlay";
        private const string OVERLAY_NAME = "VoxSubtitle";

        // 静的初期化を避けるため、初期値を設定しない
        private static ulong _overlayHandle;
        private static CVRSystem? _vrSystem;
        private static CVROverlay? _vrOverlay;

        private static string _currentText = "";
        private static string _currentSpeaker = "";
        private static DateTime _hideTime = DateTime.MinValue;
        private static bool _subtitleVisible = false;
        private static readonly object _lock = new object();
        
        // 変更検出用
        private static string _lastRenderedText = "";
        private static string _lastRenderedSpeaker = "";
        private static bool _needsTextureUpdate = false;

        private static Bitmap? _textureBitmap;
        private static int _textureWidth = 1400;
        private static int _textureHeight = 160; // 2行対応のため高さを増加

        private static bool _vrInitialized = false;
        private static bool _running = true;

        // OpenVR に接続できないまま待ち続けると、MOD側がデスクトップ版へ
        // 切り替えられない（プロセスが生き続けるため）。
        // 一定回数失敗したら終了コードを返して終了する。
        private static int _initFailCount = 0;
        private const int INIT_FAIL_LIMIT = 7;   // 3秒 x 7 = 約21秒であきらめる

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("VoxSubtitle VR Overlay v2.6");
                Console.WriteLine("===========================");
                Console.WriteLine($"実行パス: {AppDomain.CurrentDomain.BaseDirectory}");
                
                // OpenVRの定数を初期化
                _overlayHandle = 0; // OpenVR.k_ulOverlayHandleInvalid は 0

                // UDP受信スレッド開始
                Task.Run(() => UdpListenerThread());

                // メインループ
                while (_running)
                {
                    if (!_vrInitialized)
                    {
                        InitVR();
                    }

                    if (_vrInitialized)
                    {
                        try
                        {
                            UpdateOverlay();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"オーバーレイ更新エラー: {ex.Message}");
                            _vrInitialized = false;
                            _vrSystem = null;
                            _vrOverlay = null;
                        }
                    }

                    Thread.Sleep(33); // ~30fps
                }

                Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine("========== エラー発生 ==========");
                Console.WriteLine($"例外: {ex.GetType().Name}");
                Console.WriteLine($"メッセージ: {ex.Message}");
                Console.WriteLine($"スタックトレース:\n{ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"内部例外: {ex.InnerException.Message}");
                }
                Console.WriteLine("================================");
                Console.WriteLine("Enterキーで終了...");
                Console.ReadLine();
            }
        }

        static void InitVR()
        {
            try
            {
                Console.WriteLine("OpenVR初期化中...");
                
                // OpenVR初期化
                EVRInitError error = EVRInitError.None;
                _vrSystem = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);

                if (error != EVRInitError.None || _vrSystem == null)
                {
                    _initFailCount++;
                    Console.WriteLine($"VR初期化待機中... ({error}) [{_initFailCount}/{INIT_FAIL_LIMIT}]");

                    if (_initFailCount >= INIT_FAIL_LIMIT)
                    {
                        Console.WriteLine("OpenVR に接続できないため終了します。");
                        Console.WriteLine("MOD側がデスクトップ版オーバーレイへ切り替えます。");
                        Environment.Exit(2);
                    }

                    Thread.Sleep(3000);
                    return;
                }

                _initFailCount = 0;
                _vrOverlay = OpenVR.Overlay;
                if (_vrOverlay == null)
                {
                    Console.WriteLine("オーバーレイシステムが利用できません");
                    OpenVR.Shutdown();
                    return;
                }

                // オーバーレイ作成
                EVROverlayError overlayError = _vrOverlay.CreateOverlay(OVERLAY_KEY, OVERLAY_NAME, ref _overlayHandle);
                if (overlayError != EVROverlayError.None)
                {
                    Console.WriteLine($"オーバーレイ作成失敗: {overlayError}");
                    OpenVR.Shutdown();
                    return;
                }

                // オーバーレイ設定
                _vrOverlay.SetOverlayWidthInMeters(_overlayHandle, 0.9f); // 少し大きく
                _vrOverlay.SetOverlayAlpha(_overlayHandle, 1.0f);

                // HMDの前方下部に配置
                HmdMatrix34_t transform = new HmdMatrix34_t();
                transform.m0 = 1f; transform.m1 = 0f; transform.m2 = 0f; transform.m3 = 0f;
                transform.m4 = 0f; transform.m5 = 1f; transform.m6 = 0f; transform.m7 = -0.30f; // 少し下に
                transform.m8 = 0f; transform.m9 = 0f; transform.m10 = 1f; transform.m11 = -0.6f;

                _vrOverlay.SetOverlayTransformTrackedDeviceRelative(
                    _overlayHandle,
                    OpenVR.k_unTrackedDeviceIndex_Hmd,
                    ref transform
                );

                // テクスチャ用ビットマップ作成
                _textureBitmap = new Bitmap(_textureWidth, _textureHeight, PixelFormat.Format32bppArgb);

                // ウォームアップ：ダミーテクスチャを数回設定してGPUパイプラインを安定化
                using (Graphics g = Graphics.FromImage(_textureBitmap))
                {
                    g.Clear(Color.Transparent);
                }
                for (int i = 0; i < 5; i++)
                {
                    SetOverlayTexture();
                    Thread.Sleep(50);
                }

                // 初期状態は非表示
                _vrOverlay.HideOverlay(_overlayHandle);

                _vrInitialized = true;
                
                Console.WriteLine("VR初期化完了！ 字幕待機中...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VR初期化エラー: {ex.Message}");
                Console.WriteLine($"スタックトレース: {ex.StackTrace}");
                Thread.Sleep(3000);
            }
        }

        static void UdpListenerThread()
        {
            UdpClient? udpClient = null;

            while (_running)
            {
                try
                {
                    if (udpClient == null)
                    {
                        udpClient = new UdpClient(UDP_PORT);
                        Console.WriteLine($"UDP受信開始 (port {UDP_PORT})");
                    }

                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udpClient.Receive(ref remoteEP);
                    string message = Encoding.UTF8.GetString(data);

                    ProcessMessage(message);
                }
                catch (SocketException)
                {
                    udpClient?.Close();
                    udpClient = null;
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UDP エラー: {ex.Message}");
                    Thread.Sleep(100);
                }
            }

            udpClient?.Close();
        }

        static void ProcessMessage(string message)
        {
            // フォーマット: SPEAKER|TEXT|DURATION
            string[] parts = message.Split('|');
            if (parts.Length < 2) return;

            string speaker = parts[0];
            string text = parts[1];
            float duration = 5.0f;

            if (parts.Length >= 3 && float.TryParse(parts[2],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float d))
            {
                duration = d;
            }

            if (duration < 3.0f)
            {
                duration = Math.Max(3.0f, text.Length / 5.0f);
            }

            lock (_lock)
            {
                // 新しい字幕が来たら更新フラグを立てる
                if (_currentText != text || _currentSpeaker != speaker)
                {
                    _needsTextureUpdate = true;
                }
                _currentSpeaker = speaker;
                _currentText = text;
                _hideTime = DateTime.Now.AddSeconds(duration);
                _subtitleVisible = true;
            }

            Console.WriteLine($"[字幕] {speaker}：{text}");
        }

        static void UpdateOverlay()
        {
            if (_vrOverlay == null || _overlayHandle == 0)
                return;

            bool shouldShow;
            string speaker, text;
            bool needsUpdate;

            lock (_lock)
            {
                shouldShow = _subtitleVisible && DateTime.Now < _hideTime;
                speaker = _currentSpeaker;
                text = _currentText;
                needsUpdate = _needsTextureUpdate;

                if (!shouldShow && _subtitleVisible)
                {
                    _subtitleVisible = false;
                }
            }

            if (shouldShow)
            {
                // 字幕内容が変わった時だけテクスチャを更新
                if (needsUpdate || text != _lastRenderedText || speaker != _lastRenderedSpeaker)
                {
                    RenderSubtitle(speaker, text);
                    SetOverlayTexture();
                    _lastRenderedText = text;
                    _lastRenderedSpeaker = speaker;
                    
                    lock (_lock)
                    {
                        _needsTextureUpdate = false;
                    }
                }
                _vrOverlay.ShowOverlay(_overlayHandle);
            }
            else
            {
                if (_lastRenderedText != "" || _lastRenderedSpeaker != "")
                {
                    _lastRenderedText = "";
                    _lastRenderedSpeaker = "";
                }
                _vrOverlay.HideOverlay(_overlayHandle);
            }
        }

        static void RenderSubtitle(string speaker, string text)
        {
            if (_textureBitmap == null) return;

            using (Graphics g = Graphics.FromImage(_textureBitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                // 背景クリア（透明）
                g.Clear(Color.Transparent);

                // フォント設定
                using Font speakerFont = new Font("Yu Gothic UI", 26, FontStyle.Bold);
                using Font textFont = new Font("Yu Gothic UI", 28, FontStyle.Bold);
                using Brush speakerBrush = new SolidBrush(Color.Gold);
                using Brush textBrush = new SolidBrush(Color.White);

                // 話者名のサイズを測定
                string speakerText = string.IsNullOrEmpty(speaker) ? "" : speaker + "：";
                SizeF speakerSize = string.IsNullOrEmpty(speakerText) ? SizeF.Empty : g.MeasureString(speakerText, speakerFont);
                
                // 本文の利用可能幅を計算
                float availableWidth = _textureWidth - 60 - speakerSize.Width; // 左右マージン30ずつ
                
                // 本文を改行処理
                string[] lines = WrapText(g, text, textFont, availableWidth);
                bool isTwoLines = lines.Length > 1;
                
                // 背景の高さを決定
                int bgHeight = isTwoLines ? 130 : 80;
                int bgY = (_textureHeight - bgHeight) / 2;
                
                // 角丸の背景を描画
                using (var brush = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
                {
                    FillRoundedRectangle(g, brush, new Rectangle(5, bgY, _textureWidth - 10, bgHeight), 15);
                }

                float x = 30;
                float lineHeight = 42;
                float totalTextHeight = lines.Length * lineHeight;
                float startY = (_textureHeight - totalTextHeight) / 2f;

                // 話者名（1行目の左）
                if (!string.IsNullOrEmpty(speakerText))
                {
                    g.DrawString(speakerText, speakerFont, speakerBrush, x, startY + 2);
                }

                // 本文を描画
                float textX = x + speakerSize.Width;
                for (int i = 0; i < lines.Length; i++)
                {
                    float lineY = startY + (i * lineHeight);
                    // 2行目以降は左端から
                    float drawX = (i == 0) ? textX : x;
                    g.DrawString(lines[i], textFont, textBrush, drawX, lineY);
                }
            }
        }

        static string[] WrapText(Graphics g, string text, Font font, float maxWidth)
        {
            // 固定文字数（36文字）で改行
            const int MAX_CHARS_PER_LINE = 36;
            
            // 33文字以下なら1行
            if (text.Length <= MAX_CHARS_PER_LINE)
            {
                return new string[] { text };
            }

            // 33文字で分割
            string line1 = text.Substring(0, MAX_CHARS_PER_LINE);
            string line2 = text.Substring(MAX_CHARS_PER_LINE);
            
            return new string[] { line1, line2 };
        }

        static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (var path = new GraphicsPath())
            {
                path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
                path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
                path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        static void SetOverlayTexture()
        {
            if (_textureBitmap == null || _vrOverlay == null) return;

            try
            {
                BitmapData bmpData = _textureBitmap.LockBits(
                    new Rectangle(0, 0, _textureWidth, _textureHeight),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb
                );

                try
                {
                    EVROverlayError error = _vrOverlay.SetOverlayRaw(
                        _overlayHandle,
                        bmpData.Scan0,
                        (uint)_textureWidth,
                        (uint)_textureHeight,
                        4
                    );
                }
                finally
                {
                    _textureBitmap.UnlockBits(bmpData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"テクスチャ設定エラー: {ex.Message}");
            }
        }

        static void Shutdown()
        {
            Console.WriteLine("シャットダウン中...");

            if (_vrOverlay != null && _overlayHandle != 0)
            {
                _vrOverlay.DestroyOverlay(_overlayHandle);
            }

            if (_vrInitialized)
            {
                OpenVR.Shutdown();
            }

            _textureBitmap?.Dispose();
        }
    }
}
