using TridentCore.Abstractions.Repositories.Resources;

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
        public required string Version { get; set; }
        public string? Loader { get; set; }
        public IList<Entry> Packages { get; init; } = [];
        public IList<Rule> Rules { get; init; } = [];

        #region Nested type: Entry

        public class Entry
        {
            public required string Purl { get; set; }
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

                public enum SelectorType
                {
                    And,
                    Or,
                    Not,
                    Purl,
                    Repository,
                    Tag,
                    Kind,
                }

                #endregion

                public SelectorType Type { get; set; } = SelectorType.Purl;

                public IList<RuleSelector>? Children { get; set; }
                public string? Purl { get; set; }
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
