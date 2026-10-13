using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace FasterGameLoading.Tests.Compatibility
{
    [TestFixture]
    public class ModDependencyReflectionTests
    {
        [Test]
        public void DependsOnMod_ResolvesPrivateFieldNamesAndCaseInsensitivePackageId()
        {
            var metadata = new FieldMetadata(
                new FieldDependency("other.mod"),
                new FieldDependency("Example.Target"));

            Assert.That(ModDependencyReflection.DependsOnMod(metadata, "example.target"), Is.True);
        }

        [Test]
        public void DependsOnMod_ResolvesAlternatePropertyNames()
        {
            var metadata = new PropertyMetadata(new PropertyDependency("target.mod"));

            Assert.That(ModDependencyReflection.DependsOnMod(metadata, "TARGET.MOD"), Is.True);
        }

        [TestCase(null, "target.mod")]
        [TestCase(typeof(String), "target.mod")]
        public void DependsOnMod_WithMissingOrInvalidDependencyList_ReturnsFalse(Type ignored, string target)
        {
            object metadata = ignored == null ? null : "not a dependency list";

            Assert.That(ModDependencyReflection.DependsOnMod(metadata, target), Is.False);
        }

        [Test]
        public void DependsOnMod_WithNullEntriesOrUnknownPackageId_ReturnsFalse()
        {
            var metadata = new FieldMetadata(null, new UnknownDependency());

            Assert.That(ModDependencyReflection.DependsOnMod(metadata, "target.mod"), Is.False);
        }

        private sealed class FieldMetadata
        {
            private readonly IEnumerable modDependencies;

            public FieldMetadata(params object[] dependencies)
            {
                modDependencies = new ArrayList(dependencies);
            }
        }

        private sealed class FieldDependency
        {
            private readonly string packageId;

            public FieldDependency(string packageId)
            {
                this.packageId = packageId;
            }
        }

        private sealed class PropertyMetadata
        {
            public IEnumerable Dependencies { get; }

            public PropertyMetadata(params object[] dependencies)
            {
                Dependencies = new List<object>(dependencies);
            }
        }

        private sealed class PropertyDependency
        {
            public string PackageID { get; }

            public PropertyDependency(string packageId)
            {
                PackageID = packageId;
            }
        }

        private sealed class UnknownDependency
        {
            public static int Value => 1;
        }
    }
}
