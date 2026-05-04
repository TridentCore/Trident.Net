using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.Extensions;

public static class FilterExtensions
{
    extension(Filter)
    {
        public static Filter FromSetup(Profile.Rice setup)
        {
            var loader = LoaderHelper.TryParse(setup.Loader, out var result) ? result.Identity : null;
            return new(setup.Version, loader, null);
        }
    }
}
