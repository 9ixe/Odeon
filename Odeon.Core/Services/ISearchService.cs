using Odeon.Core.Contexts;
using Odeon.Core.Models;

namespace Odeon.Core.Services;

public interface ISearchService
{
    SearchResult SearchLocalLibrary(LibraryContext context, string query);
}
