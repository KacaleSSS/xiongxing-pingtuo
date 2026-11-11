using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EdgeMouse {
    // Keep button transitions in order while combining consecutive movement with the same buttons.
    sealed class ReportBuffer {
        sealed class Move { public byte Buttons; public int X,Y,W; public byte[] Keys; }
        readonly LinkedList<Move> moves=new LinkedList<Move>();
        public int Count {get {return moves.Count;}}
        public bool OnlyReturnMotion(string side){return moves.All(m=>m.Keys==null && m.Buttons==0 && m.W==0 && (side=="left"?m.X>=0:side=="top"?m.Y>=0:side=="bottom"?m.Y<=0:m.X<=0));}
        public void Clear() {moves.Clear();}
        public void Add(byte buttons,int dx,int dy,int wheel) {
            var m=moves.Last==null ? null : moves.Last.Value;
            if(m==null || m.Keys!=null || m.Buttons!=buttons) { m=new Move{Buttons=buttons}; moves.AddLast(m); }
            m.X+=dx; m.Y+=dy; m.W+=wheel;
        }
        public void AddKeys(byte[] report){moves.AddLast(new Move{Keys=report});}
        static int Step(int n) {return Math.Max(-127,Math.Min(127,n));}
        public byte[] Next() {
            var m=moves.First.Value;
            if(m.Keys!=null){moves.RemoveFirst();return m.Keys;}
            // A free-moving pointer must not replay a burst over several BLE intervals.
            // Preserve its direction and keep full-distance delivery for drags and scrolling.
            long magnitude=Math.Max(Math.Abs((long)m.X),Math.Abs((long)m.Y));
            if(m.Buttons==0 && m.W==0 && magnitude>127){
                m.X=(int)Math.Round(m.X*127.0/magnitude);
                m.Y=(int)Math.Round(m.Y*127.0/magnitude);
            }
            int x=Step(m.X),y=Step(m.Y),w=Step(m.W);
            m.X-=x;m.Y-=y;m.W-=w;
            if(m.X==0 && m.Y==0 && m.W==0) moves.RemoveFirst();
            return new byte[]{m.Buttons,unchecked((byte)x),unchecked((byte)y),unchecked((byte)w)};
        }
    }

    sealed class CaptureWindow : Form {
        public const string HotkeyReturn="已按 F8 返回电脑";
        [StructLayout(LayoutKind.Sequential)] struct RawDevice {public ushort Page,Usage; public uint Flags; public IntPtr Target;}
        [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(RawDevice[] d,uint n,uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr handle,uint command,IntPtr data,ref uint size,uint headerSize);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
        readonly IntPtr raw=Marshal.AllocHGlobal(64);
        const int Header=24; // executable is built for x64
        bool registered,hotkey,hiddenCursor;
        Point returnPoint;
        byte buttons,remoteButtons;
        int remoteWheel;
        uint? remoteOwner;
        public readonly MobileKeyboard Keyboard=new MobileKeyboard();
        public event Action<byte[]> KeysChanged;
        int wheelRemainder;
        public event Action<byte,int,int,int> Report;
        public event Action<string> Return;
        public bool Active {get; private set;}
        public CaptureWindow() {
            FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
            StartPosition=FormStartPosition.Manual; Opacity=0.02;
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}}
        public static bool HitRightEdge(Point previous,Point now,Rectangle screen,int desktopRight) {
            return screen.Right==desktopRight && screen.Contains(previous) && screen.Contains(now) &&
                previous.X<now.X && now.X==desktopRight-1;
        }
        bool Raw(uint flags,IntPtr target) {
            var d=new RawDevice{Page=1,Usage=2,Flags=flags,Target=target};
            return RegisterRawInputDevices(new[]{d},1,(uint)Marshal.SizeOf(typeof(RawDevice)));
        }
        public void RegisterInput() {
            hotkey=RegisterHotKey(Handle,1,0x4000,(uint)Keys.F8);
            if(!hotkey) throw new InvalidOperationException("F8 被其他程序占用。");
            registered=Raw(0x100,Handle);
            if(!registered) {LeaveRemote();throw new InvalidOperationException("无法接收鼠标原始输入。");}
        }
        public void EnterRemote(Rectangle screen,Point entry,string side) {
            RegisterInput();
            try{Keyboard.Start(report=>KeysChanged(report));}catch{LeaveRemote();throw;}
            SetReturnPoint(screen,entry,side);
            remoteOwner=null;buttons=remoteButtons=0; wheelRemainder=remoteWheel=0; Bounds=screen;
            Show();
            Cursor.Position=new Point(screen.Left+screen.Width/2,screen.Top+screen.Height/2);
            Cursor.Clip=new Rectangle(Cursor.Position,new Size(1,1));
            Cursor.Hide();hiddenCursor=true; Active=true;Text="EdgeMouse.MobileRelay.Active.v1";
        }
        public void SetReturnPoint(Rectangle screen,Point entry,string side){
            returnPoint=new Point(Math.Max(screen.Left,Math.Min(screen.Right-1,entry.X+(side=="left"?32:side=="right"?-32:0))),Math.Max(screen.Top,Math.Min(screen.Bottom-1,entry.Y+(side=="top"?32:side=="bottom"?-32:0))));
        }
        public void LeaveRemote(bool reposition=true) {
            bool moved=Active;Active=false;Text="";Keyboard.Stop();
            if(registered){Raw(1,IntPtr.Zero);registered=false;}
            if(hotkey){UnregisterHotKey(Handle,1);hotkey=false;}
            if(hiddenCursor){Cursor.Clip=Rectangle.Empty;Cursor.Show();hiddenCursor=false;}
            if(Visible) Hide();
            if(moved && reposition) Cursor.Position=returnPoint;
        }
        protected override void WndProc(ref Message m) {
            if(m.Msg>=0x8151 && m.Msg<=0x8154 && Active){
                int a=unchecked((int)m.WParam.ToInt64()),b=unchecked((int)m.LParam.ToInt64());
                uint source=unchecked((uint)((ulong)m.WParam.ToInt64()>>32));
                if(m.Msg==0x8154){
                    if(remoteOwner==source){remoteOwner=null;remoteButtons=0;remoteWheel=0;Return("远端桌面连接已释放，已返回 Windows");}
                    m.Result=new IntPtr(1);return;
                }
                if(remoteOwner.HasValue && remoteOwner!=source && remoteButtons!=0){m.Result=new IntPtr(1);return;}
                remoteOwner=source;
                int x=0,y=0,w=0;
                switch(m.Msg){
                    case 0x8151:x=a;y=b;break;
                    case 0x8152:
                        int bit=a==272?1:a==273?2:a==274?4:0;
                        if(b==1)remoteButtons|=(byte)bit;else if(b==0)remoteButtons=(byte)(remoteButtons&~bit);
                        break;
                    case 0x8153:if(a==0){remoteWheel+=b;w=remoteWheel/120;remoteWheel%=120;}break;
                }
                Report((byte)(buttons|remoteButtons),x,y,w);
                m.Result=new IntPtr(1);return;
            }
            if(m.Msg==0x312 && m.WParam==new IntPtr(1) && hotkey) Return(HotkeyReturn);
            if(m.Msg==0xff && Active) {
                uint size=64;
                uint n=GetRawInputData(m.LParam,0x10000003,raw,ref size,Header);
                if(n==uint.MaxValue){Return("鼠标数据读取失败");}
                else if(n>=Header+24 && Marshal.ReadInt32(raw)==0) {
                    int flags=Marshal.ReadInt16(raw,Header);
                    if((flags&1)!=0) Return("此原型需要相对位移鼠标");
                    else {
                        int b=(ushort)Marshal.ReadInt16(raw,Header+4);
                        for(int i=0;i<3;i++){
                            if((b&(1<<(2*i)))!=0) buttons|=(byte)(1<<i);
                            if((b&(2<<(2*i)))!=0) buttons=(byte)(buttons&~(1<<i));
                        }
                        if((b&0x400)!=0) wheelRemainder+=Marshal.ReadInt16(raw,Header+6);
                        int w=wheelRemainder/120;wheelRemainder%=120;
                        int x=Marshal.ReadInt32(raw,Header+12),y=Marshal.ReadInt32(raw,Header+16);
                        if(x!=0 || y!=0 || w!=0 || (b&63)!=0){if(remoteButtons==0)remoteOwner=null;Report((byte)(buttons|remoteButtons),x,y,w);}
                    }
                }
            }
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing) {
            if(disposing && !IsDisposed){LeaveRemote();Marshal.FreeHGlobal(raw);}
            base.Dispose(disposing);
        }
    }
}
