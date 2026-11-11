using System;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace EdgeMouse {
    sealed class HidMouse : IDisposable {
        GattServiceProvider hid, battery;
        bool hidStarted,batteryStarted;
        public GattLocalCharacteristic Input {get; private set;}
        readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> suspended=new System.Collections.Concurrent.ConcurrentDictionary<string,byte>();
        public bool IsSuspended(string id){return suspended.ContainsKey(id);}
        readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> buttons=new System.Collections.Concurrent.ConcurrentDictionary<string,byte>();
        public string Advertising { get { return hid.AdvertisementStatus.ToString(); } }
        public GattLocalCharacteristic KeyboardInput {get;private set;}
        readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte[]> keyState=new System.Collections.Concurrent.ConcurrentDictionary<string,byte[]>();
        public GattSubscribedClient KeyboardClient(string id){return KeyboardInput.SubscribedClients.FirstOrDefault(c=>c.Session.DeviceId.Id==id);}
        public Guid ServiceId { get { return hid.Service.Uuid; } }

        // Report 1 consists of three button bits, padding, and signed relative X, Y and wheel.
        static readonly byte[] Map = {
            0x05,1, 0x09,2, 0xa1,1, 0x85,1, 0x09,1, 0xa1,0,
            0x05,9, 0x19,1, 0x29,3, 0x15,0, 0x25,1, 0x95,3, 0x75,1, 0x81,2,
            0x95,1, 0x75,5, 0x81,1,
            0x05,1, 0x09,0x30, 0x09,0x31, 0x09,0x38, 0x15,0x81, 0x25,0x7f,
            0x75,8, 0x95,3, 0x81,6, 0xc0,0xc0,
            // Report 2: modifiers, reserved byte, six keyboard usages (USB HID keyboard).
            0x05,1, 0x09,6, 0xa1,1, 0x85,2, 0x05,7, 0x19,0xe0, 0x29,0xe7,
            0x15,0, 0x25,1, 0x75,1, 0x95,8, 0x81,2, 0x75,8, 0x95,1, 0x81,1,
            0x19,0, 0x29,0x65, 0x15,0, 0x25,0x65, 0x75,8, 0x95,6, 0x81,0, 0xc0 };
        static Guid Id(int n) { return BluetoothUuidHelper.FromShortId((uint)n); }
        static async Task<GattServiceProvider> Service(int id) {
            var r = await GattServiceProvider.CreateAsync(Id(id));
            if (r.Error != BluetoothError.Success) throw new InvalidOperationException("服务 "+id.ToString("X4")+"："+r.Error);
            return r.ServiceProvider;
        }
        static async Task<GattLocalCharacteristic> Characteristic(GattServiceProvider p, int id,
                GattCharacteristicProperties flags, byte[] value = null) {
            var args = new GattLocalCharacteristicParameters { CharacteristicProperties=flags,
                ReadProtectionLevel=GattProtectionLevel.EncryptionRequired, WriteProtectionLevel=GattProtectionLevel.EncryptionRequired };
            if(value != null) args.StaticValue = value.AsBuffer();
            var r=await p.Service.CreateCharacteristicAsync(Id(id),args);
            if(r.Error!=BluetoothError.Success) throw new InvalidOperationException("特征 "+id.ToString("X4")+"："+r.Error);
            return r.Characteristic;
        }
        public static async Task<Radio> RadioAsync() {
            var a=await BluetoothAdapter.GetDefaultAsync();
            if(a==null || !a.IsPeripheralRoleSupported) throw new InvalidOperationException("未找到支持 BLE 外设模式的蓝牙适配器。");
            return await a.GetRadioAsync();
        }
        public async Task Start() {
            var radio=await RadioAsync();
            if(radio==null || radio.State!=RadioState.On) throw new InvalidOperationException("请在 Windows 设置中打开蓝牙，然后重新启动广播。");
            hid=await Service(0x1812);
            const GattCharacteristicProperties read = GattCharacteristicProperties.Read;
            await Characteristic(hid,0x2a4a,read,new byte[]{0x11,1,0,2});
            await Characteristic(hid,0x2a4b,read,Map);
            var control=await Characteristic(hid,0x2a4c,GattCharacteristicProperties.WriteWithoutResponse);
            control.WriteRequested += async (s,e)=>{
                var d=e.GetDeferral();
                try {
                    var req=await e.GetRequestAsync();
                    if(req!=null && req.Value.Length==1) {
                        var v=DataReader.FromBuffer(req.Value).ReadByte();
                        if(v==0)suspended[e.Session.DeviceId.Id]=0;
                        else if(v==1){byte unused;suspended.TryRemove(e.Session.DeviceId.Id,out unused);}
                    }
                } catch(Exception ex) { ReportRequestFailure(ex); }
                finally { d.Complete(); }
            };
            Input=await Characteristic(hid,0x2a4d,read|GattCharacteristicProperties.Notify);
            Input.SubscribedClientsChanged += (s,e)=>{
                var live=Input.SubscribedClients.Select(c=>c.Session.DeviceId.Id).ToArray();byte unused;
                foreach(var id in suspended.Keys)if(!live.Contains(id))suspended.TryRemove(id,out unused);
                foreach(var id in buttons.Keys)if(!live.Contains(id))buttons.TryRemove(id,out unused);
            };
            Input.ReadRequested += async (s,e)=>{
                var d=e.GetDeferral();
                try { var req=await e.GetRequestAsync(); if(req!=null) {
                    byte held;buttons.TryGetValue(e.Session.DeviceId.Id,out held);byte[] value={held,0,0,0};
                    if(req.Offset>value.Length) req.RespondWithProtocolError(7);
                    else req.RespondWithValue(value.Skip((int)req.Offset).ToArray().AsBuffer());
                }} catch(Exception ex) { ReportRequestFailure(ex); }
                finally { d.Complete(); }
            };
            var desc=await Input.CreateDescriptorAsync(Id(0x2908),new GattLocalDescriptorParameters {
                ReadProtectionLevel=GattProtectionLevel.EncryptionRequired, StaticValue=new byte[]{1,1}.AsBuffer() });
            if(desc.Error!=BluetoothError.Success) throw new InvalidOperationException("报告描述符："+desc.Error);
            KeyboardInput=await Characteristic(hid,0x2a4d,read|GattCharacteristicProperties.Notify);
            KeyboardInput.SubscribedClientsChanged+=(sender,args)=>{
                var live=sender.SubscribedClients.Select(c=>c.Session.DeviceId.Id).ToArray();
                foreach(var id in keyState.Keys){byte[] old;if(!live.Contains(id))keyState.TryRemove(id,out old);}
            };
            KeyboardInput.ReadRequested+=async (sender,args)=>{
                var d=args.GetDeferral();try{
                    var req=await args.GetRequestAsync();if(req==null)return;
                    byte[] value;if(!keyState.TryGetValue(args.Session.DeviceId.Id,out value))value=new byte[8];
                    if(req.Offset>value.Length)req.RespondWithProtocolError(7);
                    else req.RespondWithValue(value.Skip((int)req.Offset).ToArray().AsBuffer());
                }catch(Exception ex){ReportRequestFailure(ex);}finally{d.Complete();}
            };
            var kdesc=await KeyboardInput.CreateDescriptorAsync(Id(0x2908),new GattLocalDescriptorParameters{
                ReadProtectionLevel=GattProtectionLevel.EncryptionRequired,StaticValue=new byte[]{2,1}.AsBuffer()});
            if(kdesc.Error!=BluetoothError.Success)throw new InvalidOperationException("键盘报告描述符："+kdesc.Error);
            battery=await Service(0x180f);
            var percent=System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent;
            byte level=(byte)(percent>=0 && percent<=1 ? Math.Round(100*percent) : 100);
            await Characteristic(battery,0x2a19,read,new byte[]{level});
            hid.StartAdvertising(new GattServiceProviderAdvertisingParameters {IsDiscoverable=true,IsConnectable=true});
            hidStarted=true;
            for(int i=0; i<30 && Advertising!="Started"; i++) await Task.Delay(100);
            if(Advertising!="Started") throw new InvalidOperationException("鼠标广播状态："+Advertising);
            // The Intel radio accepted one connectable advertiser. Expose Battery over
            // the existing connection without requesting another advertising slot.
            battery.StartAdvertising(new GattServiceProviderAdvertisingParameters {IsConnectable=false,IsDiscoverable=true});
            batteryStarted=true;
        }
        public event Action<string> RequestFailed;
        void ReportRequestFailure(Exception ex) { var f=RequestFailed; if(f!=null) f(ex.Message); }
        public async Task Send(GattSubscribedClient client,byte[] bytes) {
            bool keyboard=bytes.Length==8;
            string id=client.Session.DeviceId.Id;
            var recipient=keyboard?KeyboardClient(id):client;
            if(recipient==null)throw new InvalidOperationException("移动设备的键盘订阅已断开");
            var r=await (keyboard?KeyboardInput:Input).NotifyValueAsync(bytes.AsBuffer(),recipient);
            if(r.Status!=GattCommunicationStatus.Success) throw new InvalidOperationException(r.Status.ToString());
            if(keyboard)keyState[id]=bytes;else buttons[id]=bytes[0];
        }
        public void Dispose() {
            if(hidStarted){hid.StopAdvertising();hidStarted=false;}
            if(batteryStarted){battery.StopAdvertising();batteryStarted=false;}
        }
    }
}





