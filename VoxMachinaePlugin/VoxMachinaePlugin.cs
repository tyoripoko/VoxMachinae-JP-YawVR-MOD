// VoxMachinaePlugin.cs  v4.1
// YawVR GameLink Plugin for Vox Machinae
//
// チャンネル一覧 (24ch):
//   0:  Yaw            — heading
//   1:  Pitch          — body euler pitch
//   2:  Roll           — body euler roll
//   3:  Walk           — footstep pitch nod
//   4:  Turning        — centrifugal force
//   5:  Cockpit_Pitch   — hit shake Pitch
//   6:  Cockpit_Roll    — hit shake Roll
//   7:  Cockpit_Yaw     — hit shake Yaw
//   8:  Landing_Pitch  — landing shake Pitch
//   9:  Landing_Roll   — landing shake Roll
//  10:  Landing_Yaw    — landing shake Yaw
//  11:  Shield_Pitch   — eject shake Pitch
//  12:  Shield_Roll    — eject shake Roll
//  13:  Shield_Yaw     — eject shake Yaw
//  14:  Foot_Vib_Amp   — footstep vibration AMP (Walk+Landing統合)
//  15:  Foot_Vib_Hz    — footstep vibration HZ
//  16:  Turning_Vib_Amp — turning vibration AMP
//  17:  Turning_Vib_Hz  — turning vibration HZ
//  18:  Cockpit_Vib_Amp — hit vibration AMP
//  19:  Cockpit_Vib_Hz  — hit vibration HZ
//  20:  Jet_Vib_Amp    — jet vibration AMP
//  21:  Jet_Vib_Hz     — jet vibration HZ
//  22:  Weapon_Vib_Amp — weapon vibration AMP
//  23:  Weapon_Vib_Hz  — weapon vibration HZ

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using YawGLAPI;

namespace VoxMachinaePlugin
{
    [Export(typeof(Game))]
    [ExportMetadata("Name",        "Vox Machinae")]
    [ExportMetadata("Version",     "4.1")]
    [ExportMetadata("Description", "YAW2 motion plugin for Vox Machinae (24ch AMP/HZ分離).")]
    public class VoxMachinaePlugin : Game
    {
        public int    STEAM_ID        => 334540;
        public string PROCESS_NAME    => "vm";
        public bool   PATCH_AVAILABLE => false;
        public string AUTHOR          => "tyoripoko";
        public string Description =>
            "YAW2 plugin for Vox Machinae (24ch)\n" +
            "ch0:Yaw ch1:Pitch ch2:Roll ch3:Walk ch4:Turning\n" +
            "ch5-7:Cockpit_P/R/Y  ch8-10:Landing_P/R/Y  ch11-13:Shield_P/R/Y\n" +
            "ch14-23: Vib AMP/HZ (Foot/Turn/Cockpit/Jet/Weapon)";

        public Stream Logo       => GetResource("logo.png");
        public Stream SmallLogo  => GetResource("recent.png");
        public Stream Background => GetResource("wide.png");

        public List<Profile_Component> DefaultProfile()
        {
            if (_disp == null) return new List<Profile_Component>();
            return _disp.JsonToComponents(DEFAULT_PROFILE_JSON);
        }

        public LedEffect DefaultLED() => new LedEffect(
            EFFECT_TYPE.KNIGHT_RIDER, 0,
            new YawColor[]
            {
                new YawColor(  0, 120, 255),
                new YawColor(  0,  60, 180),
                new YawColor( 20,  80, 200),
                new YawColor( 60, 160, 255),
            }, 0.6f);

        private static readonly string[] INPUT_NAMES =
        {
            "Yaw",             //  0
            "Pitch",           //  1
            "Roll",            //  2
            "Walk",            //  3
            "Turning",         //  4
            "Cockpit_Pitch",    //  5
            "Cockpit_Roll",     //  6
            "Cockpit_Yaw",      //  7
            "Landing_Pitch",   //  8
            "Landing_Roll",    //  9
            "Landing_Yaw",     // 10
            "Shield_Pitch",    // 11
            "Shield_Roll",     // 12
            "Shield_Yaw",      // 13
            "Foot_Vib_Amp",    // 14
            "Foot_Vib_Hz",     // 15
            "Turning_Vib_Amp", // 16
            "Turning_Vib_Hz",  // 17
            "Cockpit_Vib_Amp",  // 18
            "Cockpit_Vib_Hz",   // 19
            "Jet_Vib_Amp",     // 20
            "Jet_Vib_Hz",      // 21
            "Weapon_Vib_Amp",  // 22
            "Weapon_Vib_Hz",   // 23
        };

        private IProfileManager     _pm;
        private IMainFormDispatcher _disp;
        private UdpClient           _udp;
        private Thread              _thread;
        private bool                _running;
        private const int           PORT = 20042;

        public void SetReferences(IProfileManager controller, IMainFormDispatcher dispatcher)
        {
            _pm   = controller;
            _disp = dispatcher;
        }

        public void Init()
        {
            _running = true;
            _udp     = new UdpClient(PORT);
            _udp.Client.ReceiveTimeout = 2000;
            _thread  = new Thread(Loop) { IsBackground = true, Name = "VoxMachinae_UDP" };
            _thread.Start();
        }

        public void Exit()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
        }

        public string[] GetInputData() => INPUT_NAMES;
        public Dictionary<string, ParameterInfo[]> GetFeatures() => null;
        public Type   GetConfigBody() => null;
        public void   PatchGame()     { }

        private void Loop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    var msg = Encoding.ASCII.GetString(_udp.Receive(ref ep)).Trim();
                    var p = msg.Split(',');
                    if (p.Length < 24) continue;

                    float[] v = new float[24];
                    bool ok = true;
                    for (int i = 0; i < 24; i++)
                        if (!F(p[i], out v[i])) { ok = false; break; }
                    if (!ok) continue;

                    _pm?.SetInput(0,  v[0]);                           // Yaw
                    _pm?.SetInput(1,  Math.Clamp(v[1],  -1f, 1f));     // Pitch
                    _pm?.SetInput(2,  Math.Clamp(v[2],  -1f, 1f));     // Roll
                    _pm?.SetInput(3,  Math.Clamp(v[3],  -1f, 1f));     // Walk
                    _pm?.SetInput(4,  Math.Clamp(v[4],  -1f, 1f));     // Turning
                    _pm?.SetInput(5,  Math.Clamp(v[5],  -1f, 1f));     // Cockpit_Pitch
                    _pm?.SetInput(6,  Math.Clamp(v[6],  -1f, 1f));     // Cockpit_Roll
                    _pm?.SetInput(7,  Math.Clamp(v[7],  -1f, 1f));     // Cockpit_Yaw
                    _pm?.SetInput(8,  Math.Clamp(v[8],  -1f, 1f));     // Landing_Pitch
                    _pm?.SetInput(9,  Math.Clamp(v[9],  -1f, 1f));     // Landing_Roll
                    _pm?.SetInput(10, Math.Clamp(v[10], -1f, 1f));     // Landing_Yaw
                    _pm?.SetInput(11, Math.Clamp(v[11], -1f, 1f));     // Shield_Pitch
                    _pm?.SetInput(12, Math.Clamp(v[12], -1f, 1f));     // Shield_Roll
                    _pm?.SetInput(13, Math.Clamp(v[13], -1f, 1f));     // Shield_Yaw
                    _pm?.SetInput(14, Math.Clamp(v[14], 0f, 100f));    // Foot_Vib_Amp
                    _pm?.SetInput(15, Math.Clamp(v[15], 0f, 100f));    // Foot_Vib_Hz
                    _pm?.SetInput(16, Math.Clamp(v[16], 0f, 100f));    // Turning_Vib_Amp
                    _pm?.SetInput(17, Math.Clamp(v[17], 0f, 100f));    // Turning_Vib_Hz
                    _pm?.SetInput(18, Math.Clamp(v[18], 0f, 100f));    // Cockpit_Vib_Amp
                    _pm?.SetInput(19, Math.Clamp(v[19], 0f, 100f));    // Cockpit_Vib_Hz
                    _pm?.SetInput(20, Math.Clamp(v[20], 0f, 100f));    // Jet_Vib_Amp
                    _pm?.SetInput(21, Math.Clamp(v[21], 0f, 100f));    // Jet_Vib_Hz
                    _pm?.SetInput(22, Math.Clamp(v[22], 0f, 100f));    // Weapon_Vib_Amp
                    _pm?.SetInput(23, Math.Clamp(v[23], 0f, 100f));    // Weapon_Vib_Hz
                }
                catch (SocketException)         { }
                catch (ObjectDisposedException) { break; }
                catch                           { }
            }
        }

        private static bool F(string s, out float r) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r);

        private static Stream GetResource(string name) =>
            Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("VoxMachinaePlugin." + name)
            ?? Stream.Null;

        // デフォルトプロファイル (24ch AMP/HZ分離)
        // Output_index: 0=Yaw, 1=Pitch, 2=Roll, 5=AMP, 6=HZ
        private const string DEFAULT_PROFILE_JSON = @"{
  ""gameName"": ""Vox Machinae"",
  ""Name"": ""Default"",
  ""components"": [
    { ""Constant"":false,""Input_index"":0, ""Output_index"":0,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.5,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":1, ""Output_index"":1,""MultiplierPos"":4.0,""MultiplierNeg"":4.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.3,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":2, ""Output_index"":2,""MultiplierPos"":4.0,""MultiplierNeg"":4.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.3,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":3, ""Output_index"":1,""MultiplierPos"":2.0,""MultiplierNeg"":2.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":4, ""Output_index"":2,""MultiplierPos"":4.0,""MultiplierNeg"":4.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":5, ""Output_index"":1,""MultiplierPos"":3.0,""MultiplierNeg"":3.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":6, ""Output_index"":2,""MultiplierPos"":3.0,""MultiplierNeg"":3.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":7, ""Output_index"":0,""MultiplierPos"":3.0,""MultiplierNeg"":3.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":8, ""Output_index"":1,""MultiplierPos"":3.0,""MultiplierNeg"":3.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":9, ""Output_index"":2,""MultiplierPos"":3.0,""MultiplierNeg"":3.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":10,""Output_index"":0,""MultiplierPos"":3.0,""MultiplierNeg"":3.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":11,""Output_index"":1,""MultiplierPos"":3.0,""MultiplierNeg"":3.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":12,""Output_index"":2,""MultiplierPos"":3.0,""MultiplierNeg"":3.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":13,""Output_index"":0,""MultiplierPos"":3.0,""MultiplierNeg"":3.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":14,""Output_index"":5,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":15,""Output_index"":6,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":16,""Output_index"":5,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":17,""Output_index"":6,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":18,""Output_index"":5,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":19,""Output_index"":6,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":20,""Output_index"":5,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":21,""Output_index"":6,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":22,""Output_index"":5,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] },
    { ""Constant"":false,""Input_index"":23,""Output_index"":6,""MultiplierPos"":1.0,""MultiplierNeg"":1.0,""Offset"":0.0,""Inverse"":false,""Limit"":-1.0,""Smoothing"":0.0,""Enabled"":true,""Spikeflatter"":{""Enabled"":false,""Limit"":100.0,""Strength"":0.5},""Deadzone"":0.0,""Type"":0,""Condition"":[],""Math"":[] }
  ],
  ""effects"": {
    ""EffectID"": 0, ""InputID"": 3, ""Multiplier"": 0.6,
    ""Colors"": [
      {""R"":0,""G"":120,""B"":255},{""R"":0,""G"":60,""B"":180},
      {""R"":20,""G"":80,""B"":200},{""R"":60,""G"":160,""B"":255}
    ]
  }
}";
    }
}
