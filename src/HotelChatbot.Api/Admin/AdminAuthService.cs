using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HotelChatbot.Api.Admin;

public class AdminAuthService
{
    private readonly AdminAuthOptions _options;

    public AdminAuthService(IOptions<AdminAuthOptions> options)
    {
        _options = options.Value;
    }

    public string? Authenticate(string password)
    {
        if (SecureEquals(password, _options.AdminPassword))
            return AdminRoles.Admin;

        if (SecureEquals(password, _options.HotelPassword))
            return AdminRoles.Hotel;

        return null;
    }

    public string CreateToken(string role)
    {
        var expires = DateTimeOffset.UtcNow.AddDays(_options.TokenLifetimeDays);
        var payload = $"{role}|{expires.ToUnixTimeSeconds()}";
        var signature = ComputeHmac(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{Convert.ToHexString(signature)}"));
    }

    public bool TryValidateToken(string? token, out string role)
    {
        role = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split('|');
            if (parts.Length != 3)
                return false;

            role = parts[0];
            if (role is not AdminRoles.Admin and not AdminRoles.Hotel)
                return false;

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > long.Parse(parts[1]))
                return false;

            var expectedSignature = ComputeHmac($"{parts[0]}|{parts[1]}");
            var actualSignature = Convert.FromHexString(parts[2]);

            return CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature);
        }
        catch
        {
            role = string.Empty;
            return false;
        }
    }

    private byte[] ComputeHmac(string payload)
    {
        var key = Encoding.UTF8.GetBytes(_options.TokenSecret);
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static bool SecureEquals(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
