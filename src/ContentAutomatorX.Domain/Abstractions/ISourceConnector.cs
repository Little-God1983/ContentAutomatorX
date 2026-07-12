using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ISourceConnector
{
    string Type { get; }   // matches SourceTypes.*
    Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default);
}
