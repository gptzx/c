// 배포용 실행 파일 축소 도구 — 기능은 그대로 두고 메타데이터만 줄인다 (IL·리소스(아이콘·매니페스트·버전 정보)·동작은 바뀌지 않는다).
//   Mono.Cecil 로 (1) 매개변수 이름을 지우고(Param 행 자체가 사라진다), (2) 이 어셈블리가 정의한 형식·메서드·필드·속성·이벤트의 이름을 한두 글자로 줄이고
//   (생성자, P/Invoke, 가상/재정의 메서드, 델리게이트의 런타임 메서드는 이름을 그대로 둔다 — 런타임이 이름으로 찾는 것은 이것뿐이다),
//   (3) 컴파일 때 이미 호출 자리에 새겨진 상수 필드(const, 열거형 멤버)를 지우고, (4) 실행에 영향 없는 특성(컴파일러 표식·디버거 표식·어셈블리 설명 특성)을 뗀다.
//   어셈블리 설명(제목·버전 등)은 Win32 버전 리소스에 따로 들어 있어 파일 속성 창에는 그대로 보인다.
//   이 프로그램은 리플렉션·Enum.ToString·형식 이름을 쓰지 않으므로 이름과 상수를 지워도 동작이 같다.
// 사용: mono Shrink.exe <입력.exe> <출력.exe> [이름표.txt]   (Mono.Cecil 0.11 필요)
//   이름표에는 "T|원래 전체 이름|새 전체 이름" 과 "M|원래 형식|원래 이름|인수 개수|새 이름", "F/P/E|원래 형식|원래 이름|새 이름" 줄이 기록되어 테스트가 축소된 파일에서도 원래 이름으로 멤버를 찾을 수 있다.
//   build.cmd 의 일반 빌드 결과도 그대로 동작하며, dist/ 만 이 도구를 거친다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

static class Shrink
{
    static int counter;
    static readonly List<string> map = new List<string>();

    static string Next()
    {
        int n = counter++;
        string s = "";
        do { s = (char)('a' + n % 26) + s; n = n / 26 - 1; } while (n >= 0);
        return s;
    }

    static string Rename(string kind, string type, string name, string extra)
    {
        string n = Next();
        map.Add(kind + "|" + type + "|" + name + "|" + (extra != null ? extra + "|" : "") + n);
        return n;
    }

    static bool NoRuntimeEffect(CustomAttribute a)
    {
        string n = a.AttributeType.Name;
        return n == "CompilerGeneratedAttribute" || n == "DebuggerBrowsableAttribute" || n == "DebuggerHiddenAttribute" || n == "DebuggableAttribute"
            || n == "CompilationRelaxationsAttribute" || n == "RuntimeCompatibilityAttribute"
            || n == "AssemblyTitleAttribute" || n == "AssemblyDescriptionAttribute" || n == "AssemblyProductAttribute" || n == "AssemblyFileVersionAttribute";
    }

    static void Main(string[] args)
    {
        string input = args[0], output = args[1];
        uint timestamp = PeTimestamp(input);
        AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { ReadingMode = ReadingMode.Immediate });
        List<TypeDefinition> types = asm.MainModule.GetTypes().ToList();   // 바깥 형식이 중첩 형식보다 먼저 온다
        Dictionary<TypeDefinition, string> originalName = new Dictionary<TypeDefinition, string>();
        foreach (TypeDefinition t in types) originalName[t] = t.FullName;   // 이름을 바꾸기 전의 전체 이름 (중첩 형식은 "바깥/안쪽")
        foreach (TypeDefinition t in types)
        {
            if (t.Name == "<Module>") continue;
            string full = originalName[t];
            foreach (MethodDefinition m in t.Methods)
            {
                foreach (ParameterDefinition p in m.Parameters) p.Name = "";
                bool keep = m.IsConstructor || m.IsPInvokeImpl || m.IsVirtual || m.IsRuntimeSpecialName || m.IsRuntime || m.HasOverrides;
                if (!keep) m.Name = Rename("M", full, m.Name, m.Parameters.Count.ToString());
            }
            for (int i = t.Fields.Count - 1; i >= 0; i--)
                if (t.Fields[i].IsLiteral) t.Fields.RemoveAt(i);   // 상수는 호출 자리에 이미 새겨져 있다 (열거형 멤버 포함)
            foreach (FieldDefinition f in t.Fields)
                if (!f.IsRuntimeSpecialName) f.Name = Rename("F", full, f.Name, null);
            foreach (PropertyDefinition p in t.Properties) p.Name = Rename("P", full, p.Name, null);
            foreach (EventDefinition e in t.Events) e.Name = Rename("E", full, e.Name, null);
            foreach (ICustomAttributeProvider h in t.Fields.Cast<ICustomAttributeProvider>().Concat(t.Methods).Concat(t.Properties).Concat(new ICustomAttributeProvider[] { t }).ToList())
                for (int i = h.CustomAttributes.Count - 1; i >= 0; i--)
                    if (NoRuntimeEffect(h.CustomAttributes[i])) h.CustomAttributes.RemoveAt(i);
            if (!t.IsNested) t.Namespace = "";
            t.Name = Next();
            map.Add("T|" + full + "|" + t.FullName);   // 새 전체 이름 (바깥 형식은 이미 바뀌어 있다)
        }
        for (int i = asm.CustomAttributes.Count - 1; i >= 0; i--)
            if (NoRuntimeEffect(asm.CustomAttributes[i])) asm.CustomAttributes.RemoveAt(i);
        asm.Write(output, new WriterParameters { Timestamp = timestamp });   // 입력과 같은 타임스탬프 → 같은 입력이면 같은 출력
        if (args.Length > 2) File.WriteAllLines(args[2], map.ToArray());
        Console.WriteLine(Path.GetFileName(input) + ": " + new FileInfo(input).Length + " -> " + new FileInfo(output).Length + " bytes");
    }

    static uint PeTimestamp(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        int pe = BitConverter.ToInt32(d, 0x3c);
        return BitConverter.ToUInt32(d, pe + 8);
    }
}
