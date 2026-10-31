// dist 구조 검증: 축소된 파일이 일반 빌드와 IL 명령 순서·문자열·P/Invoke·가상 메서드 이름·구조체 레이아웃·마샬링 정보·리소스를 그대로 가졌는지,
// 남은 특성이 의도한 것뿐인지(RuntimeCompatibility, STAThread) 확인한다.  사용: mono DistCheck.exe <일반 빌드.exe> <dist.exe>   (Mono.Cecil 0.11 필요)
using System; using System.IO; using System.Linq; using System.Text; using System.Collections.Generic;
using Mono.Cecil;
static class DistCheck {
  static int fails;
  static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
  static byte[] Section(byte[] d, string name) {
    int pe = BitConverter.ToInt32(d, 0x3c); int nsec = BitConverter.ToUInt16(d, pe + 6); int sec = pe + 24 + BitConverter.ToUInt16(d, pe + 20);
    for (int i = 0; i < nsec; i++) { int s = sec + i * 40; if (Encoding.ASCII.GetString(d, s, 8).TrimEnd('\0') == name) { int raw = BitConverter.ToInt32(d, s + 16), ptr = BitConverter.ToInt32(d, s + 20); byte[] r = new byte[raw]; Array.Copy(d, ptr, r, 0, raw); return r; } }
    return null;
  }
  static int Main(string[] a) {
    string plainPath = a[0], distPath = a[1];
    ModuleDefinition plain = ModuleDefinition.ReadModule(plainPath), dist = ModuleDefinition.ReadModule(distPath);
    var pt = plain.GetTypes().ToList(); var dt = dist.GetTypes().ToList();
    Check(pt.Count == dt.Count, "same number of types: " + pt.Count + " / " + dt.Count);
    Check(pt.Sum(t => t.Methods.Count) == dt.Sum(t => t.Methods.Count), "same number of methods: " + dt.Sum(t => t.Methods.Count));
    var pinvP = pt.SelectMany(t => t.Methods).Where(m => m.IsPInvokeImpl).ToList(); var pinvD = dt.SelectMany(t => t.Methods).Where(m => m.IsPInvokeImpl).ToList();
    Check(pinvP.Count == 26 && pinvD.Count == 26 && pinvP.Select(m => m.PInvokeInfo.Module.Name + "!" + (m.PInvokeInfo.EntryPoint ?? m.Name)).OrderBy(x => x).SequenceEqual(pinvD.Select(m => m.PInvokeInfo.Module.Name + "!" + (m.PInvokeInfo.EntryPoint ?? m.Name)).OrderBy(x => x)), "26 P/Invoke imports with identical module!entry points");
    Check(pinvD.All(m => m.Name == (m.PInvokeInfo.EntryPoint ?? m.Name)), "P/Invoke method names unchanged (entry point = name)");
    var virtP = pt.SelectMany(t => t.Methods).Where(m => m.IsVirtual).Select(m => m.Name).OrderBy(x => x).ToList(); var virtD = dt.SelectMany(t => t.Methods).Where(m => m.IsVirtual).Select(m => m.Name).OrderBy(x => x).ToList();
    Check(virtP.SequenceEqual(virtD), "virtual/override method names unchanged (" + virtD.Count + ")");
    Check(dt.SelectMany(t => t.Methods).Count(m => m.IsConstructor) == pt.SelectMany(t => t.Methods).Count(m => m.IsConstructor), "constructors kept");
    var attrs = dist.Assembly.CustomAttributes.Select(c => c.AttributeType.Name).OrderBy(x => x).ToList();
    Check(attrs.SequenceEqual(new[] { "RuntimeCompatibilityAttribute" }), "assembly attributes in dist: " + string.Join(",", attrs.ToArray()) + " (RuntimeCompatibility kept, description attributes moved into the version resource only)");
    var methodAttrs = dt.SelectMany(t => t.Methods).SelectMany(m => m.CustomAttributes).Select(c => c.AttributeType.Name).Distinct().OrderBy(x => x).ToList();
    Check(methodAttrs.SequenceEqual(new[] { "STAThreadAttribute" }), "method attributes in dist: " + string.Join(",", methodAttrs.ToArray()));
    int lit = dt.Sum(t => t.Fields.Count(f => f.IsLiteral));
    Check(lit == 0, "no compile-time constant fields left in dist (" + lit + ")");
    Check(dt.Where(t => t.IsEnum).All(t => t.Fields.Any(f => f.Name == "value__")), "every enum keeps value__");
    var marshP = pt.SelectMany(t => t.Fields).Count(f => f.HasMarshalInfo) + pt.SelectMany(t => t.Methods).SelectMany(m => m.Parameters).Count(p => p.HasMarshalInfo);
    var marshD = dt.SelectMany(t => t.Fields).Count(f => f.HasMarshalInfo) + dt.SelectMany(t => t.Methods).SelectMany(m => m.Parameters).Count(p => p.HasMarshalInfo);
    Check(marshP == marshD && marshD == 5, "marshal descriptors kept: " + marshD + " (3x U1 bool params, 2x fixed strings)");
    var layoutP = pt.Where(t => t.HasLayoutInfo).Select(t => t.ClassSize + "/" + t.PackingSize).OrderBy(x => x).ToList(); var layoutD = dt.Where(t => t.HasLayoutInfo).Select(t => t.ClassSize + "/" + t.PackingSize).OrderBy(x => x).ToList();
    Check(layoutP.SequenceEqual(layoutD) && layoutD.Contains("1352/0"), "explicit struct layouts kept: " + string.Join(" ", layoutD.ToArray()));
    Check(dt.SelectMany(t => t.Fields).Where(f => f.HasLayoutInfo).Count() == pt.SelectMany(t => t.Fields).Where(f => f.HasLayoutInfo).Count(), "field offsets kept");
    Check(dt.SelectMany(t => t.Fields).Count(f => f.InitialValue != null && f.InitialValue.Length > 0) == pt.SelectMany(t => t.Fields).Count(f => f.InitialValue != null && f.InitialValue.Length > 0), "static array initializer data (FieldRVA) kept");
    Check(dist.EntryPoint != null && dist.EntryPoint.HasBody && dist.EntryPoint.CustomAttributes.Any(c => c.AttributeType.Name == "STAThreadAttribute"), "entry point present with [STAThread]");
    // IL identical: same instruction count and opcode sequence per method (in type/method order)
    var ilP = pt.SelectMany(t => t.Methods).Where(m => m.HasBody).Select(m => string.Join(",", m.Body.Instructions.Select(i => i.OpCode.Name).ToArray())).ToList();
    var ilD = dt.SelectMany(t => t.Methods).Where(m => m.HasBody).Select(m => string.Join(",", m.Body.Instructions.Select(i => i.OpCode.Name).ToArray())).ToList();
    Check(ilP.SequenceEqual(ilD), "IL opcode sequences identical for all " + ilD.Count + " method bodies");
    var strP = pt.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Where(i => i.OpCode.Name == "ldstr").Select(i => (string)i.Operand).ToList();
    var strD = dt.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Where(i => i.OpCode.Name == "ldstr").Select(i => (string)i.Operand).ToList();
    Check(strP.SequenceEqual(strD), "all " + strD.Count + " string literals identical");
    byte[] pd = File.ReadAllBytes(plainPath), dd = File.ReadAllBytes(distPath);
    byte[] rsP = Section(pd, ".rsrc"), rsD = Section(dd, ".rsrc");
    Check(rsP != null && rsD != null && rsP.Length == rsD.Length, ".rsrc section same size as the plain build: " + (rsD == null ? -1 : rsD.Length));
    string wide = Encoding.Unicode.GetString(rsD);
    Check(wide.Contains("FileDescription") && wide.Contains("MAC 주소 변경 유틸리티") && wide.Contains("ProductName") && wide.Contains("MacChanger"), "version resource carries FileDescription/ProductName");
    Check(Encoding.UTF8.GetString(rsD).Contains("requireAdministrator") && Encoding.UTF8.GetString(rsD).Contains("dpiAware"), "manifest with requireAdministrator and dpiAware embedded");
    Check(dd.Length < pd.Length, "dist smaller than plain: " + dd.Length + " < " + pd.Length);
    Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
    return fails == 0 ? 0 : 1;
  }
}
