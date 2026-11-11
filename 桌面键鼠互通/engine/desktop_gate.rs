use super::{display_util, Position, RECT};
use std::{path::PathBuf, sync::{LazyLock, RwLock}, time::Duration};

#[derive(Clone, Copy)]
struct Gate { target:Option<(i32,i32)>, virtual_gate: bool, slot: Position, side: Position, anchor: [i32;4], start:i32, end:i32 }
fn parse(text:&str)->Vec<Gate>{
    text.lines().filter_map(|line|{
        let mut words=line.split_whitespace();
        let direction=|v:&str|Some(match v{"left"=>Position::Left,"right"=>Position::Right,"top"=>Position::Top,"bottom"=>Position::Bottom,_=>return None});
        let first=words.next()?;let physical=first=="physical";let key=if physical{words.next()?}else{first};let virtual_gate=key.starts_with('@');let slot=direction(key.trim_start_matches('@'))?;let side=direction(words.next()?)?;
        let values:Vec<i32>=words.map(str::parse).collect::<Result<_,_>>().ok()?;
        let [x,y,r,b,start,end,..]=values.as_slice() else{return None};
        let target=if physical{Some((*values.get(6)?,*values.get(7)?))}else{None};
        Some(Gate{target,virtual_gate,slot,side,anchor:[*x,*y,*r,*b],start:*start,end:*end})
    }).collect()
}
static GATES:LazyLock<RwLock<Vec<Gate>>>=LazyLock::new(||{
    let path=std::env::var_os("LOCALAPPDATA").map(PathBuf::from).map(|p|p.join("EdgeMouse").join("desktop-edges.txt"));
    let load=move ||path.as_ref().and_then(|p|std::fs::read_to_string(p).ok()).map(|s|parse(&s)).unwrap_or_default();
    let initial=load();
    std::thread::spawn(move||loop{
        std::thread::sleep(Duration::from_millis(400));
        *GATES.write().expect("layout lock")=load();
    });
    RwLock::new(initial)
});
impl Gate{
    fn crosses(&self,(px,py):(i32,i32),(x,y):(i32,i32))->bool{
        let [l,t,r,b]=self.anchor;
        if px<l || px>=r || py<t || py>=b{return false}
        let (axis,across)=match self.side{
            Position::Left=>(y,x<px && x<=l),
            Position::Right=>(y,x>px && x>=r-1),
            Position::Top=>(x,y<py && y<=t),
            Position::Bottom=>(x,y>py && y>=b-1)
        };
        across && axis>=self.start && axis<self.end
    }
}
pub(super) fn entered(prev:(i32,i32),curr:(i32,i32),displays:&[RECT])->Option<Position>{
    let gates=GATES.read().expect("layout lock");
    let mirror=crate::peer_routes::ROUTES.read().unwrap();
    if let Some(b)=displays.iter().find(|r|r.left<=0 && r.right>0 && r.top<=0 && r.bottom>0){
        for r in mirror.iter(){
            let vertical=matches!(r.side,Position::Left|Position::Right);
            let (start,length,axis)=if vertical{(b.top,b.bottom-b.top,curr.1)}else{(b.left,b.right-b.left,curr.0)};
            let gate=Gate{slot:r.slot,side:r.side,virtual_gate:false,target:None,anchor:[b.left,b.top,b.right,b.bottom],start:start+(r.lo*length as f64).round() as i32,end:start+(r.hi*length as f64).round() as i32};
            if gate.crosses(prev,curr){crate::record_crossing(Some(crate::peer_routes::encode(2,r.side,(axis-start) as f64/length as f64)));return Some(r.slot)}
        }
    }
    for g in gates.iter().rev().filter(|g|!mirror.iter().any(|r|r.slot==g.slot)){
        if !g.virtual_gate && displays.iter().any(|r|[r.left,r.top,r.right,r.bottom]==g.anchor) && g.crosses(prev,curr){
            let along=if matches!(g.side,Position::Left|Position::Right){curr.1}else{curr.0};
            let crossing=if let Some((origin,length))=g.target{
                let opposite=crate::peer_routes::SIDES[crate::peer_routes::index(g.side)^1];
                crate::peer_routes::encode(6,opposite,(along-origin) as f64/length as f64)
            }else{(along-g.start) as f64/(g.end-g.start).max(1) as f64};
            crate::record_crossing(Some(crossing));return Some(g.slot)
        }
    }
    crate::record_crossing(None);
    display_util::entered_barrier(prev,curr,displays).filter(|side|!gates.iter().any(|g|g.slot==*side || g.side==*side))
}
#[cfg(test)]
mod tests{
 use super::*;
 #[test]fn offset_display_segment(){let gates=parse("left top 0 0 1920 1080 300 900");let g=&gates[0];assert!(g.crosses((310,1),(310,0)));assert!(!g.crosses((920,1),(920,0)));assert!(!g.crosses((310,0),(310,1)));}
}


pub(super) fn arrival(slot:Position,ratio:f64)->Option<(i32,i32)>{
    let gates=GATES.read().unwrap();let g=gates.iter().find(|g|g.slot==slot)?;
    let [l,t,r,b]=g.anchor;if l>=r||t>=b||g.end<=g.start{return None}
    let along=g.start+((g.end-g.start-1) as f64*ratio).round() as i32;
    Some(match g.side{Position::Left=>(l+4,along),Position::Right=>(r-5,along),Position::Top=>(along,t+4),Position::Bottom=>(along,b-5)})
}

pub(super) fn virtual_gate(slot:Position)->bool{GATES.read().unwrap().iter().any(|g|g.slot==slot && g.virtual_gate)}
