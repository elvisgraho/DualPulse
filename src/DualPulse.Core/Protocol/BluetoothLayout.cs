namespace DualPulse.Core.Protocol;

// Bluetooth output report 0x31 header forms. Sequenced (Linux/SDL3): [1] = seq << 4, [2] = 0x10, common block at 3.
// Legacy (SDL2 up to 2.30): [1] = 0x02, common block at 2. Both are accepted by the controller.
public enum BluetoothLayout
{
	Sequenced,
	Legacy
}
