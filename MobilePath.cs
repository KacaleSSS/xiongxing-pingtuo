using System;
using System.Drawing;

namespace EdgeMouse {
    // iOS reports no absolute pointer coordinates. Track only motion already sent over HID.
    sealed class MobilePath {
        readonly Rectangle tile;
        readonly double scale;
        double x,y,extra;
        string pushing;
        byte previousButtons;
        public MobilePath(Rectangle tile,string enter,Point point,int span){
            this.tile=tile;
            scale=(enter=="left"||enter=="right"?tile.Width:tile.Height)/(double)span;
            x=Clamp(point.X,tile.Left,tile.Right-1);y=Clamp(point.Y,tile.Top,tile.Bottom-1);
        }
        static double Clamp(double v,int lo,int hi){return Math.Max(lo,Math.Min(hi,v));}
        public string Advance(byte[] report,out Point outside){
            int dx=unchecked((sbyte)report[1]),dy=unchecked((sbyte)report[2]);
            double nx=x+dx*scale,ny=y+dy*scale;
            x=Clamp(nx,tile.Left,tile.Right-1);y=Clamp(ny,tile.Top,tile.Bottom-1);
            double ox=nx-x,oy=ny-y;
            string side=Math.Abs(ox)>Math.Abs(oy)?(ox<0?"left":"right"):oy==0?null:oy<0?"top":"bottom";
            if(side!=pushing)extra=0;
            pushing=side;
            outside=new Point((int)x,(int)y);
            bool clean=previousButtons==0 && report[0]==0 && report[3]==0;
            previousButtons=report[0];
            if(!clean || side==null){extra=0;return null;}
            extra+=Math.Max(Math.Abs(ox),Math.Abs(oy))/scale;
            if(extra<45)return null;
            outside.Offset(side=="left"?-1:side=="right"?1:0,side=="top"?-1:side=="bottom"?1:0);
            return side;
        }
        public static string Opposite(string side){return side=="left"?"right":side=="right"?"left":side=="top"?"bottom":"top";}
    }
}
