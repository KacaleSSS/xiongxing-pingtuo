using System;
using System.Drawing;
using System.Windows.Forms;

namespace EdgeMouse {
    sealed class OptionDrawer : Panel {
        readonly SurfacePanel surface=new SurfacePanel();
        readonly FlowLayoutPanel stack=new FlowLayoutPanel();
        readonly Timer animation=new Timer{Interval=15};
        readonly Button toggle;
        bool expanded=true;
        int full;
        DateTime began;
        int from;
        public OptionDrawer(Button toggle){
            this.toggle=toggle;Dock=DockStyle.Left;BackColor=BrandUi.Canvas;
            full=216;Width=full;
            surface.BackColor=Color.White;
            stack.Dock=DockStyle.Fill;stack.BackColor=Color.White;stack.FlowDirection=FlowDirection.TopDown;
            stack.WrapContents=false;stack.AutoScroll=true;surface.Padding=new Padding(14,16,14,16);
            surface.Controls.Add(stack);Controls.Add(surface);
            toggle.Text="收起选项";
            toggle.Click+=(s,e)=>{expanded=!expanded;toggle.Text=expanded?"收起选项":"展开选项";from=Width;began=DateTime.UtcNow;animation.Start();};
            animation.Tick+=(s,e)=>{
                double t=Math.Min(1,(DateTime.UtcNow-began).TotalMilliseconds/200);
                Width=(int)Math.Round(from+((expanded?full:0)-from)*(1-Math.Pow(1-t,3)));
                if(t==1)animation.Stop();
            };
        }
        protected override void ScaleControl(SizeF factor,BoundsSpecified specified){
            full=(int)Math.Round(full*factor.Width);base.ScaleControl(factor,specified);PerformLayout();
        }
        protected override void OnLayout(LayoutEventArgs e){
            base.OnLayout(e);if(surface==null)return;
            surface.SetBounds(Width-full+16,0,Math.Max(1,full-18),Math.Max(1,Height-22));
            foreach(Control c in stack.Controls)c.Width=Math.Max(1,stack.ClientSize.Width-8);
        }
        public void AddOptions(params Control[] items){
            foreach(var c in items){c.AutoSize=false;c.Height=c is Label?32:38;c.Margin=new Padding(4,3,4,5);stack.Controls.Add(c);}
            PerformLayout();
        }
        protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
    }
}
