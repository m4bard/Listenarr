/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Listenarr.Tests.Features.Infrastructure.Notifications.Email
{
    /// <summary>
    /// A minimal SMTP server on the loopback interface, enough for MailKit to complete an
    /// unencrypted exchange against.
    /// </summary>
    /// <remarks>
    /// The transport is the one class whose whole job is to talk to a socket, so a mock of its own
    /// dependency would assert nothing about it. This is a socket on 127.0.0.1 with an
    /// operating-system-assigned port, which is a local file descriptor rather than the network:
    /// no mail leaves the machine and nothing outside the test can reach it.
    /// </remarks>
    internal sealed class StubSmtpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _loop;

        public StubSmtpServer(
            bool acceptAuthentication = true,
            int responseDelayMilliseconds = 0,
            bool answerQuit = true)
        {
            AcceptAuthentication = acceptAuthentication;
            ResponseDelayMilliseconds = responseDelayMilliseconds;
            AnswerQuit = answerQuit;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public bool AcceptAuthentication { get; }

        public int ResponseDelayMilliseconds { get; }

        /// <summary>
        /// When false the server accepts the message and then never answers QUIT, holding the
        /// socket open. Real servers that hang up on "250 queued" produce the same shape.
        /// </summary>
        public bool AnswerQuit { get; }

        /// <summary>The credential the client offered, decoded from AUTH PLAIN.</summary>
        public string? AuthenticatedUsername { get; private set; }

        /// <summary>Every command verb the server was sent, in order.</summary>
        public List<string> Commands { get; } = new();

        /// <summary>The message body, as it arrived after DATA.</summary>
        public string DeliveredMessage { get; private set; } = string.Empty;

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stopping.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    await ServeAsync(client);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
            {
                // The listener was stopped, or the client hung up. Either way this loop is done.
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

            await RespondAsync(writer, "220 stub.example.invalid ESMTP");

            string? line;
            while ((line = await reader.ReadLineAsync(_stopping.Token)) != null)
            {
                var verb = line.Split(' ')[0].ToUpperInvariant();
                lock (Commands)
                {
                    Commands.Add(verb);
                }

                switch (verb)
                {
                    case "EHLO":
                    case "HELO":
                        // No STARTTLS advertised, so SecureSocketOptions.Auto stays in plaintext.
                        await RespondAsync(writer, "250-stub.example.invalid");
                        await RespondAsync(writer, "250-AUTH PLAIN LOGIN");
                        await RespondAsync(writer, "250 8BITMIME");
                        break;
                    case "AUTH":
                        AuthenticatedUsername = DecodePlainUsername(line);
                        await RespondAsync(writer, AcceptAuthentication
                            ? "235 2.7.0 Authentication successful"
                            : "535 5.7.8 Username and Password not accepted");
                        break;
                    case "MAIL":
                    case "RCPT":
                        await RespondAsync(writer, "250 2.1.0 Ok");
                        break;
                    case "DATA":
                        await RespondAsync(writer, "354 End data with <CR><LF>.<CR><LF>");
                        DeliveredMessage = await ReadDataAsync(reader);
                        await RespondAsync(writer, "250 2.0.0 Ok: queued");
                        break;
                    case "QUIT":
                        if (!AnswerQuit)
                        {
                            // Hold the connection open and say nothing, which is what the client
                            // has to survive without reporting the accepted message as a failure.
                            await Task.Delay(Timeout.Infinite, _stopping.Token);
                        }

                        await RespondAsync(writer, "221 2.0.0 Bye");
                        return;
                    default:
                        await RespondAsync(writer, "250 2.0.0 Ok");
                        break;
                }
            }
        }

        private async Task RespondAsync(StreamWriter writer, string response)
        {
            if (ResponseDelayMilliseconds > 0)
            {
                await Task.Delay(ResponseDelayMilliseconds, _stopping.Token);
            }

            await writer.WriteLineAsync(response);
        }

        /// <summary>
        /// Pulls the username out of an "AUTH PLAIN &lt;base64&gt;" line, whose payload is
        /// authzid NUL authcid NUL password.
        /// </summary>
        private static string? DecodePlainUsername(string line)
        {
            var parts = line.Split(' ');
            if (parts.Length < 3 || !parts[1].Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var fields = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])).Split('\0');
                return fields.Length >= 2 ? fields[1] : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private async Task<string> ReadDataAsync(StreamReader reader)
        {
            var body = new StringBuilder();
            string? line;
            while ((line = await reader.ReadLineAsync(_stopping.Token)) != null && line != ".")
            {
                body.AppendLine(line);
            }

            return body.ToString();
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Stop();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // The loop was cancelled, which is how it is meant to end.
            }

            _stopping.Dispose();
        }
    }
}
