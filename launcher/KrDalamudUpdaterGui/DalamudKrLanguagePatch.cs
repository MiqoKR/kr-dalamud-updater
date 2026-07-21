using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KrDalamudUpdaterGui;

internal static class DalamudKrLanguagePatch
{
    private const string ClientLanguageTypeName = "Dalamud.Game.ClientLanguage";
    private const string CommonClientLanguageTypeName = "Dalamud.Common.ClientLanguage";
    private const string ExtensionsTypeName = "Dalamud.Utility.ClientLanguageExtensions";
    private const string DataManagerTypeName = "Dalamud.Data.DataManager";
    private const string SanitizerTypeName = "Dalamud.Game.Text.Sanitizer.Sanitizer";
    private const string LuminaLanguageTypeName = "Lumina.Data.Language";
    private const int LuminaKoreanLanguageValue = 7;

    private static readonly (string Name, int Value)[] AdditionalLanguages =
    [
        ("ChineseSimplified", 4),
        ("ChineseTraditional", 5),
        ("Korean", 6),
        ("TraditionalChinese", 7),
    ];

    private static readonly (string Code, int Value)[] LanguageCodes =
    [
        ("ja", 0),
        ("en", 1),
        ("de", 2),
        ("fr", 3),
        ("chs", 4),
        ("cht", 5),
        ("ko", 6),
        ("tc", 7),
    ];

    public static void Apply(string hookRoot)
    {
        hookRoot = Path.GetFullPath(hookRoot);
        var dalamudPath = Path.Combine(hookRoot, "Dalamud.dll");
        var commonPath = Path.Combine(hookRoot, "Dalamud.Common.dll");
        var hashesPath = Path.Combine(hookRoot, "hashes.json");
        if (!File.Exists(dalamudPath) || !File.Exists(commonPath) || !File.Exists(hashesPath))
        {
            throw new FileNotFoundException("Dalamud.dll, Dalamud.Common.dll 또는 hashes.json을 찾지 못했습니다.");
        }

        if (!IsDalamudPatched(dalamudPath))
        {
            var temporaryPath = dalamudPath + ".kr-patching";
            using var resolver = CreateResolver(hookRoot);
            using (var assembly = AssemblyDefinition.ReadAssembly(dalamudPath, new ReaderParameters
                   {
                       InMemory = true,
                       AssemblyResolver = resolver,
                   }))
            {
                var module = assembly.MainModule;
                var clientLanguage = RequireType(module, ClientLanguageTypeName);
                var extensions = RequireType(module, ExtensionsTypeName);

                AddLanguageFields(clientLanguage);
                RewriteToLumina(module, RequireMethod(extensions, "ToLumina"));
                RewriteToCode(module, RequireMethod(extensions, "ToCode"));
                RewriteToClientLanguage(module, RequireMethod(extensions, "ToClientLanguage"));
                PatchDataManager(module, RequireType(module, DataManagerTypeName), clientLanguage);
                PatchSanitizer(RequireType(module, SanitizerTypeName));
                assembly.Write(temporaryPath);
            }

            VerifyDalamudAssembly(temporaryPath);
            var backupRoot = Path.Combine(hookRoot, "kr-language-backup");
            Directory.CreateDirectory(backupRoot);
            var backupPath = Path.Combine(backupRoot, "Dalamud.dll.official");
            if (!File.Exists(backupPath))
            {
                File.Copy(dalamudPath, backupPath);
            }

            File.Move(temporaryPath, dalamudPath, true);
        }

        if (!IsCommonPatched(commonPath))
        {
            var temporaryPath = commonPath + ".kr-patching";
            using var resolver = CreateResolver(hookRoot);
            using (var assembly = AssemblyDefinition.ReadAssembly(commonPath, new ReaderParameters
                   {
                       InMemory = true,
                       AssemblyResolver = resolver,
                   }))
            {
                AddLanguageFields(RequireType(assembly.MainModule, CommonClientLanguageTypeName));
                assembly.Write(temporaryPath);
            }

            VerifyCommonAssembly(temporaryPath);
            var backupRoot = Path.Combine(hookRoot, "kr-language-backup");
            Directory.CreateDirectory(backupRoot);
            var backupPath = Path.Combine(backupRoot, "Dalamud.Common.dll.official");
            if (!File.Exists(backupPath))
            {
                File.Copy(commonPath, backupPath);
            }

            File.Move(temporaryPath, commonPath, true);
        }

        UpdateAssemblyHash(hashesPath, dalamudPath);
        UpdateAssemblyHash(hashesPath, commonPath);
        WriteMarker(hookRoot);
        Verify(hookRoot);
    }

    public static void Verify(string hookRoot)
    {
        hookRoot = Path.GetFullPath(hookRoot);
        var dalamudPath = Path.Combine(hookRoot, "Dalamud.dll");
        var commonPath = Path.Combine(hookRoot, "Dalamud.Common.dll");
        if (!File.Exists(dalamudPath) || !File.Exists(commonPath))
        {
            throw new FileNotFoundException("Dalamud.dll 또는 Dalamud.Common.dll을 찾지 못했습니다.");
        }

        VerifyDalamudAssembly(dalamudPath);
        VerifyCommonAssembly(commonPath);
    }

    private static bool IsDalamudPatched(string dalamudPath)
    {
        try
        {
            VerifyDalamudAssembly(dalamudPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCommonPatched(string commonPath)
    {
        try
        {
            VerifyCommonAssembly(commonPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void VerifyDalamudAssembly(string dalamudPath)
    {
        using var resolver = CreateResolver(Path.GetDirectoryName(dalamudPath)
            ?? throw new InvalidDataException("Dalamud.dll 경로를 확인할 수 없습니다."));
        using var assembly = AssemblyDefinition.ReadAssembly(dalamudPath, new ReaderParameters
        {
            InMemory = true,
            AssemblyResolver = resolver,
        });
        var clientLanguage = RequireType(assembly.MainModule, ClientLanguageTypeName);
        foreach (var language in AdditionalLanguages)
        {
            var field = clientLanguage.Fields.SingleOrDefault(field => field.Name == language.Name && field.HasConstant);
            if (field is null || Convert.ToInt32(field.Constant) != language.Value)
            {
                throw new InvalidDataException($"ClientLanguage.{language.Name} 패치가 없습니다.");
            }
        }

        var extensions = RequireType(assembly.MainModule, ExtensionsTypeName);
        var toLumina = RequireMethod(extensions, "ToLumina");
        var switchInstruction = toLumina.Body.Instructions.SingleOrDefault(instruction => instruction.OpCode == OpCodes.Switch);
        if (switchInstruction?.Operand is not Instruction[] targets || targets.Length != 8)
        {
            throw new InvalidDataException("ClientLanguage.ToLumina KR 패치가 없습니다.");
        }

        VerifyLanguageStrings(RequireMethod(extensions, "ToCode"));
        VerifyLanguageStrings(RequireMethod(extensions, "ToClientLanguage"));
        VerifyDataManager(RequireType(assembly.MainModule, DataManagerTypeName));
        VerifySanitizer(RequireType(assembly.MainModule, SanitizerTypeName));
    }

    private static void VerifyCommonAssembly(string commonPath)
    {
        using var resolver = CreateResolver(Path.GetDirectoryName(commonPath)
            ?? throw new InvalidDataException("Dalamud.Common.dll 경로를 확인할 수 없습니다."));
        using var assembly = AssemblyDefinition.ReadAssembly(commonPath, new ReaderParameters
        {
            InMemory = true,
            AssemblyResolver = resolver,
        });
        var clientLanguage = RequireType(assembly.MainModule, CommonClientLanguageTypeName);
        foreach (var language in AdditionalLanguages)
        {
            var field = clientLanguage.Fields.SingleOrDefault(field => field.Name == language.Name && field.HasConstant);
            if (field is null || Convert.ToInt32(field.Constant) != language.Value)
            {
                throw new InvalidDataException($"Dalamud.Common.ClientLanguage.{language.Name} 패치가 없습니다.");
            }
        }
    }

    private static void VerifyLanguageStrings(MethodDefinition method)
    {
        var strings = method.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
            .Select(instruction => instruction.Operand as string)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var language in LanguageCodes)
        {
            if (!strings.Contains(language.Code))
            {
                throw new InvalidDataException($"{method.Name}에 언어 코드 {language.Code} 패치가 없습니다.");
            }
        }
    }

    private static void AddLanguageFields(TypeDefinition clientLanguage)
    {
        foreach (var language in AdditionalLanguages)
        {
            var existing = clientLanguage.Fields.SingleOrDefault(field => field.Name == language.Name);
            if (existing is not null)
            {
                if (!existing.HasConstant || Convert.ToInt32(existing.Constant) != language.Value)
                {
                    throw new InvalidDataException($"ClientLanguage.{language.Name} 값이 예상과 다릅니다.");
                }

                continue;
            }

            clientLanguage.Fields.Add(new FieldDefinition(
                language.Name,
                Mono.Cecil.FieldAttributes.Public |
                Mono.Cecil.FieldAttributes.Static |
                Mono.Cecil.FieldAttributes.Literal |
                Mono.Cecil.FieldAttributes.HasDefault,
                clientLanguage)
            {
                Constant = language.Value,
            });
        }
    }

    private static void RewriteToLumina(ModuleDefinition module, MethodDefinition method)
    {
        ResetBody(method);
        var il = method.Body.GetILProcessor();
        var targets = Enumerable.Range(1, 8).Select(value => Instruction.Create(OpCodes.Ldc_I4, value)).ToArray();
        var throwStart = Instruction.Create(OpCodes.Ldstr, "language");

        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Switch, targets));
        il.Append(Instruction.Create(OpCodes.Br, throwStart));
        foreach (var target in targets)
        {
            il.Append(target);
            il.Append(Instruction.Create(OpCodes.Ret));
        }

        AppendOutOfRangeThrow(module, il, throwStart);
    }

    private static void RewriteToCode(ModuleDefinition module, MethodDefinition method)
    {
        ResetBody(method);
        var il = method.Body.GetILProcessor();
        var targets = LanguageCodes.Select(language => Instruction.Create(OpCodes.Ldstr, language.Code)).ToArray();
        var throwStart = Instruction.Create(OpCodes.Ldstr, "value");

        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Switch, targets));
        il.Append(Instruction.Create(OpCodes.Br, throwStart));
        foreach (var target in targets)
        {
            il.Append(target);
            il.Append(Instruction.Create(OpCodes.Ret));
        }

        AppendOutOfRangeThrow(module, il, throwStart);
    }

    private static void RewriteToClientLanguage(ModuleDefinition module, MethodDefinition method)
    {
        ResetBody(method);
        var il = method.Body.GetILProcessor();
        var equality = module.ImportReference(typeof(string).GetMethod(
            "op_Equality",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            [typeof(string), typeof(string)]) ?? throw new MissingMethodException("String.op_Equality"));
        var targets = LanguageCodes.Select(language => Instruction.Create(OpCodes.Ldc_I4, language.Value)).ToArray();

        for (var i = 0; i < LanguageCodes.Length; i++)
        {
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldstr, LanguageCodes[i].Code));
            il.Append(Instruction.Create(OpCodes.Call, equality));
            il.Append(Instruction.Create(OpCodes.Brtrue, targets[i]));
        }

        var throwStart = Instruction.Create(OpCodes.Ldstr, "value");
        AppendOutOfRangeThrow(module, il, throwStart);
        foreach (var target in targets)
        {
            il.Append(target);
            il.Append(Instruction.Create(OpCodes.Ret));
        }
    }

    private static void AppendOutOfRangeThrow(ModuleDefinition module, ILProcessor il, Instruction throwStart)
    {
        var constructor = typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])
            ?? throw new MissingMethodException("ArgumentOutOfRangeException(string)");
        il.Append(throwStart);
        il.Append(Instruction.Create(OpCodes.Newobj, module.ImportReference(constructor)));
        il.Append(Instruction.Create(OpCodes.Throw));
    }

    private static void ResetBody(MethodDefinition method)
    {
        method.Body = new MethodBody(method)
        {
            InitLocals = false,
            MaxStackSize = 4,
        };
    }

    private static void PatchDataManager(
        ModuleDefinition module,
        TypeDefinition dataManager,
        TypeDefinition clientLanguage)
    {
        var constructor = dataManager.Methods.Single(method => method.IsConstructor && !method.IsStatic);
        var languageSetter = RequireMethod(dataManager, "set_Language");
        var setterCalls = constructor.Body.Instructions
            .Where(instruction => IsCallTo(instruction, languageSetter.Name, dataManager.FullName))
            .ToArray();
        if (!setterCalls.Any(call => IsLoadedInt32(PreviousMeaningful(call), 6)))
        {
            var il = constructor.Body.GetILProcessor();
            var insertAfter = setterCalls.FirstOrDefault()
                ?? throw new InvalidDataException("DataManager.Language 초기화 코드를 찾지 못했습니다.");
            var loadThis = Instruction.Create(OpCodes.Ldarg_0);
            var loadKorean = Instruction.Create(OpCodes.Ldc_I4, 6);
            var setKorean = Instruction.Create(OpCodes.Call, languageSetter);
            il.InsertAfter(insertAfter, loadThis);
            il.InsertAfter(loadThis, loadKorean);
            il.InsertAfter(loadKorean, setKorean);
        }

        var checksumSetter = constructor.Body.Instructions.SingleOrDefault(instruction =>
            IsCallTo(instruction, "set_PanicOnSheetChecksumMismatch", "Lumina.LuminaOptions"))
            ?? throw new InvalidDataException("Lumina 체크섬 설정 코드를 찾지 못했습니다.");
        SetLoadedInt32(PreviousMeaningful(checksumSetter)
            ?? throw new InvalidDataException("Lumina 체크섬 설정값을 찾지 못했습니다."), 0);

        RewriteExcelLanguage(RequireMethod(dataManager, "GetExcelSheet"), "GetSheet");
        RewriteExcelLanguage(RequireMethod(dataManager, "GetSubrowExcelSheet"), "GetSubrowSheet");
    }

    private static void RewriteExcelLanguage(MethodDefinition method, string sheetMethodName)
    {
        var nameParameter = method.Parameters.ElementAtOrDefault(1)
            ?? throw new InvalidDataException($"{method.Name} name parameter was not found.");
        var excelGetter = method.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .FirstOrDefault(reference => reference.Name == "get_Excel" &&
                                         reference.DeclaringType.FullName == DataManagerTypeName)
            ?? throw new InvalidDataException($"{method.Name} Excel getter was not found.");
        var sheetCall = method.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
            .FirstOrDefault(instruction => instruction.Operand is MethodReference reference &&
                                           reference.Name == sheetMethodName)
            ?? throw new InvalidDataException($"{method.Name} {sheetMethodName} call was not found.");
        var nullableLuminaConstructor = method.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Newobj)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .FirstOrDefault(IsNullableLuminaLanguageConstructor)
            ?? throw new InvalidDataException($"{method.Name} Nullable<Lumina.Data.Language> constructor was not found.");

        method.Body = new MethodBody(method)
        {
            InitLocals = false,
            MaxStackSize = 4,
        };
        var il = method.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Call, excelGetter));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_7));
        il.Append(Instruction.Create(OpCodes.Newobj, nullableLuminaConstructor));
        il.Append(Instruction.Create(OpCodes.Ldarg, nameParameter));
        il.Append(Instruction.Create(sheetCall.OpCode, (MethodReference)sheetCall.Operand));
        il.Append(Instruction.Create(OpCodes.Ret));
    }

    private static void PatchSanitizer(TypeDefinition sanitizer)
    {
        var methods = sanitizer.Methods
            .Where(method => method.Name == "SanitizeByLanguage" &&
                             method.IsStatic &&
                             method.Parameters.Count == 2 &&
                             method.Parameters[1].ParameterType.FullName == ClientLanguageTypeName)
            .ToArray();
        if (methods.Length != 2)
        {
            throw new InvalidDataException("Sanitizer 언어 처리 메서드 구성이 예상과 다릅니다.");
        }

        foreach (var method in methods)
        {
            var switchInstruction = method.Body.Instructions.SingleOrDefault(instruction => instruction.OpCode == OpCodes.Switch)
                ?? throw new InvalidDataException($"{method.FullName} 언어 switch를 찾지 못했습니다.");
            if (switchInstruction.Operand is not Instruction[] targets || targets.Length < 4)
            {
                throw new InvalidDataException($"{method.FullName} 언어 switch 구성이 예상과 다릅니다.");
            }

            if (targets.Length >= 7 && ReferenceEquals(targets[6], targets[0]))
            {
                continue;
            }

            var patchedTargets = new Instruction[Math.Max(targets.Length, 7)];
            Array.Copy(targets, patchedTargets, targets.Length);
            for (var i = targets.Length; i < patchedTargets.Length; i++)
            {
                patchedTargets[i] = targets[0];
            }

            patchedTargets[6] = targets[0];
            switchInstruction.Operand = patchedTargets;
        }
    }

    private static void VerifyDataManager(TypeDefinition dataManager)
    {
        var constructor = dataManager.Methods.Single(method => method.IsConstructor && !method.IsStatic);
        var hasKoreanAssignment = constructor.Body.Instructions.Any(instruction =>
            IsCallTo(instruction, "set_Language", dataManager.FullName) &&
            IsLoadedInt32(PreviousMeaningful(instruction), 6));
        if (!hasKoreanAssignment)
        {
            throw new InvalidDataException("DataManager Korean 언어 강제 패치가 없습니다.");
        }

        var checksumSetter = constructor.Body.Instructions.SingleOrDefault(instruction =>
            IsCallTo(instruction, "set_PanicOnSheetChecksumMismatch", "Lumina.LuminaOptions"));
        if (checksumSetter is null || !IsLoadedInt32(PreviousMeaningful(checksumSetter), 0))
        {
            throw new InvalidDataException("DataManager Lumina 체크섬 패치가 없습니다.");
        }

        VerifyExcelLanguage(RequireMethod(dataManager, "GetExcelSheet"), "GetSheet");
        VerifyExcelLanguage(RequireMethod(dataManager, "GetSubrowExcelSheet"), "GetSubrowSheet");
    }

    private static void VerifyExcelLanguage(MethodDefinition method, string sheetMethodName)
    {
        if (method.Body.Instructions.Any(IsNullableClientLanguageConstructor))
        {
            throw new InvalidDataException($"{method.Name} still constructs Nullable<ClientLanguage>.");
        }

        var instructions = method.Body.Instructions;
        var koreanLoad = instructions.SingleOrDefault(instruction =>
            IsLoadedInt32(instruction, LuminaKoreanLanguageValue) &&
            instruction.Next?.OpCode == OpCodes.Newobj &&
            instruction.Next.Operand is MethodReference constructor &&
            IsNullableLuminaLanguageConstructor(constructor));
        if (koreanLoad is null)
        {
            throw new InvalidDataException($"{method.Name} does not force Lumina Korean safely.");
        }

        if (!instructions.Any(instruction =>
                (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                instruction.Operand is MethodReference reference &&
                reference.Name == sheetMethodName))
        {
            throw new InvalidDataException($"{method.Name} no longer calls {sheetMethodName}.");
        }
    }

    private static bool IsNullableClientLanguageConstructor(Instruction instruction)
    {
        if (instruction.OpCode != OpCodes.Newobj || instruction.Operand is not MethodReference method || method.Name != ".ctor")
        {
            return false;
        }

        return method.DeclaringType is GenericInstanceType generic &&
               generic.ElementType.FullName == "System.Nullable`1" &&
               generic.GenericArguments.Count == 1 &&
               generic.GenericArguments[0].FullName == ClientLanguageTypeName;
    }

    private static bool IsNullableLuminaLanguageConstructor(MethodReference method)
    {
        return method.Name == ".ctor" &&
               method.DeclaringType is GenericInstanceType generic &&
               generic.ElementType.FullName == "System.Nullable`1" &&
               generic.GenericArguments.Count == 1 &&
               generic.GenericArguments[0].FullName == LuminaLanguageTypeName;
    }

    private static void VerifySanitizer(TypeDefinition sanitizer)
    {
        var methods = sanitizer.Methods
            .Where(method => method.Name == "SanitizeByLanguage" &&
                             method.IsStatic &&
                             method.Parameters.Count == 2 &&
                             method.Parameters[1].ParameterType.FullName == ClientLanguageTypeName)
            .ToArray();
        if (methods.Length != 2 || methods.Any(method =>
                method.Body.Instructions.SingleOrDefault(instruction => instruction.OpCode == OpCodes.Switch)?.Operand is not Instruction[] targets ||
                targets.Length < 7 ||
                !ReferenceEquals(targets[6], targets[0])))
        {
            throw new InvalidDataException("Sanitizer Korean 언어 패치가 없습니다.");
        }
    }

    private static bool IsCallTo(Instruction instruction, string methodName, string declaringType)
        => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
           instruction.Operand is MethodReference reference &&
           reference.Name == methodName &&
           reference.DeclaringType.FullName == declaringType;

    private static Instruction? PreviousMeaningful(Instruction instruction)
    {
        var previous = instruction.Previous;
        while (previous?.OpCode == OpCodes.Nop)
        {
            previous = previous.Previous;
        }

        return previous;
    }

    private static bool IsLoadedInt32(Instruction? instruction, int value)
    {
        if (instruction is null)
        {
            return false;
        }

        return value switch
        {
            -1 => instruction.OpCode == OpCodes.Ldc_I4_M1,
            0 => instruction.OpCode == OpCodes.Ldc_I4_0,
            1 => instruction.OpCode == OpCodes.Ldc_I4_1,
            2 => instruction.OpCode == OpCodes.Ldc_I4_2,
            3 => instruction.OpCode == OpCodes.Ldc_I4_3,
            4 => instruction.OpCode == OpCodes.Ldc_I4_4,
            5 => instruction.OpCode == OpCodes.Ldc_I4_5,
            6 => instruction.OpCode == OpCodes.Ldc_I4_6 ||
                 (instruction.OpCode == OpCodes.Ldc_I4 && Equals(instruction.Operand, 6)) ||
                 (instruction.OpCode == OpCodes.Ldc_I4_S && Convert.ToInt32(instruction.Operand) == 6),
            7 => instruction.OpCode == OpCodes.Ldc_I4_7,
            8 => instruction.OpCode == OpCodes.Ldc_I4_8,
            _ => (instruction.OpCode == OpCodes.Ldc_I4 && Equals(instruction.Operand, value)) ||
                 (instruction.OpCode == OpCodes.Ldc_I4_S && Convert.ToInt32(instruction.Operand) == value),
        };
    }

    private static void SetLoadedInt32(Instruction instruction, int value)
    {
        instruction.OpCode = value switch
        {
            -1 => OpCodes.Ldc_I4_M1,
            0 => OpCodes.Ldc_I4_0,
            1 => OpCodes.Ldc_I4_1,
            2 => OpCodes.Ldc_I4_2,
            3 => OpCodes.Ldc_I4_3,
            4 => OpCodes.Ldc_I4_4,
            5 => OpCodes.Ldc_I4_5,
            6 => OpCodes.Ldc_I4_6,
            7 => OpCodes.Ldc_I4_7,
            8 => OpCodes.Ldc_I4_8,
            _ => OpCodes.Ldc_I4,
        };
        instruction.Operand = value is >= -1 and <= 8 ? null : value;
    }

    private static TypeDefinition RequireType(ModuleDefinition module, string fullName)
        => module.Types.SingleOrDefault(type => type.FullName == fullName)
           ?? throw new InvalidDataException($"지원되지 않는 Dalamud 빌드입니다. 형식 없음: {fullName}");

    private static MethodDefinition RequireMethod(TypeDefinition type, string name)
        => type.Methods.SingleOrDefault(method => method.Name == name)
           ?? throw new InvalidDataException($"지원되지 않는 Dalamud 빌드입니다. 메서드 없음: {type.FullName}::{name}");

    private static DefaultAssemblyResolver CreateResolver(string hookRoot)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(hookRoot);
        return resolver;
    }

    private static void UpdateAssemblyHash(string hashesPath, string assemblyPath)
    {
        var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(hashesPath))
            ?? throw new InvalidDataException("hashes.json을 읽지 못했습니다.");
        var fileName = Path.GetFileName(assemblyPath);
        var key = hashes.Keys.SingleOrDefault(key => key.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"hashes.json에 {fileName} 항목이 없습니다.");
        using var stream = File.OpenRead(assemblyPath);
        hashes[key] = Convert.ToHexString(MD5.HashData(stream));
        File.WriteAllText(hashesPath, JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteMarker(string hookRoot)
    {
        var marker = new
        {
            Patch = "Dalamud KR ClientLanguage",
            Version = 4,
            AppliedAtUtc = DateTimeOffset.UtcNow,
            Languages = AdditionalLanguages.Select(language => new { language.Name, language.Value }),
            CorePatches = new[] { "DataManagerKorean", "ExcelLanguageKorean", "LuminaChecksum", "SanitizerKorean" },
        };
        File.WriteAllText(
            Path.Combine(hookRoot, "Dalamud.KR.Language.Patch.json"),
            JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }
}
