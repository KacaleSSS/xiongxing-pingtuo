using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace EdgeMouse {
    static class BrandUi {
        internal static readonly Color Navy=Color.FromArgb(33,36,43),Blue=Color.FromArgb(0,108,255),
            Sky=Color.FromArgb(122,184,255),Canvas=Color.FromArgb(246,246,248),Muted=Color.FromArgb(93,99,110),Field=Color.FromArgb(242,243,245);
        internal static Font Type(float size,bool bold=false){return new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular);}
        internal static void Window(Form form){
            form.Font=Type(9);form.BackColor=Canvas;form.ForeColor=Navy;
            form.Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        internal static void ButtonStyle(Button b,bool primary=false){
            var soft=b as SoftButton;if(soft!=null)soft.Primary=primary;
            b.AutoSize=true;b.MinimumSize=new Size(88,36);b.Padding=new Padding(9,1,9,1);
            b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;
            b.FlatAppearance.BorderColor=primary?Blue:Color.FromArgb(197,211,231);
            b.UseVisualStyleBackColor=false;b.BackColor=primary?Blue:Color.White;b.ForeColor=primary?Color.White:Navy;
            b.Cursor=Cursors.Hand;
        }
        internal static Label Heading(string title){return new Label{Text=title,Dock=DockStyle.Top,
            Height=35,Font=Type(13,true),ForeColor=Navy,TextAlign=ContentAlignment.MiddleLeft};}
        internal static Panel Header(string title,Button action){
            var header=new Panel{Dock=DockStyle.Top,Height=96,BackColor=Canvas,Padding=new Padding(22,15,22,15)};
            var mark=new PictureBox{Left=29,Top=25,Size=new Size(47,47),SizeMode=PictureBoxSizeMode.Zoom};
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("brand.png"))
            using(var original=Image.FromStream(input))mark.Image=new Bitmap(original);
            mark.Disposed+=(s,e)=>mark.Image.Dispose();header.Controls.Add(mark);
            header.Controls.Add(new Label{Text="ZZXX CloudTech",AutoSize=true,Location=new Point(90,57),Font=new Font("Segoe UI",9),ForeColor=Muted});
            header.Controls.Add(new Label{Text=title,AutoSize=true,Location=new Point(87,16),Font=Type(21,true),ForeColor=Navy});
            if(action!=null){
                ButtonStyle(action);action.Anchor=AnchorStyles.Right|AnchorStyles.Top;header.Controls.Add(action);
                header.Resize+=(s,e)=>action.Location=new Point(header.ClientSize.Width-action.Width-22,28);
            }
            return header;
        }
        internal static bool ConfirmPeer(Form owner,string name,string code){
            using(var dialog=new Form{Text="配对请求 · ZZXX CloudTech",FormBorderStyle=FormBorderStyle.FixedDialog,
                ClientSize=new Size(490,275),StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false})
            using(var expiry=new Timer{Interval=52000}){
                Window(dialog);
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=55,Padding=new Padding(14,3,0,0)};
                var deny=new SoftButton{Text="拒绝",DialogResult=DialogResult.Cancel};var yes=new SoftButton{Text="允许连接",DialogResult=DialogResult.OK};
                ButtonStyle(deny);ButtonStyle(yes,true);buttons.Controls.Add(deny);buttons.Controls.Add(yes);
                var prompt=new Label{Dock=DockStyle.Fill,Padding=new Padding(19,8,16,0),Text="请核对另一台电脑显示的配对码。允许后，"+name+" 可以双向控制键鼠。"};
                var digits=new Label{Text=code,Dock=DockStyle.Bottom,Height=50,Padding=new Padding(18,4,0,0),Font=new Font("Consolas",19,FontStyle.Bold),ForeColor=Navy};
                dialog.Controls.Add(prompt);dialog.Controls.Add(digits);dialog.Controls.Add(buttons);dialog.Controls.Add(Header("配对请求",null));
                dialog.AcceptButton=deny;dialog.CancelButton=deny;
                expiry.Tick+=(s,e)=>dialog.DialogResult=DialogResult.Cancel;expiry.Start();
                return dialog.ShowDialog(owner)==DialogResult.OK;
            }
        }
        internal static Panel Card(string title,Control body,Control actions=null){
            var result=new SurfacePanel{Dock=DockStyle.Fill,BackColor=Color.White,Margin=new Padding(0,0,0,12),Padding=new Padding(22,12,22,15)};
            body.Dock=DockStyle.Fill;result.Controls.Add(body);
            if(actions!=null){actions.Dock=DockStyle.Bottom;result.Controls.Add(actions);}
            result.Controls.Add(Heading(title));return result;
        }
    }
}
