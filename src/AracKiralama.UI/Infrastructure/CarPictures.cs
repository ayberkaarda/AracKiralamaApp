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
/// DNS change), without redirects or proxies, with a time limit, a download size limit and a picture
/// size limit checked before decoding, and only a few at a time. Anything else, and
/// every failure, leaves the picture empty, where <see cref="Theming.VehiclePicture"/> draws the application's own
/// vehicle art. Per picture box only the latest request is
/// applied, so a slow answer never replaces the picture of a car chosen later.
/// </summary>
internal static class CarPictures
{
    /// <summary>Largest picture that is downloaded.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>Largest width or height that is decoded.</summary>
    public const int MaxDimension = 4096;

    /// <summary>Largest number of pixels that is decoded (16 megapixels, 64 MB as 32-bit colour).</summary>
    public const long MaxPixels = 16L * 1024 * 1024;

    /// <summary>Pictures downloaded and decoded at the same time; the others wait for a free place.</summary>
    public const int MaxConcurrentLoads = 3;

    internal const string TooLarge = "The picture's dimensions are too large.";

    internal const string NotAPicture = "Not an acceptable picture.";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly ConditionalWeakTable<PictureBox, RequestSequence> Sequences = new();

    private static readonly Lazy<HttpClient> Client = new(CreateClient);

    private static readonly SemaphoreSlim Loads = new(MaxConcurrentLoads, MaxConcurrentLoads);

    /// <summary>
    /// Empties the picture at once and, for an acceptable address, shows the downloaded picture when it
    /// arrives, unless another picture was asked for in the meantime.
    /// </summary>
    public static void Show(PictureBox pictureBox, string? address)
    {
        ArgumentNullException.ThrowIfNull(pictureBox);

        var request = Sequences.GetValue(pictureBox, _ => new RequestSequence()).Next();
        Replace(pictureBox, null);
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
    /// unspecified), not a private, shared or link-local network, not multicast or broadcast. An IPv6
    /// address that carries an IPv4 address (IPv4-mapped, IPv4-compatible, NAT64 64:ff9b::/96 and 6to4
    /// 2002::/16) is judged by that IPv4 address, since a gateway or the stack connects to it; the
    /// local-use NAT64 prefix 64:ff9b:1::/48 is never public.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            if (bytes is [0x00, 0x64, 0xFF, 0x9B, 0x00, 0x01, ..])
            {
                return false;
            }

            if (EmbeddedIPv4(bytes) is { } embedded)
            {
                address = embedded;
            }
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

    /// <summary>The IPv4 address inside an IPv6 transition address, or <see langword="null"/>.</summary>
    private static IPAddress? EmbeddedIPv4(byte[] v6)
    {
        var prefixOf96 = v6.AsSpan(0, 12);
        ReadOnlySpan<byte> mapped = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF];
        ReadOnlySpan<byte> compatible = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        ReadOnlySpan<byte> nat64 = [0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0];
        if (prefixOf96.SequenceEqual(mapped) || prefixOf96.SequenceEqual(compatible) || prefixOf96.SequenceEqual(nat64))
        {
            return new IPAddress(v6.AsSpan(12, 4));
        }

        // 6to4: 2002:AABB:CCDD::/48 carries AA.BB.CC.DD.
        return v6[0] == 0x20 && v6[1] == 0x02 ? new IPAddress(v6.AsSpan(2, 4)) : null;
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

    private static Task<Image> DownloadAsync(Uri uri) => LoadThrottledAsync(() => FetchAsync(uri));

    /// <summary>
    /// Fetches the bytes of a picture with <paramref name="fetch"/> and decodes them, with at most
    /// <see cref="MaxConcurrentLoads"/> loads at a time: a catalog full of cards that share one address
    /// downloads and opens a few pictures at once, not all of them.
    /// </summary>
    internal static async Task<Image> LoadThrottledAsync(Func<Task<byte[]>> fetch)
    {
        ArgumentNullException.ThrowIfNull(fetch);

        await Loads.WaitAsync().ConfigureAwait(false);
        try
        {
            var data = await fetch().ConfigureAwait(false);
            return Decode(data);
        }
        finally
        {
            Loads.Release();
        }
    }

    /// <summary>
    /// Decodes a downloaded picture. Its width and height are read from the file's header first, without
    /// decoding anything, and a picture of an unknown format or larger than <see cref="MaxDimension"/>
    /// or <see cref="MaxPixels"/> is refused before it is opened: a small file can declare a picture that
    /// needs gigabytes once decoded.
    /// </summary>
    internal static Image Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!PictureHeader.TryReadSize(data, out var width, out var height) || width <= 0 || height <= 0)
        {
            throw new InvalidDataException(NotAPicture);
        }

        if (width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
        {
            throw new InvalidDataException(TooLarge);
        }

        using var buffer = new MemoryStream(data, writable: false);
        using var decoded = Image.FromStream(buffer, useEmbeddedColorManagement: false, validateImageData: true);
        if (decoded.Width > MaxDimension || decoded.Height > MaxDimension)
        {
            throw new InvalidDataException(TooLarge);
        }

        // A copy detaches the image from the stream.
        return new Bitmap(decoded);
    }

    private static async Task<byte[]> FetchAsync(Uri uri)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        using var response = await Client.Value.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType is not { } type || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || response.Content.Headers.ContentLength > MaxBytes)
        {
            throw new InvalidDataException(NotAPicture);
        }

        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
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

        return buffer.ToArray();
    }

    private static void Replace(PictureBox pictureBox, Image? image)
    {
        var previous = pictureBox.Image;
        pictureBox.Image = image;
        if (previous is not null && !ReferenceEquals(previous, image))
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
