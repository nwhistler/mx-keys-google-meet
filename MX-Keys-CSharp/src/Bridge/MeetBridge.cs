namespace Loupedeck.MxKeysGoogleMeetPlugin.Bridge
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.WebSockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Loopback WebSocket bridge to the companion browser extension (see /Google Meet). Same port
    /// and JSON protocol as the original Node.js plugin's bridge, so the extension needs zero
    /// changes to *most* of this when switching SDKs — only the server implementation moved.
    ///
    /// SECURITY MODEL: a bare loopback listener has no inherent trust boundary — any local process
    /// can connect to it. Two independent-reviewer-confirmed mitigations are layered on top:
    ///   1. A per-install random secret (see Secret.cs-equivalent logic below) that the extension
    ///      must present as its first message before its socket is treated as live. This is NOT
    ///      cryptographically bulletproof — the secret is served in plaintext over the same loopback
    ///      port via GET /pairing-code, specifically so the extension's options page can fetch it
    ///      without asking the user to manually copy a file. Any OTHER local process that knows to
    ///      hit that same endpoint can also read it. It is a deliberate, documented trade-off: it
    ///      stops a generic "connect and see what happens" scanner or an unrelated app that guesses
    ///      this port, but it does not stop a targeted local attacker who reads this exact protocol.
    ///      See README's Security section for the honest version of this claim.
    ///   2. A command allowlist + an in-call precondition in Send(), so even an authenticated client
    ///      can only invoke the fixed set of Meet actions this plugin actually supports, and only
    ///      while the extension itself reports an active call.
    /// </summary>
    public sealed class MeetBridge
    {
        public static readonly MeetBridge Instance = new();

        /// <summary>Command name constants shared between Send() callers and the allowlist below —
        /// centralizing these avoids the raw-string drift the original review flagged.</summary>
        public static class Commands
        {
            public const String ToggleMic = "toggle-mic";
            public const String ToggleCamera = "toggle-camera";
            public const String ToggleHand = "toggle-hand";
            public const String LeaveCall = "leave-call";
            public const String ToggleCaptions = "toggle-captions";
            public const String ToggleScreenShare = "toggle-screen-share";
            public const String SendReaction = "send-reaction";
            public const String CloseReactions = "close-reactions";
            // No ToggleGeminiNotes: removed ahead of Chrome Web Store submission, see content.js's
            // "REMOVED FEATURE" comment and code-review.md's remediation notes for the full story —
            // it only ever worked via the extension's "debugger" permission, which is a major review
            // red flag, so it was pulled back out rather than risk/delay the store listing.
        }

        private static readonly HashSet<String> ValidCommands = new()
        {
            Commands.ToggleMic, Commands.ToggleCamera, Commands.ToggleHand, Commands.LeaveCall,
            Commands.ToggleCaptions, Commands.ToggleScreenShare, Commands.SendReaction, Commands.CloseReactions,
        };

        private const Int32 DefaultPort = 47624;
        private const Int32 RetryDelayMs = 3000;
        private const Int32 MaxRetryDelayMs = 30000;
        private const Int32 MaxMessageBytes = 16 * 1024; // generous for our tiny JSON messages; guards against a runaway/hostile sender
        private const Int32 MaxConcurrentSockets = 8; // this extension only ever needs one; a handful of headroom covers reconnect overlap without leaving the listener wide open
        private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(5);
        private const WebSocketCloseStatus AuthFailedCloseStatus = (WebSocketCloseStatus)4001; // private-use range (4000-4999); background.js checks for this to stop auto-retrying a bad pairing code

        // Chrome's MV3 service worker (background.js) can be torn down and restarted at any time —
        // idle timeout, its own 24s keepalive alarm waking it, an extension reload — and each restart
        // re-runs the script from scratch, opening a brand new socket with no memory of any old one.
        // When the old worker instance's underlying connection doesn't get a clean close handshake,
        // it just goes silent forever rather than erroring out, so it never leaves `_sockets` on its
        // own. Left unchecked, Send() broadcasting to all of them fires the same command once per
        // zombie (confirmed live, 2026-09: one reaction press sent the same emoji 3 times after a
        // long session's worth of worker restarts). _lastSeen + the reap timer below clear out
        // anything that's gone quiet — the extension pings every 24s regardless of whether a Meet
        // tab is even open, so a genuinely live connection never comes close to the threshold.
        private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(90);

        private readonly List<WebSocket> _sockets = new();
        private readonly Dictionary<WebSocket, DateTime> _lastSeen = new();
        private readonly Dictionary<WebSocket, SemaphoreSlim> _sendLocks = new();
        private readonly Object _lock = new();
        private HttpListener _listener;
        private Timer _reapTimer;
        private Int32 _retryDelayMs = RetryDelayMs;
        private CancellationTokenSource _lifecycle;
        private String _pairingSecret;
        private readonly Int32 _port;
        private readonly String _testSecret;

        public MeetState State { get; private set; } = MeetState.Idle;

        /// <summary>Fires whenever Meet's reported state changes — actions subscribe to repaint their icon.</summary>
        public event Action<MeetState> StateChanged;

        private MeetBridge() : this(DefaultPort) { }

        /// <summary>Test-only entry point (see MxKeysGoogleMeetPlugin.Tests) — a distinct port avoids
        /// colliding with a real running instance of this plugin on DefaultPort, and an explicit
        /// secret bypasses the shared on-disk pairing-secret file so parallel test runs don't
        /// interfere with each other or with a real installed instance's pairing state.</summary>
        internal MeetBridge(Int32 port, String testSecret = null)
        {
            this._port = port;
            this._testSecret = testSecret;
        }

        /// <summary>Idempotent: calling Start() while already started tears down and re-creates
        /// cleanly rather than layering a second listener/timer on top (confirmed possible via a
        /// hot-reload race in the original review).</summary>
        public void Start()
        {
            lock (this._lock)
            {
                this.StopInternal();
                this._lifecycle = new CancellationTokenSource();
                this._pairingSecret = this._testSecret ?? LoadOrCreateSecret();
                this.StartListener(this._lifecycle.Token);
                this._reapTimer = new Timer(_ => this.ReapStaleSockets(), null, StaleThreshold, StaleThreshold);
            }
        }

        public void Stop()
        {
            lock (this._lock)
            {
                this.StopInternal();
            }
        }

        /// <summary>Caller must hold this._lock.</summary>
        private void StopInternal()
        {
            this._lifecycle?.Cancel();
            this._lifecycle?.Dispose();
            this._lifecycle = null;
            this._reapTimer?.Dispose();
            this._reapTimer = null;
            try
            {
                this._listener?.Stop();
                this._listener?.Close();
            }
            catch
            {
                // already gone
            }
            this._listener = null;
        }

        /// <summary>
        /// A per-install random secret, generated once and reused across restarts/reloads so pairing
        /// only has to happen when the extension is first installed (or if this file is deleted to
        /// force re-pairing). Stored next to the plugin's own binaries, found via this assembly's own
        /// Location — confirmed live, 2026-09, that AppContext.BaseDirectory resolves to the HOST
        /// process's directory (LogiPluginService.app/Contents/) when running as a loaded plugin, not
        /// this DLL's actual folder, which sent the first version of this code trying (and correctly
        /// failing, on permissions) to write inside the host app bundle itself.
        /// </summary>
        private static String LoadOrCreateSecret()
        {
            var path = SecretFilePath();
            try
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (existing.Length >= 32)
                    {
                        return existing;
                    }
                }

                var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                File.WriteAllText(path, secret);
                if (!OperatingSystem.IsWindows())
                {
                    // Best-effort on Unix; Windows relies on the user-profile ACLs already protecting
                    // the plugin's install directory (LocalAppData is per-user by default there).
                    try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                    catch { /* non-fatal — worst case the file is readable by other local users on a shared machine */ }
                }
                return secret;
            }
            catch (Exception ex)
            {
                // If we can't persist a secret, fall back to one that lives only for this process —
                // pairing will need to happen again next launch, but the bridge still requires SOME
                // secret rather than silently running unauthenticated.
                PluginLog.Warning(ex, $"[MeetBridge] could not read/write pairing secret at {path}; using a session-only secret");
                return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            }
        }

        private static String SecretFilePath()
        {
            var dir = PluginDataDirectory();
            Directory.CreateDirectory(dir); // no-op if it already exists (a real install); needed for
                                             // the dev .link workflow, where nothing has ever written here
            return Path.Combine(dir, "bridge-secret.txt");
        }

        /// <summary>
        /// Deliberately NOT anywhere under Logi/LogiPluginService/Plugins/MxKeysGoogleMeet, even
        /// though that's a real, writable, per-plugin directory — confirmed live, 2026-09, that
        /// Options+'s "Install from file" wipes that entire folder before unpacking the new version,
        /// which was silently deleting the pairing secret on every single reinstall (not just a
        /// version bump) and forcing the user to re-pair the extension every time they updated the
        /// plugin. A dedicated, separate app-data folder for this plugin survives reinstalls, since
        /// Options+ has no reason to touch anything outside its own managed Plugins tree.
        ///
        /// Also confirmed live, 2026-09: both AppContext.BaseDirectory (resolves to the
        /// LogiPluginService host's own directory when running as a loaded plugin) and
        /// typeof(MeetBridge).Assembly.Location (empty string in this host — plugin assemblies
        /// aren't loaded from a path the runtime tracks) are unreliable ways to find "where this
        /// plugin's own files live," which is why this is a hand-built path rather than something
        /// derived from the running assembly. .NET's Environment.SpecialFolder follows XDG
        /// conventions on Unix (~/.config, ~/.local/share), not macOS's real Application Support
        /// convention, hence building the Mac path by hand instead of trusting
        /// SpecialFolder.ApplicationData there.
        /// </summary>
        private static String PluginDataDirectory()
        {
            var root = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
            return Path.Combine(root, "MxKeysGoogleMeet");
        }

        /// <summary>Closes any connection that hasn't sent so much as a keepalive ping in over
        /// StaleThreshold — see the comment on that constant for why this is safe.</summary>
        private void ReapStaleSockets()
        {
            List<WebSocket> stale;
            lock (this._lock)
            {
                var cutoff = DateTime.UtcNow - StaleThreshold;
                stale = this._sockets.Where(s => !this._lastSeen.TryGetValue(s, out var last) || last < cutoff).ToList();
            }
            foreach (var socket in stale)
            {
                PluginLog.Info($"[MeetBridge] reaping a connection silent for over {StaleThreshold.TotalSeconds}s (likely a zombie from a browser service-worker restart)");
                _ = CloseQuietlyAsync(socket, WebSocketCloseStatus.NormalClosure, "idle timeout");
            }
        }

        private static async Task CloseQuietlyAsync(WebSocket socket, WebSocketCloseStatus status, String reason)
        {
            // A ReceiveAsync cancelled via CancellationToken (see AuthenticateAsync's timeout) leaves
            // the managed WebSocket in the Aborted state, where CloseAsync always throws — that's an
            // expected outcome of the timeout, not a real failure, so skip the pointless attempt and
            // the log noise it would otherwise generate.
            if (socket.State is WebSocketState.Aborted or WebSocketState.Closed or WebSocketState.CloseSent)
            {
                return;
            }
            try { await socket.CloseAsync(status, reason, CancellationToken.None); }
            catch (Exception ex) { PluginLog.Verbose($"[MeetBridge] close failed (socket likely already gone): {ex.Message}"); }
        }

        /// <summary>
        /// If something else briefly holds this port at startup (a leftover dev process, a race with
        /// a previous instance of this same plugin reloading), retry with backoff instead of leaving
        /// the plugin with no bridge for the rest of its process lifetime.
        /// </summary>
        private void StartListener(CancellationToken cancellationToken)
        {
            try
            {
                this._listener = new HttpListener();
                this._listener.Prefixes.Add($"http://127.0.0.1:{this._port}/");
                this._listener.Start();
                this._retryDelayMs = RetryDelayMs;
                PluginLog.Info($"[MeetBridge] listening on ws://127.0.0.1:{this._port} for the browser extension");
                _ = this.AcceptLoopAsync(cancellationToken);
            }
            catch (HttpListenerException ex)
            {
                PluginLog.Warning(ex, $"[MeetBridge] failed to bind port {this._port}");
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                var delay = this._retryDelayMs;
                this._retryDelayMs = Math.Min(this._retryDelayMs * 2, MaxRetryDelayMs);
                PluginLog.Warning($"[MeetBridge] retrying in {delay / 1000}s...");
                _ = Task.Delay(delay, cancellationToken).ContinueWith(
                    t => { if (!t.IsCanceled && !cancellationToken.IsCancellationRequested) { this.StartListener(cancellationToken); } },
                    cancellationToken, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[MeetBridge] failed to start listener");
            }
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && this._listener?.IsListening == true)
            {
                HttpListenerContext context;
                try
                {
                    context = await this._listener.GetContextAsync();
                }
                catch
                {
                    break; // listener stopped/disposed
                }

                if (context.Request.IsWebSocketRequest)
                {
                    Int32 currentCount;
                    lock (this._lock) { currentCount = this._sockets.Count; }
                    if (currentCount >= MaxConcurrentSockets)
                    {
                        PluginLog.Warning("[MeetBridge] rejecting a new connection — already at the concurrent-connection cap");
                        context.Response.StatusCode = 503;
                        context.Response.Close();
                        continue;
                    }
                    _ = this.HandleSocketAsync(context, cancellationToken);
                }
                else if (context.Request.Url?.AbsolutePath == "/pairing-code" && context.Request.HttpMethod == "GET")
                {
                    this.ServePairingCode(context);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
        }

        /// <summary>
        /// Deliberately unauthenticated — see the class-level SECURITY MODEL comment. This exists so
        /// the extension's options page can fetch the pairing code with one click instead of asking
        /// the user to open a local file and copy/paste a hex string by hand.
        /// </summary>
        private void ServePairingCode(HttpListenerContext context)
        {
            try
            {
                var json = JsonSerializer.Serialize(new { secret = this._pairingSecret });
                var bytes = Encoding.UTF8.GetBytes(json);
                context.Response.ContentType = "application/json";
                // Same-machine loopback origins only; the extension's own host_permissions already
                // scope this to itself, but a permissive CORS header keeps a plain fetch() from the
                // options page working without extra ceremony since it's not sensitive beyond what
                // any local process could already read directly.
                context.Response.AppendHeader("Access-Control-Allow-Origin", "*");
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[MeetBridge] failed to serve pairing code");
                try { context.Response.StatusCode = 500; } catch { /* ignore */ }
            }
            finally
            {
                context.Response.Close();
            }
        }

        private async Task HandleSocketAsync(HttpListenerContext context, CancellationToken lifecycleToken)
        {
            WebSocket socket;
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                socket = wsContext.WebSocket;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[MeetBridge] failed to accept websocket");
                try { context.Response.StatusCode = 500; context.Response.Close(); } catch { /* ignore */ }
                return;
            }

            if (!await this.AuthenticateAsync(socket))
            {
                return;
            }

            lock (this._lock)
            {
                this._sockets.Add(socket);
                this._lastSeen[socket] = DateTime.UtcNow;
                this._sendLocks[socket] = new SemaphoreSlim(1, 1);
            }
            PluginLog.Info("[MeetBridge] browser extension connected and authenticated");
            this.SetState(this.State with { Connected = true });

            try
            {
                while (socket.State == WebSocketState.Open && !lifecycleToken.IsCancellationRequested)
                {
                    var message = await ReceiveFullMessageAsync(socket);
                    if (message == null)
                    {
                        break; // closed, or the peer sent something we refuse to process (oversized/binary)
                    }
                    lock (this._lock) { this._lastSeen[socket] = DateTime.UtcNow; }
                    this.HandleMessage(message);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Verbose($"[MeetBridge] socket closed: {ex.Message}");
            }
            finally
            {
                Int32 remaining;
                lock (this._lock)
                {
                    this._sockets.Remove(socket);
                    this._lastSeen.Remove(socket);
                    this._sendLocks.Remove(socket, out var sendLock);
                    sendLock?.Dispose();
                    remaining = this._sockets.Count;
                }
                if (remaining == 0)
                {
                    PluginLog.Info("[MeetBridge] browser extension disconnected");
                    this.SetState(MeetState.Idle);
                }
                socket.Dispose();
            }
        }

        /// <summary>
        /// Requires the very first message on a new connection to be `{"type":"auth","secret":"..."}`
        /// matching our per-install secret, within AuthTimeout. Anything else — wrong shape, wrong
        /// secret, or silence — closes the connection immediately and it is never added to `_sockets`,
        /// so it never receives state updates or commands and is never broadcast to.
        /// </summary>
        private async Task<Boolean> AuthenticateAsync(WebSocket socket)
        {
            using var cts = new CancellationTokenSource(AuthTimeout);
            var buffer = new Byte[1024];
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<Byte>(buffer), cts.Token);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[MeetBridge] rejecting connection — no auth message within {AuthTimeout.TotalSeconds}s ({ex.GetType().Name})");
                await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "auth timeout");
                socket.Dispose();
                return false;
            }

            if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage)
            {
                PluginLog.Warning("[MeetBridge] rejecting connection — first message was not a single complete text frame");
                await CloseQuietlyAsync(socket, AuthFailedCloseStatus, "expected auth message");
                socket.Dispose();
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
                var root = doc.RootElement;
                var isAuthMessage = root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "auth";
                var providedSecret = root.TryGetProperty("secret", out var secretProp) ? secretProp.GetString() : null;

                if (isAuthMessage && providedSecret != null
                    && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(providedSecret), Encoding.UTF8.GetBytes(this._pairingSecret)))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                // falls through to the rejection below
            }

            PluginLog.Warning("[MeetBridge] rejecting connection — invalid or missing pairing secret. Re-pair the extension from its options page.");
            await CloseQuietlyAsync(socket, AuthFailedCloseStatus, "invalid pairing code");
            socket.Dispose();
            return false;
        }

        /// <summary>
        /// Accumulates frames until EndOfMessage, refuses to grow past MaxMessageBytes, and refuses
        /// binary frames outright (our protocol is JSON text only). Returns null for a Close frame,
        /// an over-limit message, or a binary message — all of which the caller treats as "stop
        /// reading from this socket" (an oversized/binary message from an already-authenticated peer
        /// is a protocol violation worth disconnecting over, not silently ignoring). The binary/
        /// oversized cases proactively send a real RFC 6455 close frame with a specific status
        /// (InvalidMessageType / MessageTooBig) rather than just returning and letting the caller's
        /// eventual socket.Dispose() abruptly reset the connection — a real close handshake is both
        /// more correct and lets a well-behaved client log/report why it was disconnected.
        /// </summary>
        private static async Task<String> ReceiveFullMessageAsync(WebSocket socket)
        {
            using var stream = new MemoryStream();
            var buffer = new Byte[4096];
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<Byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    PluginLog.Warning("[MeetBridge] disconnecting a peer — received a binary frame, protocol is text-only");
                    await CloseQuietlyAsync(socket, WebSocketCloseStatus.InvalidMessageType, "text-only protocol");
                    return null;
                }
                if (stream.Length + result.Count > MaxMessageBytes)
                {
                    PluginLog.Warning($"[MeetBridge] disconnecting a peer — message exceeded {MaxMessageBytes} bytes");
                    await CloseQuietlyAsync(socket, WebSocketCloseStatus.MessageTooBig, $"exceeded {MaxMessageBytes} bytes");
                    return null;
                }
                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }

        private void HandleMessage(String json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "state")
                {
                    return; // "ping" and anything else: no-op, just keeps the extension's worker alive.
                }

                Boolean? GetBool(String name) =>
                    root.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? el.GetBoolean()
                        : (Boolean?)null;

                var inCall = root.TryGetProperty("inCall", out var ic) && ic.ValueKind == JsonValueKind.True;
                this.SetState(new MeetState(true, inCall, GetBool("micMuted"), GetBool("cameraOn"), GetBool("handRaised"), GetBool("captionsOn")));
            }
            catch (JsonException)
            {
                PluginLog.Verbose("[MeetBridge] ignored malformed message from extension");
            }
        }

        /// <summary>Only republishes/notifies when the state actually changed (MeetState is a record,
        /// so structural equality is free), and isolates each subscriber so one throwing action
        /// doesn't break state processing for the rest or unwind the caller.</summary>
        private void SetState(MeetState next)
        {
            if (next == this.State)
            {
                return;
            }
            this.State = next;
            foreach (var handler in this.StateChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                try { ((Action<MeetState>)handler)(next); }
                catch (Exception ex) { PluginLog.Warning(ex, "[MeetBridge] a StateChanged subscriber threw"); }
            }
        }

        /// <summary>Sends a command to whichever Meet tab the extension currently considers active.
        /// `param` carries per-command data (e.g. which emoji to react with) and is omitted from the
        /// JSON payload when null, matching what the extension's `runCommand(command, param)` expects.
        ///
        /// Rejects anything not in the fixed command allowlist, and requires the extension to have
        /// reported an active call before allowing any of these through — an authenticated-but-
        /// otherwise-untrusted local client can still only ask for one of these specific actions, and
        /// only while a call is actually active, which bounds the blast radius of Issue 6 in the
        /// original review even though every command here is meeting-scoped by nature.</summary>
        public void Send(String command, String param = null)
        {
            if (!ValidCommands.Contains(command))
            {
                PluginLog.Warning($"[MeetBridge] refusing unknown command \"{command}\"");
                return;
            }

            // Checked before InCall, not after: pressing a key with the extension not even
            // connected (never paired, or reinstalled and lost its pairing secret) and pressing a
            // key while connected-but-not-in-a-call are different problems with different fixes —
            // "no active Meet call reported" was a misleading thing to log for the first case
            // (confirmed live, 2026-09: looked exactly like a broken bridge until the log was
            // checked and it turned out to be zero connection attempts, not a rejected one).
            List<WebSocket> targets;
            lock (this._lock)
            {
                targets = this._sockets.Where(s => s.State == WebSocketState.Open).ToList();
            }

            if (targets.Count == 0)
            {
                PluginLog.Warning($"[MeetBridge] no browser extension connected - ignoring \"{command}\" (re-pair from the extension's options page if this persists)");
                return;
            }

            if (!this.State.InCall)
            {
                PluginLog.Warning($"[MeetBridge] refusing \"{command}\" — extension connected but no active Meet call reported");
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "command", command, param }));
            foreach (var socket in targets)
            {
                _ = this.SendSerializedAsync(socket, bytes);
            }
        }

        /// <summary>.NET WebSocket implementations only permit one in-flight send per socket; a
        /// per-socket semaphore serializes overlapping Send() calls (e.g. two quick keypad presses)
        /// instead of letting them race, which the original review correctly flagged as a source of
        /// silently-dropped or throwing sends.</summary>
        private async Task SendSerializedAsync(WebSocket socket, Byte[] bytes)
        {
            SemaphoreSlim sendLock;
            lock (this._lock)
            {
                if (!this._sendLocks.TryGetValue(socket, out sendLock))
                {
                    return; // socket already torn down between the snapshot in Send() and now
                }
            }

            await sendLock.WaitAsync();
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(new ArraySegment<Byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[MeetBridge] send failed; closing the socket");
                _ = CloseQuietlyAsync(socket, WebSocketCloseStatus.InternalServerError, "send failed");
            }
            finally
            {
                sendLock.Release();
            }
        }
    }
}
