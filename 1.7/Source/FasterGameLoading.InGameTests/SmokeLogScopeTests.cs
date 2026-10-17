using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DevTools.Testing;
using Verse;

namespace FasterGameLoading.InGameTests
{
    /// <summary>重新檢查 DevTools 設定允許的模組掃描警告，避免掩蓋已啟用模組的問題。</summary>
    internal sealed class SmokeLogScopeTests
    {
        // 必須與 Mod/Configs/SmokeTestConfig.xml 的 warningsAllowed 保持一致。
        private static readonly Regex DependencyMetadataWarning = new(
            @"^Mod (?<mod>.+?) (?:has a dependency \([^)]+\) with empty display name\.|dependency \([^)]+\) needs to have <downloadUrl> and/or <steamWorkshopUrl> specified\.)$",
            RegexOptions.CultureInvariant);

        public SmokeLogScopeTests() { }

        [SmokeTest(TestType.MainMenu)]
        public void AllowedDependencyWarningsComeOnlyFromInactiveMods()
        {
            var activeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mod in ModsConfig.ActiveModsInLoadOrder) activeNames.Add(mod.Name);

            foreach (var entry in Log.Messages)
            {
                if (entry.type != LogMessageType.Warning) continue;

                var match = DependencyMetadataWarning.Match(entry.text);
                if (!match.Success) continue;

                var stack = entry.StackTrace;
                var sourceIsModScan = stack != null
                    && stack.Contains("Verse.ModMetaData+ModMetaDataInternal.ValidateDependencies", StringComparison.Ordinal)
                    && stack.Contains("Verse.ModLister.RebuildModList", StringComparison.Ordinal);
                if (!sourceIsModScan || activeNames.Contains(match.Groups["mod"].Value))
                {
                    Test.Fail($"Dependency warning requires investigation: {entry.text}");
                }
            }
        }
    }
}
