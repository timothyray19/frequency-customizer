import Foundation

/// Converts a target frequency into a pitch shift relative to standard A4 = 440 Hz tuning.
/// Same math as the Windows app (src/FrequencyCustomizer/Tuning/TuningMath.cs).
public enum TuningMath {
    public static let standardA4 = 440.0
    public static let a4Midi = 69

    private static let names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]

    public static func noteFrequency(_ midi: Int) -> Double {
        standardA4 * pow(2.0, Double(midi - a4Midi) / 12.0)
    }

    public static func noteName(_ midi: Int) -> String {
        names[((midi % 12) + 12) % 12] + String(midi / 12 - 1)
    }

    public static func nearestNote(_ hz: Double) -> Int {
        Int((Double(a4Midi) + 12.0 * log2(hz / standardA4)).rounded())
    }

    public static func ratio(targetHz: Double, referenceMidi: Int) -> Double {
        targetHz / noteFrequency(referenceMidi)
    }

    public static func cents(_ ratio: Double) -> Double { 1200.0 * log2(ratio) }

    public static func ratio(fromCents cents: Double) -> Double { pow(2.0, cents / 1200.0) }
}

public enum TuningPreset: String, CaseIterable, Codable, Identifiable, Sendable {
    case off440, hz432, hz528, hz639, custom

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .off440: return "440 Hz"
        case .hz432: return "432 Hz"
        case .hz528: return "528 Hz"
        case .hz639: return "639 Hz"
        case .custom: return "Custom"
        }
    }

    public var subtitle: String {
        switch self {
        case .off440: return "Off"
        case .hz432: return "A4"
        case .hz528: return "C5"
        case .hz639: return "D#5"
        case .custom: return "any Hz"
        }
    }
}

public struct TuningTarget: Equatable, Sendable {
    public let targetHz: Double
    public let referenceMidi: Int

    public init(targetHz: Double, referenceMidi: Int) {
        self.targetHz = targetHz
        self.referenceMidi = referenceMidi
    }

    public var ratio: Double { TuningMath.ratio(targetHz: targetHz, referenceMidi: referenceMidi) }
    public var cents: Double { TuningMath.cents(ratio) }
    public var referenceName: String { TuningMath.noteName(referenceMidi) }
    public var referenceHz: Double { TuningMath.noteFrequency(referenceMidi) }

    public var summary: String {
        if abs(cents) < 0.005 { return "Standard tuning (A4 = 440 Hz), no pitch change" }
        return String(format: "%@ %.2f Hz → %.2f Hz  ·  %+.1f cents  ·  %.2f%%",
                      referenceName, referenceHz, targetHz, cents, ratio * 100)
    }

    public static func forPreset(_ preset: TuningPreset, customHz: Double = 432, customRef: Int? = nil) -> TuningTarget {
        switch preset {
        case .off440: return TuningTarget(targetHz: 440, referenceMidi: 69)
        case .hz432: return TuningTarget(targetHz: 432, referenceMidi: 69)   // A4 440 → 432
        case .hz528: return TuningTarget(targetHz: 528, referenceMidi: 72)   // C5 523.25 → 528 (≈ A 444)
        case .hz639: return TuningTarget(targetHz: 639, referenceMidi: TuningMath.nearestNote(639)) // D#5
        case .custom: return TuningTarget(targetHz: customHz, referenceMidi: customRef ?? TuningMath.nearestNote(customHz))
        }
    }
}
