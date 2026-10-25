namespace DualPulse.Core.Protocol;

// One parsed input report. BatteryStatus is the raw status byte (see DualSenseBatteryStatus.Parse);
// BluetoothSequence is the 4-bit report counter, null over USB. HeadphonesPlugged: something is in the headset jack.
public readonly record struct InputSnapshot(
	Transport Transport,
	byte LeftX,
	byte LeftY,
	byte RightX,
	byte RightY,
	byte L2,
	byte R2,
	Buttons Buttons,
	short GyroX,
	short GyroY,
	short GyroZ,
	short AccelX,
	short AccelY,
	short AccelZ,
	uint SensorTimestamp,
	TouchPoint Touch0,
	TouchPoint Touch1,
	byte BatteryStatus,
	int? BluetoothSequence,
	bool HeadphonesPlugged = false);
