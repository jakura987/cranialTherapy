using System.Collections.ObjectModel;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace CranialTherapy;

// BluetoothService 的收发部分，单独放文件方便阅读。
public sealed partial class BluetoothService
{
    private GattCharacteristic? _receiveCharacteristic;
    private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? _receiveHandler;
    private int _receiveGeneration;

    public ObservableCollection<BleChannel> ReceiveChannels { get; } = [];
    public ObservableCollection<BleChannel> SendChannels { get; } = [];
    public ObservableCollection<string> DataLog { get; } = [];
    public bool IsReceiving { get; private set; }
    public int MaxSendBytes => _session == null ? 20 : Math.Clamp(_session.MaxPduSize - 3, 20, 512);

    public async Task StartReceivingAsync(BleChannel channel)
    {
        if (_disposed || IsBusy || !IsConnected || IsReceiving || !ReceiveChannels.Contains(channel)) return;
        var device = _device;
        IsBusy = true;
        SetStatus("正在开启数据接收…");
        try
        {
            var characteristic = channel.Characteristic;
            int generation = ++_receiveGeneration;
            _receiveCharacteristic = characteristic;
            _receiveHandler = (sender, args) => OnDataReceived(sender, args, generation, channel.ShortId);
            // 先注册回调，避免设备在订阅成功的一瞬间发送数据而被漏掉。
            characteristic.ValueChanged += _receiveHandler;
            var mode = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(mode);
            if (_disposed || device != _device) return;
            if (result != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"开启通知失败（{result}）。");
            IsReceiving = true;
            SetStatus($"正在接收 {channel.ShortId} 的数据，可以在手机上发送文本或 HEX。");
        }
        catch (Exception ex)
        {
            if (!_disposed && device == _device)
            {
                DetachReceiver();
                SetStatus($"接收失败：{ex.Message}");
            }
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    public async Task StopReceivingAsync()
    {
        if (_disposed || IsBusy || !IsReceiving || _receiveCharacteristic == null) return;
        var device = _device;
        IsBusy = true;
        SetStatus("正在停止接收…");
        try
        {
            var result = await _receiveCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None);
            if (_disposed || device != _device) return;
            if (result != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"关闭通知失败（{result}），可重试或断开连接。");
            DetachReceiver();
            SetStatus("已停止接收，蓝牙仍保持连接。");
        }
        catch (Exception ex)
        {
            if (!_disposed && device == _device) SetStatus($"停止接收失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    public async Task SendAsync(BleChannel channel, string input, bool isHex)
    {
        if (_disposed || IsBusy || !IsConnected || !SendChannels.Contains(channel)) return;
        var device = _device;
        IsBusy = true;
        SetStatus("正在发送…");
        try
        {
            byte[] bytes = BleDataCodec.Encode(input, isHex, MaxSendBytes);
            var characteristic = channel.Characteristic;
            bool withResponse = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write);
            using var writer = new DataWriter();
            writer.WriteBytes(bytes);
            var result = await characteristic.WriteValueAsync(writer.DetachBuffer(), withResponse
                ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse);
            if (_disposed || device != _device) return;
            if (result != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"写入失败（{result}）。");
            AddDataLog(withResponse ? "发送" : "发送（无响应）", channel.ShortId, bytes);
            SetStatus(withResponse ? $"已写入 {bytes.Length} 字节。" : $"已提交 {bytes.Length} 字节，设备不提供写入响应。");
        }
        catch (Exception ex)
        {
            if (!_disposed && device == _device) SetStatus($"发送失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    private void OnDataReceived(GattCharacteristic sender, GattValueChangedEventArgs args, int generation, string channelId)
    {
        // 蓝牙回调在后台线程。先复制原始字节，再回到界面线程显示。
        try
        {
            using var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            RunOnUi(() =>
            {
                if (sender == _receiveCharacteristic && generation == _receiveGeneration)
                    AddDataLog("接收", channelId, bytes);
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                if (sender == _receiveCharacteristic && generation == _receiveGeneration)
                    SetStatus($"读取通知数据失败：{ex.Message}");
            });
        }
    }

    private void AddDataLog(string direction, string channel, byte[] bytes)
    {
        DataLog.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {direction} [{channel}]  {bytes.Length} 字节\n{BleDataCodec.FormatPayload(bytes)}");
        while (DataLog.Count > 500) DataLog.RemoveAt(DataLog.Count - 1);
    }

    public void ClearDataLog() => DataLog.Clear();

    private void DetachReceiver()
    {
        ++_receiveGeneration;
        if (_receiveCharacteristic != null && _receiveHandler != null)
            _receiveCharacteristic.ValueChanged -= _receiveHandler;
        _receiveCharacteristic = null;
        _receiveHandler = null;
        IsReceiving = false;
    }

    private void ClearDataConnection()
    {
        DetachReceiver();
        ReceiveChannels.Clear();
        SendChannels.Clear();
    }
}
