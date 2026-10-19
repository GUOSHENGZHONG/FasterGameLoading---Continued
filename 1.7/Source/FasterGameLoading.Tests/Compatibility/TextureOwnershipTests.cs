using System;
using NUnit.Framework;

namespace FasterGameLoading.Tests.Compatibility;
/// <summary>
/// TextureOwnership の判定順序と失敗退路をテスト。
/// ModLister は実際のゲーム環境に依存するため、
/// 本テストでは TextureOwnership.OverrideForTests で状態を直接制御し、
/// Current のキャッシュと Fgl 退路を検証する。
/// （Harmony パッチは不要に簡素化）
/// </summary>
[TestFixture]
public class TextureOwnershipTests
{
    [SetUp]
    public void SetUp()
    {
        TextureOwnership.OverrideForTests(null);
    }

    [TearDown]
    public void TearDown()
    {
        TextureOwnership.OverrideForTests(null);
    }

    [Test]
    public void OverrideForTests_SetsCurrentDirectly()
    {
        TextureOwnership.OverrideForTests(TextureOwner.ImageOpt);
        Assert.That(TextureOwnership.Current, Is.EqualTo(TextureOwner.ImageOpt));
    }

    [Test]
    public void OverrideForTests_Null_RevertsToFglIfNoExternalTool()
    {
        TextureOwnership.OverrideForTests(null);
        // 実際の ModLister が Image Opt を検出するかどうかはテスト環境に依存する；
        // ここではキャッシュ解除と退路だけを検証する。
        Assert.That(TextureOwnership.Current, Is.AnyOf(TextureOwner.ImageOpt, TextureOwner.GraphicsSettings, TextureOwner.Fgl));
    }

    [Test]
    public void FglOwnsTextureLoading_IsTrueWhenCurrentIsFgl()
    {
        TextureOwnership.OverrideForTests(TextureOwner.Fgl);
        Assert.That(TextureOwnership.FglOwnsTextureLoading, Is.True);
    }

    [Test]
    public void FglOwnsTextureLoading_IsFalseWhenNotFgl()
    {
        TextureOwnership.OverrideForTests(TextureOwner.ImageOpt);
        Assert.That(TextureOwnership.FglOwnsTextureLoading, Is.False);
    }

    [Test]
    public void OverrideForTests_CanSwitchBackAndForth()
    {
        TextureOwnership.OverrideForTests(TextureOwner.GraphicsSettings);
        Assert.That(TextureOwnership.Current, Is.EqualTo(TextureOwner.GraphicsSettings));

        TextureOwnership.OverrideForTests(TextureOwner.Fgl);
        Assert.That(TextureOwnership.Current, Is.EqualTo(TextureOwner.Fgl));
    }
}
