using Reactive.Bindings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Windows.Gaming.Input;

namespace _7KLOG
{
    public class MainWindowViewModel
    {
        #region クラス定義
        /// <summary>
        /// ComboBoxにバインドする用のクラス
        /// </summary>
        /// <param name="controller"></param>
        public class GameControllerViewModel(RawGameController controller)
        {
            private RawGameController _controller = controller;

            public RawGameController Controller { get { return _controller; } }
            public string DisplayString { get => $"{_controller.DisplayName} ({_controller.HardwareVendorId:X4}:{_controller.HardwareProductId:X4})"; }
        }
        #endregion

        #region プロパティ
        /// <summary>
        /// コントローラー一覧
        /// </summary>
        public ReactiveCollection<GameControllerViewModel> Controllers { get; set; } = [];
        /// <summary>
        /// 選択されたコントローラー
        /// </summary>
        public ReactiveProperty<GameControllerViewModel?> SelectedController { get; set; } = new();

        /// <summary>
        /// 1～7鍵
        /// </summary>
        public ReactiveProperty<bool>[] ButtonPressed { get; set; } = new ReactiveProperty<bool>[7];

        /// <summary>
        /// E1～4
        /// </summary>
        public ReactiveProperty<bool>[] SubButtonPressed { get; set; } = new ReactiveProperty<bool>[4];

        /// <summary>
        /// ターンテーブル角度
        /// </summary>
        public ReactiveProperty<double> TurnTableAngle { get; set; } = new();

        /// <summary>
        /// ターンテーブル上照明
        /// </summary>
        public ReactiveProperty<bool> TurnTableUpper { get; set; } = new();

        /// <summary>
        /// ターンテーブル下照明
        /// </summary>
        public ReactiveProperty<bool> TurnTableLower { get; set; } = new();

        /// <summary>
        /// ラジオボタン1P
        /// </summary>
        public ReactiveProperty<bool> Is1P { get; set; } = new(true);
        /// <summary>
        /// ラジオボタン2P
        /// </summary>
        public ReactiveProperty<bool> Is2P { get; set; } = new(false);
        /// <summary>
        /// CN終端カウントチェックボックス
        /// </summary>
        public ReactiveProperty<bool> EnableCNReleaseCount { get; set; } = new(false);

        /// <summary>
        /// ターンテーブル位置
        /// </summary>
        public ReadOnlyReactiveProperty<int> TurnTableColumn { get; }

        /// <summary>
        /// 打鍵数
        /// </summary>
        public ReactiveProperty<int> TotalCount { get; set; } = new();
        /// <summary>
        /// NPS
        /// </summary>
        public ReactiveProperty<int> NotesPerSecond { get; set; } = new();
        /// <summary>
        /// リリースアベレージ
        /// </summary>
        public ReactiveProperty<int> AverageRelease { get; set; } = new();
        #endregion

        /// <summary>
        /// 鍵盤発光色
        /// </summary>
        public ReactiveProperty<Brush>[] GlowColor { get; set; } = 
            [new(Brushes.White), new(Brushes.DeepSkyBlue), new(Brushes.White), new(Brushes.DeepSkyBlue), new(Brushes.White), new(Brushes.DeepSkyBlue), new(Brushes.White)];

        /// <summary>
        /// リセットボタン
        /// </summary>
        public ReactiveCommand ResetCommand { get; set; } = new();

        #region メンバ変数
        private double rawrelease = 0.0;
        private int releasebasecount = 0;

        private Stopwatch stopwatch = new();

        private List<Stopwatch> npswatches = [];

        private Stopwatch[] releasewatches = [new(), new(), new(), new(), new(), new(), new()];
       
        private ControllerInput.ControllerReader controllerInput;
        #endregion

        public MainWindowViewModel() {
            #region プロパティ関連
            Is1P.Value = Settings.Default.Is1P;
            Is2P.Value = !Settings.Default.Is1P;

            TurnTableColumn = Is1P.Select(x => x ? 0 : 2).ToReadOnlyReactiveProperty();

            Is1P.Subscribe(x =>
            {
                Settings.Default.Is1P = x;
                Settings.Default.Save();
            });

            ResetCommand.Subscribe(x =>
            {
                TotalCount.Value = 0;
                rawrelease = 0.0;
                releasebasecount = 0;
                AverageRelease.Value = 0;
            });

            for (int i = 0; i < ButtonPressed.Length; i++)
            {
                ButtonPressed[i] = new();
            }

            for (int i = 0; i < SubButtonPressed.Length; i++)
            {
                SubButtonPressed[i] = new();
            }

            #endregion

            #region コントローラー関連
            // コールバック登録
            RawGameController.RawGameControllerAdded += RawGameController_RawGameControllerAdded;
            RawGameController.RawGameControllerRemoved += RawGameController_RawGameControllerRemoved;

            // コントローラーリストに今あるのを放り込む
            for (int i = 0; i < RawGameController.RawGameControllers.Count; i++)
            {
                Controllers.Add(new(RawGameController.RawGameControllers[i]));
            }

            // フレーム間隔計測用
            stopwatch.Start();

            System.Windows.Media.CompositionTarget.Rendering += (s, e) => RenderCallback();

            controllerInput = new();

            #endregion

            #region マッピング
            Dictionary<string, int> buttons = [];
            Dictionary<string, int> axes = [];

            for (int i = 0; i < 11; i++)
            {
                buttons.Add((i >= 7 ? "E" : "B") + (i >= 7 ? i - 7 + 1: i + 1).ToString(), i >= 7 ? i - 7 + 8 : i);
            }

            axes.Add("TurntableAxis", 0);

            var mapping = new ControllerInput.ControllerMapping(buttons, axes, ControllerInput.TurntableMode.Axis);

            controllerInput.Mapping = mapping;
            #endregion
        }

        private double _previousangle = 0.0;

        private Stopwatch _turntable = new Stopwatch();

        /// <summary>
        /// 1Fごとに呼ばれる(らしい)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RenderCallback()
        {
            {
                for (int i = 0; i < npswatches.Count; i++)
                {
                    if(npswatches[i].ElapsedMilliseconds >= 1000)
                    {
                        npswatches.Remove(npswatches[i]);
                    }
                }

                NotesPerSecond.Value = npswatches.Count;

                for (int i = 0; i < 7; i++)
                {
                    Brush basecolor = (i % 2) == 0 ? Brushes.White : Brushes.DeepSkyBlue;

                    if (ButtonPressed[i].Value)
                    {
                        if (releasewatches[i].ElapsedMilliseconds >= 200)
                        {
                            GlowColor[i].Value = Brushes.Yellow;
                        }
                    }
                    else
                    {
                        GlowColor[i].Value = basecolor;
                    }
                }
            }

            if (SelectedController.Value != null)
            {
                RawGameController controller = SelectedController.Value.Controller;

                if (controller != null)
                {
                    controllerInput.Controller = controller;

                    controllerInput.Update(stopwatch.Elapsed.TotalSeconds);
                    stopwatch.Restart();

                    for (int i = 0; i < ButtonPressed.Length; i++)
                    {
                        if (controllerInput.State.Buttons.TryGetValue($"B{i + 1}", out bool buttonPressed))
                        {
                            if (ButtonPressed[i].Value != buttonPressed)
                            {
                                ButtonPressed[i].Value = buttonPressed;
                                if (buttonPressed)
                                {
                                    TotalCount.Value++;
                                    npswatches.Add(Stopwatch.StartNew());
                                    releasewatches[i].Restart();
                                }
                                else
                                {
                                    if (releasewatches[i].ElapsedMilliseconds < 200)
                                    {
                                        releasebasecount++;
                                        rawrelease += releasewatches[i].ElapsedMilliseconds;
                                        AverageRelease.Value = (int)Math.Round(rawrelease / releasebasecount);
                                    }
                                    else if (EnableCNReleaseCount.Value)
                                    {
                                        TotalCount.Value++;
                                        npswatches.Add(Stopwatch.StartNew());
                                    }
                                }
                            }
                        }
                    }

                    for (int i = 0; i < SubButtonPressed.Length; i++)
                    {
                        if (controllerInput.State.Buttons.TryGetValue($"E{i + 1}", out bool buttonPressed))
                        {
                            if (SubButtonPressed[i].Value != buttonPressed)
                            {
                                SubButtonPressed[i].Value = buttonPressed;
                            }
                        }
                    }

                    double anglediff = controllerInput.State.RawTurntableAngle - _previousangle;

                    if(Is1P.Value)
                    {
                        anglediff = -anglediff;
                    }

                    if (anglediff != 0)
                    {
                        TurnTableAngle.Value = controllerInput.State.TurntableAngle;

                        _turntable.Restart();

                        if (anglediff > 0)
                        {
                            if(TurnTableUpper.Value != true)
                            {
                                TotalCount.Value++;
                                npswatches.Add(Stopwatch.StartNew());
                            }

                            TurnTableUpper.Value = true;
                            TurnTableLower.Value = false;
                        }
                        else
                        {
                            if (TurnTableLower.Value != true)
                            {
                                TotalCount.Value++;
                                npswatches.Add(Stopwatch.StartNew());
                            }

                            TurnTableLower.Value = true;
                            TurnTableUpper.Value = false;
                        }
                    }

                    if(_turntable.ElapsedMilliseconds > 200)
                    {
                        TurnTableUpper.Value = false;
                        TurnTableLower.Value = false;
                    }

                    _previousangle = controllerInput.State.RawTurntableAngle;
                }
            }
        }

        private void RawGameController_RawGameControllerRemoved(object? sender, RawGameController e)
        {
            var controllers = Controllers.Where(x => x.Controller == e);

            foreach (var controller in controllers) Controllers.RemoveOnScheduler(controller);
        }

        private void RawGameController_RawGameControllerAdded(object? sender, RawGameController e)
        {
            Controllers.AddOnScheduler(new(e));
        }
    }
}
