using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace EdgeMouse {
    // Set-1 scan codes identify physical keys across Windows and injected Mac input.
    sealed class MobileKeyboard {
        static readonly byte[] ordinary={
            0,41,30,31,32,33,34,35,36,37,38,39,45,46,42,43,
            20,26,8,21,23,28,24,12,18,19,47,48,40,224,4,22,
            7,9,10,11,13,14,15,51,52,53,225,49,29,27,6,25,
            5,17,16,54,55,56,229,85,226,44,57,58,59,60,61,62,
            63,64,65,66,67,83,71,95,96,97,86,92,93,94,87,89,
            90,91,98,99,0,0,100,68,69
        };
        static byte Usage(int scan,bool extended,int vk){
            if(vk==0x13)return 72;
            if(vk==0x2c)return 70;
            if(!extended)return scan>=0 && scan<ordinary.Length?ordinary[scan]:(byte)0;
            switch(scan){
                case 0x1c:return 88;case 0x1d:return 228;case 0x35:return 84;case 0x38:return 230;
                case 0x47:return 74;case 0x48:return 82;case 0x49:return 75;case 0x4b:return 80;
                case 0x4d:return 79;case 0x4f:return 77;case 0x50:return 81;case 0x51:return 78;
                case 0x52:return 73;case 0x53:return 76;case 0x5b:return 227;case 0x5c:return 231;
                case 0x5d:return 101;default:return 0;
            }
        }
        readonly SortedSet<byte> held=new SortedSet<byte>();
        public bool AnyHeld {get{return held.Count!=0;}}
        public byte[] Change(int scan,bool extended,int vk,bool down){
            byte key=Usage(scan,extended,vk);
            if(key==0 || !(down?held.Add(key):held.Remove(key)))return null;
            var report=new byte[8];int n=2;
            foreach(byte k in held){
                if(k>=224)report[0]|=(byte)(1<<(k-224));
                else if(n<8)report[n++]=k;
            }
            if(held.Count(k=>k<224)>6)for(int i=2;i<8;i++)report[i]=1;
            return report;
        }
        delegate IntPtr KeyProc(int code,IntPtr msg,IntPtr data);
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,KeyProc proc,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr msg,IntPtr data);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
        public static bool AnyDesktopKeyDown(){
            for(int vk=8;vk<255;vk++)if(GetAsyncKeyState(vk)<0)return true;
            return false;
        }
        KeyProc proc;IntPtr hook;
        public void Start(Action<byte[]> emit){
            held.Clear();
            proc=(code,msg,data)=>{
                if(code>=0){
                    int vk=Marshal.ReadInt32(data);
                    if(vk!=0x77){ // F8 is reserved by the capture window.
                        int flags=Marshal.ReadInt32(data,8);
                        var report=Change(Marshal.ReadInt32(data,4),(flags&1)!=0,vk,(flags&128)==0);
                        if(report!=null)emit(report);
                        return new IntPtr(1);
                    }
                }
                return CallNextHookEx(hook,code,msg,data);
            };
            hook=SetWindowsHookEx(13,proc,GetModuleHandle(null),0);
            if(hook==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
        }
        public void Stop(){
            if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}
            held.Clear();
        }
    }
}
