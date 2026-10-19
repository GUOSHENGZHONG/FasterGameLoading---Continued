using System;
using System.Collections.Generic;
#pragma warning disable MA0148, MA0002, MA0003, MA0016, MA0051, MA0106, MA0134, MA0032, MA0039, S3776, S3267, S3398, S108, S2486, S2701
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace FasterGameLoading.Tests;
/// <summary>
/// 單元測試初始化設定。
/// 透過 AssemblyResolve 事件，在單元測試執行期間自動從本地 RimWorld 安裝目錄載入 Assembly-CSharp.dll 與其相依的 Unity DLL，
/// 以解決單元測試執行時的 FileNotFoundException。
/// </summary>
[SetUpFixture]
public class TestSetup
{
    private static bool registered;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void ModuleInit()
    {
        Initialize();
    }

    static TestSetup()
    {
        Initialize();
    }

    /// <summary>
    /// 註冊組件解析器並套用 headless 測試環境的全域 Harmony patch。
    /// 整個流程以 try/catch 包覆：本機若缺少 RimWorld 安裝，應讓個別測試以明確錯誤失敗，
    /// 而非讓模組初始化例外中斷整個測試組件的載入（否則 NUnit 只會回報「找不到任何測試」）。
    /// </summary>
    private static void Initialize()
    {
        try
        {
            RegisterResolver();
            ApplyGlobalPatches();
        }
        catch (Exception ex)
        {
            // AssemblyLoadContext.Resolving 事件處理常式必須同步回傳，無法改用非同步輸出
#pragma warning disable MA0045
            Console.WriteLine("[TestSetup] 初始化失敗：" + ex.GetType().Name + ": " + ex.Message);
#pragma warning restore MA0045
        }
    }

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        RegisterResolver();
        ApplyGlobalPatches();
    }

    private static bool patchesApplied;

    /// <summary>
    /// 套用 headless 測試環境所需的全域 Harmony patch。
    /// 目標：在無 Unity 圖形與主執行緒環境下，隔離會觸發 ECall / 靜態建構函式連鎖的 Unity 與 Verse API，
    /// 使單元測試得以在 dotnet test 環境中穩定執行。
    /// </summary>
    private static void ApplyGlobalPatches()
    {
        if (patchesApplied) return;
        patchesApplied = true;

        // Unity 物件的 op_Equality/op_Inequality 在無實體物件（FormatterServices 建立）下會觸發 ECall，
        // 改以 ReferenceEquals 語意取代，避免測試環境中的 NullReference 或原生檢查。
        try
        {
            var opEqualityHarmony = new HarmonyLib.Harmony("FasterGameLoading.Tests.HeadlessEnvironment");
            var opEq = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Object), "op_Equality", [typeof(UnityEngine.Object), typeof(UnityEngine.Object)]);
            var opIneq = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality", [typeof(UnityEngine.Object), typeof(UnityEngine.Object)]);
            var prefixOpEq = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_OpEqualityStub), BindingFlags.Static | BindingFlags.NonPublic));
            var prefixOpIneq = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_OpInequalityStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (opEq != null) opEqualityHarmony.Patch(opEq, prefix: prefixOpEq);
            if (opIneq != null) opEqualityHarmony.Patch(opIneq, prefix: prefixOpIneq);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TestSetup] op_Equality patch failed: " + ex.GetType().Name + ": " + ex.Message);
        }

        // 隔離會連鎖觸發 ECall 的靜態建構函式（ModsConfig/ShaderDatabase/BaseContent），
        // 防止其在型別首次存取時拋出原生例外。
        try
        {
            var staticCtorHarmony = new HarmonyLib.Harmony("FasterGameLoading.Tests.StaticCtorIsolation");
            var prefixFalse = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_FalseStub), BindingFlags.Static | BindingFlags.NonPublic));
            foreach (var cctorType in new System.Type[] {
                typeof(Verse.ModsConfig),
                typeof(Verse.ShaderDatabase),
                typeof(Verse.BaseContent),
            })
            {
                try
                {
                    var cctor = cctorType.GetConstructor(BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (cctor != null) staticCtorHarmony.Patch(cctor, prefix: prefixFalse);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[TestSetup] cctor patch failed for " + cctorType.FullName + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TestSetup] cctor patch setup failed: " + ex.GetType().Name + ": " + ex.Message);
        }

        // 以 mock 取代 Verse.Prefs 資料，避免偏好設定檔案存取。
        // 1.7 的 UnityStubs 沒有 ResourcesAPI 可替換（Resources 為受控實作），不再 mock 資源載入。
        try
        {
            var prefsDataField = HarmonyLib.AccessTools.Field(typeof(Verse.Prefs), "data");
            if (prefsDataField != null && prefsDataField.GetValue(null) == null)
            {
                var prefsDataType = typeof(Verse.Prefs).Assembly.GetType("Verse.PrefsData");
                if (prefsDataType != null)
                {
                    prefsDataField.SetValue(null, Activator.CreateInstance(prefsDataType));
                }
            }
        }
        catch { }

        try
        {
            var globalHarmony = new HarmonyLib.Harmony("FasterGameLoading.Tests.GlobalSetup");

            // 隔離 ContentFinder<Texture2D>.Get
            var contentFinderGet = HarmonyLib.AccessTools.Method(typeof(Verse.ContentFinder<UnityEngine.Texture2D>), nameof(Verse.ContentFinder<UnityEngine.Texture2D>.Get), [typeof(string), typeof(bool)]);
            var prefixTex = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_TexStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (contentFinderGet != null) globalHarmony.Patch(contentFinderGet, prefix: prefixTex);

            // 隔離 Verse.Log 呼叫 UnityEngine.StackTraceUtility ECall
            var logMessage = HarmonyLib.AccessTools.Method(typeof(Verse.Log), nameof(Verse.Log.Message), [typeof(string)]);
            var logWarning = HarmonyLib.AccessTools.Method(typeof(Verse.Log), nameof(Verse.Log.Warning), [typeof(string)]);
            var logError = HarmonyLib.AccessTools.Method(typeof(Verse.Log), nameof(Verse.Log.Error), [typeof(string)]);
            var prefixLogMessage = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_LogMessageStub), BindingFlags.Static | BindingFlags.NonPublic));
            var prefixLogWarning = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_LogWarningStub), BindingFlags.Static | BindingFlags.NonPublic));
            var prefixLogError = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_LogErrorStub), BindingFlags.Static | BindingFlags.NonPublic));

            if (logMessage != null) globalHarmony.Patch(logMessage, prefix: prefixLogMessage);
            if (logWarning != null) globalHarmony.Patch(logWarning, prefix: prefixLogWarning);
            if (logError != null) globalHarmony.Patch(logError, prefix: prefixLogError);

            // 隔離 Verse.UnityData.IsInMainThread
            var isInMainThread = HarmonyLib.AccessTools.PropertyGetter(typeof(Verse.UnityData), nameof(Verse.UnityData.IsInMainThread));
            var prefixMainThread = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_TrueStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (isInMainThread != null) globalHarmony.Patch(isInMainThread, prefix: prefixMainThread);

            // 隔離 Verse.ModLister.RebuildModList
            var rebuildModList = HarmonyLib.AccessTools.Method(typeof(Verse.ModLister), "RebuildModList");
            var prefixFalse = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_FalseStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (rebuildModList != null) globalHarmony.Patch(rebuildModList, prefix: prefixFalse);

            // 隔離 Verse.GenFilePaths 路徑方法
            var getOrCreateModsFolder = HarmonyLib.AccessTools.Method(typeof(Verse.GenFilePaths), "GetOrCreateModsFolder");
            var prefixDirInfo = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_DirInfoStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (getOrCreateModsFolder != null) globalHarmony.Patch(getOrCreateModsFolder, prefix: prefixDirInfo);

            var configFolderPath = HarmonyLib.AccessTools.PropertyGetter(typeof(Verse.GenFilePaths), nameof(Verse.GenFilePaths.ConfigFolderPath));
            var prefixStringTemp = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_StringTempStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (configFolderPath != null) globalHarmony.Patch(configFolderPath, prefix: prefixStringTemp);

            var saveDataFolderPath = HarmonyLib.AccessTools.PropertyGetter(typeof(Verse.GenFilePaths), nameof(Verse.GenFilePaths.SaveDataFolderPath));
            if (saveDataFolderPath != null) globalHarmony.Patch(saveDataFolderPath, prefix: prefixStringTemp);

            var officialModsFolderPath = HarmonyLib.AccessTools.PropertyGetter(typeof(Verse.GenFilePaths), nameof(Verse.GenFilePaths.OfficialModsFolderPath));
            if (officialModsFolderPath != null) globalHarmony.Patch(officialModsFolderPath, prefix: prefixStringTemp);

            // 隔離 Verse.GenFilePaths.ModsConfigFilePath (ModsConfig..cctor 的循環依賴根源)
            var modsConfigFilePath = HarmonyLib.AccessTools.PropertyGetter(typeof(Verse.GenFilePaths), "ModsConfigFilePath");
            if (modsConfigFilePath != null) globalHarmony.Patch(modsConfigFilePath, prefix: prefixStringTemp);

            // 隔離 Verse.ShaderDatabase.LoadShader
            var loadShader = HarmonyLib.AccessTools.Method(typeof(Verse.ShaderDatabase), "LoadShader", [typeof(string)]);
            var prefixShader = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_ShaderStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (loadShader != null) globalHarmony.Patch(loadShader, prefix: prefixShader);

            // 隔離 Verse.ShaderUtility.SupportsMaskTex
            var supportsMaskTex = HarmonyLib.AccessTools.Method(typeof(Verse.ShaderUtility), "SupportsMaskTex");
            if (supportsMaskTex != null) globalHarmony.Patch(supportsMaskTex, prefix: prefixFalse);

            // 隔離 Verse.SolidColorMaterials.NewSolidColorTexture
            var newSolidColorTex = HarmonyLib.AccessTools.Method(typeof(Verse.SolidColorMaterials), "NewSolidColorTexture",
                [typeof(UnityEngine.Color)]);
            var prefixSolidColor = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_TexStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (newSolidColorTex != null) globalHarmony.Patch(newSolidColorTex, prefix: prefixSolidColor);

            // 隔離 Verse.MaterialAllocator.Create (internal class: 使用反射取得型別)
            var matAllocType = HarmonyLib.AccessTools.TypeByName("Verse.MaterialAllocator");
            var matAllocCreate = matAllocType != null
                ? HarmonyLib.AccessTools.Method(matAllocType, "Create", [typeof(UnityEngine.Shader)])
                : null;
            var prefixMat = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_MatStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (matAllocCreate != null) globalHarmony.Patch(matAllocCreate, prefix: prefixMat);

            // 隔離 UnityEngine.Resources.Load
            var resLoad = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Resources), "Load", [typeof(string), typeof(Type)]);
            var prefixResLoad = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_NullStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (resLoad != null) globalHarmony.Patch(resLoad, prefix: prefixResLoad);

            // 隔離 RimWorld.DefOfHelper.EnsureInitializedInCtor
            var ensureInit = HarmonyLib.AccessTools.Method(typeof(RimWorld.DefOfHelper), "EnsureInitializedInCtor");
            if (ensureInit != null) globalHarmony.Patch(ensureInit, prefix: prefixFalse);

            // 隔離 UnityEngine.StackTraceUtility.ExtractStackTrace
            var extractStackTrace = HarmonyLib.AccessTools.Method(typeof(UnityEngine.StackTraceUtility), "ExtractStackTrace");
            var prefixStringEmpty = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_StringEmptyStub), BindingFlags.Static | BindingFlags.NonPublic));
            if (extractStackTrace != null) globalHarmony.Patch(extractStackTrace, prefix: prefixStringEmpty);

            // 隔離 Verse.ModsConfig.Reset / Save / TrySortMods
            var modsConfigReset = HarmonyLib.AccessTools.Method(typeof(Verse.ModsConfig), "Reset");
            if (modsConfigReset != null) globalHarmony.Patch(modsConfigReset, prefix: prefixFalse);
            var modsConfigSave = HarmonyLib.AccessTools.Method(typeof(Verse.ModsConfig), "Save");
            if (modsConfigSave != null) globalHarmony.Patch(modsConfigSave, prefix: prefixFalse);
            var modsConfigSort = HarmonyLib.AccessTools.Method(typeof(Verse.ModsConfig), "TrySortMods");
            if (modsConfigSort != null) globalHarmony.Patch(modsConfigSort, prefix: prefixFalse);
        }
        catch { }
    }

    internal static Action<string> OnLogMessage;
    internal static Action<string> OnLogWarning;
    internal static Action<string> OnLogError;
    internal static Func<bool> IsInMainThreadOverride;

    private static bool Prefix_TexStub(ref UnityEngine.Texture2D __result)
    {
        __result = (UnityEngine.Texture2D)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.Texture2D));
        return false;
    }

    private static bool Prefix_OpEqualityStub(UnityEngine.Object x, UnityEngine.Object y, ref bool __result)
    {
        __result = ReferenceEquals(x, y);
        return false;
    }

    private static bool Prefix_OpInequalityStub(UnityEngine.Object x, UnityEngine.Object y, ref bool __result)
    {
        __result = !ReferenceEquals(x, y);
        return false;
    }

    private static bool Prefix_ShaderStub(ref UnityEngine.Shader __result)
    {
        __result = (UnityEngine.Shader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.Shader));
        return false;
    }

    private static bool Prefix_MatStub(ref UnityEngine.Material __result)
    {
        __result = (UnityEngine.Material)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.Material));
        return false;
    }

    private static bool Prefix_NullStub(ref object __result)
    {
        __result = null;
        return false;
    }

    private static bool Prefix_StringEmptyStub(ref string __result)
    {
        __result = string.Empty;
        return false;
    }

    private static bool Prefix_LogMessageStub(string text)
    {
        OnLogMessage?.Invoke(text);
        return false;
    }

    private static bool Prefix_LogWarningStub(string text)
    {
        OnLogWarning?.Invoke(text);
        return false;
    }

    private static bool Prefix_LogErrorStub(string text)
    {
        OnLogError?.Invoke(text);
        return false;
    }

    private static bool Prefix_FalseStub()
    {
        return false;
    }

    private static bool Prefix_TrueStub(ref bool __result)
    {
        if (IsInMainThreadOverride != null)
        {
            __result = IsInMainThreadOverride();
        }
        else
        {
            __result = true;
        }
        return false;
    }

    private static bool Prefix_StringTempStub(ref string __result)
    {
        __result = Path.GetTempPath();
        return false;
    }

    private static bool Prefix_DirInfoStub(ref DirectoryInfo __result)
    {
        __result = new DirectoryInfo(Path.GetTempPath());
        return false;
    }

    /// <summary>
    /// 取得可載入 RimWorld 1.7 執行期組件的本機目錄候選清單。
    /// 優先序：環境變數 RIMWORLD_MANAGED_DIR → 建置時期嵌入的遊戲安裝目錄 → 預設 Steam 安裝路徑。
    /// RimWorld 1.7 的 managed 組件（Assembly-CSharp.dll、UnityStubs.dll 等）位於安裝根目錄。
    /// 版本相符由 <see cref="IsCompatibleGameAssembly"/> 檢查：RimWorld 1.6 與 1.7 共用同一個
    /// Steam 安裝目錄，若誤載 1.6 的 Assembly-CSharp 會讓測試以另一個版本通過而掩蓋問題。
    /// </summary>
    private static IReadOnlyList<string> ResolveSearchDirectories()
    {
        var dirs = new List<string>();

        void AddDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            var trimmed = directory.TrimEnd('\\', '/');
            foreach (var existing in dirs)
            {
                if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)) return;
            }
            dirs.Add(trimmed);
        }

        AddDirectory(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"));

        AddDirectory(EmbeddedRimWorldDir());

        const string defaultInstallDir = @"c:\Program Files (x86)\Steam\steamapps\common\RimWorld";
        AddDirectory(defaultInstallDir);

        return dirs;
    }

    /// <summary>
    /// 檢查候選的遊戲組件是否與測試目標的 RimWorld 版本相符。
    /// 僅遊戲主組件 Assembly-CSharp.dll 的版本編碼 RimWorld 版本（1.6 為 1.6.x、1.7 為 1.7.x）；
    /// 其餘組件各有自己的版本規則（多數 Unity 模組為 0.0.0.0，Brutal.* 為日期版本），
    /// 無法據以判斷 RimWorld 版本，一律接受。
    /// </summary>
    private static bool IsCompatibleGameAssembly(string path)
    {
        if (!string.Equals(Path.GetFileName(path), "Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase)) return true;

        var expected = ExpectedRimWorldMinorVersion();
        if (expected <= 0) return true;
        try
        {
            var version = AssemblyName.GetAssemblyName(path).Version;
            if (version == null || version.Minor == 0) return true;
            return version.Minor == expected;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// 讀取建置時期由 csproj 以 AssemblyMetadata 嵌入的 RimWorld 次要版本。
    /// </summary>
    private static int ExpectedRimWorldMinorVersion()
    {
        foreach (var attribute in typeof(TestSetup).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, "RimWorldMinorVersion", StringComparison.Ordinal)
                && int.TryParse(attribute.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minor))
            {
                return minor;
            }
        }
        return 0;
    }

    /// <summary>
    /// 讀取建置時期由 csproj 以 AssemblyMetadata 嵌入的 RimWorld 安裝目錄，
    /// 使測試在未設定 RIMWORLD_MANAGED_DIR 環境變數時仍能載入遊戲 managed 組件。
    /// </summary>
    private static string EmbeddedRimWorldDir()
    {
        foreach (var attribute in typeof(TestSetup).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, "RimWorldDir", StringComparison.Ordinal))
            {
                return attribute.Value;
            }
        }
        return null;
    }

    private static void RegisterResolver()
    {
        if (registered) return;
        registered = true;

        var searchDirs = ResolveSearchDirectories();

#if NETCOREAPP || NET5_0_OR_GREATER
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            var name = assemblyName.Name;
            if (name == null || name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 優先從測試組件所在目錄載入相依組件（確保被覆蓋率工具插樁的組件正確載入）
            var testDir = Path.GetDirectoryName(typeof(TestSetup).Assembly.Location)
                ?? AppDomain.CurrentDomain.BaseDirectory;
            var localPath = Path.Combine(testDir, name + ".dll");
            if (File.Exists(localPath))
            {
                try
                {
                    return context.LoadFromAssemblyPath(localPath);
                }
                catch (Exception ex)
                {
                    // AssemblyLoadContext.Resolving 事件處理常式必須同步回傳，無法改用非同步輸出
#pragma warning disable MA0045
                    TestContext.Progress.WriteLine($"Assembly load failed for local '{localPath}': {ex}");
#pragma warning restore MA0045
                }
            }

            foreach (var dir in searchDirs)
            {
                var path = Path.Combine(dir, name + ".dll");
                if (!File.Exists(path)) continue;
                if (!IsCompatibleGameAssembly(path)) continue;
                try
                {
                    return context.LoadFromAssemblyPath(path);
                }
                catch (Exception ex)
                {
                    // AssemblyLoadContext.Resolving 事件處理常式必須同步回傳，無法改用非同步輸出
#pragma warning disable MA0045
                    TestContext.Progress.WriteLine($"Assembly load failed for '{path}': {ex}");
#pragma warning restore MA0045
                }
            }
            return null;
        };
#endif

        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var assemblyName = new AssemblyName(args.Name).Name;
            if (assemblyName == null) return null;
            if (assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                || string.Equals(assemblyName, "mscorlib", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 優先從測試組件所在目錄載入相依組件（確保被覆蓋率工具插樁的組件正確載入）
            var testDir = Path.GetDirectoryName(typeof(TestSetup).Assembly.Location)
                ?? AppDomain.CurrentDomain.BaseDirectory;
            var localPath = Path.Combine(testDir, assemblyName + ".dll");
            if (File.Exists(localPath))
            {
                try
                {
                    return Assembly.LoadFrom(localPath);
                }
                catch (Exception ex)
                {
                    // AppDomain.AssemblyResolve 事件處理常式必須同步回傳，無法改用非同步輸出
#pragma warning disable MA0045
                    TestContext.Progress.WriteLine(
                        $"Assembly load failed for local dependency '{localPath}': {ex}");
#pragma warning restore MA0045
                }
            }

            foreach (var dir in searchDirs)
            {
                var path = Path.Combine(dir, assemblyName + ".dll");
                if (!File.Exists(path)) continue;
                if (!IsCompatibleGameAssembly(path)) continue;
                try
                {
                    return Assembly.LoadFrom(path);
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"Assembly load failed for '{path}': {ex}");
                }
            }
            return null;
        };
    }

}
#pragma warning restore MA0148, MA0002, MA0003, MA0016, MA0051, MA0106, MA0134, MA0032, MA0039, S3776, S3267, S3398, S108, S2486, S2701
