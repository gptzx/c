// 배포용 실행 파일 축소 도구 — 기능과 공개 API 는 그대로 두고 메타데이터만 줄인다.
//   Mono.Cecil 로 매개변수 이름을 지우고(Param 행 자체가 사라진다), 비공개·내부 멤버와 내부 형식의 이름을 한두 글자로 줄이고, 실행에 영향 없는 특성을 뗀다.
//   공개 형식의 공개 멤버·P/Invoke 이름·열거형 멤버·가상/특수 메서드는 그대로 둔다. IL·리소스(아이콘·매니페스트·버전)·동작은 바뀌지 않는다.
// 사용: mono Shrink.exe <입력.exe> <출력.exe>   (Mono.Cecil 0.11 필요)   — build.cmd 의 일반 빌드 결과도 그대로 동작하며, dist/ 만 이 도구를 거친다.
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

static class Shrink
{
    static int counter;

    static string Next()
    {
        int n = counter++;
        string s = "";
        do { s = (char)('a' + n % 26) + s; n = n / 26 - 1; } while (n >= 0);
        return s;
    }

    static bool Exposed(TypeDefinition t)
    {
        return t.IsPublic || (t.IsNestedPublic && Exposed(t.DeclaringType));
    }

    static bool NoRuntimeEffect(CustomAttribute a)
    {
        string n = a.AttributeType.Name;
        return n == "CompilerGeneratedAttribute" || n == "DebuggerBrowsableAttribute" || n == "DebuggerHiddenAttribute" || n == "DebuggableAttribute";
    }

    static void Main(string[] args)
    {
        string input = args[0], output = args[1];
        uint timestamp = PeTimestamp(input);
        AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { ReadingMode = ReadingMode.Immediate });
        foreach (TypeDefinition t in asm.MainModule.GetTypes().ToList())
        {
            if (t.Name == "<Module>") continue;
            bool exposed = Exposed(t);
            foreach (MethodDefinition m in t.Methods)
            {
                foreach (ParameterDefinition p in m.Parameters) p.Name = "";
                bool keep = m.IsConstructor || m.IsPInvokeImpl || m.IsVirtual || m.IsSpecialName || m.IsRuntimeSpecialName || m.HasOverrides
                    || (exposed && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly));
                if (!keep) m.Name = Next();
            }
            if (!t.IsEnum)
                foreach (FieldDefinition f in t.Fields)
                    if (!f.IsRuntimeSpecialName && !f.IsSpecialName && !(exposed && (f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))) f.Name = Next();
            foreach (PropertyDefinition p in t.Properties)
                if (!exposed || !(p.GetMethod ?? p.SetMethod).IsPublic) p.Name = Next();
            if (!exposed && !t.IsEnum)
            {
                if (!t.IsNested) t.Namespace = "";
                t.Name = Next();
            }
            foreach (ICustomAttributeProvider h in t.Fields.Cast<ICustomAttributeProvider>().Concat(t.Methods).Concat(t.Properties).Concat(new ICustomAttributeProvider[] { t }).ToList())
                for (int i = h.CustomAttributes.Count - 1; i >= 0; i--)
                    if (NoRuntimeEffect(h.CustomAttributes[i])) h.CustomAttributes.RemoveAt(i);
        }
        for (int i = asm.CustomAttributes.Count - 1; i >= 0; i--)
            if (NoRuntimeEffect(asm.CustomAttributes[i])) asm.CustomAttributes.RemoveAt(i);
        asm.Write(output, new WriterParameters { Timestamp = timestamp });   // 입력과 같은 타임스탬프 → 같은 입력이면 같은 출력
        Console.WriteLine(Path.GetFileName(input) + ": " + new FileInfo(input).Length + " -> " + new FileInfo(output).Length + " bytes");
    }

    static uint PeTimestamp(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        int pe = BitConverter.ToInt32(d, 0x3c);
        return BitConverter.ToUInt32(d, pe + 8);
    }

}
