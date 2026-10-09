using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Privileged;
using PhantomVault.PrivilegedBroker;
using Xunit;

namespace PhantomVault.PrivilegedBroker.Tests;

public sealed class BrokerShutdownTests
{
    [Fact]
    public async Task Shutdown_AcknowledgesBeforeStoppingTheHost()
    {
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        bool stopped = false;
        var server = new BrokerPipeServer(_ => { }, null!, () =>
        {
            using var response = JsonDocument.Parse(stream.ToArray());
            Assert.Equal((int)BrokerMessageType.Result, response.RootElement.GetProperty("type").GetInt32());
            Assert.True(response.RootElement.GetProperty("boolResult").GetBoolean());
            stopped = true;
        });
        await server.DispatchAsync(new BrokerRequest { Operation = BrokerOperation.Shutdown },
            writer, new object(), CancellationToken.None);
        Assert.True(stopped);
    }
}
