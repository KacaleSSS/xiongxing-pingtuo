using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace EdgeMouse {
    sealed class FileShareWindow : Form {
        readonly TextBox folder=new TextBox{ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Color.White};
        readonly SoftChoice network=new SoftChoice();
        readonly TextBox server=new TextBox{ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Color.White};
        readonly Label state=new Label{AutoSize=true,ForeColor=BrandUi.Muted};
        readonly SoftButton enable=new SoftButton{Text="开启共享"},disable=new SoftButton{Text="关闭共享"};
        readonly SoftButton choose=new SoftButton{Text="选择文件夹"},refresh=new SoftButton{Text="刷新网络"};
        readonly TableLayoutPanel content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,AutoScroll=true,Padding=new Padding(22,0,22,18)};
        bool busy;
        public FileShareWindow(){
            Text="文件共享 · 雄兴屏拓";AutoScaleMode=AutoScaleMode.Dpi;BrandUi.Window(this);
            ClientSize=new Size(680,685);MinimumSize=new Size(620,620);StartPosition=FormStartPosition.CenterParent;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            Controls.Add(content);Controls.Add(BrandUi.Header("文件共享",null));
            folder.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"雄兴屏拓共享");
            if(File.Exists(FileSharing.ConfigPath)){
                var saved=new JavaScriptSerializer().Deserialize<FileSharing.Request>(File.ReadAllText(FileSharing.ConfigPath));
                folder.Text=saved.Folder;
            }
            var open=new SoftButton{Text="打开文件夹"};
            AddSection("共享文件夹",new Control[]{folder,Buttons(choose,open)});
            open.Click+=(s,e)=>{
                try{
                    Directory.CreateDirectory(folder.Text);
                    Process.Start(new ProcessStartInfo("explorer.exe","\""+folder.Text+"\""){UseShellExecute=true});
                }catch(IOException ex){state.Text=ex.Message;}
                catch(UnauthorizedAccessException ex){state.Text=ex.Message;}
                catch(Win32Exception ex){state.Text=ex.Message;}
            };
            choose.Click+=(s,e)=>{
                using(var picker=new FolderBrowserDialog{Description="选择 iPhone 可以读写的文件夹",SelectedPath=folder.Text}){
                    if(picker.ShowDialog(this)==DialogResult.OK)folder.Text=FileSharing.ValidateFolder(picker.SelectedPath);
                }
            };
            AddSection("连接网络",new Control[]{network,Buttons(refresh,enable,disable)});
            var copy=new SoftButton{Text="复制服务器地址"};
            AddSection("在 iPhone 上连接",new Control[]{server,Buttons(copy),Paragraph(
                "1. iPhone 与电脑连接同一局域网。\n"+
                "2. 打开「文件 → 浏览 → ⋯ → 连接服务器」，输入上方地址。\n"+
                "3. 选择「注册用户」，使用下面的 Windows 账户和账户密码登录。\n"+
                "4. 打开 "+FileSharing.ShareName(FileSharing.Sid)+"，即可双向复制文件。"),
                Paragraph("账户："+FileSharing.Account+"\n密码为 Windows 账户密码，PIN 码不能用于此处。")});
            AddSection("共享状态",new Control[]{state,Paragraph(
                "开启和关闭共享时，Windows 会请求管理员授权。所选网络的同网段设备可通过账户验证访问此文件夹。\n"+
                "共享由 Windows 维持，退出应用后继续可用。停止使用时点击「关闭共享」。")});
            refresh.Click+=(s,e)=>LoadNetworks();
            network.SelectedIndexChanged+=(s,e)=>{UpdateAddress();};
            copy.Click+=(s,e)=>{if(server.Text.StartsWith("smb://"))Clipboard.SetText(server.Text);};
            enable.Click+=async (s,e)=>await Change(true);
            disable.Click+=async (s,e)=>await Change(false);
            FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
            LoadNetworks();UpdateState();
        }
        static Label Paragraph(string text){return new Label{Text=text,AutoSize=true,ForeColor=BrandUi.Muted,Margin=new Padding(0,4,0,8)};}
        static FlowLayoutPanel Buttons(params Button[] buttons){
            var flow=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Margin=new Padding(0,3,0,6)};
            foreach(var b in buttons){BrandUi.ButtonStyle(b,b.Text=="开启共享");b.Margin=new Padding(0,3,8,3);flow.Controls.Add(b);}
            return flow;
        }
        void AddSection(string title,Control[] controls){
            var card=new SurfacePanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,
                Padding=new Padding(18,12,18,12),Margin=new Padding(0,0,0,12),BackColor=Color.White};
            var rows=new TableLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=1,BackColor=Color.White};
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var c in controls){c.Anchor=AnchorStyles.Left|AnchorStyles.Right;c.Margin=new Padding(0,4,0,6);rows.Controls.Add(c);}
            rows.SizeChanged+=(s,e)=>{foreach(Control c in rows.Controls){var label=c as Label;if(label!=null)label.MaximumSize=new Size(Math.Max(80,rows.ClientSize.Width),0);}};
            card.Controls.Add(rows);card.Controls.Add(BrandUi.Heading(title));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));content.Controls.Add(card);
        }
        void LoadNetworks(){
            string previous=(network.SelectedItem as FileSharing.Network)==null?null:((FileSharing.Network)network.SelectedItem).Address;
            network.Items.Clear();
            foreach(var item in FileSharing.Networks())network.Items.Add(item);
            var selected=network.Items.Cast<FileSharing.Network>().FirstOrDefault(n=>n.Address==previous);
            network.SelectedItem=selected??network.Items.Cast<FileSharing.Network>().FirstOrDefault();
            UpdateAddress();
        }
        void UpdateAddress(){
            var current=network.SelectedItem as FileSharing.Network;
            server.Text=current==null?"请先连接 Wi-Fi 或有线局域网":"smb://"+current.Address;
            enable.Enabled=!busy && current!=null;
        }
        void UpdateState(){
            try{
                bool active=FileSharing.Enabled();
                state.Text=active?(FileSharing.Listening()?"共享已开启 · 请在 iPhone 上连接并验证":"共享已创建，但 Windows SMB 服务连接失败"):"共享已关闭";
                disable.Enabled=active && !busy;choose.Enabled=!active && !busy;
                network.Enabled=!busy;refresh.Enabled=!busy;
                enable.Text=active?"更新共享":"开启共享";
                enable.Enabled=!busy && network.SelectedItem!=null;
            }catch(Win32Exception ex){state.Text="无法读取共享状态："+ex.Message;enable.Enabled=false;disable.Enabled=false;}
        }
        async System.Threading.Tasks.Task Change(bool turnOn){
            busy=true;enable.Enabled=disable.Enabled=choose.Enabled=refresh.Enabled=network.Enabled=false;
            state.Text="等待 Windows 管理员授权…";
            try{
                if(turnOn)Directory.CreateDirectory(folder.Text);
                bool changed=await FileSharing.Change(turnOn?"enable":"disable",folder.Text,network.SelectedItem as FileSharing.Network);
                busy=false;UpdateState();
                if(!changed)state.Text="共享设置未完成，请根据 Windows 提示处理后重试。";
            }catch(Win32Exception ex){
                busy=false;UpdateState();state.Text=ex.NativeErrorCode==1223?"已取消管理员授权。":"无法设置共享："+ex.Message;
            }catch(IOException ex){
                busy=false;UpdateState();state.Text="文件夹或设置保存失败："+ex.Message;
            }catch(UnauthorizedAccessException ex){
                busy=false;UpdateState();state.Text="文件夹访问失败："+ex.Message;
            }
        }
    }
}
