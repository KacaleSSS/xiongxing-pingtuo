using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;

namespace EdgeMouse {
    sealed class MainWindow : Form {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window,int command);
        readonly Label info=new Label { AutoSize=false, Dock=DockStyle.Fill };
        readonly Button arm=new SoftButton {Text="启用边缘切换",Enabled=false,AutoSize=true};
        readonly Button start=new SoftButton {Text="启动服务",AutoSize=true};
        readonly CheckBox autoReturn=new SoftSwitch {Text="边缘返回",Checked=true,AutoSize=true};
        readonly Button calibrate=new SoftButton {Text="校准移动设备",AutoSize=true,Enabled=false};
        readonly DesktopPeers desktopPeers=new DesktopPeers();
        readonly Timer tick=new Timer {Interval=16};
        readonly CaptureWindow capture=new CaptureWindow();
        readonly ReportBuffer buffer=new ReportBuffer();
        HidMouse mouse;
        GattSubscribedClient target;
        LeftEdgeReturn edge;
        MobilePath mobilePath;
        DeviceLayout layout=DeviceLayout.Load();
        int width=LeftEdgeReturn.DefaultWidth, padWidth=LeftEdgeReturn.DefaultWidth;
        bool controllingPad,keyboardAvailable;
        bool calibrateNext;
        byte physicalButtons;
        const string SettingsKey=@"HKEY_CURRENT_USER\Software\iPadEdgeMouse";
        bool ready,armed,sending,closing,neutralPending;
        Point previous;
        string lastEntry="";
        DateTime nextStatus=DateTime.MinValue;
        string last="打开 Windows 蓝牙，然后启动广播以连接 iPhone 或 iPad。";
        public MainWindow() {
            Text="雄兴屏拓 · ZZXX CloudTech";AutoScaleMode=AutoScaleMode.Dpi;BrandUi.Window(this);
            ClientSize=new Size(980,590);MinimumSize=new Size(800,550);StartPosition=FormStartPosition.CenterScreen;
            object saved=Registry.GetValue(SettingsKey,"Width",null);
            if(saved is int && (int)saved>=100 && (int)saved<=LeftEdgeReturn.MaxWidth) width=(int)saved;
            var savedPad=Registry.GetValue(SettingsKey,"PadWidth",null);
            if(savedPad is int && (int)savedPad>=100 && (int)savedPad<=LeftEdgeReturn.MaxWidth)padWidth=(int)savedPad;
            var stop=new SoftButton{Text="关闭服务"};var arrange=new SoftButton{Text="屏幕布局"};
            var toggle=new SoftButton();
            var drawer=new OptionDrawer(toggle);
            BrandUi.ButtonStyle(start,true);BrandUi.ButtonStyle(stop);BrandUi.ButtonStyle(arm);BrandUi.ButtonStyle(calibrate);BrandUi.ButtonStyle(arrange);
            var files=new SoftButton{Text="文件共享"};BrandUi.ButtonStyle(files);
            files.Click+=(s,e)=>{using(var window=new FileShareWindow())window.ShowDialog(this);};
            drawer.AddOptions(start,stop,arrange,files,arm,autoReturn,calibrate);
            stop.Click+=async (s,e)=>{
                stop.Enabled=false;start.Enabled=false;tick.Stop();Disarm("服务已关闭");desktopPeers.StopDiscovery();
                while(sending && !closing)await Task.Delay(20);
                await desktopPeers.WaitStopped();if(closing)return;
                if(mouse!=null)mouse.Dispose();mouse=null;ready=false;target=null;arm.Enabled=false;calibrate.Enabled=false;
                info.Text="手机与平板服务已关闭";start.Enabled=true;
            };
            arrange.Click+=async (s,e)=>{
                bool wasArmed=armed;armed=false;
                try {using(var editor=new LayoutEditor(layout,await MobileChoices())){
                    if(editor.ShowDialog(this)==DialogResult.OK){layout=editor.Result;layout.WriteDesktopEdges();desktopPeers.LayoutChanged();}
                }}finally{armed=wasArmed && ready && mouse.Input.SubscribedClients.Count>0;previous=Cursor.Position;}
            };
            var content=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12,0,22,10),ColumnCount=1,RowCount=2};
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute,152));content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var computerCard=BrandUi.Card("电脑连接",desktopPeers);
            content.Controls.Add(computerCard,0,0);
            // The status can wrap after discovery or a DPI change. Size the card to its actual last control.
            Action sizeComputerCard=()=>{
                int childrenBottom=desktopPeers.Controls.Cast<Control>().Max(c=>c.Bottom+c.Margin.Bottom);
                int height=computerCard.Margin.Vertical+computerCard.Padding.Bottom+desktopPeers.Top+childrenBottom;
                if(content.RowStyles[0].Height!=height)content.RowStyles[0].Height=height;
            };
            foreach(Control child in desktopPeers.Controls){
                child.LocationChanged+=(s,e)=>sizeComputerCard();child.SizeChanged+=(s,e)=>sizeComputerCard();
            }
            desktopPeers.LocationChanged+=(s,e)=>sizeComputerCard();
            Load+=(s,e)=>sizeComputerCard();
            content.Controls.Add(BrandUi.Card("手机与平板",info),0,1);
            Controls.Add(content);Controls.Add(drawer);Controls.Add(BrandUi.Header("雄兴屏拓",toggle));
            desktopPeers.IncomingWindows+=(side,slot)=>{
                if(!new[]{"left","right","top","bottom"}.Contains(side))return;
                if(layout.PC==null || layout.PC.Side!=side)layout.PC=DeviceLayout.DefaultDock(side);
                layout.PC.Slot=slot;layout.Save();layout.WriteDesktopEdges();
            };
            Shown+=(s,e)=>{layout.WriteDesktopEdges();desktopPeers.StartDiscovery();};
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplaysChanged;
            info.Padding=new Padding(0,6,0,0);info.ForeColor=BrandUi.Muted; info.Text=last;
            start.Click+=async (s,e)=>{stop.Enabled=false;desktopPeers.StartDiscovery();try{await Start();}finally{if(!closing)stop.Enabled=true;}};
            autoReturn.CheckedChanged+=(s,e)=>{calibrateNext=false;};
            calibrate.Click+=(s,e)=>{
                calibrateNext=true;
                last="校准：推入手机，等指针对齐后，移到手机对边，停稳并按 F8 保存。";RefreshStatus();
            };
            arm.Click+=(s,e)=>{
                if(armed) {Disarm("已停用边缘切换");return;}
                var clients=mouse.Input.SubscribedClients;
                if(!clients.Any(c=>CanRoute(c,clients.Count))) {Disarm("请在屏幕布局中指定在线的 iPhone 和 iPad");return;}
                target=null;armed=true;previous=Cursor.Position;last="已启用，向布局中的移动设备移动鼠标";
                arm.Text="暂停切换";RefreshStatus();
            };
            capture.Report+=(b,x,y,w)=>{
                physicalButtons=b;
                if(buffer.Count>96) {Disarm("输入积压，已返回电脑");return;}
                buffer.Add(b,x,y,w); Pump();
            };
            capture.KeysChanged+=bytes=>{
                if(!capture.Active || target==null)return;
                if(!keyboardAvailable){last="此设备尚未订阅蓝牙键盘";return;}
                if(buffer.Count>96){Disarm("键鼠输入积压，已返回电脑");return;}
                buffer.AddKeys(bytes);if(!sending)BeginInvoke((Action)Pump);
            };
            capture.Return+=reason=>{
                if(edge!=null && edge.Calibrating && reason==CaptureWindow.HotkeyReturn){
                    int measured=edge.Position;
                    bool settled=!sending && buffer.Count==0;
                    ReturnLocal(reason);
                    if(settled && measured>=100 && measured<=LeftEdgeReturn.MaxWidth){
                        if(controllingPad)padWidth=measured;else width=measured;
                        try{Registry.SetValue(SettingsKey,controllingPad?"PadWidth":"Width",measured);last="屏宽校准已保存。";}
                        catch(UnauthorizedAccessException){last="屏宽校准仅在本次运行生效。";}
                    }else last="未取得有效屏宽。请重新校准，到右侧后停稳再按 F8。";
                }else ReturnLocal(reason);
            };
            tick.Tick+=(s,e)=>Tick();
            FormClosing+=(s,e)=>{
                closing=true; tick.Stop(); capture.LeaveRemote();
                if(target!=null && mouse!=null && !neutralPending){
                    // Keep GATT alive through the final button release, with a bounded wait.
                    e.Cancel=true;FinishClose();
                }
            };
            FormClosed+=(s,e)=>{Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplaysChanged;desktopPeers.StopDiscovery();tick.Dispose();capture.Dispose();if(mouse!=null) mouse.Dispose();};
        }
        static readonly string[] SlotNames={"left","right","top","bottom"};
        internal static int DesktopCoordinate(Rectangle tile,string side,Point entry){
            bool horizontal=side=="left" || side=="right";
            double f=horizontal?(entry.Y-tile.Top)/(double)tile.Height:(entry.X-tile.Left)/(double)tile.Width;
            return (6+Array.IndexOf(SlotNames,side))*1000000+Math.Max(0,Math.Min(999999,(int)Math.Round(f*1000000)));
        }
        bool ToDesktop(Point point,string direction){
            foreach(int k in new[]{1,3}){
                var d=k==1?(layout.Mac??DeviceLayout.DefaultDock("left")):layout.PC;
                var tile=layout.RemoteRect(k);if(d==null || !tile.Contains(point))continue;
                return desktopPeers.EnterDesktop(Array.IndexOf(SlotNames,d.Slot??d.Side),DesktopCoordinate(tile,MobilePath.Opposite(direction),point));
            }
            return false;
        }
        protected override void WndProc(ref Message m){
            if(m.Msg==0x8156 && !closing){
                int slot=m.WParam.ToInt32(),code=m.LParam.ToInt32();
                if(slot>=0 && slot<4 && code>=2000000 && code<6000000)
                    DesktopExit(SlotNames[slot],SlotNames[code/1000000-2],code%1000000);
            }
            base.WndProc(ref m);
        }
        void DesktopExit(string slot,string direction,int fraction){
            int source=slot==(layout.Mac==null?"left":layout.Mac.Side)?1:3;
            var tile=layout.RemoteRect(source);if(tile.IsEmpty)return;
            bool lateral=direction=="right"||direction=="left";
            // Received fractions are rounded to one millionth by the peer.
            int along=lateral?tile.Top+Math.Min(tile.Height-1,(int)Math.Round(tile.Height*fraction/1000000.0)):
                tile.Left+Math.Min(tile.Width-1,(int)Math.Round(tile.Width*fraction/1000000.0));
            var point=lateral?new Point(direction=="right"?tile.Right:tile.Left-1,along):
                new Point(along,direction=="bottom"?tile.Bottom:tile.Top-1);
            var screen=Screen.AllScreens.FirstOrDefault(v=>v.Bounds.Contains(point));
            if(screen!=null){
                var b=screen.Bounds;point.Offset(direction=="left"?-4:direction=="right"?4:0,direction=="top"?-4:direction=="bottom"?4:0);
                previous=Cursor.Position=new Point(Math.Max(b.Left,Math.Min(b.Right-1,point.X)),Math.Max(b.Top,Math.Min(b.Bottom-1,point.Y)));return;
            }
            if(ToDesktop(point,direction))return;
            if(!ready || !armed || capture.Active || sending)return;
            bool pad=layout.MobileRect(true).Contains(point);var mobile=layout.MobileRect(pad);if(!mobile.Contains(point))return;
            var clients=mouse.Input.SubscribedClients;
            var client=clients.FirstOrDefault(c=>CanRoute(c,clients.Count) && (c.Session.DeviceId.Id==layout.PadId)==pad);
            if(client==null)return;
            int span=pad?padWidth:width;
            try{
                capture.EnterRemote(Screen.FromPoint(Cursor.Position).Bounds,Cursor.Position,direction);
                controllingPad=pad;target=client;physicalButtons=0;keyboardAvailable=mouse.KeyboardClient(target.Session.DeviceId.Id)!=null;
                mobilePath=new MobilePath(mobile,direction,point,span);edge=new LeftEdgeReturn{Width=span,Side=direction};
                edge.Begin(false);edge.AlignSharedEdge(mobile,point);Pump();
            }catch(InvalidOperationException ex){Disarm(ex.Message);}
        }
        void DisplaysChanged(object s,EventArgs e){if(!closing && IsHandleCreated)BeginInvoke((Action)(()=>layout.WriteDesktopEdges()));}
        internal static bool MatchesMobile(string id,int count,string phone,string pad){
            return id==phone || id==pad || (count==1 && phone==null && pad==null);
        }
        bool CanRoute(GattSubscribedClient c,int count){
            return c.Session.SessionStatus==GattSessionStatus.Active && !mouse.IsSuspended(c.Session.DeviceId.Id) &&
                MatchesMobile(c.Session.DeviceId.Id,count,layout.PhoneId,layout.PadId);
        }
        async Task<LayoutEditor.Mobile[]> MobileChoices(){
            if(!ready)return new LayoutEditor.Mobile[0];
            var clients=mouse.Input.SubscribedClients.ToArray();
            return await Task.WhenAll(clients.Select(async c=>{
                string id=c.Session.DeviceId.Id;string name=null;
                try{using(var device=await Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(id)){
                    if(device!=null)name=device.Name;
                }}catch(System.Runtime.InteropServices.COMException){/* A disconnected device retains its ID. */}
                catch(ArgumentException){/* Some BLE stacks expose only the session identifier. */}
                catch(UnauthorizedAccessException){/* Friendly name unavailable under device privacy settings. */}
                return new LayoutEditor.Mobile{Id=id,Name=(name??"蓝牙设备")+" · "+id.Substring(Math.Max(0,id.Length-8))};
            }));
        }
        async void FinishClose(){
            neutralPending=true;QueueRelease();Pump();
            var end=DateTime.UtcNow.AddMilliseconds(1200);
            while(sending && DateTime.UtcNow<end) await Task.Delay(15);
            Close();
        }
        async Task Start() {
            start.Enabled=false; var h=new HidMouse();
            h.RequestFailed+=msg=>{ if(!closing && IsHandleCreated) BeginInvoke((Action)(()=>{if(mouse==h)Disarm("GATT 请求失败："+msg);})); };
            try{
                await h.Start();
                if(closing){h.Dispose();return;}
                mouse=h; ready=true;
                last="iPhone：打开辅助触控，在「设备 → 蓝牙设备」中选择电脑。\r\niPad：在「设置 → 蓝牙」中选择电脑。";
                tick.Start();RefreshStatus();
            }catch(Exception ex){
                h.Dispose();if(closing)return;
                info.Text=ex.Message;start.Enabled=true;
            }
        }
        void RefreshStatus(){
            var clients=mouse.Input.SubscribedClients;
            keyboardAvailable=target!=null && mouse.KeyboardClient(target.Session.DeviceId.Id)!=null;
            if(armed && (clients.Count==0 || (capture.Active && !clients.Any(c=>c.Session.DeviceId.Id==target.Session.DeviceId.Id && CanRoute(c,clients.Count))))) {
                Disarm("连接或订阅发生变化，已把鼠标交还电脑");
            }
            arm.Enabled=armed || (!sending && clients.Any(c=>CanRoute(c,clients.Count)));
            calibrate.Enabled=autoReturn.Checked && armed && !capture.Active && !calibrateNext;
            info.Text="电脑："+Environment.MachineName+"    HID 广播："+mouse.Advertising+
                "\r\n鼠标订阅："+clients.Count+"    键盘订阅："+mouse.KeyboardInput.SubscribedClients.Count+lastEntry+
                "\r\n\r\n"+last+"\r\n"+(autoReturn.Checked?"向连接电脑的一侧轻推返回。":"")+"F8 可随时返回。";
        }
        void Tick(){
            if(!ready || closing)return;
            try{
                if(DateTime.UtcNow>=nextStatus){nextStatus=DateTime.UtcNow.AddMilliseconds(200);RefreshStatus();}
                if(!armed || capture.Active || sending || buffer.Count>0) return;
                Point p=Cursor.Position;var display=Screen.FromPoint(p);Rectangle bounds=display.Bounds;
                var clients=mouse.Input.SubscribedClients;
                if(MouseButtons==MouseButtons.None && (p!=previous)){
                    foreach(var client in clients){
                    if(!CanRoute(client,clients.Count))continue;
                    bool pad=client.Session.DeviceId.Id==layout.PadId;

                    var mobile=layout.MobileRect(pad);string side=DeviceLayout.Touching(bounds,mobile);
                    if(side==null)continue;
                    var next=p;next.Offset(side=="left"?-1:side=="right"?1:0,side=="top"?-1:side=="bottom"?1:0);
                    bool outward=side=="left"?p.X<previous.X:side=="right"?p.X>previous.X:side=="top"?p.Y<previous.Y:p.Y>previous.Y;
                    if(!bounds.Contains(previous) || !outward || !mobile.Contains(next) || MobileKeyboard.AnyDesktopKeyDown())continue;
                    controllingPad=pad;target=client;keyboardAvailable=mouse.KeyboardClient(target.Session.DeviceId.Id)!=null;
                    capture.EnterRemote(bounds,p,side);
                    lastEntry="    最近切入："+Screen.FromPoint(p).DeviceName+" ("+p.X+", "+p.Y+")";
                    physicalButtons=0;
                    {
                        mobilePath=new MobilePath(layout.MobileRect(pad),side,p,pad?padWidth:width);
                        edge=new LeftEdgeReturn {Width=pad?padWidth:width,Side=side};edge.Begin(calibrateNext);
                        if(!calibrateNext)edge.AlignSharedEdge(layout.MobileRect(pad),p);
                        last=calibrateNext?"校准：匀速移到手机对边，停稳并按 F8。":"正在控制移动设备";
                        calibrateNext=false;Pump();
                    }
                    RefreshStatus();break;
                    }
                }
                previous=p;
            }catch(Exception ex){Disarm("已停用切换："+ex.Message);}
        }
        void QueueRelease(){
            buffer.Clear();buffer.Add(0,0,0,0);
            if(target!=null && mouse.KeyboardClient(target.Session.DeviceId.Id)!=null)buffer.AddKeys(new byte[8]);
        }
        void ReturnLocal(string reason,bool reposition=true){
            edge=null;
            if(capture.Active){
                capture.LeaveRemote(reposition);previous=Cursor.Position;
                if(target!=null){QueueRelease();Pump();}else buffer.Clear();
            }
            last=reason;
        }
        void Disarm(string reason){
            calibrateNext=false;
            armed=false;ReturnLocal(reason);arm.Text="启用边缘切换";last=reason;
            if(!sending){buffer.Clear();target=null;}
        }
        async void Pump(){
            if(sending || target==null)return;
            sending=true;
            try{
                while(true){
                    var activeEdge=capture.Active ? edge : null;
                    var home=activeEdge==null ? null : activeEdge.NextHomeReport();
                    if(home==null && buffer.Count==0) break;
                    byte[] bytes=home ?? buffer.Next();
                    var send=mouse.Send(target,bytes);
                    if(await Task.WhenAny(send,Task.Delay(1200))!=send) {
                        // Windows may still complete the pending notification. No new peer is armed until it settles.
                        armed=false;edge=null;capture.LeaveRemote();arm.Text="启用边缘切换";last="蓝牙发送超时，已返回电脑";
                        buffer.Clear();if(bytes.Any(v=>v!=0))QueueRelease();
                    }
                    await send;
                    if(bytes.Length==4 && activeEdge!=null && activeEdge==edge && capture.Active && home==null){
                        activeEdge.Delivered(bytes);
                        Point outside;string direction=mobilePath.Advance(bytes,out outside);
                        if(activeEdge.Calibrating || direction==null || physicalButtons!=0 || capture.Keyboard.AnyHeld ||
                            !buffer.OnlyReturnMotion(MobilePath.Opposite(direction)))continue;
                        var desktop=Screen.AllScreens.FirstOrDefault(s=>s.Bounds.Contains(outside));
                        if(desktop!=null){
                            if(!autoReturn.Checked)continue;
                            capture.SetReturnPoint(desktop.Bounds,outside,MobilePath.Opposite(direction));
                            ReturnLocal("已返回对应的 Windows 屏幕");continue;
                        }
                        if(ToDesktop(outside,direction)){ReturnLocal("已切入相邻电脑",false);continue;}
                        bool pad=!controllingPad;
                        var neighbour=layout.MobileRect(pad);
                        if(!neighbour.Contains(outside))continue;
                        var id=pad?layout.PadId:layout.PhoneId;
                        var next=mouse.Input.SubscribedClients.FirstOrDefault(c=>c.Session.DeviceId.Id==id && CanRoute(c,2));
                        if(next==null)continue;
                        // A clean motion report with all buttons released was acknowledged on the old host.
                        // Pending free motion belongs to the old host, never replay it on the new one.
                        buffer.Clear();target=next;controllingPad=pad;keyboardAvailable=mouse.KeyboardClient(target.Session.DeviceId.Id)!=null;
                        int span=pad?padWidth:width;
                        edge=new LeftEdgeReturn{Width=span,Side=direction};edge.Begin(false);edge.AlignSharedEdge(neighbour,outside);
                        mobilePath=new MobilePath(neighbour,direction,outside,span);
                        last=(pad?"正在控制 iPad":"正在控制 iPhone")+(keyboardAvailable?" · 键鼠":" · 仅鼠标");
                    }
                }
            }catch(Exception ex){buffer.Clear();armed=false;edge=null;capture.LeaveRemote();arm.Text="启用边缘切换";last="蓝牙发送失败："+ex.Message;}
            finally{sending=false;if(!armed)target=null;}
        }
    }
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,uint msg,IntPtr w,IntPtr l);
        [STAThread] static int Main(string[] args){
            if(args.Length==2 && args[0]=="--file-share")return FileSharing.RunElevated(args[1]);
            if(args.Length==1 && args[0]=="--check") {
                try{
                    WindowsFormsSynchronizationContext.AutoInstall=false;
                    CheckInputRegistration();
                    Check().GetAwaiter().GetResult();return 0;
                }
                catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            }
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow());return 0;
        }
        static void Verify(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
        static void CheckInputRegistration(){
            // Exercise the real window message path without moving or confining the user's pointer.
            using(var window=new CaptureWindow()){
                int returns=0;
                window.Return+=reason=>{returns++;window.LeaveRemote();};
                for(int i=0;i<2;i++){
                    window.RegisterInput();
                    SendMessage(window.Handle,0x312,new IntPtr(1),new IntPtr((int)Keys.F8<<16));
                }
                Verify(returns==2,"raw input and F8 registration, return message, release and re-registration");
            }
        }
        static async Task Check(){
            var e=new LeftEdgeReturn {Width=100};
            e.Begin(false);
            Verify(e.NextHomeReport().SequenceEqual(new byte[]{0,129,0,0}) &&
                e.NextHomeReport().SequenceEqual(new byte[]{0,233,0,0}) && e.NextHomeReport()==null,
                "entry alignment uses legal ordered relative reports");
            e.Delivered(new byte[]{0,120,0,0});
            Verify(e.Position==100 && !e.Delivered(new byte[]{0,156,0,0}) && !e.Delivered(new byte[]{0,230,0,0}) &&
                e.Delivered(new byte[]{0,236,0,0}),"right clamp, left edge and deliberate overshoot");
            e.Begin(false);
            Verify(!e.Delivered(new byte[]{1,170,0,0}) && !e.Delivered(new byte[]{0,170,0,0}) &&
                !e.Delivered(new byte[]{0,170,0,1}),"drag, release and wheel cannot trigger return");
            e.Begin(true);e.Delivered(new byte[]{0,110,0,0});
            Verify(e.Position==110 && !e.Delivered(new byte[]{0,130,0,0}),"calibration measures full travel without automatic return");
            var queue=new ReportBuffer();
            queue.Add(0,140,-130,1);queue.Add(0,0,0,1);queue.Add(1,0,0,0);queue.Add(0,0,0,0);
            Verify(queue.Next().SequenceEqual(new byte[]{0,127,129,2}) &&
                queue.Next().SequenceEqual(new byte[]{0,13,253,0}) &&
                queue.Next()[0]==1 && queue.Next()[0]==0 && queue.Count==0,"HID report packing and button transitions");
            queue.Add(0,800,400,0);queue.Add(0,470,235,0);
            queue.Add(1,254,0,0);queue.Add(0,0,0,0);
            Verify(queue.Next().SequenceEqual(new byte[]{0,127,64,0}) &&
                queue.Next().SequenceEqual(new byte[]{1,127,0,0}) &&
                queue.Next().SequenceEqual(new byte[]{1,127,0,0}) &&
                queue.Next().SequenceEqual(new byte[]{0,0,0,0}) && queue.Count==0,
                "free movement burst has one pending report; drag and release retain order");
            queue.Add(0,-3,5,0);
            Verify(queue.Next().SequenceEqual(new byte[]{0,253,5,0}),"fine pointer motion unchanged");
                        var a=new Rectangle(0,0,100,100);var b=new Rectangle(100,0,120,100);
            Verify(CaptureWindow.HitRightEdge(new Point(80,20),new Point(99,20),a,a.Right) &&
                !CaptureWindow.HitRightEdge(new Point(80,20),new Point(99,20),a,b.Right) &&
                !CaptureWindow.HitRightEdge(new Point(99,30),new Point(99,20),a,a.Right),"outer right edge only");
            var ext=new Rectangle(0,0,1920,1080);var laptop=new Rectangle(1920,-75,2560,1600);
            Verify(!CaptureWindow.HitRightEdge(new Point(1918,550),new Point(1919,550),ext,4480) &&
                !CaptureWindow.HitRightEdge(new Point(1919,550),new Point(1920,550),laptop,4480) &&
                !CaptureWindow.HitRightEdge(new Point(1918,5),new Point(1919,5),ext,4480) &&
                CaptureWindow.HitRightEdge(new Point(4477,550),new Point(4479,550),laptop,4480), "laptop physical outside edge only");
            foreach(var display in Screen.AllScreens){var pos=WindowsDisplays.Current(display.DeviceName);WindowsDisplays.Test(display.DeviceName,pos);}
            Verify(true,"Windows driver accepted position-only display tests (no layout changes)");
            Verify(MainWindow.MatchesMobile("one",2,"one","two") && MainWindow.MatchesMobile("two",2,"one","two") &&
                !MainWindow.MatchesMobile("three",2,"one","two") && !MainWindow.MatchesMobile("one",2,null,null) &&
                MainWindow.MatchesMobile("one",1,null,null),"stable mobile identities and single-device compatibility");
            var tile=new Rectangle(0,0,1000,1000);
            foreach(string side in new[]{"left","right","top","bottom"}){
                var dock=new DockPlace{Side=side,Offset=.5};
                var n=new Point(side=="left"?0:side=="right"?999:500,side=="top"?0:side=="bottom"?999:500);
                Verify(dock.Hit(new Point(500,500),n,tile,new[]{tile}) && !dock.Hit(n,n,tile,new[]{tile}),"docked phone "+side);
                var tracker=new LeftEdgeReturn{Side=side,Width=100};tracker.Begin(false);
                Verify(tracker.Delivered(tracker.NextHomeReport()),"outward return axis "+side);
            }
            Verify(LayoutEditor.ProtectedSeam(new[]{ext,laptop},new Point(1920,400),25) && !LayoutEditor.ProtectedSeam(new[]{ext,laptop},new Point(1920,1200),25),"protected Windows seam is limited to the actual shared segment");
            Verify(LayoutEditor.CheckInsertion(),"repeat drag inserts iPhone between Windows and Mac while protecting physical monitors");
            Verify(MainWindow.DesktopCoordinate(tile,"bottom",new Point(250,999))==9250000,"shared segment lands on matching desktop edge and coordinate");
            var pairLayout=new DeviceLayout{Phone=DeviceLayout.DefaultDock("right"),Pad=new DockPlace{Device="@phone",Side="bottom"}};
            Rectangle firstCard=pairLayout.MobileRect(false),secondCard=pairLayout.MobileRect(true);
            Verify(firstCard.Bottom==secondCard.Top && firstCard.Left==secondCard.Left && !firstCard.IntersectsWith(secondCard),"saved phone/tablet arrangement resolves to distinct adjoining tiles");
            var phoneRect=new Rectangle(0,0,300,600);
            var ipadRect=new DockPlace{Device="@phone",Side="right"}.AtMobile(phoneRect);
            Verify(!phoneRect.IntersectsWith(ipadRect) && phoneRect.Right==ipadRect.Left,"disjoint adjacent mobile tiles");
            var path=new MobilePath(phoneRect,"right",new Point(298,320),300);
            Point next;Verify(path.Advance(new byte[]{0,64,0,0},out next)=="right" && ipadRect.Contains(next),"iPhone to iPad through common edge");
            path=new MobilePath(ipadRect,"right",next,300);
            Verify(path.Advance(new byte[]{1,180,0,0},out next)==null && path.Advance(new byte[]{0,180,0,0},out next)==null &&
                path.Advance(new byte[]{0,180,0,0},out next)=="left" && phoneRect.Contains(next),"reverse handoff after button release");
            var keys=new MobileKeyboard();
            var command=keys.Change(0x5b,true,0x5b,true);
            var copy=keys.Change(0x2e,false,0x43,true);
            Verify(command[0]==8 && copy[0]==8 && copy[2]==6 && keys.AnyHeld && keys.Change(0x2e,false,0x43,true)==null,"GUI modifier and character; repeat suppressed");
            keys.Change(0x2e,false,0x43,false);
            Verify(keys.Change(0x5b,true,0x5b,false).All(v=>v==0) && !keys.AnyHeld,"keyboard release");
            queue.Add(0,5,0,0);queue.AddKeys(copy);queue.Add(1,0,0,0);
            Verify(!queue.OnlyReturnMotion("left") && queue.Next()[1]==5 && queue.Next().SequenceEqual(copy) && queue.Next()[0]==1,"pointer and keyboard report order");
            var radio=await HidMouse.RadioAsync();
            bool restore=radio.State==RadioState.Off;
            try {
                if(restore){
                    Verify(await Radio.RequestAccessAsync()==RadioAccessStatus.Allowed,"radio access");
                    Verify(await radio.SetStateAsync(RadioState.On)==RadioAccessStatus.Allowed,"temporary radio on");
                    await Task.Delay(1500);
                }
                using(var hid=new HidMouse()){
                    await hid.Start();
                    Verify(hid.ServiceId==new Guid("00001812-0000-1000-8000-00805f9b34fb") &&
                        hid.Input.Uuid==new Guid("00002a4d-0000-1000-8000-00805f9b34fb") &&
                        hid.Input.Descriptors.Any(d=>d.Uuid==new Guid("00002908-0000-1000-8000-00805f9b34fb")),
                        "actual GATT service, input and report reference UUIDs match Bluetooth SIG");
                    Verify(hid.Advertising=="Started","HID service, report map, input, reference, control, battery and advertising");
                    Verify(hid.KeyboardInput!=null && hid.KeyboardInput!=hid.Input && hid.KeyboardInput.Descriptors.Any(d=>d.Uuid==new Guid("00002908-0000-1000-8000-00805f9b34fb")),"second HID keyboard input report and descriptor");
                    Console.WriteLine("Subscribed clients: "+hid.Input.SubscribedClients.Count);
                }
            }finally{
                if(restore){var result=radio.SetStateAsync(RadioState.Off).AsTask().GetAwaiter().GetResult();System.Threading.Thread.Sleep(500);
                    Verify(result==RadioAccessStatus.Allowed && radio.State==RadioState.Off,"restored radio off");}
            }
        }
    }
}

