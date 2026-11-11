using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace EdgeMouse {
    sealed class LayoutEditor : Form {
        sealed class Canvas : Panel { public Canvas(){DoubleBuffered=true;} }
        readonly Canvas canvas=new Canvas{Dock=DockStyle.Fill,BackColor=BrandUi.Canvas};
        readonly Label hint=new Label{AutoSize=true,Padding=new Padding(6)};
        readonly Screen[] screens=Screen.AllScreens;
        readonly Rectangle[] original,rects;
        readonly DockPlace[] docks=new DockPlace[4];
        readonly string[] sides={"left","right","top","bottom"};
        readonly SoftChoice phoneChoice=new SoftChoice{Width=190};
        readonly SoftChoice padChoice=new SoftChoice{Width=190};
        internal sealed class Mobile { public string Id,Name;public override string ToString(){return Name;} }
        DockPlace[] initialDocks;
        int drag=-1;Point grab,mouseDown;float zoom;
        PreviewAxis horizontalAxis,verticalAxis;
        public DeviceLayout Result {get;private set;}
        public LayoutEditor(DeviceLayout current,Mobile[] mobiles){
            Text="屏幕布局 · ZZXX CloudTech";BrandUi.Window(this);ClientSize=new Size(1020,650);MinimumSize=new Size(850,525);StartPosition=FormStartPosition.CenterParent;
            original=screens.Select(s=>s.Bounds).ToArray();rects=(Rectangle[])original.Clone();
            var js=new System.Web.Script.Serialization.JavaScriptSerializer();
            var copy=js.Deserialize<DeviceLayout>(js.Serialize(current));
            var placements=new[]{copy.Phone,copy.Mac,copy.Pad,copy.PC};var defaults=new[]{"right","left","bottom","top"};
            var names=screens.Select(screen=>screen.DeviceName).ToArray();
            for(int i=0;i<4;i++)docks[i]=placements[i]??DeviceLayout.DefaultDock(defaults[i]);
            for(int i=0;i<4;i++)if(DeviceLayout.Resolve(i,docks,names,rects).IsEmpty)docks[i]=DeviceLayout.DefaultDock(defaults[i]);
            for(int i=0;i<4;i++){
                if(placements[i]!=null)continue;
                var choices=from side in new[]{defaults[i]}.Concat(sides).Distinct()
                            from fraction in new[]{.5,0.0,1.0}
                            let outer=DeviceLayout.DefaultDock(side)
                            select new DockPlace{Device=outer.Device,Side=side,Offset=fraction};
                foreach(var place in choices){
                    var tile=place.Tile(rects[Index(place)],i%2==0);
                    if(!Enumerable.Range(0,4).Any(j=>j!=i && (j<i || placements[j]!=null) && Tile(rects.Length+j).IntersectsWith(tile))){
                        docks[i]=place;break;
                    }
                }
            }
            // A missing or overlapping tablet gets an adjacent, separate slot whenever possible.
            if((copy.Pad==null || Tile(rects.Length).IntersectsWith(Tile(rects.Length+2))) && !docks[0].MobileParent){
                var originalPad=docks[2];
                foreach(var side in new[]{docks[0].Side}.Concat(sides).Distinct()){
                    docks[2]=new DockPlace{Device="@phone",Side=side};
                    if(ClearLayout())break;docks[2]=originalPad;
                }
            }
            var toggle=new SoftButton();var drawer=new OptionDrawer(toggle);
            foreach(var box in new[]{phoneChoice,padChoice}){
                string id=box==phoneChoice?copy.PhoneId:copy.PadId;
                box.Items.Add(new Mobile{Name="未指定"});foreach(var m in mobiles)box.Items.Add(m);
                if(id!=null && !mobiles.Any(m=>m.Id==id))box.Items.Add(new Mobile{Name="已绑定（离线）",Id=id});
                box.SelectedItem=box.Items.Cast<Mobile>().First(m=>m.Id==id);
                drawer.AddOptions(new Label{Text=box==phoneChoice?"iPhone":"iPad",TextAlign=ContentAlignment.BottomLeft,Padding=new Padding(8,0,8,2)},box);
            }
            var save=new SoftButton{Text="应用布局"};var cancel=new SoftButton{Text="取消",DialogResult=DialogResult.Cancel};
            BrandUi.ButtonStyle(save,true);BrandUi.ButtonStyle(cancel);drawer.AddOptions(save,cancel);
            hint.Dock=DockStyle.Bottom;hint.Height=48;hint.AutoSize=false;hint.Padding=new Padding(20,6,20,6);
            hint.Text="把设备拖到相邻屏幕的接缝处插入，再应用布局。";
            Controls.Add(canvas);Controls.Add(drawer);Controls.Add(hint);Controls.Add(BrandUi.Header("屏幕布局",toggle));CancelButton=cancel;
            canvas.Paint+=(s,e)=>Draw(e.Graphics);canvas.Resize+=(s,e)=>{Fit();canvas.Invalidate();};
            canvas.MouseDown+=(s,e)=>{
                if(e.Button!=MouseButtons.Left)return;
                Point world=World(e.Location);
                for(int i=rects.Length+3;i>=0;i--)if(Tile(i).Contains(world)){drag=i;initialDocks=(DockPlace[])docks.Clone();grab=new Point(world.X-Tile(i).X,world.Y-Tile(i).Y);mouseDown=e.Location;canvas.Capture=true;break;}
            };
            canvas.MouseMove+=(s,e)=>{if(drag>=0 && e.Location!=mouseDown){MoveTile(World(e.Location));Fit();canvas.Invalidate();}};
            canvas.MouseUp+=(s,e)=>{drag=-1;canvas.Capture=false;Fit();canvas.Invalidate();};
            save.Click+=(s,e)=>Commit();
            Shown+=(s,e)=>{Fit();canvas.Invalidate();};
        }
        int Index(DockPlace d){return Array.FindIndex(screens,s=>s.DeviceName==d.Device);}
        Rectangle Tile(int i){
            if(i<rects.Length)return rects[i];
            return DeviceLayout.Resolve(i-rects.Length,docks,screens.Select(s=>s.DeviceName).ToArray(),rects);
        }
        bool ClearLayout(){
            int count=rects.Length+4;
            if(Enumerable.Range(0,count).Any(i=>Tile(i).IsEmpty))return false;
            for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)
                if(Tile(i).IntersectsWith(Tile(j)))return false;
            return true;
        }
        string TileName(int index){
            return index<rects.Length?screens[index].DeviceName.Replace(@"\\.\DISPLAY","屏幕 "):new[]{"iPhone","Mac","iPad","Windows"}[index-rects.Length];
        }
        // Map shared boundaries once so growing narrow cards preserves adjacency and hit testing.
        sealed class PreviewAxis {
            readonly int[] edges;
            readonly double[] pixels;
            public PreviewAxis(int[] starts,int[] ends,int[] minimum,int viewport,double scale){
                edges=starts.Concat(ends).Distinct().OrderBy(x=>x).ToArray();
                pixels=new double[edges.Length];
                // Reserve each card's text width/height first; the other space follows screen proportions.
                double low=0,high=scale;
                for(int pass=0;pass<18;pass++){
                    double trial=(low+high)/2;
                    for(int j=1;j<edges.Length;j++){
                        double next=pixels[j-1]+(edges[j]-edges[j-1])*trial;
                        for(int i=0;i<ends.Length;i++)if(ends[i]==edges[j])
                            next=Math.Max(next,pixels[Array.BinarySearch(edges,starts[i])]+minimum[i]);
                        pixels[j]=next;
                    }
                    if(pixels[pixels.Length-1]>viewport-20)high=trial;else low=trial;
                }
                double offset=Math.Max(8,(viewport-pixels[pixels.Length-1])/2);
                for(int j=0;j<pixels.Length;j++)pixels[j]+=offset;
            }
            public int Pixel(int boundary){return (int)Math.Round(pixels[Array.BinarySearch(edges,boundary)]);}
            public int World(int pixel){
                int j=Array.BinarySearch(pixels,(double)pixel);
                if(j>=0)return edges[j];
                j=Math.Max(1,Math.Min(edges.Length-1,~j));
                return (int)Math.Round(edges[j-1]+(pixel-pixels[j-1])*(edges[j]-edges[j-1])/(pixels[j]-pixels[j-1]));
            }
        }
        void Fit(){
            var tiles=Enumerable.Range(0,rects.Length+4).Select(Tile).ToArray();
            var span=tiles.Aggregate(Rectangle.Union);
            zoom=Math.Max(.01f,Math.Min((canvas.Width-20f)/span.Width,(canvas.Height-20f)/span.Height)*.94f);
            Size[] labels;
            using(var g=canvas.CreateGraphics())labels=Enumerable.Range(0,tiles.Length)
                .Select(i=>TextRenderer.MeasureText(g,TileName(i),Font,Size.Empty,TextFormatFlags.NoPrefix)).ToArray();
            horizontalAxis=new PreviewAxis(tiles.Select(t=>t.Left).ToArray(),tiles.Select(t=>t.Right).ToArray(),
                labels.Select(s=>s.Width+20).ToArray(),canvas.Width,zoom);
            verticalAxis=new PreviewAxis(tiles.Select(t=>t.Top).ToArray(),tiles.Select(t=>t.Bottom).ToArray(),
                labels.Select(s=>s.Height+14).ToArray(),canvas.Height,zoom);
        }
        Point World(Point p){return new Point(horizontalAxis.World(p.X),verticalAxis.World(p.Y));}
        void Draw(Graphics g){
            if(zoom<=0)return;
            using(var dots=new SolidBrush(Color.FromArgb(224,226,231))){
                for(int x=16;x<canvas.Width;x+=32)for(int y=16;y<canvas.Height;y+=32)g.FillEllipse(dots,x,y,2,2);
            }
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var flags=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix;
            for(int i=0;i<rects.Length+4;i++){
                var r=Tile(i);string name=TileName(i);
                var box=Rectangle.FromLTRB(horizontalAxis.Pixel(r.Left),verticalAxis.Pixel(r.Top),horizontalAxis.Pixel(r.Right),verticalAxis.Pixel(r.Bottom));
                if(box.Width<4 || box.Height<4)continue;
                box.Inflate(-2,-2);if(box.Width<5 || box.Height<5)continue;
                int corner=Math.Min(i<rects.Length?20:16,Math.Min(box.Width,box.Height)/2);
                using(var shape=new System.Drawing.Drawing2D.GraphicsPath())
                using(var fill=new SolidBrush(i==drag?Color.FromArgb(223,237,255):i<rects.Length?Color.White:(i-rects.Length)%2==0?Color.FromArgb(225,237,248):Color.FromArgb(232,230,242)))
                using(var rim=new Pen(i==drag?BrandUi.Blue:Color.FromArgb(205,210,219),i==drag?2:1)){
                    int d=corner*2;
                    shape.AddArc(box.X,box.Y,d,d,180,90);shape.AddArc(box.Right-d,box.Y,d,d,270,90);
                    shape.AddArc(box.Right-d,box.Bottom-d,d,d,0,90);shape.AddArc(box.X,box.Bottom-d,d,d,90,90);shape.CloseFigure();
                    g.FillPath(fill,shape);g.DrawPath(rim,shape);
                }
                box.Inflate(-5,-3);
                string label=name;
                if(i<rects.Length){
                    string detail=r.Width+" × "+r.Height+(screens[i].Primary?" · 主屏":"");
                    var both=TextRenderer.MeasureText(g,name+"\n"+detail,Font,Size.Empty,flags);
                    if(both.Width<=box.Width && both.Height<=box.Height)label+="\n"+detail;
                }
                TextRenderer.DrawText(g,label,Font,box,BrandUi.Navy,flags);
            }
        }
        static int Clamp(int n,int low,int high){return Math.Max(low,Math.Min(high,n));}
        internal static bool ProtectedSeam(Rectangle[] displays,Point pointer,int radius){
            for(int i=0;i<displays.Length;i++)for(int j=i+1;j<displays.Length;j++){
                var a=displays[i];var b=displays[j];string side=DeviceLayout.Touching(a,b);
                if(side==null)continue;
                if(side=="left" || side=="right"){
                    if(Math.Abs(pointer.X-(side=="left"?a.Left:a.Right))<=radius && pointer.Y>=Math.Max(a.Top,b.Top) && pointer.Y<Math.Min(a.Bottom,b.Bottom))return true;
                }else if(Math.Abs(pointer.Y-(side=="top"?a.Top:a.Bottom))<=radius && pointer.X>=Math.Max(a.Left,b.Left) && pointer.X<Math.Min(a.Right,b.Right))return true;
            }
            return false;
        }
        void MoveTile(Point world){
            Point wanted=new Point(world.X-grab.X,world.Y-grab.Y);
            double best=double.MaxValue;
            bool physical=drag<rects.Length;
            Rectangle chosen=Tile(drag);DockPlace picked=null;
            if(!physical){
                Array.Copy(initialDocks,docks,4);
                if(ProtectedSeam(rects,world,(int)Math.Ceiling(7/zoom))){hint.Text="本机屏与外接屏之间保留 Windows 的直接通道。";return;}
                hint.Text="松手保留摆放，点击「应用布局」同步到已连接的电脑。";
                int k=drag-rects.Length;string key=DeviceLayout.Keys[k];
                // Close the old gap before evaluating a new insertion against the drag-start layout.
                for(int i=0;i<4;i++)if(docks[i].Device==key)
                    docks[i]=new DockPlace{Device=docks[k].Device,Side=docks[k].Side,Offset=docks[k].Offset};
                var baseDocks=(DockPlace[])docks.Clone();DockPlace[] winner=null;bool seamSelected=false;

                for(int j=0;j<rects.Length+4;j++){
                    if(j==drag)continue;
                    var anchor=Tile(j);string parent=j<rects.Length?screens[j].DeviceName:DeviceLayout.Keys[j-rects.Length];
                    foreach(string side in sides){
                        Array.Copy(baseDocks,docks,4);
                        var dock=new DockPlace{Device=parent,Side=side};docks[k]=dock;
                        var r=Tile(drag);if(r.IsEmpty)continue;
                        bool horizontal=side=="right"||side=="left";
                        int span=horizontal?anchor.Height-r.Height:anchor.Width-r.Width;
                        int offset=horizontal?wanted.Y-anchor.Y:wanted.X-anchor.X;
                        dock.Offset=span==0?.5:Math.Max(0,Math.Min(1,offset/(double)span));r=Tile(drag);
                        bool overSeam=false;
                        for(int i=0;i<4;i++){
                            if(i==k || rects.Length+i==j)continue;
                            var neighbour=Tile(rects.Length+i);
                            if(DeviceLayout.Touching(anchor,neighbour)!=side || !neighbour.IntersectsWith(r))continue;
                            // Moving an ancestor under one of its descendants would create a cycle.
                            int ancestor=j-rects.Length;
                            while(ancestor>=0 && ancestor!=i)ancestor=Array.IndexOf(DeviceLayout.Keys,docks[ancestor].Device);
                            if(ancestor==i)continue;
                            int axis=horizontal?world.Y:world.X;
                            int edge=side=="left"?anchor.Left:side=="right"?anchor.Right:side=="top"?anchor.Top:anchor.Bottom;
                            overSeam|=Math.Abs((horizontal?world.X:world.Y)-edge)*zoom<=14 &&
                                axis>=(horizontal?Math.Max(anchor.Top,neighbour.Top):Math.Max(anchor.Left,neighbour.Left)) &&
                                axis<(horizontal?Math.Min(anchor.Bottom,neighbour.Bottom):Math.Min(anchor.Right,neighbour.Right));
                            docks[i]=new DockPlace{Device=key,Side=side,Offset=docks[i].Offset};
                        }
                        if(!ClearLayout() || (seamSelected && !overSeam))continue;
                        double x=r.X-wanted.X,y=r.Y-wanted.Y,dist=x*x+y*y;
                        if((overSeam && !seamSelected) || dist<best){best=dist;winner=(DockPlace[])docks.Clone();seamSelected=overSeam;}
                    }
                    Array.Copy(baseDocks,docks,4);
                }
                if(winner==null){
                    Array.Copy(initialDocks,docks,4);
                    for(int j=0;j<rects.Length+4;j++){
                        if(j==drag)continue;
                        string parent=j<rects.Length?screens[j].DeviceName:DeviceLayout.Keys[j-rects.Length];
                        foreach(string side in sides){
                            docks[k]=new DockPlace{Device=parent,Side=side};
                            var r=Tile(drag);if(r.IsEmpty || !ClearLayout())continue;
                            double x=r.X-wanted.X,y=r.Y-wanted.Y,dist=x*x+y*y;
                            if(dist<best){best=dist;winner=(DockPlace[])docks.Clone();}
                        }
                    }
                }
                Array.Copy(winner??initialDocks,docks,4);return;
            }
            for(int j=0;j<rects.Length;j++){
                if(j==drag)continue;
                var anchor=rects[j];
                foreach(string side in sides){
                    bool horizontal=side=="left"||side=="right";
                    var dock=new DockPlace{Device=screens[j].DeviceName,Side=side};
                    Rectangle r=rects[drag];
                    if(horizontal){r.X=side=="left"?anchor.Left-r.Width:anchor.Right;r.Y=Clamp(wanted.Y,anchor.Top-r.Height+1,anchor.Bottom-1);if(Math.Abs(r.Y-anchor.Y)*zoom<12)r.Y=anchor.Y;}
                    else{r.Y=side=="top"?anchor.Top-r.Height:anchor.Bottom;r.X=Clamp(wanted.X,anchor.Left-r.Width+1,anchor.Right-1);if(Math.Abs(r.X-anchor.X)*zoom<12)r.X=anchor.X;}
                    var old=rects[drag];rects[drag]=r;bool clear=ClearLayout();rects[drag]=old;if(!clear)continue;
                    double x=r.X-wanted.X,y=r.Y-wanted.Y,dist=x*x+y*y;
                    if(dist<best){best=dist;chosen=r;picked=dock;}
                }
            }
            if(picked!=null)rects[drag]=chosen;
        }
        internal static bool CheckInsertion(){
            var mac=DeviceLayout.DefaultDock("left");var phone=DeviceLayout.DefaultDock("right");
            var model=new DeviceLayout{Phone=phone,Mac=mac,Pad=new DockPlace{Device="@phone",Side="right"},PC=DeviceLayout.DefaultDock("top")};
            using(var editor=new LayoutEditor(model,new Mobile[0])){
                int count=editor.rects.Length;
                editor.drag=count;editor.initialDocks=(DockPlace[])editor.docks.Clone();
                var r=editor.Tile(count);editor.grab=new Point(r.Width/2,r.Height/2);editor.zoom=.15f;
                var target=editor.Tile(count+1);var seam=new Point(target.Right,target.Top+target.Height/2);
                editor.MoveTile(seam);editor.MoveTile(seam);
                return editor.ClearLayout() && editor.docks[1].Device=="@phone" &&
                    DeviceLayout.Touching(editor.Tile(count),editor.Tile(count+1))=="left" &&
                    DeviceLayout.Touching(editor.rects[editor.Index(mac)],editor.Tile(count))=="left" &&
                    editor.original.SequenceEqual(editor.rects);
            }
        }
        void Commit(){
            var mac=docks[1];var phoneId=((Mobile)phoneChoice.SelectedItem).Id;var padId=((Mobile)padChoice.SelectedItem).Id;
            if(phoneId!=null && phoneId==padId){hint.Text="同一台蓝牙设备不能绑定两个位置。";return;}
            // Lan Mouse addresses peers by logical side; Windows gates map that slot to a physical edge segment.
            var pc=docks[3];
            pc.Slot=pc.Side!=mac.Side?pc.Side:sides.First(v=>v!=mac.Side);
            for(int i=0;i<4;i++){
                var tile=Tile(rects.Length+i);
                if(rects.Any(r=>r.IntersectsWith(tile)) || Enumerable.Range(0,i).Any(j=>tile.IntersectsWith(Tile(rects.Length+j)))){
                    hint.Text="卡片重叠，请沿屏幕边缘错开。";return;
                }
            }
            int primary=Array.FindIndex(screens,s=>s.Primary);
            var desired=rects.Select(r=>new Rectangle(r.X-rects[primary].X,r.Y-rects[primary].Y,r.Width,r.Height)).ToArray();
            bool changed=!desired.SequenceEqual(original);
            string[] names=screens.Select(s=>s.DeviceName).ToArray();
            if(!screens.Select((s,i)=>Screen.AllScreens.Any(now=>now.DeviceName==s.DeviceName && now.Bounds==original[i] && now.Primary==s.Primary)).All(match=>match) || names.Length!=Screen.AllScreens.Length){hint.Text="显示器连接已变化，请重新打开布局。";return;}
            try{
                if(changed){
                    WindowsDisplays.Apply(names,desired);
                    using(var prompt=new Form{Text="保留显示排列？",StartPosition=FormStartPosition.CenterScreen,ClientSize=new Size(350,90),TopMost=true})
                    using(var timer=new Timer{Interval=1000}){
                        var keep=new SoftButton{Text="保留",DialogResult=DialogResult.OK,Dock=DockStyle.Left};
                        var undo=new SoftButton{Text="还原",DialogResult=DialogResult.Cancel,Dock=DockStyle.Right};
                        prompt.Controls.Add(keep);prompt.Controls.Add(undo);int seconds=15;
                        timer.Tick+=(s,e)=>{prompt.Text="保留显示排列？ "+(--seconds)+" 秒后还原";if(seconds<=0)prompt.DialogResult=DialogResult.Cancel;};
                        timer.Start();if(prompt.ShowDialog()!=DialogResult.OK){WindowsDisplays.Apply(names,original);return;}
                    }
                }
                var result=new DeviceLayout{Phone=docks[0],Mac=mac,Pad=docks[2],PC=docks[3],PhoneId=phoneId,PadId=padId};result.Save();Result=result;DialogResult=DialogResult.OK;
            }catch(Exception ex){
                if(changed){try{WindowsDisplays.Apply(names,original);}catch(Exception restore){MessageBox.Show(this,"还原失败："+restore.Message);}}
                MessageBox.Show(this,ex.Message);
            }
        }
    }
}
