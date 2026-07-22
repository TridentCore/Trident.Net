using System.Text.Json.Serialization;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.FileModels;

public class Profile
{
    public const string OVERRIDE_JAVA_HOME = "java.home";
    public const string OVERRIDE_JAVA_MAX_MEMORY = "java.max_memory";
    public const string OVERRIDE_JAVA_ADDITIONAL_ARGUMENTS = "java.additional_arguments";
    public const string OVERRIDE_WINDOW_HEIGHT = "window.height";
    public const string OVERRIDE_WINDOW_WIDTH = "window.width";
    public const string OVERRIDE_WINDOW_TITLE = "window.title";
    public const string OVERRIDE_BEHAVIOR_DEPLOY_METHOD = "behavior.deploy.method";
    public const string OVERRIDE_BEHAVIOR_DEPLOY_FASTMODE = "behavior.deploy.fastmode";
    public const string OVERRIDE_BEHAVIOR_RESOLVE_DEPENDENCY = "behavior.resolve.dependency";
    public const string OVERRIDE_BEHAVIOR_CONNECT_SERVER = "behavior.connect.address";
    public const string OVERRIDE_BEHAVIOR_COMMAND_WRAPPER = "behavior.command.wrapper";
    public const string OVERRIDE_MODPACK_NAME = "modpack.name";
    public const string OVERRIDE_MODPACK_AUTHOR = "modpack.author";
    public const string OVERRIDE_MODPACK_VERSION = "modpack.version";

    public required string Name { get; set; }
    public required Rice Setup { get; set; }

    public IDictionary<string, object> Overrides { get; set; } = new Dictionary<string, object>();

    #region Nested type: Rice

    public class Rice
    {
        public string? Source { get; set; }

        // Source URIs ordered by overlay strength: earlier laid first, later overrides earlier.
        // The last entry is the topmost layer. Empty = rely on the tier defaults (manual top,
        // unlisted non-modpack middle, current modpack bottom).
        public IList<string> SourceOrders { get; init; } = [];

        public required string Version { get; set; }
        public string? Loader { get; set; }
        public IList<Entry> Packages { get; init; } = [];
        public IList<Rule> Rules { get; init; } = [];

        #region Nested type: Entry

        public class Entry
        {
            public string Pref { get; set; } = null!;

            [Obsolete("compat: legacy purl key, remove once on-disk profiles have migrated")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Purl
            {
                get => null;
                set => Pref = PackageHelper.SafeMigrate(value);
            }

            public required bool Enabled { get; set; }
            public string? Source { get; set; }
            public IList<string> Tags { get; init; } = [];
        }

        #endregion

        #region Nested type: Rule

        public class Rule
        {
            public required RuleSelector Selector { get; init; }
            public bool Enabled { get; set; } = true;

            #region Nested type: RuleSelector

            public class RuleSelector
            {
                #region SelectorType enum

                public enum SelectorType { And, Or, Not, Pref, Repository, Tag, Kind }

                #endregion

                public SelectorType Type { get; set; } = SelectorType.Pref;

                public IList<RuleSelector>? Children { get; set; }
                public string? Pref { get; set; }

                [Obsolete("compat: legacy purl key, remove once on-disk profiles have migrated")]
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                public string? Purl
                {
                    get => null;
                    set => Pref = PackageHelper.SafeMigrate(value);
                }

                public string? Repository { get; set; }
                public string? Tag { get; set; }
                public ResourceKind? Kind { get; set; }
            }

            #endregion

            #region Rule Override

            public string? Destination { get; set; }
            public bool Skipping { get; set; }
            public bool Normalizing { get; set; }

            #endregion
        }

        #endregion
    }

    #endregion
}
