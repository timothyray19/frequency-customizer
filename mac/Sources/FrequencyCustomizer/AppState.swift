import AppKit
import AVFoundation
import CoreAudio
import SwiftUI
import TuningCore

@MainActor
final class AppState: ObservableObject {
    @Published var preset: TuningPreset { didSet { settingsChanged() } }
    @Published var customHz: Double { didSet { settingsChanged() } }
    @Published var customRef: Int { didSet { settingsChanged() } }
    @Published var bypass = false { didSet { engine.bypass = bypass } }

    @Published private(set) var isRunning = false
    @Published private(set) var status = "Pick a tuning, then click Start. Everything your Mac plays will be retuned."
    @Published private(set) var statusIsError = false
    @Published private(set) var showPrivacyHint = false
    @Published private(set) var level: Float = 0
    @Published private(set) var latencyMs: Double = 0

    private let engine = RetuneEngine()
    private let defaults = UserDefaults.standard
    private var timer: Timer?
    private var restartWork: DispatchWorkItem?
    private var lastStart = Date.distantPast
    private var silentTicks = 0

    init() {
        _preset = Published(initialValue: TuningPreset(rawValue: defaults.string(forKey: "preset") ?? "") ?? .hz432)
        let hz = defaults.double(forKey: "customHz")
        _customHz = Published(initialValue: (20...20_000).contains(hz) ? hz : 432)
        let ref = defaults.integer(forKey: "customRef")
        _customRef = Published(initialValue: (36...96).contains(ref) ? ref : 69)

        engine.pitchCents = Float(target.cents)
        observeDeviceChanges()
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        NotificationCenter.default.addObserver(forName: NSApplication.willTerminateNotification,
                                               object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.engine.stop() }
        }
    }

    var target: TuningTarget {
        TuningTarget.forPreset(preset, customHz: customHz, customRef: customRef)
    }

    var headline: String {
        preset == .off440 ? "440 Hz · Off" : String(format: "%g Hz", (target.targetHz * 100).rounded() / 100)
    }

    /// Cents slider for the Custom preset; moving it changes the target Hz.
    var customCents: Double {
        get { max(-100, min(100, target.cents)) }
        set { customHz = ((TuningMath.noteFrequency(customRef) * TuningMath.ratio(fromCents: newValue)) * 100).rounded() / 100 }
    }

    func useNearestNote() { customRef = TuningMath.nearestNote(customHz) }

    private func settingsChanged() {
        engine.pitchCents = Float(target.cents)
        defaults.set(preset.rawValue, forKey: "preset")
        defaults.set(customHz, forKey: "customHz")
        defaults.set(customRef, forKey: "customRef")
    }

    // MARK: - Start / stop

    func toggle() { isRunning ? stop() : start() }

    func start() {
        do {
            try engine.start()
            lastStart = Date()
            isRunning = true
            silentTicks = 0
            showPrivacyHint = false
            setStatus("Retuning everything your Mac plays → \(engine.outputName).", error: false)
        } catch {
            isRunning = false
            setStatus("Could not start: \(error.localizedDescription)", error: true)
        }
    }

    func stop() {
        engine.stop()
        isRunning = false
        showPrivacyHint = false
        setStatus("Stopped. Your Mac's sound is back to normal.", error: false)
    }

    func openPrivacySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AudioCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    private func setStatus(_ text: String, error: Bool) {
        status = text
        statusIsError = error
    }

    private func tick() {
        level = engine.inputPeak
        latencyMs = engine.latencyMs
        guard isRunning else { return }
        silentTicks = level < 0.00001 ? silentTicks + 1 : 0
        let silent = silentTicks > 16 // ~4 s
        if silent != showPrivacyHint { showPrivacyHint = silent }
    }

    // MARK: - Follow output device changes (e.g. plugging in headphones)

    private func observeDeviceChanges() {
        var addr = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDefaultOutputDevice,
                                              mScope: kAudioObjectPropertyScopeGlobal,
                                              mElement: kAudioObjectPropertyElementMain)
        AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &addr, .main) { [weak self] _, _ in
            Task { @MainActor in self?.scheduleRestart() }
        }
        NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange,
                                               object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.scheduleRestart() }
        }
    }

    private func scheduleRestart() {
        guard isRunning else { return }
        restartWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            Task { @MainActor in
                guard let self, self.isRunning, Date().timeIntervalSince(self.lastStart) > 1.0 else { return }
                self.start()
            }
        }
        restartWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.7, execute: work)
    }
}
