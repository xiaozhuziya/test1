using IndustrialHmi.Core.Models;
using IndustrialHmi.Core.Services;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace IndustrialHmi.OpcUa;

public sealed class OpcUaPlcClient(HmiSettings settings, string? certificateStorePath = null) : IPlcClient {
    private Session? session;
    public async Task ConnectAsync(CancellationToken ct) {
        ct.ThrowIfCancellationRequested(); settings.Validate();
        var pki = certificateStorePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IndustrialHmi", "pki");
        var configuration = new ApplicationConfiguration {
            ApplicationName = "IndustrialHmi", ApplicationUri = $"urn:{System.Net.Dns.GetHostName()}:IndustrialHmi",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory", StorePath = Path.Combine(pki, "own"), SubjectName = "CN=IndustrialHmi" },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pki, "trusted") },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pki, "issuers") },
                RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pki, "rejected") },
                AutoAcceptUntrustedCertificates = false, RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048, AddAppCertToTrustedStore = false
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = 3000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 10000 }
        };
        await configuration.Validate(ApplicationType.Client);
        var app = new ApplicationInstance { ApplicationName = "IndustrialHmi", ApplicationType = ApplicationType.Client, ApplicationConfiguration = configuration };
        if (!await app.CheckApplicationInstanceCertificate(false, 2048)) throw new InvalidOperationException("无法创建 OPC UA 客户端证书。");
        var endpoint = await Task.Run(() => CoreClientUtils.SelectEndpoint(configuration, settings.Endpoint, true, 5000), ct);
        if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt || endpoint.SecurityPolicyUri != SecurityPolicies.Basic256Sha256)
            throw new InvalidOperationException("PLC 必须提供 Basic256Sha256 / SignAndEncrypt 端点。本案例不降级为不安全连接。");
        var configured = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration));
        var user = Environment.GetEnvironmentVariable("PLC_OPCUA_USER");
        var password = Environment.GetEnvironmentVariable("PLC_OPCUA_PASSWORD");
        if (!string.IsNullOrEmpty(user) && string.IsNullOrEmpty(password)) throw new InvalidOperationException("已设置用户名，但缺少 PLC_OPCUA_PASSWORD。");
        IUserIdentity identity = string.IsNullOrEmpty(user) ? new UserIdentity(new AnonymousIdentityToken()) : new UserIdentity(user, password);
        session = await Session.Create(configuration, configured, false, true, "IndustrialHmi", 10000, identity, null);
        if (ct.IsCancellationRequested) { await DisconnectAsync(CancellationToken.None); ct.ThrowIfCancellationRequested(); }
    }
    public Task<IReadOnlyDictionary<Tag, object>> ReadAsync(IReadOnlyList<Tag> tags, CancellationToken ct) => Task.Run<IReadOnlyDictionary<Tag, object>>(() => {
        ct.ThrowIfCancellationRequested(); var s = RequireSession();
        var request = new ReadValueIdCollection(tags.Select(t => new ReadValueId { NodeId = NodeId.Parse(settings.Nodes[t.ToString()]), AttributeId = Attributes.Value }));
        s.Read(null, 0, TimestampsToReturn.Both, request, out var results, out var diagnostics);
        ClientBase.ValidateResponse(results, request); ClientBase.ValidateDiagnosticInfos(diagnostics, request);
        var values = new Dictionary<Tag, object>();
        for (var i = 0; i < tags.Count; i++) {
            if (!StatusCode.IsGood(results[i].StatusCode)) throw new ServiceResultException(results[i].StatusCode.Code, $"读取 {tags[i]} 失败（质量码不为 Good）。");
            if (results[i].Value is null || results[i].Value.GetType() != TagCatalog.ValueType(tags[i]))
                throw new InvalidDataException($"{tags[i]} 类型必须是 {TagCatalog.ValueType(tags[i]).Name}。请核对 PLC 类型。");
            values[tags[i]] = results[i].Value;
        }
        return values;
    }, ct);
    public Task WriteAsync(Tag tag, object value, CancellationToken ct) => Task.Run(() => {
        ct.ThrowIfCancellationRequested();
        if (value.GetType() != TagCatalog.ValueType(tag)) throw new InvalidDataException($"{tag} 写入类型不匹配。");
        var request = new WriteValueCollection { new WriteValue {
            NodeId = NodeId.Parse(settings.Nodes[tag.ToString()]), AttributeId = Attributes.Value, Value = new DataValue(new Variant(value))
        }};
        RequireSession().Write(null, request, out var results, out var diagnostics);
        ClientBase.ValidateResponse(results, request); ClientBase.ValidateDiagnosticInfos(diagnostics, request);
        if (!StatusCode.IsGood(results[0])) throw new ServiceResultException(results[0].Code, $"写入 {tag} 失败。");
    }, ct);
    private Session RequireSession() => session is { Connected: true } s ? s : throw new IOException("OPC UA 会话未连接。");
    public async Task DisconnectAsync(CancellationToken ct) {
        var old = session; session = null;
        if (old is not null) await Task.Run(() => { try { if (old.Connected) old.Close(1000); } finally { old.Dispose(); } }, CancellationToken.None);
    }
    public async ValueTask DisposeAsync() => await DisconnectAsync(CancellationToken.None);
}
