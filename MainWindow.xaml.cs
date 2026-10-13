using System.Windows;

namespace CranialTherapy;

public partial class MainWindow : Window
{
    private readonly BluetoothService _bluetooth;

    public MainWindow()
    {
        InitializeComponent();
        _bluetooth = new BluetoothService(Dispatcher);
        DeviceList.ItemsSource = _bluetooth.Devices;
        ReceiveChannelBox.ItemsSource = _bluetooth.ReceiveChannels;
        SendChannelBox.ItemsSource = _bluetooth.SendChannels;
        DataLogList.ItemsSource = _bluetooth.DataLog;

        _bluetooth.StateChanged += Bluetooth_StateChanged;
        UpdateControls();
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bluetooth.IsScanning)
            _bluetooth.StopScan();
        else
            await _bluetooth.StartScanAsync();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is BleDeviceInfo device)
            await _bluetooth.ConnectAsync(device);
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e) => _bluetooth.Disconnect();

    private async void ReceiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bluetooth.IsReceiving)
            await _bluetooth.StopReceivingAsync();
        else if (ReceiveChannelBox.SelectedItem is BleChannel channel)
            await _bluetooth.StartReceivingAsync(channel);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (SendChannelBox.SelectedItem is BleChannel channel)
            await _bluetooth.SendAsync(channel, SendInput.Text, SendFormatBox.SelectedIndex == 1);
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e) => _bluetooth.ClearDataLog();

    private void Channel_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_bluetooth != null) UpdateControls();
    }

    private void DeviceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // XAML 加载期间也可能触发事件，此时蓝牙对象尚未创建。
        if (_bluetooth != null) UpdateControls();
    }

    private void Bluetooth_StateChanged(object? sender, EventArgs e) => UpdateControls();

    private void UpdateControls()
    {
        ScanButton.Content = _bluetooth.IsScanning ? "停止扫描" : "扫描设备";
        ScanButton.IsEnabled = !_bluetooth.IsBusy && !_bluetooth.IsConnected;
        ConnectButton.IsEnabled = !_bluetooth.IsBusy && !_bluetooth.IsConnected
                                  && DeviceList.SelectedItem is BleDeviceInfo;
        DisconnectButton.IsEnabled = !_bluetooth.IsBusy && _bluetooth.IsConnected;
        DeviceList.IsEnabled = !_bluetooth.IsBusy && !_bluetooth.IsConnected;

        bool canTransfer = _bluetooth.IsConnected && !_bluetooth.IsBusy;
        ReceiveChannelBox.IsEnabled = canTransfer && !_bluetooth.IsReceiving;
        SendChannelBox.IsEnabled = canTransfer;
        ReceiveButton.Content = _bluetooth.IsReceiving ? "停止接收" : "开始接收";
        ReceiveButton.IsEnabled = canTransfer && (_bluetooth.IsReceiving || ReceiveChannelBox.SelectedItem is BleChannel);
        SendButton.IsEnabled = canTransfer && SendChannelBox.SelectedItem is BleChannel;
        SendInput.IsEnabled = canTransfer;
        SendFormatBox.IsEnabled = canTransfer;
        SendHint.Text = _bluetooth.IsConnected
            ? $"文本使用 UTF-8；单次最多 {_bluetooth.MaxSendBytes} 字节，不自动添加换行。HEX 示例：68 65 6C 6C 6F。"
            : "连接后选择通道。文本使用 UTF-8；HEX 示例：68 65 6C 6C 6F。";
        StatusText.Text = _bluetooth.Status;
    }

    protected override void OnClosed(EventArgs e)
    {
        _bluetooth.StateChanged -= Bluetooth_StateChanged;
        _bluetooth.Dispose();
        base.OnClosed(e);
    }
}
