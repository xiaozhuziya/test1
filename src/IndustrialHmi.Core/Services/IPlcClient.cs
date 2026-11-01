using IndustrialHmi.Core.Models;
namespace IndustrialHmi.Core.Services;
public interface IPlcClient : IAsyncDisposable {
    Task ConnectAsync(CancellationToken ct);
    Task<IReadOnlyDictionary<Tag, object>> ReadAsync(IReadOnlyList<Tag> tags, CancellationToken ct);
    Task WriteAsync(Tag tag, object value, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
}
