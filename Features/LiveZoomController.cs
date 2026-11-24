using System.Windows.Threading;
using MyZoom.Core;
using MyZoom.Interop;

namespace MyZoom.Features;

/// <summary>
/// 라이브 줌: Windows 돋보기 API(MagSetFullscreenTransform)로 움직이는 화면을 그대로 확대한다.
/// 오프셋 잡는 방식은 설정으로 고른다 — 가운데 고정(포인터가 화면 가운데, 확대 화면이 따라다님,
/// 화면 끝에선 멈춤) 또는 비례 이동(커서가 화면에서 같은 자리에 머묾). 어느 쪽이든 보이는 곳을
/// 그대로 클릭할 수 있다. 휠(설정에 따라 Ctrl+휠)로 배율 조절.
/// </summary>
public sealed class LiveZoomController
{
    readonly Func<AppSettings> _settings;
    readonly DispatcherTimer _timer;
    double _scale = 1, _target = 1;
    bool _stopping;
    int _offX, _offY;

    public bool IsActive { get; private set; }
    public event Action<string>? Notify;

    public LiveZoomController(Func<AppSettings> settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Toggle()
    {
        if (IsActive && !_stopping) Stop();
        else Start();
    }

    public void Start()
    {
        var s = _settings();
        if (!IsActive)
        {
            if (!Native.MagInitialize())
            {
                Notify?.Invoke("라이브 줌을 시작하지 못했습니다 (Windows 돋보기를 쓸 수 없음)");
                return;
            }
            IsActive = true;
            _scale = 1;
            GlobalMouseHook.Acquire();
            GlobalMouseHook.Wheel = OnWheel;
            _timer.Start();
        }
        _stopping = false;
        _target = Math.Clamp(s.LiveZoomInitial, 1.1, s.MaxZoom);
        if (!s.AnimateZoom) _scale = _target;
    }

    public void Stop()
    {
        if (!IsActive) return;
        _stopping = true;
        _target = 1;
        if (!_settings().AnimateZoom) Finish();
    }

    /// <summary>애니메이션 없이 즉시 끝낸다. (그리기 전환·프로그램 종료 시)</summary>
    public void StopImmediately()
    {
        if (IsActive) Finish();
    }

    /// <summary>현재 확대 상태 (그리기 모드로 넘길 때 사용). 좌표는 물리 픽셀.</summary>
    public (double Scale, int OffsetX, int OffsetY) Current => (_scale, _offX, _offY);

    bool OnWheel(int delta, bool ctrl)
    {
        if (!IsActive || _stopping) return false;
        var s = _settings();
        // 기본: 휠만으로 확대·축소 (라이브 줌 중엔 페이지 스크롤 대신 배율이 바뀜).
        // 설정에서 끄면 Ctrl+휠일 때만 배율 조절, 그냥 휠은 아래 프로그램 스크롤.
        if (!ctrl && !s.LiveZoomPlainWheel) return false;
        _target = Math.Clamp(_target * Math.Pow(1.2, delta / 120.0), 1.0, s.MaxZoom);
        if (!s.AnimateZoom) _scale = _target;
        if (_target <= 1.0001) Stop(); // 끝까지 축소하면 라이브 줌 종료 (휠이 다시 스크롤로 돌아옴)
        return true; // 아래 프로그램(브라우저 확대 등)에는 전달하지 않음
    }

    void Tick()
    {
        if (Math.Abs(_target - _scale) > 0.001)
        {
            _scale += (_target - _scale) * 0.2;
            if (Math.Abs(_target - _scale) < 0.003) _scale = _target;
        }

        Native.GetCursorPos(out var p);
        if (_settings().LiveZoomCenterCursor)
        {
            // 포인터가 확대 화면 가운데에 오도록: off = p - (보이는 폭)/2, 화면 밖이 보이지 않게 끝에서 멈춤
            int w = Native.GetSystemMetrics(0), h = Native.GetSystemMetrics(1);
            double vw = w / _scale, vh = h / _scale;
            _offX = (int)Math.Round(Math.Clamp(p.X - vw / 2, 0, w - vw));
            _offY = (int)Math.Round(Math.Clamp(p.Y - vh / 2, 0, h - vh));
        }
        else
        {
            // 커서 위치가 확대 후에도 같은 화면 위치에 오도록: off = p * (1 - 1/s)
            double k = 1 - 1 / _scale;
            _offX = (int)Math.Round(p.X * k);
            _offY = (int)Math.Round(p.Y * k);
        }
        Native.MagSetFullscreenTransform((float)_scale, _offX, _offY);

        if (_stopping && _scale <= 1.001) Finish();
    }

    void Finish()
    {
        _timer.Stop();
        Native.MagSetFullscreenTransform(1f, 0, 0);
        Native.MagUninitialize();
        if (GlobalMouseHook.Wheel == OnWheel) GlobalMouseHook.Wheel = null;
        GlobalMouseHook.Release();
        IsActive = false;
        _stopping = false;
        _scale = _target = 1;
    }
}
