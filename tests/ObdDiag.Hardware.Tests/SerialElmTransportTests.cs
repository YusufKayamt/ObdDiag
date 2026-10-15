using FluentAssertions;
using ObdDiag.Hardware;

namespace ObdDiag.Hardware.Tests;

public class SerialElmTransportTests
{
    /// <summary>Her komuta "&lt;komut&gt;OK\r\r&gt;" cevabı veren basit bağlantı.</summary>
    private static FakeSerialConnection Echoing() => new()
    {
        Responder = cmd => Task.FromResult<string[]?>(new[] { $"{cmd}OK\r\r>" })
    };

    private static SerialElmTransport Create(params Func<FakeSerialConnection>[] connections)
    {
        var queue = new Queue<Func<FakeSerialConnection>>(connections);
        var transport = new SerialElmTransport(() =>
        {
            // Sıra bittiyse son bağlantı türü tekrar kullanılır.
            var next = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return next();
        })
        {
            ReconnectDelay = TimeSpan.FromMilliseconds(5),
            ResponseTimeout = TimeSpan.FromSeconds(2)
        };
        return transport;
    }

    [Fact]
    public async Task Sends_Command_With_CarriageReturn_And_Returns_Response_With_Prompt()
    {
        var conn = Echoing();
        var transport = Create(() => conn);
        await transport.ConnectAsync();

        var answer = await transport.SendCommandAsync("010C");

        answer.Should().Be("010COK\r\r>");
        conn.WrittenCommands.Should().Equal("010C");
        transport.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Response_Split_Into_Pieces_Is_Joined_Until_Prompt()
    {
        var conn = new FakeSerialConnection
        {
            Responder = _ => Task.FromResult<string[]?>(new[] { "41 0", "C 1A", " F8\r", "\r", ">" })
        };
        var transport = Create(() => conn);
        await transport.ConnectAsync();

        (await transport.SendCommandAsync("010C")).Should().Be("41 0C 1A F8\r\r>");
    }

    [Fact]
    public async Task Nul_Characters_Are_Removed()
    {
        var conn = new FakeSerialConnection
        {
            Responder = _ => Task.FromResult<string[]?>(new[] { "OK\0\r\r>" })
        };
        var transport = Create(() => conn);
        await transport.ConnectAsync();

        (await transport.SendCommandAsync("ATE0")).Should().Be("OK\r\r>");
    }

    [Fact]
    public async Task Stale_Data_Before_Command_Is_Discarded()
    {
        var conn = Echoing();
        conn.Enqueue("eski veri>");
        var transport = Create(() => conn);
        await transport.ConnectAsync();

        (await transport.SendCommandAsync("ATI")).Should().Be("ATIOK\r\r>");
    }

    [Fact]
    public async Task SendCommand_Without_Connect_Throws()
    {
        var transport = Create(Echoing);
        var act = () => transport.SendCommandAsync("ATZ");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Empty_Command_Is_Rejected()
    {
        var transport = Create(Echoing);
        await transport.ConnectAsync();
        var act = () => transport.SendCommandAsync("  ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task No_Answer_Causes_Timeout()
    {
        var conn = new FakeSerialConnection(); // hiç cevap vermez
        var transport = Create(() => conn);
        transport.ResponseTimeout = TimeSpan.FromMilliseconds(100);
        await transport.ConnectAsync();

        var act = () => transport.SendCommandAsync("010C");
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Cancellation_Throws_OperationCanceled_Not_Timeout()
    {
        var conn = new FakeSerialConnection();
        var transport = Create(() => conn);
        await transport.ConnectAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => transport.SendCommandAsync("010C", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Transport_Works_Again_After_Timeout()
    {
        var first = true;
        var conn = new FakeSerialConnection
        {
            Responder = cmd => Task.FromResult<string[]?>(first ? null : new[] { "OK\r\r>" })
        };
        var transport = Create(() => conn);
        transport.ResponseTimeout = TimeSpan.FromMilliseconds(100);
        await transport.ConnectAsync();

        await ((Func<Task>)(() => transport.SendCommandAsync("ATI"))).Should().ThrowAsync<TimeoutException>();

        first = false;
        (await transport.SendCommandAsync("ATI")).Should().Be("OK\r\r>");
    }

    // ---- Bağlantı kopması ----

    [Fact]
    public async Task Lost_Connection_Is_Reopened_And_Command_Retried()
    {
        var broken = new FakeSerialConnection { WriteFailure = new IOException("kablo çekildi") };
        var healthy = Echoing();
        var created = 0;
        var transport = Create(() => { created++; return created == 1 ? broken : healthy; });
        await transport.ConnectAsync();

        var answer = await transport.SendCommandAsync("0100");

        answer.Should().Be("0100OK\r\r>");
        created.Should().Be(2);
        broken.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Connection_Closed_During_Read_Is_Reopened()
    {
        var closed = new FakeSerialConnection { ClosedOnRead = true };
        var healthy = Echoing();
        var created = 0;
        var transport = Create(() => { created++; return created == 1 ? closed : healthy; });
        await transport.ConnectAsync();

        (await transport.SendCommandAsync("0100")).Should().Be("0100OK\r\r>");
        created.Should().Be(2);
    }

    [Fact]
    public async Task Gives_Up_After_Max_Reconnect_Attempts()
    {
        var created = 0;
        var transport = Create(() =>
        {
            created++;
            return new FakeSerialConnection { WriteFailure = new IOException("kablo çekildi") };
        });
        transport.MaxReconnectAttempts = 2;
        await transport.ConnectAsync();

        var act = () => transport.SendCommandAsync("0100");

        await act.Should().ThrowAsync<IOException>();
        created.Should().Be(3); // ilk deneme + 2 yeniden deneme
    }

    [Fact]
    public async Task Failure_To_Reopen_Port_Counts_As_Attempt_And_Recovers()
    {
        var created = 0;
        var healthy = Echoing();
        var transport = Create(() =>
        {
            created++;
            if (created == 1) return new FakeSerialConnection { WriteFailure = new IOException("koptu") };
            if (created == 2) throw new UnauthorizedAccessException("port meşgul");
            return healthy;
        });
        await transport.ConnectAsync();

        (await transport.SendCommandAsync("0100")).Should().Be("0100OK\r\r>");
        created.Should().Be(3);
    }

    [Fact]
    public async Task Timeout_Does_Not_Trigger_Reconnect()
    {
        var created = 0;
        var transport = Create(() => { created++; return new FakeSerialConnection(); });
        transport.ResponseTimeout = TimeSpan.FromMilliseconds(80);
        await transport.ConnectAsync();

        await ((Func<Task>)(() => transport.SendCommandAsync("0100"))).Should().ThrowAsync<TimeoutException>();
        created.Should().Be(1);
    }

    // ---- Kuyruk ----

    [Fact]
    public async Task Concurrent_Commands_Get_Their_Own_Responses()
    {
        var conn = new FakeSerialConnection
        {
            Responder = async cmd =>
            {
                await Task.Delay(30); // cihaz biraz düşünür
                return new[] { $"cevap-{cmd}\r\r>" };
            }
        };
        var transport = Create(() => conn);
        await transport.ConnectAsync();

        var tasks = new[] { "A1", "B2", "C3", "D4" }.Select(c => transport.SendCommandAsync(c)).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().Equal("cevap-A1\r\r>", "cevap-B2\r\r>", "cevap-C3\r\r>", "cevap-D4\r\r>");
        conn.WrittenCommands.Should().Equal("A1", "B2", "C3", "D4"); // gönderim sırası korunur
    }

    // ---- Kapatma ----

    [Fact]
    public async Task Disconnect_Closes_Connection_And_Blocks_Commands()
    {
        var conn = Echoing();
        var transport = Create(() => conn);
        await transport.ConnectAsync();

        await transport.DisconnectAsync();

        transport.IsConnected.Should().BeFalse();
        conn.Disposed.Should().BeTrue();
        var act = () => transport.SendCommandAsync("ATZ");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Can_Reconnect_After_Disconnect()
    {
        var transport = Create(Echoing);
        await transport.ConnectAsync();
        await transport.DisconnectAsync();

        await transport.ConnectAsync();

        (await transport.SendCommandAsync("ATI")).Should().Be("ATIOK\r\r>");
    }

    // ---- COM port listesi ----

    [Fact]
    public void PortProvider_Returns_A_List_Without_Throwing()
    {
        var ports = new SerialPortProvider().GetPortNames();
        ports.Should().NotBeNull();
        // Bilgisayarda hiç port olmayabilir (liste boş olabilir); varsa hepsi "COM..." olmalı.
        ports.All(p => p.StartsWith("COM", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }
}
