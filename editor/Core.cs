using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace KazeEditor {
 public class UserError:Exception { public UserError(string s):base(s){} }
 public class Reloc { public int Offset;public string Kind;public uint Value; }
 public class Spec {
  public int Disc,PrefixSize,DataOffset,OldDataSize,HeapOffset; public uint Base,BssSize;
  public string ExeSha,PrefixSha,Code,Chars,BlobSha,CodeSha;public string[] MenuText;
  public List<Reloc> CodeRelocs,PrefixRelocs;public Dictionary<string,string> Titles;
 }
 public class Manifest { public int Format;public List<Spec> Specs; }
 public class Cue {
  public string Id,Text,OriginalText;public int Clip,Start,End,OriginalStart,OriginalEnd;
  public bool Changed { get { return Text!=OriginalText||Start!=OriginalStart||End!=OriginalEnd; } }
  public Cue Copy(){return (Cue)MemberwiseClone();}
 }
 public class Project {
  public int Version=1,Disc;public string Source,SourceExeSha; public List<Cue> Cues=new List<Cue>();
 }
 public class RomMeta {
  public int Version=1,Disc,DataSize;public string Chars,BodySha;public List<string> Ids;
 }
 public class Glyph { public byte Advance; public byte[] Bits; public Bitmap Image; }
 public static class Data {
  public static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=64*1024*1024,RecursionLimit=100};}
  public static T ReadJson<T>(string p){return Json().Deserialize<T>(File.ReadAllText(p,Encoding.UTF8));}
  public static void WriteJson(string p,object o) {
   string dir=Path.GetDirectoryName(Path.GetFullPath(p));Directory.CreateDirectory(dir);
   string tmp=Path.Combine(dir,"."+Path.GetFileName(p)+"."+Guid.NewGuid().ToString("N")+".tmp");
   File.WriteAllText(tmp,Json().Serialize(o),new UTF8Encoding(false));
   if(File.Exists(p)){string bak=p+".bak";File.Replace(tmp,p,bak,true);}else File.Move(tmp,p);
  }
  public static byte[] Resource(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){
   if(s==null)throw new Exception("Missing resource: "+name);using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}}
  public static byte[] Unzip(byte[] bytes){using(var s=new GZipStream(new MemoryStream(bytes),CompressionMode.Decompress))using(var m=new MemoryStream()){var b=new byte[65536];int n;while((n=s.Read(b,0,b.Length))>0){if(m.Length+n>16*1024*1024)throw new UserError("압축된 편집 정보가 허용 크기를 넘습니다.");m.Write(b,0,n);}return m.ToArray();}}
  public static byte[] Zip(byte[] bytes){using(var m=new MemoryStream()){using(var z=new GZipStream(m,CompressionMode.Compress,true))z.Write(bytes,0,bytes.Length);return m.ToArray();}}
  public static string Sha(byte[] b){using(var h=SHA256.Create())return Hex(h.ComputeHash(b));}
  public static string FileSha(string p){using(var h=SHA256.Create())using(var s=File.OpenRead(p))return Hex(h.ComputeHash(s));}
  public static string Hex(byte[] b){return BitConverter.ToString(b).Replace("-","").ToLowerInvariant();}
  public static uint U32(byte[] b,int p){return BitConverter.ToUInt32(b,p);}
  public static ushort U16(byte[] b,int p){return BitConverter.ToUInt16(b,p);}
  public static uint BE32(byte[] b,int p){return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
  public static int BE16(byte[] b,int p){return b[p]*256+b[p+1];}
  public static void Put(byte[] b,int p,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,p,4);}
  public static void PutBE(byte[] b,int p,uint v){for(int i=0;i<4;i++)b[p+i]=(byte)(v>>(24-8*i));}
  public static byte[] Slice(byte[] b,int p,int n){var x=new byte[n];Buffer.BlockCopy(b,p,x,0,n);return x;}
  public static int Align(int n,int a){return (n+a-1)&~(a-1);}
  public static string Time(int ms){return string.Format(CultureInfo.InvariantCulture,"{0:00}:{1:00}.{2:000}",ms/60000,ms/1000%60,ms%1000);}
  public static Manifest Manifest;public static Dictionary<char,Glyph> Glyphs;
  public static readonly ushort[] Palette={0x0000,0x3000,0x5000,0x8000,0xA000,0xD000,0xF000,0xF222,0xF444,0xF666,0xF777,0xF999,0xFBBB,0xFCCC,0xFEEE,0xFFFF};
  public static void Init(){if(Manifest!=null)return;Manifest=Json().Deserialize<Manifest>(Encoding.UTF8.GetString(Resource("manifest.json")));
   byte[] b=Unzip(Resource("glyphs.gz"));int n=(int)U32(b,0);Glyphs=new Dictionary<char,Glyph>(n);int p=4;
   for(int i=0;i<n;i++){char c=(char)U16(b,p);var g=new Glyph{Advance=b[p+2],Bits=Slice(b,p+3,392)};Glyphs.Add(c,g);p+=395;}if(p!=b.Length)throw new Exception("Invalid font resource");
  }
  public static Bitmap GlyphImage(char c){Glyph g;if(!Glyphs.TryGetValue(c,out g))throw new UserError("지원하지 않는 글자: "+c);
   if(g.Image!=null)return g.Image;var im=new Bitmap(28,28,PixelFormat.Format32bppArgb);
   for(int y=0;y<28;y++)for(int x=0;x<28;x++){int k=g.Bits[(y*28+x)/2];k=(x%2==0)?k&15:k>>4;int p=Palette[k];im.SetPixel(x,y,Color.FromArgb((p>>12)*17,((p>>8)&15)*17,((p>>4)&15)*17,(p&15)*17));}
   g.Image=im;return im;
  }
  public static int Width(string s){int w=0;foreach(char c in s){Glyph g;if(!Glyphs.TryGetValue(c,out g))throw new UserError("지원하지 않는 글자: "+c+" (U+"+((int)c).ToString("X4")+")");w+=g.Advance;}return w;}
  public static void CheckText(string text){if(string.IsNullOrWhiteSpace(text))throw new UserError("자막 내용을 입력하세요.");
   var lines=text.Split('\n');if(lines.Length>2)throw new UserError("게임 자막은 최대 두 줄입니다. 줄바꿈을 줄여 주세요.");
   foreach(var line in lines)if(Width(line)>632)throw new UserError("자막이 화면 폭을 넘습니다. 선택한 자막에 줄바꿈을 넣거나 문장을 줄여 주세요.");
  }
  public static Bitmap Preview(string text){var im=new Bitmap(640,64);using(var g=Graphics.FromImage(im)){
    g.Clear(Color.Transparent);string[] lines=text.Split('\n');int row=2-lines.Length;
    foreach(string line in lines){int x=(640-Width(line))/2;foreach(char c in line){g.DrawImageUnscaled(GlyphImage(c),x-3,row*28);x+=Glyphs[c].Advance;}row++;}}
   return im;
  }
  public static string Wrap(string text){string flat=Regex.Replace(text.Replace('\n',' '),@"\s+"," ").Trim();if(Width(flat)<=632)return flat;
   int best=-1,score=int.MaxValue;for(int i=1;i<flat.Length;i++){string a=flat.Substring(0,i).TrimEnd(),b=flat.Substring(i).TrimStart();int wa=Width(a),wb=Width(b);if(wa>632||wb>632)continue;
    int s=Math.Abs(wa-wb)+(flat[i-1]==' '?0:200);if(s<score){best=i;score=s;}}
   if(best<0)throw new UserError("두 줄에도 들어가지 않습니다. 문장을 줄여 주세요.");return flat.Substring(0,best).TrimEnd()+"\n"+flat.Substring(best).TrimStart();
  }
 }
 public class IsoEntry {public string Name;public int Lba,Size;public long Record;}
 public class Track:IDisposable {
  FileStream stream;public string Path;public Track(string path,bool write=false){Path=path;stream=new FileStream(path,FileMode.Open,write?FileAccess.ReadWrite:FileAccess.Read,FileShare.Read);}
  public byte[] Sector(int lba){byte[] b=new byte[2048];stream.Position=(long)(lba-45000)*2352+16;int got=stream.Read(b,0,b.Length);if(got!=2048)throw new UserError("디스크 트랙이 잘렸습니다.");return b;}
  public byte[] Read(int lba,int size){var b=new byte[size];for(int i=0;i<(size+2047)/2048;i++){var s=Sector(lba+i);Buffer.BlockCopy(s,0,b,i*2048,Math.Min(2048,size-i*2048));}return b;}
  public void WriteSector(int lba,byte[] data){byte[] raw=new byte[2352];KazeKR.Sector.Build(data,0,lba,raw);stream.Position=(long)(lba-45000)*2352;stream.Write(raw,0,2352);}
  public void WriteFile(int lba,byte[] b,int allocated){for(int i=0;i<(allocated+2047)/2048;i++){var s=new byte[2048];int n=Math.Min(2048,b.Length-i*2048);if(n>0)Buffer.BlockCopy(b,i*2048,s,0,n);WriteSector(lba+i,s);}stream.Flush(true);}
  public List<IsoEntry> Entries(){byte[] p=Sector(45016);if(Encoding.ASCII.GetString(p,1,5)!="CD001")throw new UserError("지원하지 않는 GD-ROM 파일 시스템입니다.");
   var result=new List<IsoEntry>();Walk((int)Data.U32(p,158),(int)Data.U32(p,166),"",result,0);return result;}
  void Walk(int lba,int size,string prefix,List<IsoEntry> list,int depth){if(depth>8||size<0||size>16*1024*1024)throw new UserError("잘못된 파일 시스템입니다.");var b=Read(lba,size);int p=0;
   while(p<size){int n=b[p];if(n==0){p=(p/2048+1)*2048;continue;}if(n<34||p+n>b.Length)throw new UserError("손상된 디렉터리입니다.");
    int namelen=b[p+32];string name=Encoding.ASCII.GetString(b,p+33,namelen).Split(';')[0];if(namelen!=1||b[p+33]>1){int start=(int)Data.U32(b,p+2),length=(int)Data.U32(b,p+10);
     if((b[p+25]&2)!=0)Walk(start,length,prefix+name+"/",list,depth+1);else list.Add(new IsoEntry{Name=prefix+name,Lba=start,Size=length,Record=(long)lba*2048+p});}p+=n;
   }}
  public void Dispose(){stream.Dispose();}
 }
 public class Clip {public int Id,Lba,Size,Rate,Samples,Offset;public string Title;public int Duration{get{return Rate>0?(int)Math.Round((double)Samples*1000/Rate):0;}} }
 public class ModuleResult {public byte[] Bytes;public uint Base,BssStart,BssEnd;}
 public sealed class Session:IDisposable {
  public Spec Spec;public Project Project;public string Work,SourceSha,TrackPath,SourceExeSha;public List<Clip> Clips;public byte[] Exe,Blob;public string Chars;public List<IsoEntry> Entries;
  string chdman;FileStream cacheLock;public Action<string,double> Progress=delegate{};public Action<string> Log=delegate{};
  public volatile bool Cancel;Process process;bool disposed;public bool IsBusy{get{return process!=null;}}
  public static string Root {get {string p=Environment.GetEnvironmentVariable("KAZE_EDITOR_WORK");return string.IsNullOrEmpty(p)?System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KazeSubtitleEditor"):p;}}
  public void Run(string stage,string dir,params string[] args){if(Cancel)throw new OperationCanceledException();
   string a=string.Join(" ",args.Select(x=>"\""+x.Replace('\\','/')+"\"").ToArray());
   var psi=new ProcessStartInfo(chdman,a){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=dir};
   var tail=new StringBuilder();var rx=new Regex(@"([0-9.]+)% complete");
   using(var p=Process.Start(psi)){process=p;
    Data.WriteJson(System.IO.Path.Combine(Work,"helper-process.json"),new{Pid=p.Id,Exe=chdman,Started=p.StartTime.ToUniversalTime().ToString("o"),Stage=stage});
    Action<string> capture=line=>{if(line==null)return;lock(tail){tail.AppendLine(line);if(tail.Length>20000)tail.Remove(0,10000);}var m=rx.Match(line);if(m.Success)Progress(stage,double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)/100);};
    p.OutputDataReceived+=(o,e)=>capture(e.Data);p.ErrorDataReceived+=(o,e)=>capture(e.Data);p.BeginOutputReadLine();p.BeginErrorReadLine();
    while(!p.WaitForExit(200)){if(Cancel){p.Kill();p.WaitForExit();process=null;throw new OperationCanceledException();}}p.WaitForExit();process=null;
    File.AppendAllText(System.IO.Path.Combine(Work,"chdman.log"),stage+"\r\n"+tail+"\r\n",Encoding.UTF8);
    if(p.ExitCode!=0)throw new UserError(stage+" 실패:\r\n"+tail.ToString().Substring(Math.Max(0,tail.Length-3000)));
   }Progress(stage,1);
  }
  public void Open(string input){Data.Init();input=System.IO.Path.GetFullPath(input);if(!File.Exists(input))throw new UserError("CHD 파일을 찾을 수 없습니다.");
   Progress("CHD 확인",0);SourceSha=Data.FileSha(input);Work=System.IO.Path.Combine(Root,"cache",SourceSha);Directory.CreateDirectory(Work);
   try{cacheLock=new FileStream(System.IO.Path.Combine(Work,"session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){throw new UserError("같은 CHD를 다른 편집기가 사용 중입니다. 해당 편집기를 닫은 뒤 열어 주세요.");}
   chdman=System.IO.Path.Combine(Work,"chdman.exe");byte[] tool=Data.Resource("chdman.exe");if(!File.Exists(chdman)||Data.FileSha(chdman)!=Data.Sha(tool))File.WriteAllBytes(chdman,tool);
   string marker=System.IO.Path.Combine(Work,"extracted.ok"),gdi=System.IO.Path.Combine(Work,"disc.gdi");
   string cachedTrack=System.IO.Path.Combine(Work,"disc03.bin");
   if(!File.Exists(marker)||!File.Exists(gdi)||!File.Exists(cachedTrack)||new FileInfo(cachedTrack).Length!=1185760800L||!File.Exists(System.IO.Path.Combine(Work,"disc01.bin"))||!File.Exists(System.IO.Path.Combine(Work,"disc02.raw"))){CheckSpace(Work,1500000000);Run("CHD 데이터 읽기",Work,"extractcd","-f","-i",input,"-o","disc.gdi","-ob","disc.bin");File.WriteAllText(marker,SourceSha);}
   string row=File.ReadAllLines(gdi).FirstOrDefault(x=>Regex.IsMatch(x,@"^3\s+45000\s+4\s+2352\s+"));if(row==null)throw new UserError("Rev A 드림캐스트 CHD 형식이 아닙니다.");
   var parts=Regex.Matches(row,"\"[^\"]+\"|\\S+").Cast<Match>().Select(x=>x.Value.Trim('"')).ToArray();TrackPath=System.IO.Path.Combine(Work,parts[4]);
   using(var track=new Track(TrackPath)){Entries=track.Entries();var entry=Entries.FirstOrDefault(x=>x.Name=="1ST_READ.BIN");if(entry==null)throw new UserError("게임 실행 파일이 없습니다.");Exe=track.Read(entry.Lba,entry.Size);}
   SourceExeSha=Data.Sha(Exe);var spec=Data.Manifest.Specs.FirstOrDefault(x=>x.ExeSha==SourceExeSha);RomMeta meta=null;int coreSize=Exe.Length;
   if(Exe.Length>12&&Encoding.ASCII.GetString(Exe,Exe.Length-8,8)=="KRSEDIT1"){
    int length=(int)Data.U32(Exe,Exe.Length-12);if(length<1||length>1000000||length>Exe.Length-12)throw new UserError("편집 정보가 손상됐습니다.");coreSize=Exe.Length-12-length;
    meta=Data.Json().Deserialize<RomMeta>(Encoding.UTF8.GetString(Data.Unzip(Data.Slice(Exe,coreSize,length))));
    if(meta.Version!=1||meta.BodySha!=Data.Sha(Data.Slice(Exe,0,coreSize)))throw new UserError("편집본의 무결성 확인에 실패했습니다.");spec=Data.Manifest.Specs.FirstOrDefault(x=>x.Disc==meta.Disc);
   }
   if(spec==null)throw new UserError("현재 도구는 사진·자막 동시 표시 v1.4 한글패치 CHD와 이 편집기가 저장한 CHD를 지원합니다. 다른 버전이나 일본어 원본은 열 수 없습니다.");Spec=spec;Chars=meta==null?Spec.Chars:meta.Chars;
   int blobSize=meta==null?Spec.OldDataSize:meta.DataSize;int blobAt=Spec.PrefixSize+Spec.DataOffset;
   if(blobSize<88||blobAt+blobSize>coreSize)throw new UserError("자막 데이터 범위가 올바르지 않습니다.");Blob=Data.Slice(Exe,blobAt,blobSize);
   ValidateBlob();var module=BuildModule(Blob);var normalized=Data.Slice(Exe,0,Spec.PrefixSize);RestorePrefix(normalized);
   if(Data.Sha(normalized)!=Spec.PrefixSha||!Data.Slice(Exe,Spec.PrefixSize,coreSize-Spec.PrefixSize).SequenceEqual(module.Bytes))throw new UserError("게임 코드가 지원하는 한글패치와 다릅니다. 안전하게 열 수 없습니다.");
   Project=new Project{Disc=Spec.Disc,Source=input,SourceExeSha=SourceExeSha};var ids=meta==null?null:meta.Ids;int ni=0;
   int nf=(int)Data.U32(Blob,24),ft=(int)Data.U32(Blob,28),ct=(int)Data.U32(Blob,32),tt=(int)Data.U32(Blob,36);
   for(int fid=0;fid<nf;fid++){int first=(int)Data.U32(Blob,ft+fid*8),count=(int)Data.U32(Blob,ft+fid*8+4);
    for(int i=0;i<count;i++){int p=ct+(first+i)*12,start=(int)Data.U32(Blob,p),end=(int)Data.U32(Blob,p+4),text=tt+(int)Data.U32(Blob,p+8);var s=new StringBuilder();
     while(true){ushort g=Data.U16(Blob,text);text+=2;if(g==0xFFFE)break;s.Append(g==0xFFFF?'\n':Chars[g]);}
     string id=ids==null?string.Format("d{0}:clip{1:0000}:cue{2:0000}",Spec.Disc,fid,i):ids[ni];ni++;
     Project.Cues.Add(new Cue{Id=id,Clip=fid,Start=start,End=end,Text=s.ToString(),OriginalText=s.ToString(),OriginalStart=start,OriginalEnd=end});
    }}if(ids!=null&&ids.Count!=ni)throw new UserError("자막 식별 정보 수가 다릅니다.");
   using(var track=new Track(TrackPath))Clips=ReadClips(track);Validate(Project,false);Progress("불러오기 완료",1);
  }
  void RestorePrefix(byte[] prefix){int oldImage=Data.Align(Spec.DataOffset+Spec.OldDataSize+4,32);var old=new ModuleResult{Base=Spec.Base,BssStart=Spec.Base+(uint)oldImage,BssEnd=Spec.Base+(uint)oldImage+Spec.BssSize};
   foreach(var r in Spec.PrefixRelocs)Data.Put(prefix,r.Offset,Resolve(r,old,Spec.OldDataSize));Data.Put(prefix,Spec.HeapOffset,Spec.Base&0xFFFFFFF);}
  void ValidateBlob(){if(Chars==null||Chars.Length==0||Chars.Length>65000||Chars.Distinct().Count()!=Chars.Length)throw new UserError("글자표가 잘못됐습니다.");
   int n=(int)Data.U32(Blob,0),cw=(int)Data.U32(Blob,4),ch=(int)Data.U32(Blob,8),adv=(int)Data.U32(Blob,16),glyph=(int)Data.U32(Blob,20),nf=(int)Data.U32(Blob,24),ft=(int)Data.U32(Blob,28),ct=(int)Data.U32(Blob,32),tt=(int)Data.U32(Blob,36);
   if(n!=Chars.Length||cw!=28||ch!=28||adv!=88||nf<6||nf>1000||ft<glyph+n*392||ct<ft+nf*8||tt<ct||tt>Blob.Length)throw new UserError("자막 헤더가 손상됐습니다.");
   for(int i=0;i<n;i++){Glyph g;if(!Data.Glyphs.TryGetValue(Chars[i],out g)||g.Advance!=Blob[adv+i]||!g.Bits.SequenceEqual(Data.Slice(Blob,glyph+i*392,392)))throw new UserError("글리프가 지원하는 글꼴과 다릅니다.");}
   int expected=0;for(int i=0;i<nf;i++){int first=(int)Data.U32(Blob,ft+8*i),count=(int)Data.U32(Blob,ft+8*i+4);if(first!=expected||count<0||ct+(long)(first+count)*12>tt)throw new UserError("자막 표가 손상됐습니다.");expected+=count;
    int last=-1;for(int j=0;j<count;j++){int p=ct+(first+j)*12,s=(int)Data.U32(Blob,p),e=(int)Data.U32(Blob,p+4);long t=tt+(long)Data.U32(Blob,p+8);if(s<last||s<0||e<=s||t<tt||t>=Blob.Length-1)throw new UserError("자막 시간이 손상됐습니다.");last=s;bool ended=false;
     for(;t+1<Blob.Length;t+=2){int g=Data.U16(Blob,(int)t);if(g==0xFFFE){ended=true;break;}if(g!=0xFFFF&&g>=n)throw new UserError("자막 글자 번호가 손상됐습니다.");}if(!ended)throw new UserError("자막 끝 표시가 없습니다.");}}
  }
  static Clip Info(int id,int lba,int size,int offset,byte[] h,string title){if(h.Length<24||h[0]!=0x80||h[4]!=3||h[6]!=4||h[7]<1||h[7]>2)throw new UserError("지원하지 않는 음성 형식입니다.");return new Clip{Id=id,Lba=lba,Size=size,Offset=offset,Rate=(int)Data.BE32(h,8),Samples=(int)Data.BE32(h,12),Title=title};}
  List<Clip> ReadClips(Track track){var afs=Entries.First(x=>x.Name=="DISK0"+Spec.Disc+".AFS");byte[] h=track.Read(afs.Lba,2048);if(Encoding.ASCII.GetString(h,0,4)!="AFS\0")throw new UserError("AFS 음성 자료를 읽을 수 없습니다.");int count=(int)Data.U32(h,4);h=track.Read(afs.Lba,8+count*8);var clips=new List<Clip>();
   for(int i=0;i<count;i++){int off=(int)Data.U32(h,8+i*8),size=(int)Data.U32(h,12+i*8);string title;Spec.Titles.TryGetValue(i.ToString(),out title);byte[] ah=track.Read(afs.Lba+off/2048,Math.Min(size,64));
    if(ah[0]!=0x80){clips.Add(new Clip{Id=i,Title=title??("음성 "+i)});continue;}clips.Add(Info(i,afs.Lba+off/2048,size,off%2048,ah,title??("음성 "+i)));}
   var acx=Entries.First(x=>x.Name=="ONMEM.ACX");byte[] data=track.Read(acx.Lba,acx.Size);int acount=(int)Data.BE32(data,4);if(acount<6||acount>256)throw new UserError("메뉴 음성 묶음이 올바르지 않습니다.");
   for(int i=0;i<6;i++){int off=(int)Data.BE32(data,8+i*8),size=(int)Data.BE32(data,12+i*8);clips.Add(Info(count+i,acx.Lba+off/2048,size,off%2048,Data.Slice(data,off,Math.Min(64,size)),"메뉴 · "+Spec.MenuText[i]));}
   if(clips.Count!=Data.U32(Blob,24))throw new UserError("음성 파일 수가 자막 표와 다릅니다.");return clips;
  }
  public string Label(int fid){return string.Format("{0:000} · {1}",fid,Clips[fid].Title);}
  public void ApplyProject(Project project){if(project==null||project.Version!=1||project.Disc!=Spec.Disc||project.SourceExeSha!=SourceExeSha)throw new UserError("프로젝트의 원본 CHD가 다릅니다.");
   if(project.Cues==null||project.Cues.Count!=Project.Cues.Count||!project.Cues.Select(x=>x.Id).OrderBy(x=>x).SequenceEqual(Project.Cues.Select(x=>x.Id).OrderBy(x=>x)))throw new UserError("프로젝트 자막 식별 정보가 다릅니다.");
   var baseline=Project.Cues.ToDictionary(x=>x.Id);foreach(var c in project.Cues){var b=baseline[c.Id];if(c.Clip!=b.Clip||c.OriginalStart!=b.Start||c.OriginalEnd!=b.End||c.OriginalText!=b.Text)throw new UserError("프로젝트의 원래 값이 CHD와 다릅니다.");}
   Validate(project,true);project.Source=Project.Source;Project=project;
  }
  public List<string> Warnings(Project project){var outp=new List<string>();foreach(var c in project.Cues){int dur=Clips[c.Clip].Duration;if(dur>0&&c.End>dur)outp.Add(c.Id+": 음성 종료 이후의 유지시간은 게임에서 표시되지 않습니다.");}return outp;}
  public void Validate(Project project,bool strict){if(project.Cues==null||project.Cues.Select(x=>x.Id).Distinct().Count()!=project.Cues.Count)throw new UserError("중복된 자막 식별 정보입니다.");
   foreach(var group in project.Cues.GroupBy(x=>x.Clip)){var cs=group.OrderBy(x=>x.Start).ToList();if(group.Key<0||group.Key>=(int)Data.U32(Blob,24))throw new UserError("음성 번호가 잘못됐습니다.");
    for(int i=0;i<cs.Count;i++){var c=cs[i];if(c.Start<0||c.End<=c.Start||c.End>24*60*60*1000)throw new UserError(c.Id+": 시작과 유지시간을 확인하세요.");Data.CheckText(c.Text);
     if(strict&&(c.Start!=c.OriginalStart||c.End!=c.OriginalEnd)){int duration=Clips[c.Clip].Duration;if(duration<=0||c.Start>=duration||c.End>duration)throw new UserError(c.Id+": 시간은 이 음성의 길이("+Data.Time(duration)+") 안에서 지정하세요. 음성이 끝나면 게임이 자막을 지웁니다.");}
     if(!strict)continue;for(int j=0;j<i;j++){var other=cs[j];int overlap=Math.Min(c.End,other.End)-Math.Max(c.Start,other.Start),before=Math.Min(c.OriginalEnd,other.OriginalEnd)-Math.Max(c.OriginalStart,other.OriginalStart);
      if(overlap>0&&overlap>Math.Max(0,before))throw new UserError(c.Id+": 다른 자막("+other.Id+")과 "+overlap+"ms 겹칩니다. 다른 자막은 이동하지 않았습니다.");}
    }}
  }
  uint Resolve(Reloc r,ModuleResult m,int dataSize){switch(r.Kind){case "code":return m.Base+r.Value;case "data":return m.Base+(uint)Spec.DataOffset+r.Value;case "tail":return m.Base+(uint)(Spec.DataOffset+dataSize)+r.Value;case "bss":return m.BssStart+r.Value;default:return r.Value;}}
  public ModuleResult BuildModule(byte[] blob){byte[] code=Convert.FromBase64String(Spec.Code);int image=Data.Align(code.Length+blob.Length+4,32);uint baseAddr=(0x8D000000u-((uint)image+Spec.BssSize))&0xFFFF0000u;
   if(baseAddr<0x8CE00000u)throw new UserError("자막 데이터가 게임의 안전한 메모리 범위를 넘습니다.");var m=new ModuleResult{Base=baseAddr,BssStart=baseAddr+(uint)image,BssEnd=baseAddr+(uint)image+Spec.BssSize,Bytes=new byte[image]};
   Buffer.BlockCopy(code,0,m.Bytes,0,code.Length);Buffer.BlockCopy(blob,0,m.Bytes,code.Length,blob.Length);Data.Put(m.Bytes,code.Length+blob.Length,0xFFFFFFFE);
   foreach(var r in Spec.CodeRelocs)Data.Put(m.Bytes,r.Offset,Resolve(r,m,blob.Length));return m;
  }
  public byte[] Pack(Project project,out string chars,out List<string> ids){Validate(project,true);chars=new string(project.Cues.SelectMany(x=>x.Text).Where(x=>x!='\n').Distinct().OrderBy(x=>x).ToArray());if(chars.Length>=65534)throw new UserError("글자 종류가 너무 많습니다.");
   var map=chars.Select((c,i)=>new{c,i}).ToDictionary(x=>x.c,x=>x.i);int nf=(int)Data.U32(Blob,24);ids=new List<string>();var files=new List<uint>();var cues=new List<uint>();var text=new MemoryStream();
   for(int f=0;f<nf;f++){var cs=project.Cues.Where(x=>x.Clip==f).OrderBy(x=>x.Start).ThenBy(x=>x.Id,StringComparer.Ordinal).ToList();files.Add((uint)cues.Count/3);files.Add((uint)cs.Count);
    foreach(var c in cs){ids.Add(c.Id);cues.Add((uint)c.Start);cues.Add((uint)c.End);cues.Add((uint)text.Length);foreach(char ch in c.Text){ushort g=ch=='\n'?(ushort)0xFFFF:(ushort)map[ch];text.WriteByte((byte)g);text.WriteByte((byte)(g>>8));}text.WriteByte(0xFE);text.WriteByte(0xFF);while(text.Length%4!=0)text.WriteByte(0);}}
   int adv=88,glyph=Data.Align(adv+chars.Length,4),ft=glyph+chars.Length*392,ct=ft+files.Count*4,tt=ct+cues.Count*4;var b=new byte[tt+text.Length];Buffer.BlockCopy(Blob,0,b,0,88);
   uint[] header={(uint)chars.Length,28,28,28,(uint)adv,(uint)glyph,(uint)nf,(uint)ft,(uint)ct,(uint)tt,Data.U32(Blob,40),392,3};for(int i=0;i<header.Length;i++)Data.Put(b,i*4,header[i]);
   for(int i=0;i<chars.Length;i++){var g=Data.Glyphs[chars[i]];b[adv+i]=g.Advance;Buffer.BlockCopy(g.Bits,0,b,glyph+i*392,392);}for(int i=0;i<files.Count;i++)Data.Put(b,ft+i*4,files[i]);for(int i=0;i<cues.Count;i++)Data.Put(b,ct+i*4,cues[i]);Buffer.BlockCopy(text.ToArray(),0,b,tt,(int)text.Length);return b;
  }
  public byte[] BuildExe(Project project,bool metadata){string chars;List<string> ids;byte[] blob=Pack(project,out chars,out ids);var module=BuildModule(blob);byte[] prefix=Data.Slice(Exe,0,Spec.PrefixSize);
   foreach(var r in Spec.PrefixRelocs)Data.Put(prefix,r.Offset,Resolve(r,module,blob.Length));Data.Put(prefix,Spec.HeapOffset,module.Base&0xFFFFFFF);
   byte[] body=new byte[prefix.Length+module.Bytes.Length];Buffer.BlockCopy(prefix,0,body,0,prefix.Length);Buffer.BlockCopy(module.Bytes,0,body,prefix.Length,module.Bytes.Length);if(!metadata)return body;
   var meta=new RomMeta{Disc=Spec.Disc,Chars=chars,DataSize=blob.Length,Ids=ids,BodySha=Data.Sha(body)};byte[] zip=Data.Zip(Encoding.UTF8.GetBytes(Data.Json().Serialize(meta)));
   using(var m=new MemoryStream()){m.Write(body,0,body.Length);m.Write(zip,0,zip.Length);m.Write(BitConverter.GetBytes(zip.Length),0,4);byte[] magic=Encoding.ASCII.GetBytes("KRSEDIT1");m.Write(magic,0,8);return m.ToArray();}
  }
  static void CheckSpace(string path,long bytes){var drive=new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path)));if(drive.AvailableFreeSpace<bytes)throw new UserError("디스크 빈 공간이 부족합니다. 약 "+(bytes/1e9).ToString("0.0")+"GB가 필요합니다.");}
  public void SaveChd(string destination){Validate(Project,true);destination=System.IO.Path.GetFullPath(destination);if(string.Equals(destination,Project.Source,StringComparison.OrdinalIgnoreCase))throw new UserError("원본 CHD에는 덮어쓸 수 없습니다. 새 파일 이름을 지정하세요.");if(File.Exists(destination))throw new UserError("같은 이름의 파일이 있습니다. 다른 이름으로 저장하세요.");
   string outputDir=System.IO.Path.GetDirectoryName(destination);Directory.CreateDirectory(outputDir);CheckSpace(outputDir,1200000000);string temp=System.IO.Path.Combine(outputDir,"."+System.IO.Path.GetFileNameWithoutExtension(destination)+"."+Guid.NewGuid().ToString("N")+".partial.chd");
   var edits=Project.Cues.Where(x=>x.Changed).Select(x=>new{x.Id,x.Clip,x.Start,x.End,x.Text,x.OriginalStart,x.OriginalEnd,x.OriginalText}).ToArray();
   if(edits.Length==0){Progress("변경 없는 CHD 복사",0);File.Copy(Project.Source,temp);if(Data.FileSha(temp)!=SourceSha){File.Delete(temp);throw new UserError("CHD 복사 검증에 실패했습니다.");}File.Move(temp,destination);Progress("저장 완료",1);return;}
   byte[] newExe=BuildExe(Project,true);var entry=Entries.First(x=>x.Name=="1ST_READ.BIN");int next=Entries.Where(x=>x.Lba>entry.Lba&&x.Name!="1ST_READ.BIN").Min(x=>x.Lba);int capacity=(next-entry.Lba)*2048;
   if(newExe.Length>capacity)throw new UserError("수정한 글자와 자막이 디스크의 자막 저장 공간을 넘습니다. 필요 "+newExe.Length+"바이트 / 가능 "+capacity+"바이트. 문장을 줄이거나 새 글자 종류를 줄여 주세요.");
   string build=System.IO.Path.Combine(Work,"build_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(build);CheckSpace(build,1400000000);string track3=System.IO.Path.Combine(build,"track03.bin");
   try{Progress("편집 데이터 준비",0);File.Copy(TrackPath,track3);string gdiSource=System.IO.Path.Combine(Work,"disc.gdi");var lines=File.ReadAllLines(gdiSource);var outLines=new List<string>{lines[0]};
    foreach(var line in lines.Skip(1)){var ps=Regex.Matches(line,"\"[^\"]+\"|\\S+").Cast<Match>().Select(x=>x.Value.Trim('"')).ToArray();if(ps.Length<6)continue;int no=int.Parse(ps[0]);string name="track"+no.ToString("00")+(no==2?".raw":".bin");if(no!=3)File.Copy(System.IO.Path.Combine(Work,ps[4]),System.IO.Path.Combine(build,name));outLines.Add(string.Join(" ",new[]{ps[0],ps[1],ps[2],ps[3],name,ps[5]}));}
    File.WriteAllLines(System.IO.Path.Combine(build,"disc.gdi"),outLines,Encoding.ASCII);
    using(var t=new Track(track3,true)){int allocated=Math.Max(Data.Align(entry.Size,2048),Data.Align(newExe.Length,2048));t.WriteFile(entry.Lba,newExe,allocated);
     int dlba=(int)(entry.Record/2048),offset=(int)(entry.Record%2048);byte[] sector=t.Sector(dlba);Data.Put(sector,offset+10,(uint)newExe.Length);Data.PutBE(sector,offset+14,(uint)newExe.Length);t.WriteSector(dlba,sector);
     if(!t.Read(entry.Lba,newExe.Length).SequenceEqual(newExe))throw new UserError("자막 삽입 검증에 실패했습니다.");}
    Run("새 CHD 압축",build,"createcd","-i","disc.gdi","-o",temp,"-np","4");Run("새 CHD 무결성 검사",build,"verify","-i",temp);
    // Verify the new CHD itself, not just the uncompressed staging track.
    string raw=System.IO.Path.Combine(build,"readback.raw");int n=(newExe.Length+2047)/2048;Run("저장된 자막 다시 읽기",build,"extractraw","-f","-i",temp,"-o",raw,"-isb",((45004L+entry.Lba-45000)*2448).ToString(),"-ib",((long)n*2448).ToString());
    byte[] r=File.ReadAllBytes(raw),actual=new byte[newExe.Length];for(int i=0;i<n;i++)Buffer.BlockCopy(r,i*2448+16,actual,i*2048,Math.Min(2048,newExe.Length-i*2048));if(!actual.SequenceEqual(newExe))throw new UserError("저장된 CHD의 자막 재검증에 실패했습니다.");
    File.Move(temp,destination);Data.WriteJson(destination+".verification.json",new{Editor="Kaze Subtitle Editor 1.0",Disc=Spec.Disc,SourceSha256=SourceSha,OutputSha256=Data.FileSha(destination),ChangedCues=edits,UnchangedCues=Project.Cues.Count-edits.Length,ChdVerified=true,ExeReadbackVerified=true,NoAutomaticTimingChanges=true});Progress("저장 완료",1);
   }finally{if(File.Exists(temp))File.Delete(temp);if(Directory.Exists(build))Directory.Delete(build,true);}
  }
  public string Audio(int fid){var clip=Clips[fid];if(clip.Rate<=0)throw new UserError("이 항목에는 재생할 음성이 없습니다.");string wav=System.IO.Path.Combine(Work,"voice_"+fid.ToString("0000")+".wav");if(File.Exists(wav))return wav;
   byte[] bytes;using(var track=new Track(TrackPath))bytes=track.Read(clip.Lba,clip.Size+clip.Offset);bytes=Data.Slice(bytes,clip.Offset,clip.Size);Adx.Decode(bytes,wav);return wav;
  }
  public void Dispose(){if(disposed)return;disposed=true;if(process!=null)throw new InvalidOperationException("작업이 실행 중입니다.");if(cacheLock!=null)cacheLock.Dispose();}
 }
 public static class Adx {
  public static void Decode(byte[] b,string output){int off=Data.BE16(b,2)+4,bs=b[5],ch=b[7],rate=(int)Data.BE32(b,8),ns=(int)Data.BE32(b,12),hp=Data.BE16(b,16);
   if(b[0]!=0x80||b[4]!=3||b[6]!=4||ch<1||ch>2||bs<3||rate<8000||rate>192000||ns<0||ns>(long)rate*3600)throw new UserError("지원하지 않는 ADX 음성입니다.");
   double z=Math.Cos(2*Math.PI*hp/rate),a=Math.Sqrt(2)-z,bb=Math.Sqrt(2)-1,c=(a-Math.Sqrt((a+bb)*(a-bb)))/bb;int c1=(int)(c*8192),c2=(int)(c*c*-4096);int[] h1=new int[ch],h2=new int[ch];int spf=(bs-2)*2;int p=off;var pcm=new short[spf*ch];string tmp=output+".tmp";
   using(var stream=new FileStream(tmp,FileMode.Create,FileAccess.Write))using(var w=new BinaryWriter(stream)){
    w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+ns*ch*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)ch);w.Write(rate);w.Write(rate*ch*2);w.Write((short)(ch*2));w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(ns*ch*2);
    for(int done=0;done<ns;done+=spf,p+=bs*ch){if(p+bs*ch>b.Length)throw new UserError("음성 데이터가 잘렸습니다.");int count=Math.Min(spf,ns-done);
     for(int k=0;k<ch;k++){int f=p+k*bs,sc=(Data.BE16(b,f)&0x1fff)+1;for(int i=0;i<count;i++){int nib=b[f+2+i/2];nib=(i%2==0)?nib>>4:nib&15;if(nib>=8)nib-=16;int s=nib*sc+((c1*h1[k]+c2*h2[k])>>12);s=Math.Max(-32768,Math.Min(32767,s));h2[k]=h1[k];h1[k]=s;pcm[i*ch+k]=(short)s;}}
     for(int i=0;i<count*ch;i++)w.Write(pcm[i]);}
   }if(File.Exists(output))File.Delete(output);File.Move(tmp,output);
  }
 }
 // PCM is sent directly to WinMM. No MCI command parser or filename-length restriction.
 // https://learn.microsoft.com/windows/win32/api/mmeapi/nf-mmeapi-waveoutgetposition
 public sealed class Player:IDisposable {
  [StructLayout(LayoutKind.Sequential,Pack=2)]struct WaveFormat {public ushort Tag,Channels;public uint Rate,BytesPerSecond;public ushort Align,Bits,Extra;}
  [StructLayout(LayoutKind.Sequential)]struct WaveHeader {public IntPtr Data;public uint Length,Recorded;public IntPtr User;public uint Flags,Loops;public IntPtr Next,Reserved;}
  [StructLayout(LayoutKind.Explicit,Size=12)]struct MmTime {[FieldOffset(0)]public uint Type;[FieldOffset(4)]public uint Value;}
  [DllImport("winmm.dll")]static extern int waveOutOpen(out IntPtr handle,uint id,ref WaveFormat format,IntPtr callback,IntPtr instance,uint flags);
  [DllImport("winmm.dll")]static extern int waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")]static extern int waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")]static extern int waveOutWrite(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")]static extern int waveOutGetPosition(IntPtr handle,ref MmTime time,uint size);
  [DllImport("winmm.dll")]static extern int waveOutPause(IntPtr handle);
  [DllImport("winmm.dll")]static extern int waveOutRestart(IntPtr handle);
  [DllImport("winmm.dll")]static extern int waveOutReset(IntPtr handle);
  [DllImport("winmm.dll")]static extern int waveOutClose(IntPtr handle);
  [DllImport("winmm.dll",CharSet=CharSet.Unicode)]static extern int waveOutGetErrorText(int error,StringBuilder text,int size);
  IntPtr handle,header;GCHandle pin;byte[] pcm;string file;int bytesPerSecond,align,dataLength,startByte,endByte,cursor;bool prepared,paused;
  static readonly uint HeaderSize=(uint)Marshal.SizeOf(typeof(WaveHeader));
  public bool Opened{get{return handle!=IntPtr.Zero;}}
  static void Check(int code){if(code==0)return;var text=new StringBuilder(256);waveOutGetErrorText(code,text,256);throw new UserError("음성 재생 오류: "+text);}
  WaveHeader Header{get{return (WaveHeader)Marshal.PtrToStructure(header,typeof(WaveHeader));}}
  public void Open(string path){if(file==path&&Opened)return;Close();byte[] b=File.ReadAllBytes(path);
   if(b.Length<44||Encoding.ASCII.GetString(b,0,4)!="RIFF"||Encoding.ASCII.GetString(b,36,4)!="data"||Data.U16(b,20)!=1||Data.U16(b,34)!=16)throw new UserError("PCM 음성 파일이 올바르지 않습니다.");
   var format=new WaveFormat{Tag=1,Channels=Data.U16(b,22),Rate=Data.U32(b,24),BytesPerSecond=Data.U32(b,28),Align=Data.U16(b,32),Bits=16};
   if(format.Align!=format.Channels*2||format.BytesPerSecond!=format.Rate*format.Align||Data.U32(b,40)>b.Length-44)throw new UserError("PCM 음성 길이가 올바르지 않습니다.");
   Check(waveOutOpen(out handle,0xFFFFFFFF,ref format,IntPtr.Zero,IntPtr.Zero,0));
   try{pcm=b;pin=GCHandle.Alloc(pcm,GCHandleType.Pinned);header=Marshal.AllocHGlobal((int)HeaderSize);bytesPerSecond=(int)format.BytesPerSecond;align=format.Align;dataLength=(int)Data.U32(b,40);file=path;cursor=0;}
   catch{Close();throw;}
  }
  void ResetBuffer(){if(!Opened)return;Check(waveOutReset(handle));if(prepared){Check(waveOutUnprepareHeader(handle,header,HeaderSize));prepared=false;}paused=false;}
  public int Position{get{if(!Opened)return 0;if(!prepared)return cursor;if((Header.Flags&1)!=0)return (int)((long)endByte*1000/bytesPerSecond);
    var time=new MmTime{Type=4};Check(waveOutGetPosition(handle,ref time,12));long bytes=time.Type==4?time.Value:time.Type==2?(long)time.Value*align:(long)time.Value*bytesPerSecond/1000;
    return (int)((startByte+Math.Min(endByte-startByte,bytes))*1000/bytesPerSecond);}}
  public bool Playing{get{return Opened&&prepared&&!paused&&(Header.Flags&1)==0;}}
  public void Play(int from,int to){if(!Opened)return;ResetBuffer();startByte=(int)Math.Min(dataLength,(long)Math.Max(0,from)*bytesPerSecond/1000);startByte-=startByte%align;endByte=(int)Math.Min(dataLength,(long)Math.Max(from+1,to)*bytesPerSecond/1000);endByte-=endByte%align;cursor=(int)((long)startByte*1000/bytesPerSecond);if(endByte<=startByte)return;
   var wh=new WaveHeader{Data=IntPtr.Add(pin.AddrOfPinnedObject(),44+startByte),Length=(uint)(endByte-startByte)};Marshal.StructureToPtr(wh,header,false);Check(waveOutPrepareHeader(handle,header,HeaderSize));prepared=true;Check(waveOutRestart(handle));Check(waveOutWrite(handle,header,HeaderSize));paused=false;}
  public void Pause(){if(Opened&&prepared){Check(waveOutPause(handle));paused=true;}}
  public void Seek(int ms){if(!Opened)return;ResetBuffer();cursor=Math.Max(0,Math.Min(ms,(int)((long)dataLength*1000/bytesPerSecond)));}
  public void Close(){if(handle!=IntPtr.Zero){ResetBuffer();Check(waveOutClose(handle));handle=IntPtr.Zero;}if(header!=IntPtr.Zero){Marshal.FreeHGlobal(header);header=IntPtr.Zero;}if(pin.IsAllocated)pin.Free();pcm=null;file=null;}
  public static object Test(string silentWave){using(var p=new Player()){p.Open(silentWave);p.Play(100,800);System.Threading.Thread.Sleep(220);int position=p.Position;if(position<150||!p.Playing)throw new Exception("PCM playback did not advance");p.Pause();p.Seek(350);if(Math.Abs(p.Position-350)>20)throw new Exception("PCM seek failed");p.Play(400,550);System.Threading.Thread.Sleep(300);if(p.Playing||Math.Abs(p.Position-550)>20)throw new Exception("PCM range playback failed");return new{PlaybackAdvances=true,Pause=true,Seek=true,RangeEnd=true,Position=position,PathLength=silentWave.Length,Backend="WinMM PCM"};}}
  public void Dispose(){Close();}
 }
}
