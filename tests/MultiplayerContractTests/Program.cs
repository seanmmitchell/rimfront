using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

if (args.Length < 2) { Console.Error.WriteLine("Usage: MultiplayerContractTests <1.6|1.7> <Multiplayer directory> [additional DLLs...]"); return 2; }
string version = args[0], root = args[1];
if (version != "1.6" && version != "1.7") return 2;
string versionRoot = Directory.Exists(Path.Combine(root, version)) ? Path.Combine(root, version) : root;
string mpPath = Path.Combine(versionRoot, "AssembliesCustom", "Multiplayer.dll");
string apiPath = Path.Combine(versionRoot, "Assemblies", "0MultiplayerAPI.dll");
string commonPath = Path.Combine(versionRoot, "AssembliesCustom", "MultiplayerCommon.dll");
string expected = version == "1.6" ? "28D2CDA41D78497970565748BE0E4B7E23F6ECE03310A304359A840AB38FAAD1" : "2C3403FEE45CB142AEE6B212E04A8C204DC4AF5AEE99A632D787B51ADA0018CE";
using var mp = AssemblyDefinition.ReadAssembly(mpPath);
using var api = AssemblyDefinition.ReadAssembly(apiPath);
using var common = AssemblyDefinition.ReadAssembly(commonPath);
var contracts = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception("Contract failed: " + name); contracts.Add(name); }
TypeDefinition Type(AssemblyDefinition assembly, string name) => assembly.MainModule.Types.Single(t => t.FullName == name);
bool Field(TypeDefinition type, string name, string fieldType) => type.Fields.Any(f => f.Name == name && f.FieldType.FullName == fieldType);
try
{
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mpPath))) == expected, "pinned Multiplayer SHA256");
    var command = Type(common, "Multiplayer.Common.ScheduledCommand");
    Check(Field(command, "playerId", "System.Int32"), "trusted command sender field");
    foreach (string name in new[] { "Multiplayer.Client.AsyncTimeComp", "Multiplayer.Client.AsyncTime.AsyncWorldTimeComp" })
    {
        var execute = Type(mp, name).Methods.Single(m => m.Name == "ExecuteCmd");
        Check(execute.ReturnType.FullName == "System.Void" && execute.Parameters.Count == 1
            && execute.Parameters[0].ParameterType.FullName == command.FullName && execute.HasBody, name + " execution scope");
        Check(execute.Body.Instructions.Any(i => i.Operand is FieldReference f && f.FullName == command.Fields.Single(f => f.Name == "playerId").FullName), name + " uses native sender");
    }
    Check(Field(Type(mp, "Multiplayer.Client.MultiplayerSession"), "playerId", "System.Int32"), "local session identity");
    Check(Field(Type(mp, "Multiplayer.Client.Comp.MultiplayerGameComp"), "multifaction", "System.Boolean"), "shared-colony guard");
    var apiType = Type(api, "Multiplayer.API.MP");
    Check(apiType.Methods.Any(m => m.Name == "RegisterSyncMethod" && m.Parameters[0].ParameterType.FullName == "System.Reflection.MethodInfo"), "reflection registration overload");
    foreach (string name in new[] { "IsHosting", "InInterface", "IsInMultiplayer" })
        Check(apiType.Properties.Any(p => p.Name == name && p.PropertyType.FullName == "System.Boolean"), name);
    foreach (string name in new[] { "SetContext", "SetHostOnly" })
        Check(Type(api, "Multiplayer.API.ISyncMethod").Methods.Any(m => m.Name == name), name);
    var timeControl = Type(mp, "Multiplayer.Client.AsyncTime.TimeControlPatch");
    Check(timeControl.Properties.Any(p => p.Name == "CurTimeSpeedUI") && timeControl.Methods.Any(m => m.Name == "TogglePaused")
        && timeControl.Methods.Any(m => m.Name == "SendTimeVote"), "native pause voting");
    var paths = new[] { mpPath, commonPath, apiPath }.Concat(args.Skip(2));
    var artifacts = paths.Select(path => {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        return new { name = Path.GetFileName(path), assemblyVersion = assembly.Name.Version.ToString(), moduleVersionId = assembly.MainModule.Mvid.ToString(),
            targetFramework = assembly.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute")?.ConstructorArguments[0].Value?.ToString(),
            runtimeReferences = assembly.MainModule.AssemblyReferences.Where(r => r.Name == "mscorlib" || r.Name == "System.Private.CoreLib" || r.Name == "UnityStubs" || r.Name.StartsWith("UnityEngine")).Select(r => r.Name).ToArray(),
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() };
    }).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { rimWorld = version, contracts, artifacts, liveValidated = false }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
