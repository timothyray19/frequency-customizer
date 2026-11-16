import AVFoundation
import CoreAudio

enum RetuneError: LocalizedError {
    case coreAudio(String, OSStatus)
    case message(String)

    var errorDescription: String? {
        switch self {
        case .coreAudio(let what, let status): return "\(what) failed (Core Audio error \(status))."
        case .message(let text): return text
        }
    }
}

private let unknownObject = AudioObjectID(kAudioObjectUnknown)
private let systemObject = AudioObjectID(kAudioObjectSystemObject)

/// Live pipeline:
///   every app's audio ─► Core Audio process tap (originals muted) ─► StereoRing
///   ─► AVAudioSourceNode ─► AVAudioUnitTimePitch (pitch in cents, speed unchanged) ─► speakers
final class RetuneEngine {
    private let engine = AVAudioEngine()
    private let timePitch = AVAudioUnitTimePitch()
    private var sourceNode: AVAudioSourceNode?
    private let ring = StereoRing(capacityFrames: 192_000)
    private let ioQueue = DispatchQueue(label: "FrequencyCustomizer.capture", qos: .userInteractive)

    private var tapID = unknownObject
    private var aggregateID = unknownObject
    private var ioProcID: AudioDeviceIOProcID?

    private(set) var isRunning = false
    private(set) var sampleRate: Double = 48_000
    private(set) var outputName = "your speakers"

    var pitchCents: Float = 0 { didSet { applyPitch() } }
    var bypass = false { didSet { applyPitch() } }

    var inputPeak: Float { ring.peak }

    /// Extra delay added by the app, in milliseconds.
    var latencyMs: Double {
        guard isRunning else { return 0 }
        return Double(ring.bufferedFrames) / sampleRate * 1000 + timePitch.latency * 1000
    }

    init() {
        engine.attach(timePitch)
        timePitch.rate = 1
        timePitch.overlap = 8
    }

    private func applyPitch() { timePitch.pitch = bypass ? 0 : pitchCents }

    func start() throws {
        stop()
        do {
            try startInternal()
            isRunning = true
        } catch {
            stop()
            throw error
        }
    }

    private func startInternal() throws {
        // 1. Start playback first: this gives our own process a Core Audio identity,
        //    which we exclude from the tap so the app never re-captures its own output.
        let outRate = engine.outputNode.outputFormat(forBus: 0).sampleRate
        try buildGraph(sampleRate: outRate > 0 ? outRate : 48_000)
        try engine.start()
        let ownProcess = try waitForOwnProcessObject()

        // 2. Tap everything else; the originals are muted at the speakers while we read them.
        let description = CATapDescription(stereoGlobalTapButExcludeProcesses: [ownProcess])
        description.name = "Frequency Customizer"
        description.uuid = UUID()
        description.isPrivate = true
        description.muteBehavior = .mutedWhenTapped
        var tap = unknownObject
        try check(AudioHardwareCreateProcessTap(description, &tap), "Creating the system audio tap")
        tapID = tap

        let tapRate = try tapSampleRate(tap)
        if tapRate > 0 && abs(tapRate - sampleRate) > 0.5 {
            engine.stop()
            try buildGraph(sampleRate: tapRate)
            try engine.start()
        }

        // 3. A private aggregate device lets us read the tap, clocked by the current output device.
        let outDevice = try defaultOutputDevice()
        let outUID = try stringProperty(outDevice, kAudioDevicePropertyDeviceUID)
        outputName = (try? stringProperty(outDevice, kAudioObjectPropertyName)) ?? "your speakers"

        let aggregate: [String: Any] = [
            kAudioAggregateDeviceNameKey: "Frequency Customizer Tap",
            kAudioAggregateDeviceUIDKey: UUID().uuidString,
            kAudioAggregateDeviceMainSubDeviceKey: outUID,
            kAudioAggregateDeviceIsPrivateKey: true,
            kAudioAggregateDeviceIsStackedKey: false,
            kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceSubDeviceListKey: [[kAudioSubDeviceUIDKey: outUID]],
            kAudioAggregateDeviceTapListKey: [[
                kAudioSubTapDriftCompensationKey: true,
                kAudioSubTapUIDKey: description.uuid.uuidString,
            ]],
        ]
        var agg = unknownObject
        try check(AudioHardwareCreateAggregateDevice(aggregate as CFDictionary, &agg), "Creating the capture device")
        aggregateID = agg

        let ring = self.ring
        var procID: AudioDeviceIOProcID?
        try check(AudioDeviceCreateIOProcIDWithBlock(&procID, agg, ioQueue) { _, inInput, _, outOutput, _ in
            ring.write(UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: inInput)))
            for buffer in UnsafeMutableAudioBufferListPointer(outOutput) {
                if let data = buffer.mData { memset(data, 0, Int(buffer.mDataByteSize)) }
            }
        }, "Starting capture")
        ioProcID = procID
        try check(AudioDeviceStart(agg, procID), "Starting capture")
    }

    func stop() {
        if aggregateID != unknownObject {
            if let proc = ioProcID {
                AudioDeviceStop(aggregateID, proc)
                AudioDeviceDestroyIOProcID(aggregateID, proc)
            }
            AudioHardwareDestroyAggregateDevice(aggregateID)
        }
        ioProcID = nil
        aggregateID = unknownObject
        if tapID != unknownObject {
            AudioHardwareDestroyProcessTap(tapID)
            tapID = unknownObject
        }
        engine.stop()
        ring.clear()
        isRunning = false
    }

    // MARK: - Graph

    private func buildGraph(sampleRate: Double) throws {
        if let old = sourceNode {
            engine.disconnectNodeOutput(old)
            engine.detach(old)
        }
        self.sampleRate = sampleRate
        ring.configure(sampleRate: sampleRate)

        guard let format = AVAudioFormat(standardFormatWithSampleRate: sampleRate, channels: 2) else {
            throw RetuneError.message("Unsupported sample rate \(sampleRate).")
        }
        let ring = self.ring
        let node = AVAudioSourceNode(format: format) { _, _, frameCount, audioBufferList in
            ring.read(into: UnsafeMutableAudioBufferListPointer(audioBufferList), frames: Int(frameCount))
            return noErr
        }
        engine.attach(node)
        engine.connect(node, to: timePitch, format: format)
        engine.connect(timePitch, to: engine.mainMixerNode, format: format)
        sourceNode = node
        engine.prepare()
    }

    // MARK: - Core Audio helpers

    private func check(_ status: OSStatus, _ what: String) throws {
        if status != noErr { throw RetuneError.coreAudio(what, status) }
    }

    private func address(_ selector: AudioObjectPropertySelector) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector,
                                   mScope: kAudioObjectPropertyScopeGlobal,
                                   mElement: kAudioObjectPropertyElementMain)
    }

    private func waitForOwnProcessObject() throws -> AudioObjectID {
        for _ in 0..<40 {
            var addr = address(kAudioHardwarePropertyTranslatePIDToProcessObject)
            var pid = ProcessInfo.processInfo.processIdentifier
            var object = unknownObject
            var size = UInt32(MemoryLayout<AudioObjectID>.size)
            let status = AudioObjectGetPropertyData(systemObject, &addr,
                                                    UInt32(MemoryLayout<pid_t>.size), &pid, &size, &object)
            if status == noErr && object != unknownObject { return object }
            Thread.sleep(forTimeInterval: 0.025)
        }
        throw RetuneError.message("Core Audio did not register the app's own output, so it could not start safely.")
    }

    private func defaultOutputDevice() throws -> AudioObjectID {
        var addr = address(kAudioHardwarePropertyDefaultOutputDevice)
        var device = unknownObject
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        try check(AudioObjectGetPropertyData(systemObject, &addr, 0, nil, &size, &device), "Finding the output device")
        if device == unknownObject { throw RetuneError.message("No audio output device found.") }
        return device
    }

    private func stringProperty(_ object: AudioObjectID, _ selector: AudioObjectPropertySelector) throws -> String {
        var addr = address(selector)
        var value: Unmanaged<CFString>?
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        try check(AudioObjectGetPropertyData(object, &addr, 0, nil, &size, &value), "Reading a device property")
        guard let string = value?.takeRetainedValue() else { throw RetuneError.message("Empty device property.") }
        return string as String
    }

    private func tapSampleRate(_ tap: AudioObjectID) throws -> Double {
        var addr = address(kAudioTapPropertyFormat)
        var asbd = AudioStreamBasicDescription()
        var size = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        try check(AudioObjectGetPropertyData(tap, &addr, 0, nil, &size, &asbd), "Reading the tap format")
        return asbd.mSampleRate
    }
}
