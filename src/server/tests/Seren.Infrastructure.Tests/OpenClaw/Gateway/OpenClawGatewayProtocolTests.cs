using System.Text.Json;
using Seren.Infrastructure.OpenClaw.Gateway;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Gateway;

public sealed class OpenClawGatewayProtocolTests
{
    [Fact]
    public void GatewayRequest_Serialize_EmitsTypeDiscriminator()
    {
        var req = new GatewayRequest("abc", "connect", JsonSerializer.SerializeToElement(new { hi = 1 }));

        var json = JsonSerializer.Serialize(req, OpenClawGatewayJsonContext.Default.GatewayRequest);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("type").GetString().ShouldBe("req");
        doc.RootElement.GetProperty("id").GetString().ShouldBe("abc");
        doc.RootElement.GetProperty("method").GetString().ShouldBe("connect");
        doc.RootElement.GetProperty("params").GetProperty("hi").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void GatewayRequest_Serialize_NullParamsAreOmitted()
    {
        var req = new GatewayRequest("abc", "ping", null);

        var json = JsonSerializer.Serialize(req, OpenClawGatewayJsonContext.Default.GatewayRequest);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("params", out _).ShouldBeFalse();
    }

    [Fact]
    public void GatewayResponse_Deserialize_CamelCaseFields()
    {
        const string json = """
            {"type":"res","id":"abc","ok":true,"payload":{"x":42}}
            """;

        var response = JsonSerializer.Deserialize(
            json, OpenClawGatewayJsonContext.Default.GatewayResponse);

        response.ShouldNotBeNull();
        response!.Id.ShouldBe("abc");
        response.Ok.ShouldBeTrue();
        response.Payload!.Value.GetProperty("x").GetInt32().ShouldBe(42);
        response.Error.ShouldBeNull();
    }

    [Fact]
    public void GatewayResponse_Deserialize_ErrorFrame()
    {
        const string json = """
            {"type":"res","id":"abc","ok":false,"error":{"code":"X","message":"nope","retryable":true,"retryAfterMs":250}}
            """;

        var response = JsonSerializer.Deserialize(
            json, OpenClawGatewayJsonContext.Default.GatewayResponse);

        response!.Ok.ShouldBeFalse();
        response.Error.ShouldNotBeNull();
        response.Error!.Code.ShouldBe("X");
        response.Error.Message.ShouldBe("nope");
        response.Error.Retryable.ShouldBe(true);
        response.Error.RetryAfterMs.ShouldBe(250);
    }

    [Fact]
    public void GatewayResponse_Deserialize_TolerantOfUnknownFields()
    {
        const string json = """
            {"type":"res","id":"abc","ok":true,"payload":{"x":1},"newServerField":"ignore-me"}
            """;

        var response = JsonSerializer.Deserialize(
            json, OpenClawGatewayJsonContext.Default.GatewayResponse);

        response.ShouldNotBeNull();
        response!.Ok.ShouldBeTrue();
    }

    [Fact]
    public void GatewayEvent_Deserialize_WithOptionalSeqAndStateVersion()
    {
        const string json = """
            {"type":"event","event":"tick","payload":null,"seq":42,"stateVersion":{"presence":1,"health":2}}
            """;

        var ev = JsonSerializer.Deserialize(json, OpenClawGatewayJsonContext.Default.GatewayEvent);

        ev.ShouldNotBeNull();
        ev!.Event.ShouldBe("tick");
        ev.Seq.ShouldBe(42);
        ev.StateVersion!.Value.GetProperty("presence").GetInt32().ShouldBe(1);
        ev.StateVersion!.Value.GetProperty("health").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void ConnectParams_Serialize_OmitsNullAuthWhenTokenMissing()
    {
        var connect = new ConnectParams(
            MinProtocol: 3, MaxProtocol: 3,
            Client: new ConnectClient("gateway-client", "1.0.0", "linux", "backend", null, null),
            Role: "operator",
            Scopes: null,
            Device: null,
            Auth: null);

        var json = JsonSerializer.Serialize(connect, OpenClawGatewayJsonContext.Default.ConnectParams);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("auth", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("client", out _).ShouldBeTrue();
        doc.RootElement.GetProperty("client").TryGetProperty("displayName", out _).ShouldBeFalse();
    }

    [Fact]
    public void HelloOkPayload_Deserialize_AllRequiredFields()
    {
        const string json = """
            {
              "protocol": 3,
              "server": {"version":"1.2.3","connId":"conn-abc"},
              "features": {"methods":["chat.send","sessions.list"],"events":["tick","channel:message"]},
              "policy": {"maxPayload":524288,"maxBufferedBytes":1048576,"tickIntervalMs":5000}
            }
            """;

        var hello = JsonSerializer.Deserialize(json, OpenClawGatewayJsonContext.Default.HelloOkPayload);

        hello.ShouldNotBeNull();
        hello!.Protocol.ShouldBe(3);
        hello.Server.Version.ShouldBe("1.2.3");
        hello.Server.ConnId.ShouldBe("conn-abc");
        hello.Features.Methods.ShouldContain("chat.send");
        hello.Features.Events.ShouldContain("tick");
        hello.Policy.TickIntervalMs.ShouldBe(5000);
    }
}
