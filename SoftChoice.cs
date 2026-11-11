using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace EdgeMouse {
    sealed class SoftChoice : Button {
        public readonly BindingList<object> Items=new BindingList<object>();
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        object selected;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex {
            get{return Items.IndexOf(selected);}
            set{SelectedItem=value<0?null:Items[value];}
        }
        public object SelectedItem {
            get{return selected;}
            set{
                if(selected==value)return;
                selected=value;AccessibleDescription=Caption;Invalidate();
                if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);
            }
        }
        string Caption{get{return selected==null?"等待发现设备":selected.ToString();}}
        public SoftChoice(){
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            Height=36;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.DropList;
            menu.AutoSize=true;menu.ShowCheckMargin=false;menu.ShowImageMargin=false;
            menu.Renderer=new ChoiceRenderer();menu.DropShadowEnabled=true;
            menu.Closed+=(s,e)=>Invalidate();
            menu.SizeChanged+=(s,e)=>{
                if(menu.Width<2 || menu.Height<2)return;
                var old=menu.Region;
                using(var path=Contour.Rounded(new RectangleF(0,0,menu.Width,menu.Height),Font.Height*.8f))menu.Region=new Region(path);
                if(old!=null)old.Dispose();
            };
            Items.ListChanged+=(s,e)=>{
                if(selected!=null && !Items.Contains(selected))SelectedItem=null;
                if(menu.Visible)BuildMenu();
            };
        }
        protected override void OnClick(EventArgs e){
            base.OnClick(e);
            if(menu.Visible){menu.Close();return;}
            BuildMenu();menu.Show(this,new Point(0,Height+3));
            if(SelectedIndex>=0)menu.Items[SelectedIndex].Select();
            Invalidate();
        }
        protected override bool ProcessCmdKey(ref Message m,Keys key){
            if(key==Keys.Down || key==Keys.F4 || key==(Keys.Alt|Keys.Down)){OnClick(EventArgs.Empty);return true;}
            return base.ProcessCmdKey(ref m,key);
        }
        void BuildMenu(){
            menu.SuspendLayout();
            try{
                while(menu.Items.Count>0)menu.Items[0].Dispose();
                menu.Font=Font;menu.Padding=new Padding(5);
                var working=Screen.FromControl(this).WorkingArea;
                var values=Items.Count==0?new object[]{null}:Items.ToArray();
                int inset=Font.Height*3;
                int width=Math.Min(working.Width-24,Math.Max(Width-10,values.Max(v=>TextRenderer.MeasureText(v==null?Caption:v.ToString(),Font).Width)+inset));
                foreach(var value in values){
                    var item=new ToolStripMenuItem{Text=value==null?Caption:value.ToString(),Tag=value,
                        AutoSize=false,Enabled=value!=null,Checked=value!=null && value==selected,Font=Font};
                    var size=TextRenderer.MeasureText(item.Text,Font,new Size(Math.Max(1,width-inset),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                    item.Size=new Size(width,Math.Max(Font.Height*2,size.Height+12));
                    item.Click+=(s,e)=>{SelectedItem=item.Tag;};menu.Items.Add(item);
                }
            }finally{menu.ResumeLayout(true);}
        }
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.Clear(Parent==null?Color.White:Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
            var box=new RectangleF(1,1,Width-2,Height-2);
            Contour.Fill(g,box,box.Height/2,menu.Visible?Color.FromArgb(230,238,248):BrandUi.Field);
            if(Focused && ShowFocusCues)using(var path=Contour.Rounded(box,box.Height/2))using(var pen=new Pen(BrandUi.Blue))g.DrawPath(pen,path);
            int pad=Math.Max(8,Font.Height*2/3),y=Height/2,x=Width-pad-5;
            TextRenderer.DrawText(g,Caption,Font,new Rectangle(pad,0,Math.Max(1,x-pad-9),Height),selected==null?BrandUi.Muted:BrandUi.Navy,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            int sign=menu.Visible?-1:1;
            using(var pen=new Pen(BrandUi.Muted,1.5f))g.DrawLines(pen,new[]{new Point(x-4,y-sign*2),new Point(x,y+sign*2),new Point(x+4,y-sign*2)});
        }
        protected override void Dispose(bool disposing){if(disposing)menu.Dispose();base.Dispose(disposing);}
        sealed class ChoiceRenderer : ToolStripRenderer {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){
                e.Graphics.Clear(Color.White);
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){
                var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var path=Contour.Rounded(new RectangleF(.5f,.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),e.ToolStrip.Font.Height*.8f))
                using(var pen=new Pen(Color.FromArgb(218,223,231)))g.DrawPath(pen,path);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
                var item=(ToolStripMenuItem)e.Item;
                if(!item.Selected && !item.Checked)return;
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                Contour.Fill(e.Graphics,new RectangleF(1,1,item.Width-2,item.Height-2),item.Font.Height*.6f,
                    item.Selected?Color.FromArgb(231,239,250):BrandUi.Field);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){
                var item=(ToolStripMenuItem)e.Item;int pad=item.Font.Height*2/3;
                TextRenderer.DrawText(e.Graphics,item.Text,item.Font,new Rectangle(pad,0,item.Width-item.Font.Height*3,item.Height),
                    item.Enabled?BrandUi.Navy:BrandUi.Muted,TextFormatFlags.WordBreak|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
                if(!item.Checked)return;
                int x=item.Width-pad-7,y=item.Height/2;
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                using(var p=new Pen(BrandUi.Blue,2))e.Graphics.DrawLines(p,new[]{new Point(x-4,y),new Point(x-1,y+3),new Point(x+5,y-4)});
            }
        }
    }
}
