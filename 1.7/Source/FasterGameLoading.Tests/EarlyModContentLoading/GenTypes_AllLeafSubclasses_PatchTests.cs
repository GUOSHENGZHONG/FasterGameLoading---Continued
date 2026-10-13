using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.EarlyModContentLoading
{
    [TestFixture]
    public class GenTypes_AllLeafSubclasses_PatchTests
    {
        [SetUp]
        public void SetUp()
        {
            GenTypes_AllLeafSubclasses_Patch.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            GenTypes_AllLeafSubclasses_Patch.ClearCache();
        }

        [Test]
        public void Prefix_WithCachedLeafTypesReturnsCopyAndSkipsOriginal()
        {
            GenTypes_AllLeafSubclasses_Patch.keyValuePairs[typeof(TestRoot)] =
                new HashSet<Type> { typeof(TestLeaf), typeof(TestSiblingLeaf) };
            IEnumerable<Type> result = null;

            var shouldRunOriginal = GenTypes_AllLeafSubclasses_Patch.Prefix(
                ref result, typeof(TestRoot));

            Assert.That(shouldRunOriginal, Is.False);
            Assert.That(result, Is.EquivalentTo(new[] { typeof(TestLeaf), typeof(TestSiblingLeaf) }));
        }

        [Test]
        public void Prefix_ReturnsCopySoCallersCannotMutateSharedCache()
        {
            GenTypes_AllLeafSubclasses_Patch.keyValuePairs[typeof(TestRoot)] =
                new HashSet<Type> { typeof(TestLeaf) };
            IEnumerable<Type> first = null;
            GenTypes_AllLeafSubclasses_Patch.Prefix(ref first, typeof(TestRoot));

            var mutableCopy = (List<Type>)first;
            mutableCopy.Clear();
            IEnumerable<Type> second = null;
            GenTypes_AllLeafSubclasses_Patch.Prefix(ref second, typeof(TestRoot));

            Assert.That(second, Is.EquivalentTo(new[] { typeof(TestLeaf) }));
        }

        [Test]
        public void ClearCache_RemovesAllBaseTypeEntries()
        {
            GenTypes_AllLeafSubclasses_Patch.keyValuePairs[typeof(TestRoot)] =
                new HashSet<Type> { typeof(TestLeaf) };

            GenTypes_AllLeafSubclasses_Patch.ClearCache();

            Assert.That(GenTypes_AllLeafSubclasses_Patch.keyValuePairs, Is.Empty);
        }

        [Test]
        public void GenTypesClearCache_AlsoRemovesAllLeafEntries()
        {
            const string harmonyId = "FasterGameLoading.Tests.GenTypesClearCache";
            var harmony = new Harmony(harmonyId);
            try
            {
                harmony.CreateClassProcessor(typeof(GenTypes_ClearCache_Patch)).Patch();
                GenTypes_AllLeafSubclasses_Patch.keyValuePairs[typeof(TestRoot)] =
                    new HashSet<Type> { typeof(TestLeaf) };

                GenTypes.ClearCache();

                Assert.That(GenTypes_AllLeafSubclasses_Patch.keyValuePairs, Is.Empty);
            }
            finally
            {
                harmony.UnpatchAll(harmonyId);
            }
        }

        private class TestRoot;

        private class TestIntermediate : TestRoot;

        private sealed class TestLeaf : TestIntermediate;

        private sealed class TestSiblingLeaf : TestRoot;
    }
}
