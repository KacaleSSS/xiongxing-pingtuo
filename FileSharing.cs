using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace EdgeMouse {
    static class FileSharing {
        internal sealed class Network {
            public string Adapter,Address,Subnet;
            public override string ToString(){return Adapter+" · "+Address;}
        }
        internal sealed class Request {
            public string Action,Folder,UserSid,Adapter,Address,Subnet;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct Share502 {
            [MarshalAs(UnmanagedType.LPWStr)]public string Name;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)]public string Remark;
            public uint Permissions,MaxUses,Uses;
            [MarshalAs(UnmanagedType.LPWStr)]public string Path;
            [MarshalAs(UnmanagedType.LPWStr)]public string Password;
            public uint Reserved;
            public IntPtr Security;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct Share1 {
            [MarshalAs(UnmanagedType.LPWStr)]public string Name;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)]public string Remark;
        }
        [DllImport("netapi32.dll",CharSet=CharSet.Unicode)]static extern int NetShareAdd(string server,int level,ref Share502 share,out uint parameter);
        [DllImport("netapi32.dll",CharSet=CharSet.Unicode)]static extern int NetShareGetInfo(string server,string name,int level,out IntPtr buffer);
        [DllImport("netapi32.dll",CharSet=CharSet.Unicode)]static extern int NetShareDel(string server,string name,int reserved);
        [DllImport("netapi32.dll")]static extern int NetApiBufferFree(IntPtr buffer);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text,uint revision,out IntPtr descriptor,out uint length);
        [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr pointer);
        internal static string Sid{get{return WindowsIdentity.GetCurrent().User.Value;}}
        internal static string ShareName(string sid){return "雄兴屏拓-"+sid.Substring(sid.LastIndexOf('-')+1);}
        static string Marker(string sid){return "ZZXX CloudTech / XiongXing / "+sid;}
        static string RuleName(string sid){return "XiongXing.FileShare."+sid;}
        internal static string ConfigPath{get{return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EdgeMouse","file-sharing.json");}}
        internal static string Account{get{return WindowsIdentity.GetCurrent().Name;}}
        internal static Network[] Networks(){
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n=>n.OperationalStatus==OperationalStatus.Up &&
                    (n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211 || n.NetworkInterfaceType==NetworkInterfaceType.Ethernet) &&
                    n.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .OrderBy(n=>n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211?0:1)
                .SelectMany(n=>n.GetIPProperties().UnicastAddresses
                    .Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
                    .Select(a=>new Network{Adapter=n.Name,Address=a.Address.ToString(),Subnet=Subnet(a.Address,a.IPv4Mask)})).ToArray();
        }
        internal static string Subnet(IPAddress address,IPAddress mask){
            var a=address.GetAddressBytes();var m=mask.GetAddressBytes();
            return new IPAddress(a.Select((b,i)=>(byte)(b&m[i])).ToArray())+"/"+mask;
        }
        internal static bool Enabled(){
            IntPtr buffer;int error=NetShareGetInfo(null,ShareName(Sid),1,out buffer);
            if(error==2310 || error==2114)return false;
            if(error!=0)throw new Win32Exception(error);
            try{return ((Share1)Marshal.PtrToStructure(buffer,typeof(Share1))).Remark==Marker(Sid);}
            finally{NetApiBufferFree(buffer);}
        }
        internal static bool Listening(string address="127.0.0.1"){
            // Kernel SMB endpoints may be absent from Windows TCP listener enumeration.
            try{
                using(var client=new TcpClient()){
                    var pending=client.BeginConnect(address,445,null,null);
                    using(pending.AsyncWaitHandle){if(!pending.AsyncWaitHandle.WaitOne(2000))return false;}
                    client.EndConnect(pending);client.ReceiveTimeout=2000;client.SendTimeout=2000;
                    using(var stream=client.GetStream()){
                        var packet=new byte[112];packet[3]=108;
                        packet[4]=254;packet[5]=83;packet[6]=77;packet[7]=66;packet[8]=64;packet[18]=1;
                        packet[68]=36;packet[70]=4;packet[72]=1;
                        Array.Copy(Guid.NewGuid().ToByteArray(),0,packet,80,16);
                        byte[] dialects={2,2,16,2,0,3,2,3};Array.Copy(dialects,0,packet,104,8);
                        stream.Write(packet,0,packet.Length);
                        var response=new byte[76];int received=0;
                        while(received<response.Length){
                            int count=stream.Read(response,received,response.Length-received);
                            if(count==0)return false;received+=count;
                        }
                        return response[4]==254 && response[5]==83 && response[6]==77 && response[7]==66 &&
                            BitConverter.ToUInt32(response,12)==0 && BitConverter.ToUInt16(response,16)==0 &&
                            BitConverter.ToUInt16(response,68)==65;
                    }
                }
            }catch(SocketException){return false;}
            catch(IOException){return false;}
        }
        internal static string ValidateFolder(string folder){
            string full=System.IO.Path.GetFullPath(folder);
            if(!Directory.Exists(full))throw new IOException("请选择已经存在的文件夹。");
            if(full.StartsWith(@"\\") || String.Equals(full.TrimEnd('\\'),System.IO.Path.GetPathRoot(full).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
                throw new IOException("请选择本机磁盘中的一个文件夹。");
            return full;
        }
        internal static int RunElevated(string payload){
            try{
                var request=new JavaScriptSerializer().Deserialize<Request>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                if(request==null || request.UserSid!=Sid || (request.Action!="enable" && request.Action!="disable"))
                    throw new InvalidOperationException("共享操作或 Windows 账户不匹配。请使用当前账户授权。");
                if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("请允许管理员授权以设置 Windows 文件共享。");
                if(request.Action=="enable"){
                    request.Folder=ValidateFolder(request.Folder);
                    if(!Networks().Any(n=>n.Address==request.Address && n.Adapter==request.Adapter && n.Subnet==request.Subnet))
                        throw new InvalidOperationException("网络地址已变化，请重新选择网络后开启共享。");
                }
                Apply(request);return 0;
            }catch(Exception ex){
                MessageBox.Show(ex.Message,"雄兴屏拓 · 文件共享",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
            }
        }
        static void Apply(Request r){
            string name=ShareName(r.UserSid);
            using(var service=new ServiceController("LanmanServer")){
                if(service.Status!=ServiceControllerStatus.Running && r.Action=="enable"){
                    service.Start();service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(15));
                }
            }
            IntPtr found;int error=NetShareGetInfo(null,name,502,out found);
            bool exists=error==0;
            if(error!=0 && error!=2310 && error!=2114)throw new Win32Exception(error);
            if(exists)try{
                var share=(Share502)Marshal.PtrToStructure(found,typeof(Share502));
                if(share.Remark!=Marker(r.UserSid))throw new InvalidOperationException("同名共享已由其他程序创建，请先检查 Windows 共享设置。");
                if(r.Action=="enable" && !String.Equals(share.Path,r.Folder,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("请先关闭当前共享，再更换文件夹。");
            }finally{NetApiBufferFree(found);}
            dynamic policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
            if(r.Action=="disable"){
                if(exists){int e=NetShareDel(null,name,0);if(e!=0)throw new Win32Exception(e);}
                policy.Rules.Remove(RuleName(r.UserSid));return;
            }
            bool created=false;
            try{
                if(!exists){
                    IntPtr security;uint bytes;
                    if(!ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;0x1301bf;;;"+r.UserSid+")",1,out security,out bytes))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    try{
                        var share=new Share502{Name=name,Type=0,Remark=Marker(r.UserSid),MaxUses=uint.MaxValue,Path=r.Folder,Security=security};
                        uint parameter;int e=NetShareAdd(null,502,ref share,out parameter);if(e!=0)throw new Win32Exception(e);
                        created=true;
                    }finally{LocalFree(security);}
                }
                if(!Listening(r.Address))throw new InvalidOperationException("无法连接所选地址的 Windows SMB 服务。请检查文件和打印机共享服务后重试；本次新建的共享将撤销。");
                dynamic rule=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));
                rule.Name=RuleName(r.UserSid);rule.Description="雄兴屏拓 · 当前网络文件共享";
                rule.Protocol=6;rule.LocalPorts="445";rule.LocalAddresses=r.Address;rule.RemoteAddresses=r.Subnet;
                rule.Interfaces=new object[]{r.Adapter};rule.Direction=1;rule.Action=1;rule.Profiles=7;rule.Enabled=true;
                policy.Rules.Add(rule);
            }catch{
                if(created){int e=NetShareDel(null,name,0);if(e!=0)throw new Win32Exception(e,"设置未完成，且共享撤销失败，请在 Windows 中关闭 "+name);}
                throw;
            }
        }
        internal static async System.Threading.Tasks.Task<bool> Change(string action,string folder,Network network){
            var request=new Request{Action=action,Folder=folder,UserSid=Sid,Adapter=network==null?null:network.Adapter,
                Address=network==null?null:network.Address,Subnet=network==null?null:network.Subnet};
            string payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(request)));
            var options=new ProcessStartInfo(Application.ExecutablePath,"--file-share "+payload){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            using(var process=Process.Start(options)){
                await System.Threading.Tasks.Task.Run(()=>process.WaitForExit());
                if(process.ExitCode!=0)return false;
            }
            if(action=="enable"){
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ConfigPath));
                string temp=ConfigPath+"."+Guid.NewGuid().ToString("N");
                try{
                    File.WriteAllText(temp,new JavaScriptSerializer().Serialize(request));
                    if(File.Exists(ConfigPath))File.Replace(temp,ConfigPath,null);else File.Move(temp,ConfigPath);
                }finally{File.Delete(temp);}
            }
            return true;
        }
    }
}
