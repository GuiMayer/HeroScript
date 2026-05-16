using Core.Combat.Models;
using Core.Effects;
using Xunit;

namespace Core.Tests.Effects;

public class EffectContextTests
{
    [Fact]
    public void CombatEffectContext_FromEffect_PreservesEffectIdentityAndState()
    {
        var state = new CombatState();
        var effect = new EffectInstance
        {
            SourceEntityId = "hero",
            TargetEntityId = "enemy",
            SourceActionId = "fireball",
            SourceCardId = "card-1",
            Definition = new EffectDefinition { Type = EffectType.DAMAGE }
        };

        var context = CombatEffectContext.FromEffect(effect, state);

        Assert.Equal(EffectScope.COMBAT, context.Scope);
        Assert.Equal("hero", context.SourceEntityId);
        Assert.Equal("enemy", context.TargetEntityId);
        Assert.Equal("fireball", context.SourceActionId);
        Assert.Equal("card-1", context.SourceCardId);
        Assert.Same(state, context.CombatState);
    }

    [Fact]
    public void RunEffectContext_DoesNotRequireCombatState()
    {
        var context = new RunEffectContext
        {
            RunId = "run-1",
            SourceEntityId = "reward-node",
            TargetEntityId = "player"
        };

        Assert.Equal(EffectScope.RUN, context.Scope);
        Assert.Null(context.CombatState);
    }
}
