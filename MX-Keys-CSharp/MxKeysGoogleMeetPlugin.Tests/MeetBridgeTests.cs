namespace Loupedeck.MxKeysGoogleMeetPlugin.Tests
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.MxKeysGoogleMeetPlugin.Bridge;

    using Xunit;

    /// <summary>
    /// Integration-style tests against a real MeetBridge instance on an ephemeral port (never the
    /// real plugin's port, so these can run alongside a live installed instance) with a fixed test
    /// secret (so tests never touch the real on-disk pairing-secret file). Exercises the security
    /// and protocol fixes from the independent code review: auth handshake, command allowlist,
    /// in-call precondition, and bounded/fragmented message framing.
    /// </summary>
    public sealed class MeetBridgeTests : IAsyncLifetime
    {
        private const String TestSecret = "test-secret-0123456789abcdef0123456789abcdef";
        private MeetBridge _bridge;
        private Int32 _port;

        public Task InitializeAsync()
        {
            this._port = GetFreeLoopbackPort();
            this._bridge = new MeetBridge(this._port, TestSecret);
            this._bridge.Start();
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            this._bridge.Stop();
            return Task.CompletedTask;
        }

        private static Int32 GetFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private Uri BridgeUri() => new($"ws://127.0.0.1:{this._port}/");

        private static async Task SendJsonAsync(ClientWebSocket socket, Object payload)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private static async Task<String> ReceiveTextAsync(ClientWebSocket socket, TimeSpan timeout)
        {
            var buffer = new Byte[8192];
            using var cts = new CancellationTokenSource(timeout);
            var result = await socket.ReceiveAsync(buffer, cts.Token);
            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }

        private async Task<ClientWebSocket> ConnectAndAuthenticateAsync()
        {
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(this.BridgeUri(), CancellationToken.None);
            await SendJsonAsync(socket, new { type = "auth", secret = TestSecret });
            return socket;
        }

        [Fact]
        public async Task Connection_without_any_message_is_closed_after_auth_timeout()
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(this.BridgeUri(), CancellationToken.None);

            // AuthTimeout on the server is 5s; give it real headroom rather than trying to race it.
            // The server's own ReceiveAsync was itself cancelled by ITS timeout, which leaves that
            // socket in the Aborted state (not eligible for a graceful close handshake per
            // CloseQuietlyAsync) — so the client-visible outcome is an abrupt disconnect
            // (WebSocketException) rather than a clean Close frame, unlike the wrong-secret case
            // below where the server did receive a complete message first. Either outcome proves
            // the same thing that matters here: an unauthenticated connection does not stay open.
            var buffer = new Byte[256];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                Assert.Equal(WebSocketMessageType.Close, result.MessageType);
            }
            catch (WebSocketException)
            {
                // Abrupt disconnect — also an acceptable proof the connection didn't survive.
            }
        }

        [Fact]
        public async Task Connection_with_wrong_secret_is_rejected()
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(this.BridgeUri(), CancellationToken.None);
            await SendJsonAsync(socket, new { type = "auth", secret = "not-the-right-secret" });

            var buffer = new Byte[256];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await socket.ReceiveAsync(buffer, cts.Token);

            Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        }

        [Fact]
        public async Task Connection_with_correct_secret_is_accepted_and_reports_connected()
        {
            using var socket = await this.ConnectAndAuthenticateAsync();

            // Give the server a moment to process the auth and flip Connected — poll rather than
            // sleep a fixed amount, since exact timing depends on scheduler load.
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!this._bridge.State.Connected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Assert.True(this._bridge.State.Connected);
            Assert.Equal(WebSocketState.Open, socket.State);
        }

        [Fact]
        public async Task Send_does_not_deliver_when_not_in_call()
        {
            using var socket = await this.ConnectAndAuthenticateAsync();
            // No "state" message sent, so State.InCall is still false.

            this._bridge.Send(MeetBridge.Commands.ToggleMic);

            // ThrowsAnyAsync, not ThrowsAsync: a cancelled ReceiveAsync can surface as the base
            // OperationCanceledException or the derived TaskCanceledException depending on exactly
            // where in the read pipeline the timeout lands — both equally prove "nothing arrived."
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ReceiveTextAsync(socket, TimeSpan.FromMilliseconds(500)));
        }

        [Fact]
        public async Task Send_delivers_a_valid_command_once_in_call()
        {
            using var socket = await this.ConnectAndAuthenticateAsync();
            await SendJsonAsync(socket, new { type = "state", inCall = true });

            // Wait for the state update to actually land before exercising Send()'s precondition.
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!this._bridge.State.InCall && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }
            Assert.True(this._bridge.State.InCall);

            this._bridge.Send(MeetBridge.Commands.ToggleMic);

            var received = await ReceiveTextAsync(socket, TimeSpan.FromSeconds(3));
            using var doc = JsonDocument.Parse(received);
            Assert.Equal("command", doc.RootElement.GetProperty("type").GetString());
            Assert.Equal(MeetBridge.Commands.ToggleMic, doc.RootElement.GetProperty("command").GetString());
        }

        [Fact]
        public async Task Send_refuses_a_command_outside_the_allowlist()
        {
            using var socket = await this.ConnectAndAuthenticateAsync();
            await SendJsonAsync(socket, new { type = "state", inCall = true });
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!this._bridge.State.InCall && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            this._bridge.Send("some-command-nobody-defined");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ReceiveTextAsync(socket, TimeSpan.FromMilliseconds(500)));
        }

        [Fact]
        public async Task Fragmented_state_message_is_reassembled_correctly()
        {
            using var socket = await this.ConnectAndAuthenticateAsync();

            var json = JsonSerializer.Serialize(new { type = "state", inCall = true, micMuted = true });
            var bytes = Encoding.UTF8.GetBytes(json);
            var mid = bytes.Length / 2;

            // Two frames, only the second marked EndOfMessage — exactly the fragmentation shape the
            // original review flagged as mishandled by a single unconditional ReceiveAsync call.
            await socket.SendAsync(bytes.AsMemory(0, mid), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
            await socket.SendAsync(bytes.AsMemory(mid), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (this._bridge.State.MicMuted != true && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Assert.True(this._bridge.State.InCall);
            Assert.True(this._bridge.State.MicMuted);
        }

        [Fact]
        public async Task Oversized_message_disconnects_the_socket_instead_of_crashing()
        {
            using var socket = await this.ConnectAndAuthenticateAsync();

            // MaxMessageBytes is 16 KiB; send well past it.
            var oversized = new String('x', 32 * 1024);
            var payload = JsonSerializer.Serialize(new { type = "state", inCall = true, extra = oversized });
            await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None);

            var buffer = new Byte[256];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await socket.ReceiveAsync(buffer, cts.Token);

            // The server-side receive loop returns null for an over-limit message and breaks out,
            // which tears down the connection rather than parsing/acting on it.
            Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        }
    }
}
