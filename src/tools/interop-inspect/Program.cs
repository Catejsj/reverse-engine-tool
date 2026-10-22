// List a .NET assembly type's properties and method signatures (used on BepInEx's generated interop assemblies).
// dotnet run --project src/tools/interop-inspect -- <assembly.dll> <type name suffix>
using System; using System.IO; using System.Linq;
using System.Reflection.Metadata; using System.Reflection.PortableExecutable; using System.Collections.Immutable;
var path = args[0]; var want = args[1];
using var fs = File.OpenRead(path); using var pe = new PEReader(fs); var md = pe.GetMetadataReader();
var prov = new Sig(md);
foreach (var h in md.TypeDefinitions) {
  var t = md.GetTypeDefinition(h); var full = md.GetString(t.Namespace) + "." + md.GetString(t.Name);
  if (!full.EndsWith(want)) continue;
  var bt = t.BaseType.IsNil ? "" : t.BaseType.Kind == HandleKind.TypeSpecification
      ? md.GetTypeSpecification((TypeSpecificationHandle)t.BaseType).DecodeSignature(prov, null)
      : t.BaseType.Kind == HandleKind.TypeReference ? prov.GetTypeFromReference(md, (TypeReferenceHandle)t.BaseType, 0)
      : prov.GetTypeFromDefinition(md, (TypeDefinitionHandle)t.BaseType, 0);
  Console.WriteLine("== " + full + (bt == "" ? "" : " : " + bt));
  foreach (var p in t.GetProperties()) { var pd = md.GetPropertyDefinition(p); var sig = pd.DecodeSignature(prov, null); Console.WriteLine($"  prop {md.GetString(pd.Name)} : {sig.ReturnType}"); }
  foreach (var m in t.GetMethods()) { var d = md.GetMethodDefinition(m); var n = md.GetString(d.Name); if (n.StartsWith("get_")||n.StartsWith("set_")) continue; var s = d.DecodeSignature(prov, null); Console.WriteLine($"  {( (d.Attributes & System.Reflection.MethodAttributes.Static)!=0 ? "static " : "")}{s.ReturnType} {n}({string.Join(", ", s.ParameterTypes)})"); }
}
class Sig : ISignatureTypeProvider<string, object> {
  MetadataReader md; public Sig(MetadataReader m) { md = m; }
  public string GetArrayType(string e, ArrayShape s) => e + "[,]"; public string GetByReferenceType(string e) => "ref " + e;
  public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr"; public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
  public string GetGenericMethodParameter(object c, int i) => "!!" + i; public string GetGenericTypeParameter(object c, int i) => "!" + i;
  public string GetModifiedType(string m, string u, bool r) => u; public string GetPinnedType(string e) => e; public string GetPointerType(string e) => e + "*";
  public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString(); public string GetSZArrayType(string e) => e + "[]";
  public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) { var t = r.GetTypeDefinition(h); var d = t.GetDeclaringType(); return (d.IsNil ? "" : GetTypeFromDefinition(r, d, k) + "+") + r.GetString(t.Name); }
  public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { var t = r.GetTypeReference(h); return r.GetString(t.Namespace) + "." + r.GetString(t.Name); }
  public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => "spec";
}
