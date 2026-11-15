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
- **배포:** 공개 GitHub `uts1002/myzoom` + 릴리즈 (pchelper 와 같은 방식). 설치된 앱이 켜고 30초 뒤·6시간마다 `releases/latest/download/version.txt` 확인 → `MyZoom.exe` 받아 이름 바꾸기(.old)로 교체 후 재시작.
  exe 하나가 `--install`/`--uninstall` 을 처리하고 '설치된 앱'(HKCU Uninstall) 에 등록. 제작자 eompage.com 은 설정 아래 막대·일반 탭.

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

### 3.1 맥판 (`mac/`)

- Windows 판과 코드를 공유하지 않는 별도 구현: **Swift Package (Xcode 프로젝트 없음), AppKit, macOS 14+, Swift 5 언어 모드**.
- 개발 PC(Windows)에는 맥이 없다. **빌드·확인은 GitHub Actions 로만 한다**:
  `mac/` 를 push 하면 `mac-check.yml` 이 빌드 → `--selftest` 로 그리기·확대·타이머 화면 PNG 렌더링 → 15초 실행 확인.
  `gh run download <id> -n mac-selftest` 로 PNG 를 받아 눈으로 본다. 실제 맥 확인은 사용자 맥북에서.
- 전역 단축키는 Carbon `RegisterEventHotKey`(권한 불필요), 화면 캡처는 ScreenCaptureKit(화면 기록 권한).
  키는 IME 영향을 받지 않게 `keyCode`(kVK_*) 로 판단한다.
- ⌃4 라이브 줌은 직접 구현하지 않는다: 확대된 화면에서 클릭까지 되는 확대는 맥이 다른 앱에 열어 두지 않으므로,
  맥 기본 확대/축소의 ⌥⌘8 을 CGEvent 로 대신 보낸다(`LiveZoom.swift`, 손쉬운 사용 권한).
- ⌃5 스포트라이트·⌃7 커서 강조·⌃8 키 입력 표시는 `Presenter.swift` — 클릭이 통과하는 투명 창(PassThroughWindow) + 전역 이벤트 모니터.
  마우스 모니터는 권한 불필요, 키 모니터(⌃8)는 손쉬운 사용 권한. ⌃6 은 `/usr/sbin/screencapture -i -c` 를 띄운다.
- 릴리즈: 태그 하나로 `release.yml` 이 Windows exe + `MyZoom-mac.zip` + `install-mac.sh` 를 같이 올린다. 맥 빌드가 실패해도 Windows 릴리즈는 나간다.
- 앱 묶음은 `mac/scripts/build-app.sh` 가 만든다 (유니버설, ad-hoc 서명, Info.plist 의 LSUIElement 로 Dock 아이콘 없음).

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
install.bat                                               # (개발) 소스 빌드 후 MyZoom.exe --install — 개발판 0.0.0 은 자동 업데이트 안 함
.\release.ps1 1.2.0 "바뀐 내용"                          # 태그 v1.2.0 푸시 → Actions 가 릴리즈(MyZoom.exe·version.txt·MyZoom-Setup.cmd) 생성
dotnet run -c Release --project tools/uitest -- <폴더> 120  # 설치본 실제 조작 테스트: 2분 유휴 후 시작, 사람 입력 감지 시 중단
```

UI 테스트(tools/uitest)는 사용자가 자리를 비웠을 때만 돈다. 전역 단축키 외의 키·클릭은 MyZoom 창이 앞/커서 아래일 때만 보낸다.
테스트 프로세스는 반드시 DPI 인식(PerMonitorV2)으로 — 아니면 150% 모니터에서 창 크기를 축소 좌표로 읽어 오판한다.

```bash
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
- UI 테스트 프로세스가 DPI 비인식이면 GetWindowRect 가 축소 좌표(1707x960)를 줘서 정상 동작을 FAIL 로 오판했다 → SetProcessDpiAwarenessContext(-4) 먼저.
- PowerShell `Start-Process -Wait` 는 자식 프로세스까지 기다려서, 설치 후 켜지는 MyZoom 때문에 멈췄다 → `(Start-Process ... -PassThru).WaitForExit()`.
- release.ps1 에서 $ErrorActionPreference=Stop 이면 git 의 진행 메시지(stderr)를 오류로 보고 태그만 만들고 멈췄다 → Continue + $LASTEXITCODE 로 확인.
- 처음 workflow 파일을 올릴 때 gh 토큰에 workflow 권한이 없으면 push 가 거절된다 → gh auth refresh -s workflow.
- WinForms 와 WPF 를 같이 쓰면 `Application`·`Image`·`Color` 등 이름이 겹친다 → csproj 에서 WinForms/Drawing 전역 using 제거, `WinForms` 별칭만 사용.
