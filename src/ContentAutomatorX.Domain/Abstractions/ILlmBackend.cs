using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ILlmBackend
{
    string Name { get; }
    Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default);
}
