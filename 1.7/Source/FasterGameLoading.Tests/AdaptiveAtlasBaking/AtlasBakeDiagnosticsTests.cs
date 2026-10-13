using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using UnityEngine;
using Verse;

namespace FasterGameLoading.Tests.AdaptiveAtlasBaking
{
    [TestFixture]
    public class AtlasBakeDiagnosticsTests
    {
        private static readonly FieldInfo BuildQueueField = AccessTools.Field(typeof(GlobalTextureAtlasManager), "buildQueue");
        private static readonly FieldInfo BuildQueueMasksField = AccessTools.Field(typeof(GlobalTextureAtlasManager), "buildQueueMasks");
        private static readonly FieldInfo StaticTextureAtlasesField = AccessTools.Field(typeof(GlobalTextureAtlasManager), "staticTextureAtlases");

        private object previousBuildQueue;
        private object previousBuildQueueMasks;

        [SetUp]
        public void SetUp()
        {
            previousBuildQueue = BuildQueueField?.GetValue(null);
            previousBuildQueueMasks = BuildQueueMasksField?.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            BuildQueueField?.SetValue(null, previousBuildQueue);
            BuildQueueMasksField?.SetValue(null, previousBuildQueueMasks);
        }

        [Test]
        public void LogPotentialMaskIssues_ExposesPublicDiagnosticEntryPoint()
        {
            var method = typeof(AtlasBakeDiagnostics).GetMethod(
                nameof(AtlasBakeDiagnostics.LogPotentialMaskIssues));

            Assert.That(method, Is.Not.Null);
            Assert.That(method.IsStatic, Is.True);
        }

        private static Harmony harmony;

        public static class MockTextureHelper
        {
            public static readonly IDictionary<Texture2D, (int width, int height, string name, TextureFormat format, int mips)> TextureProps = new Dictionary<Texture2D, (int width, int height, string name, TextureFormat format, int mips)>();

            public static Texture2D CreateTexture(int width = 256, int height = 256, string name = "TestTex", TextureFormat format = TextureFormat.RGBA32, int mips = 1)
            {
                var tex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
                TextureProps[tex] = (width, height, name, format, mips);
                return tex;
            }

            public static int GetWidth(Texture tex)
            {
                if (tex is Texture2D t && TextureProps.TryGetValue(t, out var p)) return p.width;
                return 256;
            }

            public static int GetHeight(Texture tex)
            {
                if (tex is Texture2D t && TextureProps.TryGetValue(t, out var p)) return p.height;
                return 256;
            }

            public static string GetName(UnityEngine.Object obj)
            {
                if (obj is Texture2D t && TextureProps.TryGetValue(t, out var p)) return p.name;
                return "MockTex";
            }

            public static TextureFormat GetFormat(Texture2D tex)
            {
                if (tex != null && TextureProps.TryGetValue(tex, out var p)) return p.format;
                return TextureFormat.RGBA32;
            }

            public static int GetMipmapCount(Texture tex)
            {
                if (tex is Texture2D t && TextureProps.TryGetValue(t, out var p)) return p.mips;
                return 1;
            }

            public static string MockDescribeTexture(Texture2D tex)
            {
                if (tex == null)
                {
                    return "<null>";
                }
                string name = GetName(tex);
                int w = GetWidth(tex);
                int h = GetHeight(tex);
                TextureFormat fmt = GetFormat(tex);
                int mips = GetMipmapCount(tex);
                return $"{name} [{w.ToString(System.Globalization.CultureInfo.InvariantCulture)}x{h.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {fmt}, mips={mips.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
            }

            public static bool MockOpEquality(UnityEngine.Object x, UnityEngine.Object y)
            {
                return ReferenceEquals(x, y);
            }

            public static bool MockOpInequality(UnityEngine.Object x, UnityEngine.Object y)
            {
                return !ReferenceEquals(x, y);
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var mockGetWidth = AccessTools.Method(typeof(MockTextureHelper), nameof(GetWidth));
                var mockGetHeight = AccessTools.Method(typeof(MockTextureHelper), nameof(GetHeight));
                var mockGetName = AccessTools.Method(typeof(MockTextureHelper), nameof(GetName));
                var mockGetFormat = AccessTools.Method(typeof(MockTextureHelper), nameof(GetFormat));
                var mockGetMipmap = AccessTools.Method(typeof(MockTextureHelper), nameof(GetMipmapCount));
                var mockDescribe = AccessTools.Method(typeof(MockTextureHelper), nameof(MockDescribeTexture));
                var mockOpEquality = AccessTools.Method(typeof(MockTextureHelper), nameof(MockOpEquality));
                var mockOpInequality = AccessTools.Method(typeof(MockTextureHelper), nameof(MockOpInequality));

                foreach (var inst in instructions)
                {
                    if ((inst.opcode == OpCodes.Call || inst.opcode == OpCodes.Callvirt) && inst.operand is MethodInfo m)
                    {
                        if (string.Equals(m.Name, "get_width", StringComparison.Ordinal))
                        {
                            yield return new CodeInstruction(OpCodes.Call, mockGetWidth);
                            continue;
                        }
                        if (string.Equals(m.Name, "get_height", StringComparison.Ordinal))
                        {
                            yield return new CodeInstruction(OpCodes.Call, mockGetHeight);
                            continue;
                        }
                        if (string.Equals(m.Name, "get_name", StringComparison.Ordinal))
                        {
                            yield return new CodeInstruction(OpCodes.Call, mockGetName);
                            continue;
                        }
                        if (string.Equals(m.Name, "get_format", StringComparison.Ordinal))
                        {
                            yield return new CodeInstruction(OpCodes.Call, mockGetFormat);
                            continue;
                        }
                        if (string.Equals(m.Name, "get_mipmapCount", StringComparison.Ordinal))
                        {
                            yield return new CodeInstruction(OpCodes.Call, mockGetMipmap);
                            continue;
                        }
                        if (string.Equals(m.Name, "DescribeTexture", StringComparison.Ordinal))
                        {
                            yield return new CodeInstruction(OpCodes.Call, mockDescribe);
                            continue;
                        }
                        if (string.Equals(m.Name, "op_Equality", StringComparison.Ordinal))
                        {
                            yield return new CodeInstruction(OpCodes.Call, mockOpEquality);
                            continue;
                        }
                        if (string.Equals(m.Name, "op_Inequality", StringComparison.Ordinal))
                        {
                            yield return new CodeInstruction(OpCodes.Call, mockOpInequality);
                            continue;
                        }
                    }
                    yield return inst;
                }
            }

        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.AtlasDiagnostics");

            var emitMethod = AccessTools.Method(typeof(FGLLog), "Emit");
            var prefixSkip = AccessTools.Method(typeof(AtlasBakeDiagnosticsTests), nameof(PrefixSkip));
            if (emitMethod != null)
            {
                harmony.Patch(emitMethod, prefix: new HarmonyMethod(prefixSkip));
            }

            var logMethod = AccessTools.Method(typeof(AtlasBakeDiagnostics), nameof(AtlasBakeDiagnostics.LogPotentialMaskIssues));
            var transpilerMethod = AccessTools.Method(typeof(MockTextureHelper), nameof(MockTextureHelper.Transpiler));
            harmony.Patch(logMethod, transpiler: new HarmonyMethod(transpilerMethod));

            // 逐 group 的檢查已抽出成 InspectGroup，貼圖寬高的模擬與
            // GlobalTextureAtlasManager.buildQueueMasks 的存取都移到了該方法內，
            // 因此同樣需要轉譯（Krafs.Publicizer 只在編譯期開放該欄位，
            // 未經 Harmony 轉譯的方法在執行期會拋 FieldAccessException）。
            var inspectGroup = AccessTools.Method(typeof(AtlasBakeDiagnostics), "InspectGroup");
            if (inspectGroup != null)
            {
                harmony.Patch(inspectGroup, transpiler: new HarmonyMethod(transpilerMethod));
            }
        }

        private static bool PrefixSkip()
        {
            return false;
        }



        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony.UnpatchAll("FasterGameLoading.Tests.AtlasDiagnostics");
            MockTextureHelper.TextureProps.Clear();
        }

        [Test]
        public void LogPotentialMaskIssues_WhenBuildQueueNullOrEmpty_ReturnsSafely()
        {
            BuildQueueField.SetValue(null, value: null);
            Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues("test-null-queue"));

            var emptyQueue = new Dictionary<TextureAtlasGroupKey, (List<Texture2D>, HashSet<Texture2D>)>();
            BuildQueueField.SetValue(null, emptyQueue);
            Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues("test-empty-queue"));
        }

        [Test]
        public void LogPotentialMaskIssues_WhenKeyHasNoMask_SkipsMaskCheck()
        {
            var mainTex = MockTextureHelper.CreateTexture(256, 256, "MainNoMask");
            var queue = new Dictionary<TextureAtlasGroupKey, (List<Texture2D>, HashSet<Texture2D>)>();
            var keyNoMask = new TextureAtlasGroupKey { hasMask = false };
            queue[keyNoMask] = (new List<Texture2D> { mainTex }, new HashSet<Texture2D>());
            BuildQueueField.SetValue(null, queue);
            BuildQueueMasksField.SetValue(null, new Dictionary<Texture2D, Texture2D>());

            Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues("test-no-mask"));
        }

        [Test]
        public void LogPotentialMaskIssues_WhenGroupContainsNullTexture_SkipsSafely()
        {
            var queue = new Dictionary<TextureAtlasGroupKey, (List<Texture2D>, HashSet<Texture2D>)>();
            var key = new TextureAtlasGroupKey { hasMask = true };
            queue[key] = (new List<Texture2D> { null }, new HashSet<Texture2D>());
            BuildQueueField.SetValue(null, queue);
            BuildQueueMasksField.SetValue(null, new Dictionary<Texture2D, Texture2D>());

            Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues("test-null-item"));
        }

        [Test]
        public void LogPotentialMaskIssues_WhenMaskMissingOrNull_LogsWarningSafely()
        {
            var mainTex1 = MockTextureHelper.CreateTexture(256, 256, "MainNoMaskEntry");
            var mainTex2 = MockTextureHelper.CreateTexture(256, 256, "MainNullMaskEntry");

            var queue = new Dictionary<TextureAtlasGroupKey, (List<Texture2D>, HashSet<Texture2D>)>();
            var key = new TextureAtlasGroupKey { hasMask = true };
            queue[key] = (new List<Texture2D> { mainTex1, mainTex2 }, new HashSet<Texture2D>());
            BuildQueueField.SetValue(null, queue);

            var masks = new Dictionary<Texture2D, Texture2D>
            {
                [mainTex2] = null, // mask is null
                // mainTex1 not in dictionary
            };
            BuildQueueMasksField.SetValue(null, masks);

            Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues("test-missing-mask"));
        }

        [Test]
        public void LogPotentialMaskIssues_WhenMaskSizeMismatched_LogsSizeMismatch()
        {
            var mainTex = MockTextureHelper.CreateTexture(256, 256, "MainTex256");
            var maskTex = MockTextureHelper.CreateTexture(128, 128, "MaskTex128");

            var queue = new Dictionary<TextureAtlasGroupKey, (List<Texture2D>, HashSet<Texture2D>)>();
            var key = new TextureAtlasGroupKey { hasMask = true };
            queue[key] = (new List<Texture2D> { mainTex }, new HashSet<Texture2D>());
            BuildQueueField.SetValue(null, queue);

            var masks = new Dictionary<Texture2D, Texture2D>
            {
                [mainTex] = maskTex,
            };
            BuildQueueMasksField.SetValue(null, masks);

            Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues("test-mismatched-size"));
        }

        [Test]
        public void LogPotentialMaskIssues_WhenMaskSizeMatches_ScansSuccessfullyWithoutIssues()
        {
            var mainTex = MockTextureHelper.CreateTexture(256, 256, "MainTex256");
            var maskTex = MockTextureHelper.CreateTexture(256, 256, "MaskTex256");

            var queue = new Dictionary<TextureAtlasGroupKey, (List<Texture2D>, HashSet<Texture2D>)>();
            var key = new TextureAtlasGroupKey { hasMask = true };
            queue[key] = (new List<Texture2D> { mainTex }, new HashSet<Texture2D>());
            BuildQueueField.SetValue(null, queue);

            var masks = new Dictionary<Texture2D, Texture2D>
            {
                [mainTex] = maskTex,
            };
            BuildQueueMasksField.SetValue(null, masks);

            Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues("test-matched-size"));
        }

        [Test]
        public void LogPotentialMaskIssues_WhenUnexpectedExceptionOccurs_CatchesAndLogsWithoutThrowing()
        {
            var mainTex = MockTextureHelper.CreateTexture(256, 256, "MainTex");
            var queue = new Dictionary<TextureAtlasGroupKey, (List<Texture2D>, HashSet<Texture2D>)>();
            var key = new TextureAtlasGroupKey { hasMask = true };
            queue[key] = (new List<Texture2D> { mainTex }, new HashSet<Texture2D>());
            BuildQueueField.SetValue(null, queue);

            // Setting buildQueueMasks to null will cause NullReferenceException inside loop when evaluating TryGetValue
            BuildQueueMasksField.SetValue(null, value: null);

            Assert.DoesNotThrow(() => AtlasBakeDiagnostics.LogPotentialMaskIssues("test-exception-handling"));
        }









    }
}

