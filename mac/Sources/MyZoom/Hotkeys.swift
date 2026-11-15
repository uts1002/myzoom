import AppKit
import Carbon.HIToolbox

/// 전역 단축키. Carbon RegisterEventHotKey 는 '손쉬운 사용' 권한 없이 동작한다.
final class Hotkeys {
    static let shared = Hotkeys()

    private var handlers: [UInt32: () -> Void] = [:]
    private var refs: [EventHotKeyRef] = []
    private var installed = false

    /// 성공하면 true. 다른 앱·시스템이 이미 쓰는 조합이면 false.
    @discardableResult
    func register(keyCode: Int, modifiers: Int, id: UInt32, handler: @escaping () -> Void) -> Bool {
        installHandlerOnce()
        var ref: EventHotKeyRef?
        let hotkeyID = EventHotKeyID(signature: OSType(0x4D5A_4F4D), id: id) // 'MZOM'
        let status = RegisterEventHotKey(UInt32(keyCode), UInt32(modifiers), hotkeyID,
                                         GetApplicationEventTarget(), 0, &ref)
        guard status == noErr, let ref else { return false }
        refs.append(ref)
        handlers[id] = handler
        return true
    }

    fileprivate func fire(_ id: UInt32) {
        handlers[id]?()
    }

    private func installHandlerOnce() {
        guard !installed else { return }
        installed = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var hk = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                              nil, MemoryLayout<EventHotKeyID>.size, nil, &hk)
            let id = hk.id
            DispatchQueue.main.async { Hotkeys.shared.fire(id) }
            return noErr
        }, 1, &spec, nil, nil)
    }
}
