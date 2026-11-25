using System.Text.Json;
using Toobit.Platform.Api.Controllers;
using Xunit;

namespace Toobit.UnitTests;

/// <summary>.NET side of the signal-ingest contract: deserializes the fixture that the Python tests validate against the JSON Schema.</summary>
public class ContractTests
{
    [Fact]
    public void Shared_signal_fixture_deserializes_into_SignalIn()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", "signal.json"));
        var s = JsonSerializer.Deserialize<SignalIn>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("BTC-SWAP-USDT", s.Symbol); Assert.Equal("enter_long", s.Action);
        Assert.Equal(60000m, s.ReferencePrice); Assert.Equal(0.8m, s.Confidence);
        Assert.Equal("options_proxy_v1", s.Explain.GetProperty("methodology").GetString());
    }
}
