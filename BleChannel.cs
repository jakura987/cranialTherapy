using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace CranialTherapy;

// 一个 BLE 特征就是一个收发通道。保留原始对象，避免同 UUID 的通道混淆。
public sealed class BleChannel
{
    internal GattCharacteristic Characteristic { get; }
    public bool CanReceive => (Characteristic.CharacteristicProperties &
        (GattCharacteristicProperties.Notify | GattCharacteristicProperties.Indicate)) != 0;
    public bool CanSend => (Characteristic.CharacteristicProperties &
        (GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse)) != 0;
    public string DisplayName { get; }
    public string Details { get; }
    public string ShortId => FormatUuid(Characteristic.Uuid);

    public BleChannel(GattCharacteristic characteristic)
    {
        Characteristic = characteristic;
        DisplayName = $"{ShortId} · {characteristic.CharacteristicProperties} · 服务 {FormatUuid(characteristic.Service.Uuid)} / {characteristic.AttributeHandle}";
        Details = $"特征：{characteristic.Uuid}\n服务：{characteristic.Service.Uuid}\n句柄：{characteristic.AttributeHandle}";
    }

    private static string FormatUuid(Guid uuid)
    {
        string value = uuid.ToString();
        return value.StartsWith("0000") && value.EndsWith("-0000-1000-8000-00805f9b34fb")
            ? value.Substring(4, 4).ToUpperInvariant()
            : value;
    }
}
