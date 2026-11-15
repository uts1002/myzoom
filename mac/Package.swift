// swift-tools-version:5.9
// MyZoom 맥판 — 메뉴 막대 앱. Windows 판(C#/WPF)과 코드를 공유하지 않는 별도 구현.
// 빌드: mac/scripts/build-app.sh (GitHub Actions macOS 러너에서). 로컬 맥이 없어도 CI 로 빌드·자체 테스트한다.
import PackageDescription

let package = Package(
    name: "MyZoom",
    platforms: [.macOS(.v14)], // ScreenCaptureKit 의 SCScreenshotManager 가 14 부터
    targets: [
        .executableTarget(
            name: "MyZoom",
            path: "Sources/MyZoom"
        ),
    ]
)
