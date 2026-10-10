using System.Net;
using System.Net.Sockets;

namespace BGPWatch.Core.Domain;

/// <summary>Prefix comparisons over <see cref="IPNetwork"/>, the prefix value type.</summary>
public static class Prefixes
{
    /// <summary>True when <paramref name="outer"/> contains <paramref name="inner"/> or equals it.</summary>
    public static bool Covers(this IPNetwork outer, IPNetwork inner)
    {
        return SameFamily(outer, inner)
            && inner.PrefixLength >= outer.PrefixLength
            && outer.Contains(inner.BaseAddress);
    }

    public static bool SameFamily(IPNetwork a, IPNetwork b)
    {
        return a.BaseAddress.AddressFamily == b.BaseAddress.AddressFamily;
    }

    /// <summary>A prefix from text whose host bits may be set; they are cleared.</summary>
    public static IPNetwork ParseLoose(string text)
    {
        var slash = text.IndexOf('/');
        var address = ParseAddress(slash < 0 ? text : text[..slash]);
        var bits = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var lengthText = slash < 0 ? bits.ToString() : text[(slash + 1)..];
        if (!lengthText.All(char.IsAsciiDigit) || !int.TryParse(lengthText, out var length) || length > bits)
        {
            throw new FormatException($"prefix length out of range: {text}");
        }
        return new IPNetwork(Masked(address, length), length);
    }

    /// <summary>
    /// An IPv4 address in dotted-quad form (four decimal octets, no leading zeros) or an
    /// IPv6 address; the shorthand and octal forms <see cref="IPAddress.Parse(string)"/>
    /// also takes are refused.
    /// </summary>
    public static IPAddress ParseAddress(string text)
    {
        if (text.Contains(':'))
        {
            return !text.Contains('%') && IPAddress.TryParse(text, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6
                ? v6
                : throw new FormatException($"not an IPv6 address: {text}");
        }
        var octets = text.Split('.');
        if (octets.Length != 4 || !octets.All(IsOctet))
        {
            throw new FormatException($"not an IPv4 address: {text}");
        }
        return new IPAddress(octets.Select(byte.Parse).ToArray());
    }

    private static bool IsOctet(string text)
    {
        return text.Length is > 0 and <= 3
            && text.All(char.IsAsciiDigit)
            && (text.Length == 1 || text[0] != '0')
            && int.Parse(text) <= 255;
    }

    /// <summary>The address with every bit past <paramref name="length"/> cleared.</summary>
    public static IPAddress Masked(IPAddress address, int length)
    {
        var bytes = address.GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            var kept = Math.Clamp(length - i * 8, 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - kept));
        }
        return new IPAddress(bytes);
    }
}
