using Trident.Abstractions.FileModels;
using Trident.Abstractions.Repositories.Resources;

namespace Trident.Abstractions.Utilities;

public static class RuleHelper
{
    public static IReadOnlyList<Result> Evaluate(IReadOnlyList<Input> input, IReadOnlyList<Profile.Rice.Rule> rules)
    {
        var results = new List<Result>();
        foreach (var one in input)
        {
            var passedRules = new List<Profile.Rice.Rule>();
            foreach (var rule in rules)
            {
                var passed = Evaluate(one, rule);
                if (passed)
                {
                    passedRules.Add(rule);
                }
            }

            results.Add(new(one, passedRules.Count > 0, passedRules, passedRules.LastOrDefault()));
        }

        return results;
    }

    public static bool Evaluate(Input input, Profile.Rice.Rule rule) =>
        rule.Selector switch
        {
            Profile.Rice.Rule.SelectorType.And => rule.Selectors?.All(x => Evaluate(input, x)) ?? false,
            Profile.Rice.Rule.SelectorType.Or => rule.Selectors?.Any(x => Evaluate(input, x)) ?? false,
            Profile.Rice.Rule.SelectorType.Not => rule.Selectors?.All(x => !Evaluate(input, x)) ?? false,
            Profile.Rice.Rule.SelectorType.Purl => rule.Purl != null
                                                && PackageHelper.IsMatched(rule.Purl, input.Package),
            Profile.Rice.Rule.SelectorType.Repository => rule.Repository != null
                                                      && string.Equals(rule.Repository,
                                                                       input.Package.Label,
                                                                       StringComparison.OrdinalIgnoreCase),
            Profile.Rice.Rule.SelectorType.Tag => rule.Tag != null && input.Entry.Tags.Contains(rule.Tag),
            Profile.Rice.Rule.SelectorType.Kind => rule.Kind != null && rule.Kind == input.Package.Kind,
            _ => false
        };

    #region Nested type: Input

    public record Input(Profile.Rice.Entry Entry, Package Package);

    #endregion

    #region Nested type: Result

    public record Result(
        Input Input,
        bool Matched,
        IReadOnlyList<Profile.Rice.Rule> AppliedRules,
        Profile.Rice.Rule? EffectiveRule);

    #endregion
}
