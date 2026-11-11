using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace EdgeMouse {
    public sealed class DockPlace {
        public string Device {get;set;}
        public string Side {get;set;}
        public string Slot {get;set;}
        public double Offset {get;set;}
        public DockPlace(){Offset=.5;}
        public Rectangle Tile(Rectangle r,bool phone){
            int baseSize=Math.Min(r.Width,r.Height);
            int w=phone?baseSize/4:baseSize*2/3,h=phone?baseSize/2:baseSize*2/5;
            return Against(r,new Size(w,h));
        }
        public Rectangle Against(Rectangle r,Size size){
            int w=size.Width,h=size.Height;
            int x=r.X+(int)((r.Width-w)*Offset),y=r.Y+(int)((r.Height-h)*Offset);
            switch(Side){
                case "left":return new Rectangle(r.Left-w,y,w,h);
                case "top":return new Rectangle(x,r.Top-h,w,h);
                case "bottom":return new Rectangle(x,r.Bottom,w,h);
                default:return new Rectangle(r.Right,y,w,h);
            }
        }
        public bool MobileParent {get{return Device=="@phone" || Device=="@pad";}}
        public Rectangle AtMobile(Rectangle parent){
            var r=parent;
            if(Side=="right")r.X=parent.Right;
            else if(Side=="left")r.X=parent.Left-r.Width;
            else if(Side=="top")r.Y=parent.Top-r.Height;
            else r.Y=parent.Bottom;
            return r;
        }
        public bool Hit(Point prev,Point now,Rectangle screen,Rectangle[] all){
            if(!screen.Contains(prev)||!screen.Contains(now))return false;
            var tile=Tile(screen,true);var outPoint=now;
            switch(Side){
                case "left":if(now.X!=screen.Left||prev.X<=now.X)return false;outPoint.X--;break;
                case "top":if(now.Y!=screen.Top||prev.Y<=now.Y)return false;outPoint.Y--;break;
                case "bottom":if(now.Y!=screen.Bottom-1||prev.Y>=now.Y)return false;outPoint.Y++;break;
                default:if(now.X!=screen.Right-1||prev.X>=now.X)return false;outPoint.X++;break;
            }
            return tile.Contains(outPoint)&&!all.Any(r=>r.Contains(outPoint));
        }
    }
    public sealed class DeviceLayout {
        public DockPlace Phone {get;set;}
        public DockPlace Mac {get;set;}
        public DockPlace Pad {get;set;}
        public DockPlace PC {get;set;}
        public string PhoneId {get;set;}
        public string PadId {get;set;}
        public static DockPlace DefaultDock(string side){
            var displays=Screen.AllScreens;
            var display=displays.OrderBy(s=>side=="left"?s.Bounds.Left:side=="right"?-s.Bounds.Right:side=="top"?s.Bounds.Top:-s.Bounds.Bottom).First();
            return new DockPlace{Device=display.DeviceName,Side=side};
        }
        internal static readonly string[] Keys={"@phone","@mac","@pad","@pc"};
        internal static Rectangle Resolve(int k,DockPlace[] docks,string[] names,Rectangle[] physical,int visited=0){
            if((visited & (1<<k))!=0 || docks[k]==null)return Rectangle.Empty;
            var d=docks[k];int j=Array.IndexOf(Keys,d.Device);
            if(j<0){int n=Array.IndexOf(names,d.Device);return n<0?Rectangle.Empty:d.Tile(physical[n],k%2==0);}
            var anchor=Resolve(j,docks,names,physical,visited|(1<<k));
            if(anchor.IsEmpty)return anchor;
            var root=docks[j];while(Array.IndexOf(Keys,root.Device)>=0)root=docks[Array.IndexOf(Keys,root.Device)];
            return d.Against(anchor,d.Tile(physical[Array.IndexOf(names,root.Device)],k%2==0).Size);
        }
        internal Rectangle RemoteRect(int k){
            var screens=Screen.AllScreens;
            return Resolve(k,new[]{Phone??DefaultDock("right"),Mac??DefaultDock("left"),Pad??DefaultDock("bottom"),PC??DefaultDock("top")},
                screens.Select(s=>s.DeviceName).ToArray(),screens.Select(s=>s.Bounds).ToArray());
        }
        public Rectangle MobileRect(bool pad){return RemoteRect(pad?2:0);}
        internal static string Touching(Rectangle a,Rectangle b){
            if(a.IsEmpty || b.IsEmpty)return null;
            if(Math.Max(a.Top,b.Top)<Math.Min(a.Bottom,b.Bottom)){
                if(a.Left==b.Right)return "left";if(a.Right==b.Left)return "right";
            }
            if(Math.Max(a.Left,b.Left)<Math.Min(a.Right,b.Right)){
                if(a.Top==b.Bottom)return "top";if(a.Bottom==b.Top)return "bottom";
            }
            return null;
        }
        public void WriteDesktopEdges(){
            var lines=new System.Collections.Generic.List<string>();
            var screens=Screen.AllScreens;
            var tiles=screens.Select(v=>v.Bounds).Concat(Enumerable.Range(0,4).Select(RemoteRect)).ToArray();
            foreach(int k in new[]{1,3}){
                var d=k==1?(Mac??DefaultDock("left")):PC;if(d==null)continue;
                string slot=d.Slot??d.Side;var tile=tiles[screens.Length+k];
                if(tile.IsEmpty)continue;
                // The primary row keeps pairing compatible with the original one-segment protocol.
                int parent=Array.IndexOf(Keys,d.Device);
                var baseScreen=screens.FirstOrDefault(v=>v.DeviceName==d.Device);
                var anchor=parent>=0?tiles[screens.Length+parent]:baseScreen==null?Rectangle.Empty:baseScreen.Bounds;
                bool lateral=d.Side=="left"||d.Side=="right";
                lines.Add(String.Join(" ",new object[]{(parent>=0?"@":"")+slot,d.Side,anchor.Left,anchor.Top,anchor.Right,anchor.Bottom,lateral?Math.Max(tile.Top,anchor.Top):Math.Max(tile.Left,anchor.Left),lateral?Math.Min(tile.Bottom,anchor.Bottom):Math.Min(tile.Right,anchor.Right)}));
                for(int j=0;j<tiles.Length;j++){
                    if(j==screens.Length+k)continue;
                    var r=tiles[j];string side=Touching(tile,r);if(side==null)continue;
                    bool h=side=="left"||side=="right";int lo=h?Math.Max(tile.Top,r.Top):Math.Max(tile.Left,r.Left),hi=h?Math.Min(tile.Bottom,r.Bottom):Math.Min(tile.Right,r.Right);
                    int start=h?tile.Top:tile.Left,length=h?tile.Height:tile.Width;
                    lines.Add(String.Join(" ",new object[]{"route",slot,side,(int)((lo-start)*1000000L/length),(int)Math.Ceiling((hi-start)*1000000.0/length)}));
                    if(j<screens.Length)lines.Add(String.Join(" ",new object[]{"physical",slot,MobilePath.Opposite(side),r.Left,r.Top,r.Right,r.Bottom,lo,hi,start,length}));
                }
            }
            string path=Path.Combine(Path.GetDirectoryName(FileName),"desktop-edges.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+"."+Guid.NewGuid().ToString("N");
            File.WriteAllLines(temp,lines);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        public static readonly string FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EdgeMouse","layout.json");
        public static DeviceLayout Load(){
            var layout=File.Exists(FileName)?new JavaScriptSerializer().Deserialize<DeviceLayout>(File.ReadAllText(FileName)):new DeviceLayout();
            var phone=layout.MobileRect(false);
            if(phone.IsEmpty || (layout.Pad!=null && !phone.IntersectsWith(layout.MobileRect(true))))return layout;
            var obstacles=Screen.AllScreens.Select(s=>s.Bounds).ToList();
            foreach(var d in new[]{layout.Mac??DefaultDock("left"),layout.PC??DefaultDock("top")}){
                var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==d.Device);
                if(screen!=null)obstacles.Add(d.Tile(screen.Bounds,false));
            }
            if(layout.Phone==null || !layout.Phone.MobileParent){
                foreach(string side in new[]{layout.Phone==null?"right":layout.Phone.Side,"right","bottom","left","top"}){
                    var pad=new DockPlace{Device="@phone",Side=side};
                    if(obstacles.Any(r=>r.IntersectsWith(pad.AtMobile(phone))))continue;
                    layout.Pad=pad;break;
                }
            }
            return layout;
        }
        public void Save(){
            Directory.CreateDirectory(Path.GetDirectoryName(FileName));
            var temp=FileName+"."+Guid.NewGuid().ToString("N");
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));
            if(File.Exists(FileName))File.Replace(temp,FileName,null);else File.Move(temp,FileName);
        }
    }
    static class WindowsDisplays {
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplaySettingsEx(string device,int mode,IntPtr dm,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int ChangeDisplaySettingsEx(string device,IntPtr dm,IntPtr hwnd,uint flags,IntPtr param);
        public static Point Current(string device){return Position(device,Point.Empty,0);}
        public static void Test(string device,Point p){Position(device,p,2);}
        static Point Position(string device,Point p,uint flags){
            // DEVMODEW's display union starts at byte 76. Preserve the driver's complete mode.
            IntPtr dm=Marshal.AllocHGlobal(220+65535);
            try{
                Marshal.WriteInt16(dm,68,220);Marshal.WriteInt16(dm,70,0);
                if(!EnumDisplaySettingsEx(device,-1,dm,0))throw new InvalidOperationException("无法读取 "+device);
                if(flags==0)return new Point(Marshal.ReadInt32(dm,76),Marshal.ReadInt32(dm,80));
                Marshal.WriteInt32(dm,72,0x20);Marshal.WriteInt32(dm,76,p.X);Marshal.WriteInt32(dm,80,p.Y);
                int result=ChangeDisplaySettingsEx(device,dm,IntPtr.Zero,flags,IntPtr.Zero);
                if(result!=0)throw new InvalidOperationException(device+" 显示位置设置失败 ("+result+")");
                return p;
            }finally{Marshal.FreeHGlobal(dm);}
        }
        public static void Apply(string[] names,Rectangle[] desired){
            for(int i=0;i<names.Length;i++)Position(names[i],desired[i].Location,2);
            for(int i=0;i<names.Length;i++)Position(names[i],desired[i].Location,0x10000001);
            int result=ChangeDisplaySettingsEx(null,IntPtr.Zero,IntPtr.Zero,0,IntPtr.Zero);
            if(result!=0)throw new InvalidOperationException("Windows 未应用布局 ("+result+")");
            for(int i=0;i<names.Length;i++)if(Current(names[i])!=desired[i].Location)throw new InvalidOperationException("Windows 返回的位置与预览不一致");
        }
    }
}
