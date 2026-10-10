using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BGPWatch.Web.Lab;

public sealed record ExecResult(long ExitCode, string Stdout, string Stderr);

/// <summary>Docker refused the request or is not reachable.</summary>
public sealed class DockerException(string message) : Exception(message);

/// <summary>
/// Runs commands in containers through the Docker Engine API, over the socket mounted
/// at /var/run/docker.sock (the named pipe docker_engine on Windows).
/// </summary>
public sealed class DockerEngine(HttpClient http)
{
    private const int FrameHeaderSize = 8;
    private const byte StdoutStream = 1;

    /// <summary>The Engine API's own field names, as written.</summary>
    private static readonly JsonSerializerOptions Names = new() { PropertyNameCaseInsensitive = true };

    public static HttpClient Client()
    {
        var handler = new SocketsHttpHandler { ConnectCallback = async (_, cancel) => await ConnectAsync(cancel) };
        return new HttpClient(handler) { BaseAddress = new Uri("http://docker/"), Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Runs a command in a container and keeps its stdout and stderr apart.</summary>
    public async Task<ExecResult> ExecAsync(string container, IReadOnlyList<string> command, CancellationToken cancel)
    {
        var created = await SendAsync($"containers/{container}/exec",
            new { AttachStdout = true, AttachStderr = true, Cmd = command }, cancel);
        var id = (await created.Content.ReadFromJsonAsync<Created>(Names, cancel))!.Id;
        var started = await SendAsync($"exec/{id}/start", new { Detach = false, Tty = false }, cancel);
        var (stdout, stderr) = Demultiplex(await started.Content.ReadAsByteArrayAsync(cancel));
        var inspected = await http.GetFromJsonAsync<Inspected>($"exec/{id}/json", Names, cancel);
        return new ExecResult(inspected!.ExitCode ?? -1, stdout, stderr);
    }

    /// <summary>The stdout and stderr of an exec stream without a terminal, from its 8-byte frames.</summary>
    public static (string Stdout, string Stderr) Demultiplex(ReadOnlySpan<byte> stream)
    {
        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        while (stream.Length >= FrameHeaderSize)
        {
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(stream[4..]), (uint)(stream.Length - FrameHeaderSize));
            (stream[0] == StdoutStream ? stdout : stderr).Write(stream.Slice(FrameHeaderSize, size));
            stream = stream[(FrameHeaderSize + size)..];
        }
        return (Encoding.UTF8.GetString(stdout.ToArray()), Encoding.UTF8.GetString(stderr.ToArray()));
    }

    private async Task<HttpResponseMessage> SendAsync(string path, object body, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(path, body, Names, cancel);
        }
        catch (HttpRequestException e)
        {
            throw new DockerException($"Docker is not reachable: {e.Message}");
        }
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<Failure>(Names, cancel);
            throw new DockerException($"{(int)response.StatusCode}: {error?.Message}");
        }
        return response;
    }

    private static async ValueTask<Stream> ConnectAsync(CancellationToken cancel)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", "docker_engine", PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancel);
            return pipe;
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/docker.sock"), cancel);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private sealed record Created(string Id);

    private sealed record Inspected(long? ExitCode);

    private sealed record Failure(string? Message);
}
