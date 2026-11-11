using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EdgeMouse {
    static class Contour {
        public static GraphicsPath Rounded(RectangleF r,float radius){
            var path=new GraphicsPath();
            float d=Math.Min(2*radius,Math.Min(r.Width,r.Height));
            path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);
            path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);
            path.CloseFigure();return path;
        }
        public static void Fill(Graphics g,RectangleF box,float radius,Color fill){
            if(box.Width<=0 || box.Height<=0)return;
            using(var path=Rounded(box,radius))using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
        }
    }
    sealed class SurfacePanel : Panel {
        public SurfacePanel(){DoubleBuffered=true;}
        protected override void OnPaintBackground(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BrandUi.Canvas);
            float unit=g.DpiX/96f;
            Contour.Fill(g,new RectangleF(1,2,Width-2,Height-3),20*unit,Color.FromArgb(224,226,230));
            Contour.Fill(g,new RectangleF(1,0,Width-2,Height-3),20*unit,Color.White);
        }
    }
    sealed class SoftButton : Button {
        public bool Primary;
        bool hover,pressed;
        public SoftButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;pressed=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);pressed=true;Invalidate();}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);pressed=false;Invalidate();}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.Clear(Parent==null?BrandUi.Canvas:Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
            var fill=!Enabled?Color.FromArgb(242,243,245):Primary?(pressed?Color.FromArgb(0,79,185):hover?Color.FromArgb(20,119,255):BrandUi.Blue):
                pressed?Color.FromArgb(217,221,227):hover?Color.FromArgb(228,231,235):Color.FromArgb(237,239,242);
            var box=new RectangleF(1,1,Width-2,Height-2);
            Contour.Fill(g,box,box.Height/2,fill);
            if(Focused && ShowFocusCues && box.Width>2 && box.Height>2)
                using(var path=Contour.Rounded(box,box.Height/2))using(var pen=new Pen(BrandUi.Blue,2))g.DrawPath(pen,path);
            TextRenderer.DrawText(g,Text,Font,ClientRectangle,!Enabled?Color.FromArgb(120,124,132):Primary?Color.White:BrandUi.Navy,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        }
    }
    sealed class SoftSwitch : CheckBox {
        public SoftSwitch(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        public override Size GetPreferredSize(Size proposed){
            var text=TextRenderer.MeasureText(Text,Font);
            return new Size(text.Width+text.Height*2+12,Math.Max(text.Height+6,28));
        }
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.Clear(Parent==null?Color.White:Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
            int h=Math.Min(Height-4,Math.Max(16,Font.Height)),w=h*2-2,top=(Height-h)/2;
            Contour.Fill(g,new RectangleF(1,top,w,h),h/2f,Checked?BrandUi.Blue:Color.FromArgb(181,185,192));
            int knob=h-4;g.FillEllipse(Brushes.White,Checked?1+w-knob-2:3,top+2,knob,knob);
            var area=new Rectangle(w+7,0,Width-w-7,Height);
            TextRenderer.DrawText(g,Text,Font,area,BrandUi.Navy,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(g,area);
        }
    }
}
