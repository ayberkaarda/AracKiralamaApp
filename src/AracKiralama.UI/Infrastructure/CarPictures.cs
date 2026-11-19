using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace AracKiralama.UI.Infrastructure;

/// <summary>
/// Loads the picture of a car from the address an admin entered. The address comes from the database, so
/// it is treated as untrusted: only absolute http and https addresses without user information are
/// fetched, only from public internet addresses (the connection itself checks the resolved address, so a
/// name that points to this computer, the local network or a link-local address is refused, also after a
/// DNS change), without redirects or proxies, with a time limit and a size limit. Anything else, and
/// every failure, shows the application's own placeholder. Per picture box only the latest request is
/// applied, so a slow answer never replaces the picture of a car chosen later.
/// </summary>
internal static class CarPictures
{
    /// <summary>Largest picture that is downloaded.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>Largest width or height that is decoded.</summary>
    public const int MaxDimension = 4096;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly ConditionalWeakTable<PictureBox, RequestSequence> Sequences = new();

    private static readonly Lazy<HttpClient> Client = new(CreateClient);

    /// <summary>
    /// Shows the placeholder at once and, for an acceptable address, replaces it with the downloaded
    /// picture when it arrives, unless another picture was asked for in the meantime.
    /// </summary>
    public static void Show(PictureBox pictureBox, string? address)
    {
        ArgumentNullException.ThrowIfNull(pictureBox);

        var request = Sequences.GetValue(pictureBox, _ => new RequestSequence()).Next();
        Replace(pictureBox, AppAssets.CarPlaceholder);
        if (TryGetAllowedUri(address, out var uri))
        {
            _ = LoadIntoAsync(pictureBox, uri, request);
        }
    }

    /// <summary>An absolute http or https address without user information, or nothing.</summary>
    public static bool TryGetAllowedUri(string? address, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(address)
            || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || parsed.IsLoopback
            || (parsed.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                && !IsPublicAddress(IPAddress.Parse(parsed.DnsSafeHost))))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="address"/> is a public unicast address: not this computer (loopback,
    /// unspecified), not a private, shared or link-local network, not multicast or broadcast.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                || b[0] == 10
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || b[0] == 127
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                || b[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC);
        }

        return false;
    }

    private static async Task LoadIntoAsync(PictureBox pictureBox, Uri uri, int request)
    {
        Image? image = null;
        try
        {
            image = await DownloadAsync(uri).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Any failure leaves the placeholder; the address is not trusted.
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        var sequence = Sequences.GetValue(pictureBox, _ => new RequestSequence());
        if (!sequence.IsLatest(request) || pictureBox.IsDisposed)
        {
            image.Dispose();
            return;
        }

        Replace(pictureBox, image);
    }

    private static async Task<Image> DownloadAsync(Uri uri)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        using var response = await Client.Value.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType is not { } type || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || response.Content.Headers.ContentLength > MaxBytes)
        {
            throw new InvalidDataException("Not an acceptable picture.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var buffer = new MemoryStream();
        await using (buffer.ConfigureAwait(false))
        {
            var chunk = new byte[81_920];
            int read;
            while ((read = await source.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                {
                    throw new InvalidDataException("The picture is too large.");
                }

                buffer.Write(chunk, 0, read);
            }

            buffer.Position = 0;
            using var decoded = Image.FromStream(buffer, useEmbeddedColorManagement: false, validateImageData: true);
            if (decoded.Width > MaxDimension || decoded.Height > MaxDimension)
            {
                throw new InvalidDataException("The picture is too large.");
            }

            // A copy detaches the image from the stream.
            return new Bitmap(decoded);
        }
    }

    private static void Replace(PictureBox pictureBox, Image image)
    {
        var previous = pictureBox.Image;
        pictureBox.Image = image;
        if (previous is not null && !ReferenceEquals(previous, image) && !ReferenceEquals(previous, AppAssets.CarPlaceholder))
        {
            previous.Dispose();
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxResponseHeadersLength = 64,
            ConnectCallback = ConnectToPublicAddressAsync,
        };
        var client = new HttpClient(handler) { Timeout = Timeout, MaxResponseContentBufferSize = MaxBytes };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }

    /// <summary>Resolves the host and connects only to a public address, so the check covers the address really used.</summary>
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        var allowed = addresses.Where(IsPublicAddress).ToArray();
        if (allowed.Length == 0 || allowed.Length != addresses.Length)
        {
            throw new HttpRequestException("The picture's host is not a public address.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
