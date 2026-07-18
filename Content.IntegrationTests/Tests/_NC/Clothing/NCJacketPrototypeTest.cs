using Content.Shared._NC.Clothing;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._NC.Clothing;

[TestFixture]
public sealed class NCJacketPrototypeTest
{
    private static readonly string[] ClosureJackets =
    [
        "ClothingOuterArmorJacketNCMaroon",
        "ClothingOuterArmorJacketNCRed",
        "ClothingOuterArmorJacketNCTurquoise",
        "ClothingOuterArmorJacketNCViolet",
    ];

    /// <summary>
    /// Loads the composed entity prototypes to catch marker loss through YAML inheritance.
    /// </summary>
    [Test]
    public async Task ClosureMarkerIsPresentOnColoredJackets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var factory = server.ResolveDependency<IComponentFactory>();
        var closureComponent = factory.GetComponentName<NCJacketClosureComponent>();

        foreach (var id in ClosureJackets)
        {
            var prototype = server.ProtoMan.Index<EntityPrototype>(id);
            Assert.That(
                prototype.Components.ContainsKey(closureComponent),
                Is.True,
                $"{id} must expose the zip/unzip interaction");
        }

        var black = server.ProtoMan.Index<EntityPrototype>("ClothingOuterArmorJacketNCBlack");
        Assert.That(
            black.Components.ContainsKey(closureComponent),
            Is.False,
            "The black jacket has no open sprite states and must not expose the closure interaction");

        await pair.CleanReturnAsync();
    }
}
