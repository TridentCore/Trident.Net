using Trident.Abstractions.FileModels;
using Trident.Abstractions.Repositories.Resources;

namespace Trident.Abstractions.Utilities;

public static class RuleHelper
{
    public static IReadOnlyList<Result> Evaluate(IReadOnlyList<Input> input, IReadOnlyList<Profile.Rice.Rule> rules) =>
        input
           .Select(x =>
            {
                var passedRules = rules.Where(y => Evaluate(x, y.Selector)).ToList();
                return new Result(x, passedRules, passedRules.LastOrDefault());
            })
           .ToList();


    public static Result Evaluate(Input input, IReadOnlyList<Profile.Rice.Rule> rules)
    {
        var passedRules = rules.Where(y => Evaluate(input, y.Selector)).ToList();
        return new(input, passedRules, passedRules.LastOrDefault());
    }

    public static bool Evaluate(Input input, Profile.Rice.Rule.RuleSelector selector) =>
        selector.Type switch
        {
            Profile.Rice.Rule.RuleSelector.SelectorType.And => selector.Children?.All(x => Evaluate(input, x)) ?? false,
            Profile.Rice.Rule.RuleSelector.SelectorType.Or => selector.Children?.Any(x => Evaluate(input, x)) ?? false,
            Profile.Rice.Rule.RuleSelector.SelectorType.Not =>
                selector.Children?.All(x => !Evaluate(input, x)) ?? false,
            Profile.Rice.Rule.RuleSelector.SelectorType.Purl => selector.Purl != null
                                                             && PackageHelper.IsMatched(selector.Purl, input.Package),
            Profile.Rice.Rule.RuleSelector.SelectorType.Repository => selector.Repository != null
                                                                   && string.Equals(selector.Repository,
                                                                          input.Package.Label,
                                                                          StringComparison.OrdinalIgnoreCase),
            Profile.Rice.Rule.RuleSelector.SelectorType.Tag => selector.Tag != null
                                                            && input.Entry.Tags.Contains(selector.Tag),
            Profile.Rice.Rule.RuleSelector.SelectorType.Kind => selector.Kind != null
                                                             && selector.Kind == input.Package.Kind,
            _ => false
        };

    #region Nested type: Input

    public record Input(Profile.Rice.Entry Entry, Package Package);

    #endregion

    #region Nested type: Result

    public record Result(Input Input, IReadOnlyList<Profile.Rice.Rule> AppliedRules, Profile.Rice.Rule? EffectiveRule)
    {
        public bool Matched => AppliedRules.Count > 0;
    }

    #endregion
}
