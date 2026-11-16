import XCTest
@testable import TuningCore

final class TuningMathTests: XCTestCase {
    func testA4Is440() {
        XCTAssertEqual(TuningMath.noteFrequency(69), 440, accuracy: 1e-9)
    }

    func testNoteNames() {
        XCTAssertEqual(TuningMath.noteName(69), "A4")
        XCTAssertEqual(TuningMath.noteName(72), "C5")
        XCTAssertEqual(TuningMath.noteName(75), "D#5")
        XCTAssertEqual(TuningMath.noteName(60), "C4")
    }

    func testPreset432() {
        let t = TuningTarget.forPreset(.hz432)
        XCTAssertEqual(t.referenceMidi, 69)
        XCTAssertEqual(t.ratio, 0.981818, accuracy: 1e-5)
        XCTAssertEqual(t.cents, -31.77, accuracy: 0.01)
    }

    func testPreset528() {
        let t = TuningTarget.forPreset(.hz528)
        XCTAssertEqual(t.referenceMidi, 72)
        XCTAssertEqual(t.cents, 15.64, accuracy: 0.01)
    }

    func testPreset639UsesNearestNote() {
        let t = TuningTarget.forPreset(.hz639)
        XCTAssertEqual(t.referenceMidi, 75)
        XCTAssertEqual(t.cents, 46.0, accuracy: 0.05)
    }

    func testOffIsUnity() {
        XCTAssertEqual(TuningTarget.forPreset(.off440).ratio, 1, accuracy: 1e-12)
    }

    func testCentsRoundTrip() {
        XCTAssertEqual(TuningMath.ratio(fromCents: TuningMath.cents(1.05)), 1.05, accuracy: 1e-12)
    }
}
