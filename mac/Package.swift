// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "FrequencyCustomizer",
    // Core Audio process taps (system-wide capture without a driver) need macOS 14.2.
    platforms: [.macOS("14.2")],
    targets: [
        .target(name: "TuningCore"),
        .executableTarget(name: "FrequencyCustomizer", dependencies: ["TuningCore"]),
        .testTarget(name: "TuningCoreTests", dependencies: ["TuningCore"]),
    ]
)
