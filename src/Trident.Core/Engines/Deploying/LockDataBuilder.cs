using IBuilder;
using Trident.Abstractions.FileModels;

namespace Trident.Core.Engines.Deploying;

public class LockDataBuilder : IBuilder<LockData>
{
    private readonly List<string> _gameArguments = [];
    private readonly List<string> _javaArguments = [];
    private readonly List<LockData.Library> _libraries = [];
    private readonly List<LockData.Parcel> _parcels = [];
    private LockData.AssetData? _assetIndex;
    private uint? _javaMajorVersion;
    private string? _mainClass;
    private LockData.ViabilityData? _viability;

    public IList<LockData.Parcel> Parcels => _parcels;
    public IList<LockData.Library> Libraries => _libraries;

    #region IBuilder<LockData> Members

    public LockData Build()
    {
        ArgumentNullException.ThrowIfNull(_assetIndex);
        ArgumentNullException.ThrowIfNull(_javaMajorVersion);
        ArgumentNullException.ThrowIfNull(_mainClass);
        ArgumentNullException.ThrowIfNull(_viability);
        ArgumentNullException.ThrowIfNull(_gameArguments);
        ArgumentNullException.ThrowIfNull(_javaArguments);

        return new(
            _viability,
            _mainClass,
            _javaMajorVersion.Value,
            _gameArguments,
            _javaArguments,
            _libraries,
            _parcels,
            _assetIndex
        );
    }

    #endregion

    public LockDataBuilder SetViability(LockData.ViabilityData viability)
    {
        _viability = viability;
        return this;
    }

    public LockDataBuilder ClearGameArguments()
    {
        _gameArguments.Clear();
        return this;
    }

    public LockDataBuilder AddGameArgument(string arg)
    {
        arg = arg.Trim();
        if (!_gameArguments.Contains(arg))
        {
            _gameArguments.Add(arg);
        }

        return this;
    }

    public LockDataBuilder AddJvmArgument(string arg)
    {
        arg = arg.Trim();
        if (!_javaArguments.Contains(arg))
        {
            _javaArguments.Add(arg);
        }

        return this;
    }

    public LockDataBuilder AddParcel(LockData.Parcel parcel)
    {
        _parcels.Add(parcel);
        return this;
    }

    public LockDataBuilder AddLibrary(LockData.Library library)
    {
        // 规则：
        //  允许除 IsNative 不同的同时存在，但不允许除了 IsPresent 不同的同时存在， IsPresent==True的优先
        var found = _libraries.FirstOrDefault(x =>
            x.Id.Namespace == library.Id.Namespace
            && x.Id.Name == library.Id.Name
            && x.Id.Platform == library.Id.Platform
            && x.Id.Extension == library.Id.Extension
            && x.IsNative == library.IsNative
        );
        if (found != null)
        {
            // Present 只能有一个

            if (found.Id.Version == library.Id.Version)
            {
                if (library.IsPresent)
                // 保留新的
                {
                    _libraries.Remove(found);
                }
                else
                // 保留旧的
                {
                    return this;
                }
            }
            else if (found.IsPresent && library.IsPresent)
            {
                _libraries.Remove(found);
            }
        }

        _libraries.Add(library);

        return this;
    }

    public LockDataBuilder SetAssetIndex(LockData.AssetData index)
    {
        _assetIndex = index;
        return this;
    }

    public LockDataBuilder SetJavaMajorVersion(uint version)
    {
        _javaMajorVersion = version;
        return this;
    }

    public LockDataBuilder SetMainClass(string mainClass)
    {
        _mainClass = mainClass;
        return this;
    }
}
