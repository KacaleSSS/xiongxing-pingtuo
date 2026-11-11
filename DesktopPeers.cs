using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace EdgeMouse {
    sealed class DesktopPeers : FlowLayoutPanel {
        public sealed class Notice {
            public string Kind {get;set;}
            public string Edge {get;set;}
            public string IP {get;set;}
            public string ID {get;set;}
            public string Name {get;set;}
            public string Message {get;set;}
            public override string ToString() {return Name+" · "+IP+" · "+ID.Substring(0,12);}
        }
        readonly Label status=new Label {AutoSize=true,Margin=new Padding(4,6,4,4)};
        readonly SoftChoice peers=new SoftChoice {Width=370};
        readonly Button confirm=new SoftButton {Text="确认连接",AutoSize=true,Enabled=false};
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        readonly HashSet<string> paired=new HashSet<string>();
        Process agent;
        bool closing,pending,closedInput;
        public bool Running {get{return agent!=null && !agent.HasExited;}}
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string name);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessageTimeout(IntPtr window,uint msg,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
        public bool EnterDesktop(int slot,int fraction){
            if(!Running)return false;
            var window=FindWindowEx(IntPtr.Zero,IntPtr.Zero,"lan-mouse-message-window-class",null);
            IntPtr result;
            return window!=IntPtr.Zero && SendMessageTimeout(window,0x8155,new IntPtr(slot),new IntPtr(fraction),2,300,out result)!=IntPtr.Zero && result==new IntPtr(1);
        }
        public DesktopPeers() {
            Dock=DockStyle.Fill;Padding=new Padding(0);WrapContents=true;BackColor=Color.White;
            BrandUi.ButtonStyle(confirm,true);status.ForeColor=BrandUi.Muted;peers.FlatStyle=FlatStyle.Flat;
            confirm.Margin=new Padding(3,6,3,3);peers.Margin=new Padding(3,6,12,3);peers.Width=260;status.Margin=new Padding(3,4,0,5);
            Resize+=(s,e)=>{status.MaximumSize=new Size(Math.Max(120,ClientSize.Width-8),0);peers.Width=Math.Max(100,Math.Min(370,ClientSize.Width-confirm.PreferredSize.Width-36));};
            Controls.Add(status);SetFlowBreak(status,true);Controls.Add(peers);Controls.Add(confirm);
            status.Text="雄兴屏拓 · 正在启动发现服务…";
            peers.SelectedIndexChanged+=(s,e)=>UpdateButton();
            confirm.Click+=(s,e)=>{
                var peer=peers.SelectedItem as Notice;if(peer==null || agent==null)return;
                pending=true;UpdateButton();status.Text="正在验证证书并启用两端键鼠…";
                try{agent.StandardInput.WriteLine(json.Serialize(new {Kind="pair",peer.IP,peer.ID}));}
                catch(IOException ex){pending=false;status.Text="发现服务已断开："+ex.Message;UpdateButton();}
            };
        }
        void UpdateButton(){
            var peer=peers.SelectedItem as Notice;
            bool known=peer!=null && paired.Contains(peer.ID+"@"+peer.IP);
            confirm.Text=known?"已配对":"确认连接";confirm.Enabled=peer!=null && !known && !pending && agent!=null && !agent.HasExited;
        }
        void Receive(string line){
            if(closing || line==null)return;
            var ev=json.Deserialize<Notice>(line);
            if(ev.Kind=="approve"){
                bool allow=BrandUi.ConfirmPeer(FindForm(),ev.Name,ev.Message);
                if(!closing && agent!=null && !agent.HasExited)agent.StandardInput.WriteLine(json.Serialize(new {Kind=allow?"allow":"deny",ev.ID}));
                return;
            }
            if(ev.Kind=="incoming"){
                if(IncomingWindows!=null)IncomingWindows(ev.Edge,ev.Message);
                status.Text="Windows 已连接 · 本机引擎已启用："+ev.Name;
            }else if(ev.Kind=="found"){
                peers.Items.Add(ev);if(peers.SelectedIndex<0)peers.SelectedIndex=0;
                status.Text="发现桌面设备 · 选择设备并点「确认连接」，首次连接时在对方电脑 核对配对码。";
            }else if(ev.Kind=="lost"){
                var old=peers.Items.Cast<Notice>().FirstOrDefault(p=>p.ID==ev.ID && p.IP==ev.IP);
                if(old!=null)peers.Items.Remove(old);paired.Remove(ev.ID+"@"+ev.IP);
                if(peers.Items.Count==0)status.Text="正在同一局域网查找 Mac / Windows…";
                else if(peers.SelectedIndex<0)peers.SelectedIndex=0;
            }else{
                if(ev.Kind=="paired"){pending=false;paired.Add(ev.ID+"@"+ev.IP);}
                if(ev.Kind=="error")pending=false;
                status.Text=ev.Message;
            }
            UpdateButton();
        }
        public event Action<string,string> IncomingWindows;
        public void LayoutChanged(){
            if(agent!=null && !agent.HasExited)agent.StandardInput.WriteLine("{\"Kind\":\"layout\"}");
        }
        public void StartDiscovery(){
            if(Running)return;closing=false;closedInput=false;
            if(agent!=null){agent.Dispose();agent=null;}
            string helper,engine;
            try{
                helper=RuntimeBundle.Component("edge-pair.exe");engine=RuntimeBundle.Component("lan-mouse.exe");
            }catch(IOException ex){status.Text="内置组件无法就绪："+ex.Message;return;}
            catch(UnauthorizedAccessException ex){status.Text="无法写入组件缓存："+ex.Message;return;}
            catch(InvalidDataException ex){status.Text="内置组件校验失败："+ex.Message;return;}
            var options=new ProcessStartInfo(helper,"windows \""+engine+"\""){UseShellExecute=false,
                RedirectStandardInput=true,RedirectStandardOutput=true,StandardOutputEncoding=Encoding.UTF8,CreateNoWindow=true};
            options.EnvironmentVariables["EDGEMOUSE_RELAY_PID"]=Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            options.EnvironmentVariables["EDGEMOUSE_WINDOW"]=FindForm().Handle.ToInt64().ToString();
            agent=new Process{StartInfo=options};
            agent.OutputDataReceived+=(s,e)=>{if(!closing && e.Data!=null && IsHandleCreated)
                try {BeginInvoke((Action)(()=>{if(s==agent)Receive(e.Data);}));} catch(InvalidOperationException){if(!closing)throw;}};
            try{agent.Start();agent.BeginOutputReadLine();}
            catch(System.ComponentModel.Win32Exception ex){status.Text="发现服务启动失败："+ex.Message;agent.Dispose();agent=null;}
        }
        public void StopDiscovery(){
            closing=true;
            if(agent!=null){if(!agent.HasExited && !closedInput){agent.StandardInput.Close();closedInput=true;}}
            paired.Clear();peers.Items.Clear();pending=false;status.Text="桌面发现已关闭";UpdateButton();
        }
        public async System.Threading.Tasks.Task WaitStopped(){var stopped=agent;if(stopped!=null)await System.Threading.Tasks.Task.Run(()=>stopped.WaitForExit());}
    }
}
