using System.Reflection;
using System.Net.Http;
using System.Runtime.Loader;
string fixture=Directory.CreateTempSubdirectory("prime-ui-offline-").FullName;
Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",fixture);
Environment.SetEnvironmentVariable("PROJECT_PRIME_UI_PERF",Path.Combine(fixture,"never-presented.json"));
try
{
    string binary=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(AppContext.BaseDirectory,"ProjectPrime.dll");
    byte[] literals=File.ReadAllBytes(binary);
    if(literals.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("Live account authentication is disabled in UI performance diagnostics."))<0
        ||literals.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("Live account requests are disabled in UI performance diagnostics."))<0)
        throw new InvalidOperationException("Refuse to call account methods: this exact binary lacks diagnostic guards.");
    var context=new AssemblyLoadContext("guarded-account-check",isCollectible:true);
    var resolver=new AssemblyDependencyResolver(binary);
    context.Resolving+=(owner,name)=>resolver.ResolveAssemblyToPath(name) is string dependency?owner.LoadFromAssemblyPath(dependency):null;
    var assembly=context.LoadFromAssemblyPath(binary);
    if(Path.GetFullPath(assembly.Location)!=binary)throw new InvalidOperationException("Refuse to call account methods on a different assembly.");
    var type=assembly.GetType("MphRead.Mods.Launcher.HunterLicenseClient",throwOnError:true)!;
    var load=(Task)type.GetMethod("LoadAsync",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,new object[]{CancellationToken.None})!;
    await load;
    Console.WriteLine("RMLPERF ACCOUNT PASS profile diagnostics return local display data without authentication");
    bool authRejected=false;
    try {await (Task)type.GetMethod("AuthenticateAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{CancellationToken.None})!;}
    catch(InvalidOperationException ex) when(ex.Message.Contains("diagnostics",StringComparison.Ordinal)){authRejected=true;}
    if(!authRejected)throw new InvalidOperationException("Diagnostic authentication was not rejected.");
    Console.WriteLine("RMLPERF ACCOUNT PASS authentication is rejected before stored-session access");
    bool requestRejected=false;
    try {type.GetMethod("Request",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object?[]{HttpMethod.Post,"/auth/v1/signup",null,new object()});}
    catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException error && error.Message.Contains("diagnostics",StringComparison.Ordinal)){requestRejected=true;}
    if(!requestRejected)throw new InvalidOperationException("Diagnostic account HTTP was not rejected.");
    Console.WriteLine("RMLPERF ACCOUNT PASS central HTTP creation rejects any caller during diagnostics");
    if(Directory.EnumerateFiles(fixture,"*",SearchOption.AllDirectories).Any())throw new InvalidOperationException("Diagnostics created account state or a session file.");
    Console.WriteLine("RMLPERF ACCOUNT PASS diagnostic checks created no account/session files");
}
finally {Directory.Delete(fixture,true);}
