using System;
#pragma warning disable MA0148, MA0002, MA0003, MA0016, MA0051, MA0106, MA0134, MA0032, MA0039, S3776, S3267, S3398, S108, S2486, S2701
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace FasterGameLoading.Tests
{
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
            RegisterResolver();
            ApplyGlobalPatches();
        }

        static TestSetup()
        {
            RegisterResolver();
            ApplyGlobalPatches();
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
                var opEq = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Object), "op_Equality", new System.Type[] { typeof(UnityEngine.Object), typeof(UnityEngine.Object) });
                var opIneq = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality", new System.Type[] { typeof(UnityEngine.Object), typeof(UnityEngine.Object) });
                var prefixOpEq = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_OpEqualityStub), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
                var prefixOpIneq = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_OpInequalityStub), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
                if (opEq != null) opEqualityHarmony.Patch(opEq, prefix: prefixOpEq);
                if (opIneq != null) opEqualityHarmony.Patch(opIneq, prefix: prefixOpIneq);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[TestSetup] op_Equality patch failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            // 隔離會連鎖觸發 ECall 的靜態建構函式（ModsConfig/ShaderDatabase/BaseContent），
            // 防止其在型別首次存取時拋出原生例外。
            try
            {
                var staticCtorHarmony = new HarmonyLib.Harmony("FasterGameLoading.Tests.StaticCtorIsolation");
                var prefixFalse = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_FalseStub), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
                foreach (var cctorType in new System.Type[] {
                    typeof(Verse.ModsConfig),
                    typeof(Verse.ShaderDatabase),
                    typeof(Verse.BaseContent),
                })
                {
                    try
                    {
                        var cctor = cctorType.GetConstructor(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
                        if (cctor != null) staticCtorHarmony.Patch(cctor, prefix: prefixFalse);
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine("[TestSetup] cctor patch failed for " + cctorType.FullName + ": " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[TestSetup] cctor patch setup failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            // 以 mock 取代 Unity 的 ResourcesAPI 與 Verse.Prefs 資料，避免原生資源載入與偏好設定檔案存取
            try
            {
                var resApiProp = HarmonyLib.AccessTools.Property(typeof(UnityEngine.ResourcesAPI), "ActiveAPI");
                if (resApiProp != null)
                {
                    resApiProp.SetValue(null, new MockResourcesAPI());
                }
            }
            catch { }

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
                var contentFinderGet = HarmonyLib.AccessTools.Method(typeof(Verse.ContentFinder<UnityEngine.Texture2D>), nameof(Verse.ContentFinder<UnityEngine.Texture2D>.Get), new Type[] { typeof(string), typeof(bool) });
                var prefixTex = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_TexStub), BindingFlags.Static | BindingFlags.NonPublic));
                if (contentFinderGet != null) globalHarmony.Patch(contentFinderGet, prefix: prefixTex);

                // 隔離 Verse.Log 呼叫 UnityEngine.StackTraceUtility ECall
                var logMessage = HarmonyLib.AccessTools.Method(typeof(Verse.Log), nameof(Verse.Log.Message), new Type[] { typeof(string) });
                var logWarning = HarmonyLib.AccessTools.Method(typeof(Verse.Log), nameof(Verse.Log.Warning), new Type[] { typeof(string) });
                var logError = HarmonyLib.AccessTools.Method(typeof(Verse.Log), nameof(Verse.Log.Error), new Type[] { typeof(string) });
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
                var loadShader = HarmonyLib.AccessTools.Method(typeof(Verse.ShaderDatabase), "LoadShader", new Type[] { typeof(string) });
                var prefixShader = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_ShaderStub), BindingFlags.Static | BindingFlags.NonPublic));
                if (loadShader != null) globalHarmony.Patch(loadShader, prefix: prefixShader);

                // 隔離 Verse.ShaderUtility.SupportsMaskTex
                var supportsMaskTex = HarmonyLib.AccessTools.Method(typeof(Verse.ShaderUtility), "SupportsMaskTex");
                if (supportsMaskTex != null) globalHarmony.Patch(supportsMaskTex, prefix: prefixFalse);

                // 隔離 Verse.SolidColorMaterials.NewSolidColorTexture
                var newSolidColorTex = HarmonyLib.AccessTools.Method(typeof(Verse.SolidColorMaterials), "NewSolidColorTexture",
                    new Type[] { typeof(UnityEngine.Color) });
                var prefixSolidColor = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_TexStub), BindingFlags.Static | BindingFlags.NonPublic));
                if (newSolidColorTex != null) globalHarmony.Patch(newSolidColorTex, prefix: prefixSolidColor);

                // 隔離 Verse.MaterialAllocator.Create (internal class: 使用反射取得型別)
                var matAllocType = HarmonyLib.AccessTools.TypeByName("Verse.MaterialAllocator");
                var matAllocCreate = matAllocType != null
                    ? HarmonyLib.AccessTools.Method(matAllocType, "Create", new Type[] { typeof(UnityEngine.Shader) })
                    : null;
                var prefixMat = new HarmonyLib.HarmonyMethod(typeof(TestSetup).GetMethod(nameof(Prefix_MatStub), BindingFlags.Static | BindingFlags.NonPublic));
                if (matAllocCreate != null) globalHarmony.Patch(matAllocCreate, prefix: prefixMat);

                // 隔離 UnityEngine.Resources.Load
                var resLoad = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Resources), "Load", new Type[] { typeof(string), typeof(Type) });
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

        private static void RegisterResolver()
        {
            if (registered) return;
            registered = true;

#if NETCOREAPP || NET5_0_OR_GREATER
            System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
            {
                var name = assemblyName.Name;
                if (name == null || name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

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

                var managedDir = Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR")
                    ?? @"c:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed";
                var path = Path.Combine(managedDir, name + ".dll");

                if (File.Exists(path))
                {
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

                // 本地 RimWorld Managed 檔案夾路徑；可透過環境變數 RIMWORLD_MANAGED_DIR 覆寫，
                // 方便在不同機器或 CI 環境中執行測試而不需修改程式碼。

                var managedDir = Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR")
                    ?? @"c:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed";
                var path = Path.Combine(managedDir, assemblyName + ".dll");

                if (File.Exists(path))
                {
                    try
                    {
                        return Assembly.LoadFrom(path);
                    }
                    catch (Exception ex)
                    {
                        TestContext.Progress.WriteLine(
                            $"Assembly load failed for '{path}': {ex}");
                    }
                }
                return null;
            };
        }
    }
}
#pragma warning restore MA0148, MA0002, MA0003, MA0016, MA0051, MA0106, MA0134, MA0032, MA0039, S3776, S3267, S3398, S108, S2486, S2701
