using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System;
using System.Collections.Generic;
using Windows.Gaming.Input;

namespace _7KLOG
{
    namespace ControllerInput
    {
        enum TurntableMode { Axis, Digital }

        record ControllerMapping(
            Dictionary<string, int> Buttons,
            Dictionary<string, int> Axes,
            TurntableMode TurntableMode
        );

        class ControllerState
        {
            public Dictionary<string, bool> Buttons { get; } = new();
            public Dictionary<string, double> Axes { get; } = new();

            // 0～360度に正規化した角度
            public double TurntableAngle { get; private set; }
            public double RawTurntableAngle => _angleAccum;

            private double _angleAccum;
            private double _prevAngle;

            // 差分で更新（Digitalモード用）
            public void UpdateTurntableDelta(double deltaDeg)
            {
                _angleAccum += deltaDeg;
                TurntableAngle = Normalize(_angleAccum);
            }

            // 絶対角度で更新（Axisモード用）
            public void UpdateTurntableAbsolute(double absoluteDeg)
            {
                double delta = absoluteDeg - _prevAngle;

                if(Math.Abs(delta) >= 180)
                {
                    if(delta < 0) delta = 360 + delta;
                    else delta = 360 - delta;
                }

                _prevAngle = absoluteDeg;
                _angleAccum += delta * 2.0;
                TurntableAngle = Normalize(_angleAccum);
            }

            private static double Normalize(double angle)
            {
                return ((angle % 360.0) + 360.0) % 360.0;
            }
        }

        class ControllerReader
        {
            private RawGameController? _controller;
            private ControllerMapping? _mapping;
            private readonly ControllerState _state = new();

            // 固定速度 (deg/sec)
            private const double TurntableSpeed = 360;

            public RawGameController? Controller { get { return _controller; }  set { _controller = value; } }
            public ControllerMapping? Mapping { get { return _mapping; } set { _mapping = value; } }

            public ControllerState State => _state;

            public void Update(double deltaTimeSec)
            {
                if (_controller != null && _mapping != null)
                {
                    var buttons = new bool[_controller.ButtonCount];
                    var switches = new GameControllerSwitchPosition[_controller.SwitchCount];
                    var axes = new double[_controller.AxisCount];

                    _controller.GetCurrentReading(buttons, switches, axes);

                    // ボタン更新
                    foreach (var kv in _mapping.Buttons)
                        if (kv.Value < buttons.Length)
                            _state.Buttons[kv.Key] = buttons[kv.Value];

                    // 軸更新
                    foreach (var kv in _mapping.Axes)
                        if (kv.Value < axes.Length)
                            _state.Axes[kv.Key] = axes[kv.Value];

                    // ターンテーブル処理
                    if (_mapping.TurntableMode == TurntableMode.Axis)
                    {
                        if (_mapping.Axes.TryGetValue("TurntableAxis", out var idx) && idx < axes.Length)
                        {
                            double raw = axes[idx]; // -1.0～+1.0
                            double angle = raw * -360.0; // 0～360°
                            _state.UpdateTurntableAbsolute(angle);
                        }
                    }
                    else if (_mapping.TurntableMode == TurntableMode.Digital)
                    {
                        bool left = _state.Buttons.GetValueOrDefault("TT_Left");
                        bool right = _state.Buttons.GetValueOrDefault("TT_Right");

                        double delta = 0;
                        if (left && !right) delta = -TurntableSpeed * deltaTimeSec;
                        if (right && !left) delta = +TurntableSpeed * deltaTimeSec;

                        _state.UpdateTurntableDelta(delta);
                    }
                }
            }
        }
    }
}
