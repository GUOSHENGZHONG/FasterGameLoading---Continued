using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FasterGameLoading.Tests.TextureDownscaler
{
    [TestFixture]
    public class TextureResizeTests
    {
        private static Harmony harmony;
        public static Vector2? LastCapturedDrawSize { get; set; }

        public static class MockGraphicHelper
        {
            public static Graphic MockGet(string path, Shader shader, Vector2 drawSize, Color color)
            {
                LastCapturedDrawSize = drawSize;
                return null;
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var mockGet = AccessTools.Method(typeof(MockGraphicHelper), nameof(MockGet));
                foreach (var inst in instructions)
                {
                    if ((inst.opcode == OpCodes.Call || inst.opcode == OpCodes.Callvirt)
                        && inst.operand is MethodInfo m && string.Equals(m.Name, nameof(GraphicDatabase.Get), StringComparison.Ordinal))
                    {
                        yield return new CodeInstruction(OpCodes.Call, mockGet);
                        continue;
                    }
                    yield return inst;
                }
            }
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.TextureResizeTests");
            var tryGetMethod = AccessTools.Method(typeof(TextureResize), nameof(TextureResize.TryGetGraphicApparel));
            if (tryGetMethod != null)
            {
                var transpiler = AccessTools.Method(typeof(MockGraphicHelper), nameof(MockGraphicHelper.Transpiler));
                harmony.Patch(tryGetMethod, transpiler: new HarmonyMethod(transpiler));
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.TextureResizeTests");
        }

        private static T Uninitialized<T>()
        {
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
        }

        [Test]
        public void GetTextureType_RecognizesRepresentativeDefCategories()
        {
            var building = Uninitialized<ThingDef>();
            building.building = Uninitialized<BuildingProperties>();

            var weapon = Uninitialized<ThingDef>();
            weapon.category = ThingCategory.Item;
            weapon.tools = new List<Tool> { Uninitialized<Tool>() };

            var apparel = Uninitialized<ThingDef>();
            apparel.category = ThingCategory.Item;
            apparel.apparel = Uninitialized<ApparelProperties>();

            var plant = Uninitialized<ThingDef>();
            plant.category = ThingCategory.Plant;
            plant.plant = Uninitialized<PlantProperties>();
            plant.thingClass = typeof(Plant);

            var tree = Uninitialized<ThingDef>();
            tree.category = ThingCategory.Plant;
            tree.plant = Uninitialized<PlantProperties>();
            tree.plant.harvestTag = "Wood";
            tree.thingClass = typeof(Plant);

            var projectile = Uninitialized<ThingDef>();
            projectile.projectile = Uninitialized<ProjectileProperties>();

            var mote = Uninitialized<ThingDef>();
            mote.category = ThingCategory.Mote;

            var filth = Uninitialized<ThingDef>();
            filth.category = ThingCategory.Filth;

            var item = Uninitialized<ThingDef>();
            item.category = ThingCategory.Item;

            var pawn = Uninitialized<ThingDef>();
            pawn.race = Uninitialized<RaceProperties>();

            Assert.That(TextureResize.GetTextureType(building), Is.EqualTo(TextureResize.TextureType.Building));
            Assert.That(TextureResize.GetTextureType(weapon), Is.EqualTo(TextureResize.TextureType.Weapon));
            Assert.That(TextureResize.GetTextureType(apparel), Is.EqualTo(TextureResize.TextureType.Apparel));
            Assert.That(TextureResize.GetTextureType(plant), Is.EqualTo(TextureResize.TextureType.Plant));
            Assert.That(TextureResize.GetTextureType(tree), Is.EqualTo(TextureResize.TextureType.Tree));
            Assert.That(TextureResize.GetTextureType(projectile), Is.EqualTo(TextureResize.TextureType.Projectile));
            Assert.That(TextureResize.GetTextureType(mote), Is.EqualTo(TextureResize.TextureType.Mote));
            Assert.That(TextureResize.GetTextureType(filth), Is.EqualTo(TextureResize.TextureType.Filth));
            Assert.That(TextureResize.GetTextureType(item), Is.EqualTo(TextureResize.TextureType.Item));
            Assert.That(TextureResize.GetTextureType(pawn), Is.EqualTo(TextureResize.TextureType.Pawn));
        }

        [Test]
        public void GetTextureType_WithNoRecognizedPropertiesReturnsNone()
        {
            Assert.That(
                TextureResize.GetTextureType(Uninitialized<ThingDef>()),
                Is.EqualTo(TextureResize.TextureType.None));
        }

        [Test]
        public void RenderAsPack_WithNonUtilityLayer_ReturnsFalse()
        {
            var def = Uninitialized<ThingDef>();
            def.apparel = Uninitialized<ApparelProperties>();
            var nonUtilityLayer = Uninitialized<ApparelLayerDef>();
            def.apparel.layers = new List<ApparelLayerDef> { nonUtilityLayer };

            Assert.That(TextureResize.RenderAsPack(def), Is.False);
        }

        [Test]
        public void TryGetGraphicApparel_WhenGraphicDataIsNull_FallsBackToVector2One()
        {
            var def = Uninitialized<ThingDef>();
            def.apparel = Uninitialized<ApparelProperties>();
            def.apparel.layers = new List<ApparelLayerDef>();
            def.graphicData = null; // graphicData 為 null 的服裝 Def

            var bodyType = Uninitialized<BodyTypeDef>();
            bodyType.defName = "Male";

            LastCapturedDrawSize = null;
            Graphic rec = null;
            bool result = TextureResize.TryGetGraphicApparel(def, "Things/Pawn/Humanlike/Apparel/TestApparel", bodyType, out rec);

            Assert.That(result, Is.True);
            Assert.That(LastCapturedDrawSize, Is.EqualTo(Vector2.one));
        }

        [Test]
        public void FormatBytes_FormatsWithOneDecimalAndMiB()
        {
            var method = typeof(TextureResize).GetMethod("FormatBytes", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            string formatted = (string)method.Invoke(null, new object[] { 2 * 1024 * 1024L });
            Assert.That(formatted, Is.EqualTo("2.0 MiB"));
        }

        [Test]
        public void TryGetGraphicApparel_WhenGraphicDataNotNull_UsesCustomDrawSize()
        {
            var def = Uninitialized<ThingDef>();
            def.apparel = Uninitialized<ApparelProperties>();
            def.apparel.layers = new List<ApparelLayerDef>();
            def.graphicData = Uninitialized<GraphicData>();
            def.graphicData.drawSize = new Vector2(3f, 4f);

            var bodyType = Uninitialized<BodyTypeDef>();
            bodyType.defName = "Female";

            LastCapturedDrawSize = null;
            Graphic rec = null;
            bool result = TextureResize.TryGetGraphicApparel(def, "Things/Pawn/Humanlike/Apparel/Custom", bodyType, out rec);

            Assert.That(result, Is.True);
            Assert.That(LastCapturedDrawSize, Is.EqualTo(new Vector2(3f, 4f)));
        }

        [Test]
        public void TryGetGraphicApparel_WhenDevelopmentalStageFilterMismatches_ReturnsFalse()
        {
            var def = Uninitialized<ThingDef>();
            def.apparel = Uninitialized<ApparelProperties>();
            def.apparel.developmentalStageFilter = DevelopmentalStage.Adult;

            Graphic rec = null;
            bool result = TextureResize.TryGetGraphicApparel(def, "Things/Pawn/Humanlike/Apparel/BabyApparel", BodyTypeDefOf.Baby, out rec);

            Assert.That(result, Is.False);
            Assert.That(rec, Is.Null);
        }
    }
}
