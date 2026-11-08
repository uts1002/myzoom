# myzoom — 프로젝트 가이드 (CLAUDE.md)

> 이 문서는 Mitchell Hashimoto 의 하네스 엔지니어링 원칙을 따른다.
> 에이전트가 실수할 때마다 방지책을 1줄씩 누적한다 — 삭제하지 않는다.
> Constrain · Inform · Verify · Correct.

자동 생성: 2026-11-08

---

## 1. 프로젝트 개요

- **이름:** MyZoom (폴더 myzoom)
- **목적:** ZoomIt 같은 화면 확대·판서·발표 보조 도구를 엄태식 선생님 개인용으로. 연수·수업·영상 제작 때 설명용.
- **현재 단계:** 알파 (본인 PC 사용)

---

## 2. 기술 스택

- C# / .NET 9 / WPF (+ WinForms 는 트레이 아이콘·색 대화상자만, `WinForms` 별칭으로)
- Win32: RegisterHotKey(전역 단축키), BitBlt(화면 캡처), Magnification API(라이브 줌), 저수준 마우스·키보드 훅
- 매니페스트 PerMonitorV2 DPI — 모든 화면 좌표는 **물리 픽셀**, 창 안 좌표는 DIP

---

## 3. 디렉토리 구조

```
App.xaml(.cs)        트레이 상주 본체. 단축키 → 기능 연결, 기능 창 켜고 끄기
Core/                설정(AppSettings), 단축키(HotkeyManager), 화면 캡처·배치(ScreenHelper),
                     트레이(TrayIcon), 자동 실행(StartupRegistration), 자체 테스트(SelfTest, DEBUG 전용)
Features/            AnnotationWindow(줌·그리기·화면 위 그리기·영역 캡처), LiveZoomController,
                     TimerWindow, SpotlightWindow, CursorHighlightWindow, KeystrokeWindow, ToastWindow
Interop/             Native(P/Invoke), GlobalHooks(마우스·키보드 훅)
UI/                  SettingsWindow(코드로 만든 설정 창), HotkeyBox(단축키 입력 칸)
scripts/             install.ps1 / uninstall.ps1 (install.bat / uninstall.bat 이 호출)
tools/make-icon.ps1  Assets/myzoom.ico 생성
```

---

## 4. 주요 데이터 모델

- `AppSettings` — 모든 설정. `%APPDATA%\MyZoom\settings.json`. **새 항목은 기본값 필수**(예전 파일 호환).
- `HotkeySpec` — Ctrl/Alt/Shift/Win + WPF `Key` 이름 문자열.
- 단축키 목록은 `AppSettings.HotkeyActions` 한 곳에서 관리 → 설정 화면·등록·트레이 메뉴가 같이 씀.

---

## 5. 자주 사용하는 명령

```bash
dotnet build                                              # 빌드 (실행 중이면 MyZoom 종료 후)
bin/Debug/net9.0-windows/MyZoom.exe --selftest <폴더>      # 창 없이 주요 화면 PNG 렌더링 + 그리기 로직 검사
install.bat                                               # Release 빌드 후 %LOCALAPPDATA%\Programs\MyZoom 설치
```

---

## 6. 인증·권한 모델

없음. 관리자 권한 없이 동작 (자동 실행은 HKCU Run 키).

---

## 7. 디자인 토큰 / 컨벤션

- UI 문구는 쉬운 한국어. 오버레이 UI 는 어두운 반투명(#202124 계열) + 흰 글자.
- 아이콘은 Segoe Fluent Icons / Segoe MDL2 글리프, 없으면 Path 로 그림.
- 기능 창은 대부분 클릭-스루(`Native.SetClickThrough`) + 작업 표시줄 숨김(`MakeOverlayStyle`).

---

## 8. 협업 컨벤션

- **언어:** 응답·UI 한국어. 코드 식별자는 영어.
- **브랜치:** master 단일
- **커밋 메시지:** 한국어 한 줄 요약 + 필요하면 본문
- **Co-Authored-By:** `Claude Opus 4.7 (1M context) <noreply@anthropic.com>`

---

## Past Failures (auto-logged)

> 글로벌 hook 이 의미 있는 명령 (build / tsc / deploy / migration 등) 실패 시 1줄씩 자동 누적.
> 사용자는 누적된 항목을 보고 진짜 방지책 (구체적 룰) 으로 다듬는다.

- 백그라운드 프로세스에서 띄운 창은 포그라운드 권한이 없어 다른 창 뒤에 깔린다 → 두 번째 실행 시 `AllowSetForegroundWindow(-1)` 후 신호, 설정 창은 Topmost 토글로 올림.
- 사용자가 PC 를 쓰는 중에는 마우스·키 입력을 흉내 내는 UI 테스트가 서로 충돌한다 → `--selftest` 렌더링 테스트로 먼저 확인.
- WinForms 와 WPF 를 같이 쓰면 `Application`·`Image`·`Color` 등 이름이 겹친다 → csproj 에서 WinForms/Drawing 전역 using 제거, `WinForms` 별칭만 사용.
