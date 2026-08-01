using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KrDalamudUpdaterGui;

internal static class DalamudKrSignaturePatch
{
    private const string ResolverTypeName = "Dalamud.Game.ClientState.ClientStateAddressResolver";
    private const string ResolverMethodName = "Setup64Bit";
    private const string GlobalSignature = "48 8D 0C 85 ?? ?? ?? ?? 8B 04 31 85 C2 0F 85";
    private const string Korean755Signature = "48 8D 0C 85 ?? ?? ?? ?? 8B 04 39 85 C2 0F 85";
    private const string GlobalIndexSignature = "0F B6 94 33 ?? ?? ?? ?? 84 D2";
    private const string Korean755IndexSignature = "0F B6 B4 3B ?? ?? ?? ?? 40 84 F6";
    private const string ClientStateTypeName = "Dalamud.Game.ClientState.ClientState";
    private const string LoggedInFallbackFieldName = "IsLoggedIntoZone";

    public static void Apply(string hookRoot)
    {
        hookRoot = Path.GetFullPath(hookRoot);
        var dalamudPath = Path.Combine(hookRoot, "Dalamud.dll");
        if (!File.Exists(dalamudPath))
        {
            throw new FileNotFoundException("Dalamud.dll을 찾지 못했습니다.", dalamudPath);
        }

        var temporaryPath = dalamudPath + ".kr-signature-patching";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        try
        {
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(hookRoot);
            using var assembly = AssemblyDefinition.ReadAssembly(dalamudPath, new ReaderParameters
            {
                InMemory = true,
                AssemblyResolver = resolver,
            });

            var method = RequireResolverMethod(assembly.MainModule);
            var globalLoads = FindSignatureLoads(method, GlobalSignature);
            var koreanLoads = FindSignatureLoads(method, Korean755Signature);
            var globalIndexLoads = FindSignatureLoads(method, GlobalIndexSignature);
            var koreanIndexLoads = FindSignatureLoads(method, Korean755IndexSignature);
            var keyboardPatched = koreanLoads.Length == 1 && globalLoads.Length == 0;
            var indexPatched = koreanIndexLoads.Length == 1 && globalIndexLoads.Length == 0;
            var loginPatched = HasLoggedInFallback(assembly.MainModule);
            if (keyboardPatched && indexPatched && loginPatched)
            {
                WriteMarker(hookRoot);
                return;
            }

            if (!keyboardPatched && (globalLoads.Length != 1 || koreanLoads.Length != 0))
            {
                throw new InvalidDataException(
                    $"예상하지 못한 키보드 시그니처 구조입니다. " +
                    $"Global={globalLoads.Length}, Korean755={koreanLoads.Length}");
            }

            if (!indexPatched && (globalIndexLoads.Length != 1 || koreanIndexLoads.Length != 0))
            {
                throw new InvalidDataException(
                    $"예상하지 못한 키보드 인덱스 시그니처 구조입니다. " +
                    $"Global={globalIndexLoads.Length}, Korean755={koreanIndexLoads.Length}");
            }

            if (!keyboardPatched)
            {
                globalLoads[0].Operand = Korean755Signature;
            }

            if (!indexPatched)
            {
                globalIndexLoads[0].Operand = Korean755IndexSignature;
            }

            if (!loginPatched)
            {
                PatchLoggedInFallback(assembly.MainModule);
            }

            assembly.Write(temporaryPath);
            File.Move(temporaryPath, dalamudPath, true);
            WriteMarker(hookRoot);
            Verify(hookRoot);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static void Verify(string hookRoot)
    {
        hookRoot = Path.GetFullPath(hookRoot);
        var dalamudPath = Path.Combine(hookRoot, "Dalamud.dll");
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(hookRoot);
        using var assembly = AssemblyDefinition.ReadAssembly(dalamudPath, new ReaderParameters
        {
            InMemory = true,
            AssemblyResolver = resolver,
        });

        var method = RequireResolverMethod(assembly.MainModule);
        var globalLoads = FindSignatureLoads(method, GlobalSignature);
        var koreanLoads = FindSignatureLoads(method, Korean755Signature);
        var globalIndexLoads = FindSignatureLoads(method, GlobalIndexSignature);
        var koreanIndexLoads = FindSignatureLoads(method, Korean755IndexSignature);
        if (globalLoads.Length != 0 ||
            koreanLoads.Length != 1 ||
            globalIndexLoads.Length != 0 ||
            koreanIndexLoads.Length != 1 ||
            !HasLoggedInFallback(assembly.MainModule))
        {
            throw new InvalidDataException(
                $"KR 7.55 시그니처 패치 검증에 실패했습니다. " +
                $"Keyboard(Global={globalLoads.Length}, Korean755={koreanLoads.Length}), " +
                $"Index(Global={globalIndexLoads.Length}, Korean755={koreanIndexLoads.Length})");
        }
    }

    // The Korean client can expose both AgentLobby login flags as false after zone entry.
    // TerritoryType is updated by Dalamud's zone-change path and remains 0 on the character-select screen.
    private static void PatchLoggedInFallback(ModuleDefinition module)
    {
        var clientState = module.Types.SingleOrDefault(type => type.FullName == ClientStateTypeName)
            ?? throw new InvalidDataException($"Dalamud ClientState type was not found: {ClientStateTypeName}");
        var getter = clientState.Properties.SingleOrDefault(property => property.Name == "IsLoggedIn")?.GetMethod
            ?? throw new InvalidDataException("Dalamud ClientState.IsLoggedIn getter was not found.");
        if (HasLoggedInFallback(getter))
            return;

        var loggedInFieldLoad = getter.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldfld && instruction.Operand is FieldReference field && field.Name == "IsLoggedIn")
            ?? throw new InvalidDataException("Dalamud ClientState.IsLoggedIn field read was not found.");
        var loggedInField = (FieldReference)loggedInFieldLoad.Operand;
        var lobbyType = loggedInField.DeclaringType.Resolve()
            ?? throw new InvalidDataException("Dalamud AgentLobby type could not be resolved.");
        var loggedIntoZoneField = lobbyType.Fields.SingleOrDefault(field => field.Name == LoggedInFallbackFieldName)
            ?? throw new InvalidDataException($"Dalamud AgentLobby.{LoggedInFallbackFieldName} was not found.");
        var lobbyInstance = getter.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference reference &&
            reference.DeclaringType.FullName == lobbyType.FullName && reference.Name == "Instance")?.Operand as MethodReference
            ?? throw new InvalidDataException("Dalamud AgentLobby.Instance call was not found.");
        var territoryType = clientState.Properties.SingleOrDefault(property => property.Name == "TerritoryType")?.GetMethod
            ?? throw new InvalidDataException("Dalamud ClientState.TerritoryType getter was not found.");

        var lobbyLocal = new VariableDefinition(new PointerType(module.ImportReference(loggedInField.DeclaringType)));
        getter.Body.ExceptionHandlers.Clear();
        getter.Body.Variables.Clear();
        getter.Body.Variables.Add(lobbyLocal);
        getter.Body.InitLocals = true;
        getter.Body.Instructions.Clear();
        var il = getter.Body.GetILProcessor();
        var checkTerritory = il.Create(OpCodes.Ldarg_0);
        var loggedIn = il.Create(OpCodes.Ldc_I4_1);
        var loggedOut = il.Create(OpCodes.Ldc_I4_0);
        var instructions = new[]
        {
            il.Create(OpCodes.Call, module.ImportReference(lobbyInstance)),
            il.Create(OpCodes.Stloc, lobbyLocal),
            il.Create(OpCodes.Ldloc, lobbyLocal),
            il.Create(OpCodes.Brfalse, checkTerritory),
            il.Create(OpCodes.Ldloc, lobbyLocal),
            il.Create(OpCodes.Ldfld, module.ImportReference(loggedInField)),
            il.Create(OpCodes.Brtrue, loggedIn),
            il.Create(OpCodes.Ldloc, lobbyLocal),
            il.Create(OpCodes.Ldfld, module.ImportReference(loggedIntoZoneField)),
            il.Create(OpCodes.Brtrue, loggedIn),
            checkTerritory,
            il.Create(OpCodes.Call, module.ImportReference(territoryType)),
            il.Create(OpCodes.Brfalse, loggedOut),
            loggedIn,
            il.Create(OpCodes.Ret),
            loggedOut,
            il.Create(OpCodes.Ret),
        };
        foreach (var instruction in instructions)
            il.Append(instruction);
    }

    private static bool HasLoggedInFallback(ModuleDefinition module)
    {
        var clientState = module.Types.SingleOrDefault(type => type.FullName == ClientStateTypeName);
        var getter = clientState?.Properties.SingleOrDefault(property => property.Name == "IsLoggedIn")?.GetMethod;
        return getter != null && HasLoggedInFallback(getter);
    }

    private static bool HasLoggedInFallback(MethodDefinition getter)
        => getter.Body.Instructions.Any(instruction =>
               instruction.OpCode == OpCodes.Ldfld && instruction.Operand is FieldReference field &&
               field.Name == LoggedInFallbackFieldName) &&
           getter.Body.Instructions.Any(instruction =>
               instruction.Operand is MethodReference reference &&
               reference.DeclaringType.FullName == ClientStateTypeName && reference.Name == "get_TerritoryType");

    private static MethodDefinition RequireResolverMethod(ModuleDefinition module)
    {
        var type = module.Types.SingleOrDefault(candidate => candidate.FullName == ResolverTypeName)
            ?? throw new InvalidDataException($"Dalamud 형식을 찾지 못했습니다: {ResolverTypeName}");
        return type.Methods.SingleOrDefault(candidate =>
                   candidate.Name == ResolverMethodName &&
                   candidate.HasBody)
               ?? throw new InvalidDataException(
                   $"Dalamud 메서드를 찾지 못했습니다: {ResolverTypeName}::{ResolverMethodName}");
    }

    private static Instruction[] FindSignatureLoads(MethodDefinition method, string signature)
        => method.Body.Instructions
            .Where(instruction =>
                instruction.OpCode == OpCodes.Ldstr &&
                instruction.Operand is string value &&
                value.Equals(signature, StringComparison.Ordinal))
            .ToArray();

    private static void WriteMarker(string hookRoot)
    {
        var marker = new
        {
            Patch = "Dalamud KR 7.55 ClientState Signature",
            Version = 3,
            AppliedAtUtc = DateTimeOffset.UtcNow,
            ExpectedGameVersion = DalamudKrCompatibilityPatch.SupportedGameVersion,
            Resolver = ResolverTypeName,
            GlobalSignature,
            Korean755Signature,
            GlobalIndexSignature,
            Korean755IndexSignature,
            LoggedInFallback = "AgentLobby.IsLoggedIn || AgentLobby.IsLoggedIntoZone || ClientState.TerritoryType != 0",
        };
        File.WriteAllText(
            Path.Combine(hookRoot, "Dalamud.KR.755.Signature.Patch.json"),
            JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }
}
