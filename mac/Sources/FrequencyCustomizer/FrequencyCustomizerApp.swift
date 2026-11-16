import AppKit
import SwiftUI
import TuningCore

@main
struct FrequencyCustomizerApp: App {
    @StateObject private var state = AppState()

    var body: some Scene {
        Window("Frequency Customizer", id: "main") {
            ContentView().environmentObject(state)
        }
        .windowResizability(.contentSize)

        MenuBarExtra {
            MenuBarContent().environmentObject(state)
        } label: {
            Image(systemName: state.isRunning ? "waveform.circle.fill" : "waveform.circle")
        }
    }
}

/// Menu-bar menu: quick presets and Start/Stop, so the window can be closed while retuning.
private struct MenuBarContent: View {
    @EnvironmentObject private var state: AppState
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Text(state.isRunning ? "Retuning: \(state.headline)" : "Stopped")
        Divider()
        ForEach(TuningPreset.allCases) { preset in
            Button((state.preset == preset ? "✓ " : "    ") + "\(preset.title) (\(preset.subtitle))") {
                state.preset = preset
            }
        }
        Divider()
        Button(state.isRunning ? "Stop" : "Start") { state.toggle() }
        Toggle("Bypass", isOn: $state.bypass).disabled(!state.isRunning)
        Divider()
        Button("Show Window") {
            openWindow(id: "main")
            NSApp.activate(ignoringOtherApps: true)
        }
        Button("Quit Frequency Customizer") {
            state.stop()
            NSApp.terminate(nil)
        }
        .keyboardShortcut("q")
    }
}
