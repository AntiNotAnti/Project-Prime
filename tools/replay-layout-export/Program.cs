using System.Reflection;
using System.Runtime.Loader;
using System.Collections;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
string dir=Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
AssemblyLoadContext.Default.Resolving += (_,n) => File.Exists(Path.Combine(dir,n.Name+".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dir,n.Name+".dll")) : null;
var a=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
var c=a.GetType("MphRead.Mods.Replay.ReplayWorldCheckpoint")!;
const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
var fields=(IDictionary)c.GetField("Fields",flags)!.GetValue(null)!;
var types=(IDictionary)c.GetField("Types",flags)!.GetValue(null)!;
var objectTypes=(Type[])c.GetField("ObjectTypes",flags)!.GetValue(null)!;
var vf=c.GetMethod("ValueFields",flags)!;
string[] Describe(FieldInfo f)=>[f.DeclaringType!.ToString(), f.Name, f.FieldType.ToString()];
var objects=new SortedDictionary<string,string[][]>(StringComparer.Ordinal);
foreach(DictionaryEntry p in fields) objects.Add(p.Key.ToString()!,((FieldInfo[])p.Value!).Select(Describe).ToArray());
var values=new SortedDictionary<string,string[][]>(StringComparer.Ordinal);
foreach(DictionaryEntry p in types) {var t=(Type)p.Value!;if(t.IsValueType&&!t.IsPrimitive&&!t.IsEnum) values.Add(t.ToString(),((FieldInfo[])vf.Invoke(null,[t])!).Select(Describe).ToArray());}
IEnumerable<DictionaryEntry> Entries(IDictionary d) {foreach(DictionaryEntry e in d) yield return e;}
string Schema(bool stable) {string N(Type t)=>stable?t.ToString():t.FullName!; return "world-codec-1|"+string.Join('|',Entries(fields).OrderBy(p=>((Type)p.Key).FullName,StringComparer.Ordinal).Select(p=>N((Type)p.Key)+":"+string.Join(',',((FieldInfo[])p.Value!).Select(f=>N(f.DeclaringType!)+"."+f.Name+":"+f.FieldType))))+string.Join('|',Entries(types).Select(p=>(Type)p.Value!).Where(t=>t.IsValueType&&!t.IsPrimitive&&!t.IsEnum).OrderBy(t=>t.FullName,StringComparer.Ordinal).Select(t=>N(t)+":"+string.Join(',',((FieldInfo[])vf.Invoke(null,[t])!).Select(f=>f.Name+":"+f.FieldType)))); }
string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
var result=new {Commit=args[2], Contract=Hash(Schema(true)), LegacyContract=Hash(Schema(false)), StableSchema=Schema(true), LegacySchema=Schema(false), AssemblyVersion=a.GetName().Version!.ToString(), Types=Entries(types).ToDictionary(p=>p.Key.ToString()!,p=>((Type)p.Value!).FullName!), Objects=objects, Values=values, ObjectTypes=objectTypes.Select(t=>t.ToString()).ToArray()};
File.WriteAllText(args[1],JsonSerializer.Serialize(result));
Console.WriteLine(args[2]+" "+result.Contract+" "+result.LegacyContract);
