using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TridentCore.Core.Utilities;

public static class MinecraftServerStatusHelper
{
    private const int DEFAULT_PORT = 25565;
    private const int STATUS_PROTOCOL_VERSION = 767;

    public sealed class ServerStatusResult
    {
        public required string Address { get; init; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public bool IsReachable { get; init; }
        public long? LatencyMilliseconds { get; init; }
        public string? Description { get; init; }
        public string? VersionName { get; init; }
        public int? ProtocolVersion { get; init; }
        public int? OnlinePlayers { get; init; }
        public int? MaxPlayers { get; init; }
        public string? FaviconBase64 { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public static async Task<ServerStatusResult> ProbeAsync(
        string address,
        int timeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default
    )
    {
        var endpoint = ParseEndpoint(address);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            timeoutCts.CancelAfter(timeoutMilliseconds);

            using var client = new TcpClient();
            await client
                .ConnectAsync(endpoint.Host, endpoint.Port, timeoutCts.Token)
                .ConfigureAwait(false);

            await using var stream = client.GetStream();
            await SendHandshakeAsync(stream, endpoint.Host, endpoint.Port, timeoutCts.Token)
                .ConfigureAwait(false);
            await SendStatusRequestAsync(stream, timeoutCts.Token).ConfigureAwait(false);

            var json = await ReadStatusResponseAsync(stream, timeoutCts.Token)
                .ConfigureAwait(false);
            var parsed = ParseStatusJson(endpoint, json);

            var stopwatch = Stopwatch.StartNew();
            await SendPingAsync(
                    stream,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    timeoutCts.Token
                )
                .ConfigureAwait(false);
            await ReadPongAsync(stream, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            return new()
            {
                Address = endpoint.Address,
                Host = endpoint.Host,
                Port = endpoint.Port,
                IsReachable = true,
                LatencyMilliseconds = stopwatch.ElapsedMilliseconds,
                Description = parsed.Description,
                VersionName = parsed.VersionName,
                ProtocolVersion = parsed.ProtocolVersion,
                OnlinePlayers = parsed.OnlinePlayers,
                MaxPlayers = parsed.MaxPlayers,
                FaviconBase64 = parsed.FaviconBase64,
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateFailure(endpoint, "Timed out");
        }
        catch (Exception ex)
        {
            return CreateFailure(endpoint, ex.Message);
        }
    }

    private static ServerStatusResult CreateFailure(
        ServerEndpoint endpoint,
        string? errorMessage
    ) =>
        new()
        {
            Address = endpoint.Address,
            Host = endpoint.Host,
            Port = endpoint.Port,
            IsReachable = false,
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Unavailable" : errorMessage,
        };

    private static async Task SendHandshakeAsync(
        Stream stream,
        string host,
        int port,
        CancellationToken cancellationToken
    )
    {
        using var packet = new MemoryStream();
        WriteVarInt(packet, 0x00);
        WriteVarInt(packet, STATUS_PROTOCOL_VERSION);
        WriteString(packet, host);

        Span<byte> portBuffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(portBuffer, (ushort)port);
        packet.Write(portBuffer);

        WriteVarInt(packet, 0x01);
        await WritePacketAsync(stream, packet.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendStatusRequestAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        using var packet = new MemoryStream();
        WriteVarInt(packet, 0x00);
        await WritePacketAsync(stream, packet.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadStatusResponseAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        _ = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        var packetId = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        if (packetId != 0x00)
        {
            throw new InvalidDataException($"Unexpected status packet id: {packetId}");
        }

        var jsonLength = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        var jsonBytes = await ReadExactAsync(stream, jsonLength, cancellationToken)
            .ConfigureAwait(false);
        return Encoding.UTF8.GetString(jsonBytes);
    }

    private static async Task SendPingAsync(
        Stream stream,
        long payload,
        CancellationToken cancellationToken
    )
    {
        using var packet = new MemoryStream();
        WriteVarInt(packet, 0x01);

        Span<byte> payloadBuffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(payloadBuffer, payload);
        packet.Write(payloadBuffer);

        await WritePacketAsync(stream, packet.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadPongAsync(Stream stream, CancellationToken cancellationToken)
    {
        _ = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        var packetId = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        if (packetId != 0x01)
        {
            throw new InvalidDataException($"Unexpected pong packet id: {packetId}");
        }

        _ = await ReadExactAsync(stream, 8, cancellationToken).ConfigureAwait(false);
    }

    private static ParsedStatus ParseStatusJson(ServerEndpoint endpoint, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string? description = null;
        if (root.TryGetProperty("description", out var descriptionElement))
        {
            description = ExtractText(descriptionElement);
        }

        string? versionName = null;
        int? protocolVersion = null;
        if (root.TryGetProperty("version", out var versionElement))
        {
            if (versionElement.TryGetProperty("name", out var nameElement))
            {
                versionName = nameElement.GetString();
            }

            if (
                versionElement.TryGetProperty("protocol", out var protocolElement)
                && protocolElement.TryGetInt32(out var protocol)
            )
            {
                protocolVersion = protocol;
            }
        }

        int? onlinePlayers = null;
        int? maxPlayers = null;
        if (root.TryGetProperty("players", out var playersElement))
        {
            if (
                playersElement.TryGetProperty("online", out var onlineElement)
                && onlineElement.TryGetInt32(out var online)
            )
            {
                onlinePlayers = online;
            }

            if (
                playersElement.TryGetProperty("max", out var maxElement)
                && maxElement.TryGetInt32(out var max)
            )
            {
                maxPlayers = max;
            }
        }

        string? faviconBase64 = null;
        if (root.TryGetProperty("favicon", out var faviconElement))
        {
            faviconBase64 = faviconElement.GetString();
        }

        return new()
        {
            Address = endpoint.Address,
            Host = endpoint.Host,
            Port = endpoint.Port,
            Description = description,
            VersionName = versionName,
            ProtocolVersion = protocolVersion,
            OnlinePlayers = onlinePlayers,
            MaxPlayers = maxPlayers,
            FaviconBase64 = faviconBase64,
        };
    }

    private static string? ExtractText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Object => ExtractObjectText(element),
            JsonValueKind.Array => string.Concat(element.EnumerateArray().Select(ExtractText)),
            _ => null,
        };
    }

    private static string? ExtractObjectText(JsonElement element)
    {
        var parts = new List<string>();

        if (element.TryGetProperty("text", out var textElement))
        {
            var text = textElement.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(text);
            }
        }

        if (element.TryGetProperty("translate", out var translateElement))
        {
            var translate = translateElement.GetString();
            if (!string.IsNullOrEmpty(translate) && parts.Count == 0)
            {
                parts.Add(translate);
            }
        }

        if (
            element.TryGetProperty("extra", out var extraElement)
            && extraElement.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var child in extraElement.EnumerateArray())
            {
                var text = ExtractText(child);
                if (!string.IsNullOrEmpty(text))
                {
                    parts.Add(text);
                }
            }
        }

        if (
            element.TryGetProperty("with", out var withElement)
            && withElement.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var child in withElement.EnumerateArray())
            {
                var text = ExtractText(child);
                if (!string.IsNullOrEmpty(text))
                {
                    parts.Add(text);
                }
            }
        }

        return parts.Count == 0 ? null : string.Concat(parts);
    }

    private static ServerEndpoint ParseEndpoint(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Address is required", nameof(address));
        }

        var trimmed = address.Trim();
        if (trimmed.StartsWith('['))
        {
            var closingIndex = trimmed.IndexOf(']');
            if (closingIndex < 0)
            {
                throw new FormatException("Invalid IPv6 endpoint format");
            }

            var host = trimmed[1..closingIndex];
            var port = DEFAULT_PORT;
            if (closingIndex + 1 < trimmed.Length)
            {
                var suffix = trimmed[(closingIndex + 1)..];
                if (!suffix.StartsWith(':') || !int.TryParse(suffix[1..], out port))
                {
                    throw new FormatException("Invalid port");
                }
            }

            return new(trimmed, host, port);
        }

        var colonCount = trimmed.Count(x => x == ':');
        if (colonCount == 1)
        {
            var splitIndex = trimmed.LastIndexOf(':');
            if (int.TryParse(trimmed[(splitIndex + 1)..], out var port))
            {
                return new(trimmed, trimmed[..splitIndex], port);
            }
        }

        return new(trimmed, trimmed, DEFAULT_PORT);
    }

    private static async Task WritePacketAsync(
        Stream stream,
        byte[] payload,
        CancellationToken cancellationToken
    )
    {
        using var frame = new MemoryStream();
        WriteVarInt(frame, payload.Length);
        frame.Write(payload);

        frame.Position = 0;
        await frame.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        var unsignedValue = (uint)value;
        do
        {
            var temp = (byte)(unsignedValue & 0x7F);
            unsignedValue >>= 7;
            if (unsignedValue != 0)
            {
                temp |= 0x80;
            }

            stream.WriteByte(temp);
        } while (unsignedValue != 0);
    }

    private static async Task<int> ReadVarIntAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        var value = 0;
        var position = 0;

        while (position < 32)
        {
            var current = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
            value |= (current & 0x7F) << position;

            if ((current & 0x80) == 0)
            {
                return value;
            }

            position += 7;
        }

        throw new InvalidDataException("VarInt is too large");
    }

    private static async Task<byte[]> ReadExactAsync(
        Stream stream,
        int count,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task<byte> ReadByteAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        var buffer = await ReadExactAsync(stream, 1, cancellationToken).ConfigureAwait(false);
        return buffer[0];
    }

    private sealed class ParsedStatus
    {
        public required string Address { get; init; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public string? Description { get; init; }
        public string? VersionName { get; init; }
        public int? ProtocolVersion { get; init; }
        public int? OnlinePlayers { get; init; }
        public int? MaxPlayers { get; init; }
        public string? FaviconBase64 { get; init; }
    }

    private sealed record ServerEndpoint(string Address, string Host, int Port);
}
