import SwiftUI
import TuningCore

struct ContentView: View {
    @EnvironmentObject private var state: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Frequency Customizer").font(.title2.bold())
                Text("Retune everything your Mac plays from standard 440 Hz tuning.")
                    .foregroundStyle(.secondary)
            }

            GroupBox {
                VStack(alignment: .leading, spacing: 4) {
                    Text(state.headline)
                        .font(.system(size: 40, weight: .bold, design: .rounded))
                        .monospacedDigit()
                    Text(state.target.summary).foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(6)
            }

            HStack(spacing: 8) {
                ForEach(TuningPreset.allCases) { preset in
                    PresetButton(preset: preset, selected: state.preset == preset) {
                        state.preset = preset
                    }
                }
            }

            if state.preset == .custom {
                GroupBox("Custom") {
                    Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 8) {
                        GridRow {
                            Text("Target frequency").foregroundStyle(.secondary)
                            HStack {
                                TextField("Hz", value: $state.customHz,
                                          format: .number.precision(.fractionLength(0...2)))
                                    .frame(width: 100)
                                Text("Hz").foregroundStyle(.secondary)
                            }
                        }
                        GridRow {
                            Text("Reference note").foregroundStyle(.secondary)
                            HStack {
                                Picker("", selection: $state.customRef) {
                                    ForEach(36...96, id: \.self) { midi in
                                        Text(String(format: "%@  (%.2f Hz)", TuningMath.noteName(midi),
                                                    TuningMath.noteFrequency(midi))).tag(midi)
                                    }
                                }
                                .labelsHidden()
                                .frame(width: 180)
                                Button("Nearest note") { state.useNearestNote() }
                            }
                        }
                        GridRow {
                            Text("Fine-tune").foregroundStyle(.secondary)
                            HStack {
                                Slider(value: $state.customCents, in: -100...100)
                                Text(String(format: "%+.1f cents", state.target.cents))
                                    .monospacedDigit().foregroundStyle(.secondary)
                                    .frame(width: 90, alignment: .trailing)
                            }
                        }
                    }
                    .padding(6)
                }
            }

            HStack(spacing: 12) {
                Button(action: state.toggle) {
                    Label(state.isRunning ? "Stop" : "Start", systemImage: state.isRunning ? "stop.fill" : "play.fill")
                        .font(.headline)
                        .frame(width: 150, height: 30)
                }
                .buttonStyle(.borderedProminent)
                .tint(state.isRunning ? .red : .accentColor)
                .keyboardShortcut(.space, modifiers: [])

                Toggle("Bypass (hear original)", isOn: $state.bypass)
                    .toggleStyle(.button)
                    .controlSize(.large)
                    .disabled(!state.isRunning)

                Spacer()
                if state.isRunning { LevelMeter(level: state.level) }
            }

            VStack(alignment: .leading, spacing: 6) {
                Text(state.status)
                    .foregroundStyle(state.statusIsError ? .red : (state.isRunning ? .green : .secondary))
                    .fixedSize(horizontal: false, vertical: true)

                if state.showPrivacyHint {
                    HStack(alignment: .top) {
                        Text("No sound is reaching the app. Play something. If it's playing and you hear nothing, allow **Frequency Customizer** under System Settings › Privacy & Security › Screen & System Audio Recording, then click Stop and Start.")
                            .font(.callout)
                            .fixedSize(horizontal: false, vertical: true)
                        Button("Open Settings") { state.openPrivacySettings() }
                    }
                    .padding(8)
                    .background(.orange.opacity(0.15), in: RoundedRectangle(cornerRadius: 6))
                }

                if state.isRunning {
                    Text(String(format: "Added delay ≈ %.0f ms%@", state.latencyMs, state.bypass ? "  ·  BYPASSED" : ""))
                        .font(.caption).foregroundStyle(.secondary).monospacedDigit()
                }
            }
        }
        .padding(20)
        .frame(width: 560)
    }
}

private struct PresetButton: View {
    let preset: TuningPreset
    let selected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: 2) {
                Text(preset.title).font(.headline)
                Text(preset.subtitle).font(.caption)
            }
            .frame(maxWidth: .infinity, minHeight: 46)
            .foregroundStyle(selected ? Color.white : Color.primary)
            .background(selected ? Color.accentColor : Color.secondary.opacity(0.15),
                        in: RoundedRectangle(cornerRadius: 8))
            .contentShape(RoundedRectangle(cornerRadius: 8))
        }
        .buttonStyle(.plain)
    }
}

private struct LevelMeter: View {
    let level: Float

    var body: some View {
        let db = 20 * log10(max(Double(level), 0.000_01))
        let fraction = max(0, min(1, (db + 60) / 60))
        HStack(spacing: 6) {
            Image(systemName: "waveform").foregroundStyle(.secondary)
            ZStack(alignment: .leading) {
                Capsule().fill(.secondary.opacity(0.2))
                Capsule().fill(.green).frame(width: 90 * fraction)
            }
            .frame(width: 90, height: 6)
        }
        .help("Input level")
    }
}
