import CoreAudio
import os

/// Small stereo FIFO between the Core Audio capture thread and the AVAudioEngine render thread.
final class StereoRing: @unchecked Sendable {
    private let capacity: Int            // frames
    private let buf: UnsafeMutablePointer<Float>
    private let lock: UnsafeMutablePointer<os_unfair_lock>
    private var readPos = 0
    private var count = 0
    private var primed = false
    private var targetFrames = 1920      // 40 ms cushion at 48 kHz
    private var maxFrames = 9600         // above 200 ms we drop audio to catch up

    /// Recent input peak (0…1), for the level meter.
    private(set) var peak: Float = 0

    init(capacityFrames: Int) {
        capacity = capacityFrames
        buf = .allocate(capacity: capacityFrames * 2)
        buf.initialize(repeating: 0, count: capacityFrames * 2)
        lock = .allocate(capacity: 1)
        lock.initialize(to: os_unfair_lock())
    }

    deinit {
        buf.deallocate()
        lock.deallocate()
    }

    func configure(sampleRate: Double) {
        os_unfair_lock_lock(lock)
        targetFrames = min(Int(sampleRate * 0.04), capacity / 4)
        maxFrames = min(Int(sampleRate * 0.2), capacity / 2)
        readPos = 0; count = 0; primed = false
        os_unfair_lock_unlock(lock)
    }

    var bufferedFrames: Int {
        os_unfair_lock_lock(lock); defer { os_unfair_lock_unlock(lock) }
        return count
    }

    func clear() {
        os_unfair_lock_lock(lock)
        readPos = 0; count = 0; primed = false
        os_unfair_lock_unlock(lock)
        peak = 0
    }

    /// Capture side. Accepts interleaved or planar float buffers (mono or stereo+).
    func write(_ abl: UnsafeMutableAudioBufferListPointer) {
        guard abl.count > 0, let d0 = abl[0].mData else { return }
        let p0 = d0.assumingMemoryBound(to: Float.self)
        let ch0 = max(Int(abl[0].mNumberChannels), 1)

        let frames: Int
        let left: UnsafeMutablePointer<Float>
        let right: UnsafeMutablePointer<Float>
        let stride: Int
        if abl.count >= 2, let d1 = abl[1].mData {           // planar: one buffer per channel
            frames = Int(abl[0].mDataByteSize) / 4 / ch0
            left = p0; right = d1.assumingMemoryBound(to: Float.self); stride = ch0
        } else if ch0 >= 2 {                                   // interleaved
            frames = Int(abl[0].mDataByteSize) / 4 / ch0
            left = p0; right = p0 + 1; stride = ch0
        } else {                                               // mono
            frames = Int(abl[0].mDataByteSize) / 4
            left = p0; right = p0; stride = 1
        }
        if frames <= 0 { return }

        var pk: Float = 0
        os_unfair_lock_lock(lock)
        for i in 0..<frames {
            let a = left[i * stride], b = right[i * stride]
            pk = max(pk, abs(a), abs(b))
            let w = (readPos + count) % capacity
            buf[w * 2] = a
            buf[w * 2 + 1] = b
            if count < capacity { count += 1 } else { readPos = (readPos + 1) % capacity }
        }
        if count > maxFrames {
            readPos = (readPos + count - targetFrames) % capacity
            count = targetFrames
        }
        os_unfair_lock_unlock(lock)
        peak = max(pk, peak * 0.9)
    }

    /// Render side. Fills planar stereo output; outputs silence until enough audio is buffered.
    func read(into abl: UnsafeMutableAudioBufferListPointer, frames: Int) {
        guard abl.count > 0 else { return }
        let outL = abl[0].mData?.assumingMemoryBound(to: Float.self)
        let outR = abl.count > 1 ? abl[1].mData?.assumingMemoryBound(to: Float.self) : nil

        os_unfair_lock_lock(lock)
        if !primed && count >= targetFrames { primed = true }
        let n = primed ? min(frames, count) : 0
        for i in 0..<n {
            outL?[i] = buf[readPos * 2]
            outR?[i] = buf[readPos * 2 + 1]
            readPos = (readPos + 1) % capacity
        }
        count -= n
        if primed && n < frames { primed = false } // underrun: re-buffer
        os_unfair_lock_unlock(lock)

        if n < frames {
            for i in n..<frames {
                outL?[i] = 0
                outR?[i] = 0
            }
        }
    }
}
