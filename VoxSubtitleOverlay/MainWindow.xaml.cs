using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VoxSubtitleOverlay
{
    public partial class MainWindow : Window
    {
        private const int UDP_PORT = 20044;
        private UdpClient _udpClient;
        private CancellationTokenSource _cts;
        private DispatcherTimer _hideTimer;

        public MainWindow()
        {
            InitializeComponent();

            // クリックスルー設定
            this.Loaded += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SetWindowExTransparent(hwnd);
            };

            // 非表示タイマー
            _hideTimer = new DispatcherTimer();
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                SubtitleBorder.Visibility = Visibility.Hidden;
            };

            // UDP受信開始
            StartUdpListener();

            // 起動確認用テスト表示
            this.Loaded += (s, e) =>
            {
                ShowSubtitle("VoxSubtitle 起動テスト", "System", 5.0f);
            };
        }

        private void StartUdpListener()
        {
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    _udpClient = new UdpClient(UDP_PORT);

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var result = await _udpClient.ReceiveAsync();
                            string message = Encoding.UTF8.GetString(result.Buffer);
                            ProcessMessage(message);
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }, _cts.Token);
        }

        private void ProcessMessage(string message)
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

            Dispatcher.Invoke(() =>
            {
                ShowSubtitle(speaker, text, duration);
            });
        }

        private void ShowSubtitle(string speaker, string text, float duration)
        {
            _hideTimer.Stop();

            if (!string.IsNullOrEmpty(speaker))
            {
                SpeakerText.Text = speaker + "：";
                SpeakerText.Visibility = Visibility.Visible;
            }
            else
            {
                SpeakerText.Visibility = Visibility.Collapsed;
            }

            SubtitleText.Text = text;
            SubtitleBorder.Visibility = Visibility.Visible;

            _hideTimer.Interval = TimeSpan.FromSeconds(duration);
            _hideTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            _udpClient?.Close();
            base.OnClosed(e);
        }

        // クリックスルー
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int GWL_EXSTYLE = -20;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private static void SetWindowExTransparent(IntPtr hwnd)
        {
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
        }
    }
}
