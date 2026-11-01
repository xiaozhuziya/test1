using System.Net;
using System.Net.Sockets;
using IndustrialHmi.Core.Models;
using IndustrialHmi.OpcUa;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Xunit;
namespace IndustrialHmi.Tests;

// Real binary OPC UA over loopback, with signed/encrypted sessions and mutual trust.
public class OpcUaIntegrationTests {
    [Fact] public async Task SecureSessionReadsWritesRejectsBadNodesAndUntrustedCertificate() {
        var root=Path.Combine(Path.GetTempPath(),"industrial-hmi-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var address=$"opc.tcp://localhost:{port}/IndustrialHmi";
        var config=new ApplicationConfiguration {
            ApplicationName="IndustrialHmiTestServer",ApplicationUri=$"urn:{Dns.GetHostName()}:IndustrialHmiTestServer",ApplicationType=ApplicationType.Server,
            SecurityConfiguration=new SecurityConfiguration {
                ApplicationCertificate=new CertificateIdentifier {StoreType="Directory",StorePath=Path.Combine(root,"server","own"),SubjectName="CN=IndustrialHmiTestServer"},
                TrustedPeerCertificates=new CertificateTrustList {StoreType="Directory",StorePath=Path.Combine(root,"server","trusted")},
                TrustedIssuerCertificates=new CertificateTrustList {StoreType="Directory",StorePath=Path.Combine(root,"server","issuers")},
                RejectedCertificateStore=new CertificateTrustList {StoreType="Directory",StorePath=Path.Combine(root,"server","rejected")},
                AutoAcceptUntrustedCertificates=false,MinimumCertificateKeySize=2048
            },
            TransportQuotas=new TransportQuotas {OperationTimeout=3000},
            ServerConfiguration=new ServerConfiguration {
                BaseAddresses=new StringCollection {address},
                SecurityPolicies=new ServerSecurityPolicyCollection {new ServerSecurityPolicy {SecurityMode=MessageSecurityMode.SignAndEncrypt,SecurityPolicyUri=SecurityPolicies.Basic256Sha256}},
                UserTokenPolicies=new UserTokenPolicyCollection {new UserTokenPolicy(UserTokenType.Anonymous)}
            }
        };
        await config.Validate(ApplicationType.Server);
        var app=new ApplicationInstance {ApplicationName=config.ApplicationName,ApplicationType=ApplicationType.Server,ApplicationConfiguration=config};
        Assert.True(await app.CheckApplicationInstanceCertificate(false,2048));
        var server=new TestServer();
        try {
            await app.Start(server);
            var settings=new HmiSettings {Simulation=false,Endpoint=address,Nodes=Enum.GetValues<Tag>().ToDictionary(t=>t.ToString(),t=>$"ns=2;s={t}")};
            // An untrusted server must be rejected; no auto-accept callback exists.
            var clientStore=Path.Combine(root,"client");
            await using(var rejected=new OpcUaPlcClient(settings,clientStore)) {
                var error=await Assert.ThrowsAsync<ServiceResultException>(()=>rejected.ConnectAsync(CancellationToken.None));
                Assert.Equal(StatusCodes.BadCertificateUntrusted,error.StatusCode);
            }
            var serverCert=await config.SecurityConfiguration.ApplicationCertificate.Find(true);
            using(var trust=new CertificateIdentifier {StoreType="Directory",StorePath=Path.Combine(clientStore,"trusted")}.OpenStore()) { await trust.Add(serverCert); }
            var clientCert=Directory.GetFiles(Path.Combine(clientStore,"own","certs"),"*.der").Single();
            using(var trust=new CertificateIdentifier {StoreType="Directory",StorePath=Path.Combine(root,"server","trusted")}.OpenStore()) {
                await trust.Add(new System.Security.Cryptography.X509Certificates.X509Certificate2(File.ReadAllBytes(clientCert)));
            }
            await config.CertificateValidator.Update(config.SecurityConfiguration);
            await using var client=new OpcUaPlcClient(settings,clientStore);
            await client.ConnectAsync(CancellationToken.None);
            var values=await client.ReadAsync(TagCatalog.Inputs,CancellationToken.None);
            Assert.Equal(TagCatalog.Inputs.Length,values.Count);Assert.Equal(true,values[Tag.EStopOk]);Assert.IsType<double>(values[Tag.ServoPosition]);Assert.IsType<int>(values[Tag.MachineState]);
            await client.WriteAsync(Tag.JogPositive,true,CancellationToken.None);
            var jog=await client.ReadAsync([Tag.JogPositive],CancellationToken.None);Assert.Equal(true,jog[Tag.JogPositive]);
            await client.WriteAsync(Tag.JogPositive,false,CancellationToken.None);
            await client.WriteAsync(Tag.JogSpeed,12.5,CancellationToken.None);
            var written=await client.ReadAsync([Tag.JogSpeed],CancellationToken.None);Assert.Equal(12.5,written[Tag.JogSpeed]);
            settings.Nodes[nameof(Tag.EStopOk)]="ns=2;s=Missing";
            await Assert.ThrowsAsync<ServiceResultException>(()=>client.ReadAsync([Tag.EStopOk],CancellationToken.None));
            settings.Nodes[nameof(Tag.JogPositive)]="ns=2;s=Missing";
            await Assert.ThrowsAsync<ServiceResultException>(()=>client.WriteAsync(Tag.JogPositive,true,CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(()=>client.WriteAsync(Tag.JogSpeed,10,CancellationToken.None));
            settings.Nodes[nameof(Tag.EStopOk)]=settings.Nodes[nameof(Tag.JogSpeed)];
            await Assert.ThrowsAsync<InvalidDataException>(()=>client.ReadAsync([Tag.EStopOk],CancellationToken.None));
            await client.DisconnectAsync(CancellationToken.None);
        } finally { server.Stop(); server.Dispose(); try {Directory.Delete(root,true);} catch(IOException) {} }
    }
    private sealed class TestServer : StandardServer {
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server,ApplicationConfiguration configuration) => new(server,configuration,null,new TestNodeManager(server,configuration));
        protected override ServerProperties LoadServerProperties() => new() {ManufacturerName="HMI tests",ProductName="Loopback PLC",ProductUri="urn:industrial-hmi:test",SoftwareVersion="1.0",BuildNumber="1",BuildDate=DateTime.UtcNow};
    }
    private sealed class TestNodeManager : CustomNodeManager2 {
        public TestNodeManager(IServerInternal server,ApplicationConfiguration configuration) : base(server,configuration,"urn:industrial-hmi:test:nodes") {SystemContext.NodeIdFactory=this;}
        public override void CreateAddressSpace(IDictionary<NodeId,IList<IReference>> externalReferences) {
            lock(Lock) {
                var folder=new FolderState(null) {NodeId=new NodeId("Hmi",NamespaceIndex),BrowseName=new QualifiedName("Hmi",NamespaceIndex),DisplayName="Hmi",TypeDefinitionId=ObjectTypeIds.FolderType,ReferenceTypeId=ReferenceTypeIds.Organizes};
                folder.AddReference(ReferenceTypeIds.Organizes,true,ObjectIds.ObjectsFolder);
                if(!externalReferences.TryGetValue(ObjectIds.ObjectsFolder,out var references)) externalReferences[ObjectIds.ObjectsFolder]=references=new List<IReference>();
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes,false,folder.NodeId));
                foreach(var t in Enum.GetValues<Tag>()) {
                    var type=TagCatalog.ValueType(t);
                    var node=new BaseDataVariableState(folder) {
                        NodeId=new NodeId(t.ToString(),NamespaceIndex),BrowseName=new QualifiedName(t.ToString(),NamespaceIndex),DisplayName=t.ToString(),
                        TypeDefinitionId=VariableTypeIds.BaseDataVariableType,ReferenceTypeId=ReferenceTypeIds.HasComponent,
                        DataType=type==typeof(bool)?DataTypeIds.Boolean:type==typeof(int)?DataTypeIds.Int32:DataTypeIds.Double,
                        ValueRank=ValueRanks.Scalar,AccessLevel=AccessLevels.CurrentReadOrWrite,UserAccessLevel=AccessLevels.CurrentReadOrWrite,
                        Value=type==typeof(bool)?(object)true:type==typeof(int)?(object)10:0d,StatusCode=StatusCodes.Good,Timestamp=DateTime.UtcNow
                    };
                    folder.AddChild(node);
                }
                AddPredefinedNode(SystemContext,folder);
            }
        }
    }
}
