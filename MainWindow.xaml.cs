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
        StatusText.Text = _bluetooth.Status;
    }

    protected override void OnClosed(EventArgs e)
    {
        _bluetooth.StateChanged -= Bluetooth_StateChanged;
        _bluetooth.Dispose();
        base.OnClosed(e);
    }
}
