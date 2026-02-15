using Trident.Abstractions.Repositories.Resources;

namespace Trident.Abstractions.FileModels;

public class Profile(string name, Profile.Rice setup, IDictionary<string, object>? overrides)
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


    public string Name { get; set; } = name ?? throw new ArgumentNullException(nameof(name));
    public Rice Setup { get; private set; } = setup ?? throw new ArgumentNullException(nameof(setup));

    public IDictionary<string, object> Overrides { get; private set; } = overrides ?? new Dictionary<string, object>();

    #region Nested type: Rice

    public class Rice(
        string? source,
        string version,
        string? loader,
        IList<Rice.Entry>? packages,
        IList<Rice.Rule>? rules)
    {
        public string? Source { get; set; } = source;
        public string Version { get; set; } = version ?? throw new ArgumentNullException(nameof(version));
        public string? Loader { get; set; } = loader;
        public IList<Entry> Packages { get; private set; } = packages ?? new List<Entry>();
        public IList<Rule> Rules { get; private set; } = new List<Rule>();

        #region Nested type: Entry

        public class Entry(string purl, bool enabled, string? source, IList<string>? tags)
        {
            public string Purl { get; set; } = purl ?? throw new ArgumentNullException(nameof(purl));
            public bool Enabled { get; set; } = enabled;
            public string? Source { get; set; } = source;
            public IList<string> Tags { get; private set; } = tags ?? new List<string>();
        }

        #endregion

        #region Nested type: Rule

        public class Rule
        {
            #region SelectorType enum

            public enum SelectorType { And, Or, Not, Purl, Repository, Tag, Kind }

            #endregion

            public SelectorType Selector { get; set; } = SelectorType.Purl;
            public bool Enabled { get; set; } = true;

            #region Rule Override

            public string? Destination { get; set; }
            public bool Solidifying { get; set; }
            public bool Skipping { get; set; }

            #endregion

            #region For Selector

            public IList<Rule>? Children { get; set; }
            public string? Purl { get; set; }
            public string? Repository { get; set; }
            public string? Tag { get; set; }
            public ResourceKind? Kind { get; set; }

            #endregion
        }

        #endregion
    }

    #endregion
}
