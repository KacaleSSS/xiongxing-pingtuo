using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

[assembly: AssemblyTitle("雄兴屏拓")]
[assembly: AssemblyProduct("雄兴屏拓")]
[assembly: AssemblyCompany("ZZXX CloudTech")]
[assembly: AssemblyVersion("6.1.1.0")]

namespace EdgeMouse {
    static class RuntimeBundle {
        static string Digest(string path){
            using(var file=File.OpenRead(path))
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant();
        }
        internal static string Component(string name){
            using(var stream=typeof(RuntimeBundle).Assembly.GetManifestResourceStream("runtime.zip"))
            using(var package=new ZipArchive(stream,ZipArchiveMode.Read)){
                var entry=package.Entries.Single(e=>e.Name==name);
                string hash=entry.FullName.Split('/')[0];
                string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EdgeMouse","components",hash);
                string path=Path.Combine(dir,name);
                if(File.Exists(path) && Digest(path)==hash)return path;
                Directory.CreateDirectory(dir);
                string temp=Path.Combine(dir,Guid.NewGuid().ToString("N")+".tmp");
                try{
                    using(var input=entry.Open())
                    using(var output=File.Create(temp))input.CopyTo(output);
                    if(Digest(temp)!=hash)throw new InvalidDataException(name);
                    try{
                        if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
                    }catch(IOException){
                        // Another instance may have installed the same verified component.
                        if(!File.Exists(path) || Digest(path)!=hash)throw;
                    }
                    return path;
                }finally{File.Delete(temp);}
            }
        }
    }
}
