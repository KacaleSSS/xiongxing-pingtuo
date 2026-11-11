using System;
using System.Drawing;

namespace EdgeMouse {
    // All distances are signed HID report units, not iPhone display pixels.
    sealed class LeftEdgeReturn {
        public const int DefaultWidth=1600;
        public const int MaxWidth=16000;
        const int Overrun=45;
        public string Side="right";
        public int Width=DefaultWidth;
        public bool Calibrating { get; private set; }
        public int Position { get; private set; }
        int homeRemaining, beyondLeft, transverseHome, transverseSeek;
        byte lastButtons;

        public void Begin(bool calibrating) {
            Calibrating=calibrating;
            Position=0; beyondLeft=0; lastButtons=0;
            // An initial leftward sweep also aligns entry after an F8 return from mid-screen.
            homeRemaining=Width + Width/2;transverseHome=transverseSeek=0;
        }
        public void AlignSharedEdge(Rectangle r,Point entry){
            bool vertical=Side=="top" || Side=="bottom";
            double factor=Width/(double)(vertical?r.Height:r.Width);
            int length=vertical?r.Width:r.Height;
            transverseHome=(int)Math.Ceiling(length*factor*1.5);
            transverseSeek=(int)Math.Round(Math.Max(0,Math.Min(length-1,vertical?entry.X-r.Left:entry.Y-r.Top))*factor);
        }
        public byte[] NextHomeReport() {
            if(homeRemaining==0 && transverseHome==0 && transverseSeek==0)return null;
            int distance=Math.Min(homeRemaining,127);
            homeRemaining-=distance;
            bool vertical=Side=="top" || Side=="bottom";
            byte delta=unchecked((byte)((Side=="top" || Side=="left")?distance:-distance));
            int cross;
            if(transverseHome>0){cross=-Math.Min(127,transverseHome);transverseHome+=cross;}
            else{cross=Math.Min(127,transverseSeek);transverseSeek-=cross;}
            byte orth=unchecked((byte)cross);
            return new byte[]{0,vertical?orth:delta,vertical?delta:orth,0};
        }
        public bool Delivered(byte[] report) {
            int dx=unchecked((sbyte)report[Side=="top" || Side=="bottom"?2:1]);
            if(Side=="left" || Side=="top")dx=-dx;
            int next=Position+dx;
            Position=Math.Max(0,Math.Min(Calibrating ? MaxWidth+1 : Width,next));
            bool clean=lastButtons==0 && report[0]==0 && report[3]==0;
            lastButtons=report[0];
            if(Calibrating || !clean || dx>=0 || next>=0) beyondLeft=0;
            else beyondLeft-=next;
            return beyondLeft>=Overrun;
        }
    }
}
