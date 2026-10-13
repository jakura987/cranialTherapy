using System.Collections.ObjectModel;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;

namespace CranialTherapy;

// 从窗口线程调用。Windows 蓝牙事件会回到窗口线程后再更新列表和状态。
public sealed class BluetoothService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _scanTimer;
    private BluetoothLEAdvertisementWatcher? _watcher;
    private BluetoothLEDevice? _device;
    private GattSession? _session;
    private IReadOnlyList<GattDeviceService> _services = [];
    private bool _disposed;

    public ObservableCollection<BleDeviceInfo> Devices { get; } = [];
    public bool IsScanning { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsConnected => _device != null;
    public string Status { get; private set; } = "未连接，请先扫描设备。";
    public event EventHandler? StateChanged;

    public BluetoothService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _scanTimer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Normal,
            (_, _) => StopScan(), dispatcher);
        _scanTimer.Stop();
    }

    public async Task StartScanAsync()
    {
        if (_disposed || IsBusy || IsScanning || IsConnected) return;
        IsBusy = true;
        SetStatus("正在检查蓝牙…");
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (_disposed) return;
            if (adapter == null || !adapter.IsLowEnergySupported)
                throw new InvalidOperationException("未找到支持 BLE 的蓝牙适配器。");

            var radio = await adapter.GetRadioAsync();
            if (_disposed) return;
            if (radio != null && radio.State != RadioState.On)
                throw new InvalidOperationException("蓝牙未开启，请在 Windows 设置中打开蓝牙。");

            Devices.Clear();
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            _watcher.Received += OnAdvertisementReceived;
            _watcher.Stopped += OnScanStopped;
            IsScanning = true;
            _watcher.Start();
            _scanTimer.Start();
            SetStatus("正在扫描，10 秒后自动停止；选中设备即可连接。");
        }
        catch (Exception ex)
        {
            ReleaseScan();
            SetStatus($"扫描失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    public void StopScan()
    {
        if (!IsScanning) return;
        ReleaseScan();
        SetStatus(Devices.Count == 0
            ? "未发现设备，请确认手机已开启从机广播，然后重新扫描。"
            : $"扫描结束，发现 {Devices.Count} 台设备。请选择设备并连接。");
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        RunOnUi(() =>
        {
            if (sender != _watcher || !IsScanning) return;
            var device = Devices.FirstOrDefault(d =>
                d.Address == args.BluetoothAddress && d.AddressType == args.BluetoothAddressType);
            if (device == null)
            {
                device = new BleDeviceInfo(args.BluetoothAddress, args.BluetoothAddressType);
                device.Update(args.Advertisement.LocalName, args.RawSignalStrengthInDBm);
                Devices.Add(device);
            }
            else
            {
                device.Update(args.Advertisement.LocalName, args.RawSignalStrengthInDBm);
            }
        });
    }

    private void OnScanStopped(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        RunOnUi(() =>
        {
            if (sender != _watcher) return;
            ReleaseScan();
            SetStatus(args.Error == BluetoothError.Success
                ? $"扫描结束，发现 {Devices.Count} 台设备。"
                : $"扫描已停止：{args.Error}。请检查蓝牙是否开启。");
        });
    }

    public async Task ConnectAsync(BleDeviceInfo selected)
    {
        if (_disposed || IsBusy || IsConnected) return;
        ReleaseScan();
        IsBusy = true;
        SetStatus($"正在连接 {selected.Name}…请保持设备广播，等待 Windows 返回结果。");

        // 先用局部变量持有资源，失败或窗口关闭时也能完整释放。
        BluetoothLEDevice? candidate = null;
        GattSession? session = null;
        IReadOnlyList<GattDeviceService> services = [];
        try
        {
            candidate = await BluetoothLEDevice.FromBluetoothAddressAsync(selected.Address, selected.AddressType);
            if (_disposed) return;
            if (candidate == null)
                throw new InvalidOperationException("无法访问设备，请重新扫描并确认手机正在广播。");

            // 仅创建 BluetoothLEDevice 不代表已经连接；实际发现服务后再判断结果。
            var result = await candidate.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            services = result.Services;
            if (_disposed) return;
            if (result.Status != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"设备服务访问失败（{result.Status}），请确认设备可连接。");

            session = await GattSession.FromDeviceIdAsync(candidate.BluetoothDeviceId);
            if (_disposed) return;
            if (session == null)
                throw new InvalidOperationException("无法创建蓝牙连接会话，请重试。");

            session.MaintainConnection = true;
            if (candidate.ConnectionStatus != BluetoothConnectionStatus.Connected)
                throw new InvalidOperationException("设备尚未连接或已断开，请重新连接。");

            _device = candidate;
            _session = session;
            _services = services;
            candidate = null;
            session = null;
            services = [];
            _device.ConnectionStatusChanged += OnConnectionStatusChanged;
            SetStatus($"已连接：{selected.Name}（{selected.AddressText}）");
            // 补查订阅事件前可能发生的断线。
            OnConnectionStatusChanged(_device, null!);
        }
        catch (Exception ex)
        {
            ReleaseConnection();
            SetStatus($"连接失败：{ex.Message}");
        }
        finally
        {
            if (session != null)
            {
                session.MaintainConnection = false;
                session.Dispose();
            }
            foreach (var service in services) service.Dispose();
            candidate?.Dispose();
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    public void Disconnect()
    {
        if (_disposed || IsBusy) return;
        ReleaseConnection();
        SetStatus("已断开连接。");
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        RunOnUi(() =>
        {
            if (sender != _device || sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;
            ReleaseConnection();
            SetStatus("设备连接已断开，请确认设备状态后重新连接。");
        });
    }

    private void ReleaseScan()
    {
        _scanTimer.Stop();
        if (_watcher != null)
        {
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stopped -= OnScanStopped;
            if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) _watcher.Stop();
            _watcher = null;
        }
        IsScanning = false;
    }

    private void ReleaseConnection()
    {
        if (_device != null) _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        if (_session != null)
        {
            _session.MaintainConnection = false;
            _session.Dispose();
            _session = null;
        }
        foreach (var service in _services) service.Dispose();
        _services = [];
        _device?.Dispose();
        _device = null;
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.InvokeAsync(() => { if (!_disposed) action(); });
    }

    private void SetStatus(string text)
    {
        Status = text;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        if (!_disposed) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseScan();
        ReleaseConnection();
    }
}
