using System.ComponentModel;
using Windows.Devices.Bluetooth;

namespace CranialTherapy;

// 列表中的一台设备。属性变化时通知界面刷新，不替换用户选中的那一行。
public sealed class BleDeviceInfo : INotifyPropertyChanged
{
    public ulong Address { get; }
    public BluetoothAddressType AddressType { get; }
    public string Name { get; private set; } = "未命名设备";
    public short Rssi { get; private set; }
    public string AddressText { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BleDeviceInfo(ulong address, BluetoothAddressType addressType)
    {
        Address = address;
        AddressType = addressType;
        string hex = address.ToString("X12");
        AddressText = string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    public void Update(string name, short rssi)
    {
        // 有些广播包不含名称，保留此前已经收到的名称。
        if (!string.IsNullOrWhiteSpace(name) && Name != name)
        {
            Name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
        if (Rssi != rssi)
        {
            Rssi = rssi;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rssi)));
        }
    }
}
