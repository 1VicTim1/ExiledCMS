using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExiledCms.AuthService.Api.Domain;

namespace ExiledCms.AuthService.Api.Infrastructure;

// PBKDF2-SHA256 with a 16-byte random salt. Iteration count is configurable so
// it can be raised over time without breaking existing hashes — the per-user
// count is stored alongside the hash.
public sealed class PasswordHasher
{
    public const string Algorithm = "pbkdf2-sha256";
    public const int DefaultIterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public sealed record HashedPassword(string Hash, string Salt, string Algorithm, int Iterations);

    public HashedPassword Hash(string password, int? iterations = null)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password must not be empty.", nameof(password));
        }

        var rounds = iterations ?? DefaultIterations;
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, rounds, HashAlgorithmName.SHA256, HashSize);
        return new HashedPassword(Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes), Algorithm, rounds);
    }

    public bool Verify(string password, string storedHash, string storedSalt, string algorithm, int iterations)
    {
        if (!string.Equals(algorithm, Algorithm, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var saltBytes = Convert.FromBase64String(storedSalt);
        var expected = Convert.FromBase64String(storedHash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

public sealed class TotpService
{
    private const int SecretSize = 20;
    private const int Digits = 6;
    private const int StepSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateSecret()
    {
        return Base32Encode(RandomNumberGenerator.GetBytes(SecretSize));
    }

    public string BuildOtpAuthUri(string issuer, string email, string secret)
    {
        var normalizedIssuer = Uri.EscapeDataString(string.IsNullOrWhiteSpace(issuer) ? "ExiledCMS" : issuer.Trim());
        var normalizedEmail = Uri.EscapeDataString(email.Trim());
        return $"otpauth://totp/{normalizedIssuer}:{normalizedEmail}?secret={secret}&issuer={normalizedIssuer}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    public bool ValidateCode(string secret, string? code, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalizedCode = new string(code.Where(char.IsDigit).ToArray());
        if (normalizedCode.Length != Digits)
        {
            return false;
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        for (var window = -1; window <= 1; window++)
        {
            if (string.Equals(GenerateCode(secret, timestamp.AddSeconds(window * StepSeconds)), normalizedCode, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public string GenerateCode(string secret, DateTimeOffset timestamp)
    {
        var key = Base32Decode(secret);
        var counter = timestamp.ToUnixTimeSeconds() / StepSeconds;
        Span<byte> counterBytes = stackalloc byte[8];
        for (var index = 7; index >= 0; index--)
        {
            counterBytes[index] = (byte)(counter & 0xff);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;
        var binaryCode =
            ((hash[offset] & 0x7f) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];

        var otp = binaryCode % (int)Math.Pow(10, Digits);
        return otp.ToString($"D{Digits}");
    }

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var output = new StringBuilder((int)Math.Ceiling(data.Length / 5d) * 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 0x1f]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1f]);
        }

        return output.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        var normalized = value.Trim().TrimEnd('=').ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return [];
        }

        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in normalized)
        {
            var index = Base32Alphabet.IndexOf(character);
            if (index < 0)
            {
                throw new FormatException("TOTP secret contains unsupported Base32 characters.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;

            if (bitsLeft < 8)
            {
                continue;
            }

            output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xff));
            bitsLeft -= 8;
        }

        return output.ToArray();
    }
}

// Hand-rolled HS256 JWT issuer — keeps the dependency footprint small. If we
// later need RS256, token revocation or a refresh-token flow, swapping to
// Microsoft.IdentityModel.Tokens is mechanical.
public sealed class JwtIssuer
{
    private readonly JwtRuntimeOptionsAccessor _optionsAccessor;

    public JwtIssuer(JwtRuntimeOptionsAccessor optionsAccessor)
    {
        _optionsAccessor = optionsAccessor;
    }

    public sealed record IssuedToken(string Token, DateTime ExpiresAtUtc);

    public IssuedToken Issue(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var opts = _optionsAccessor.GetCurrent();
        if (string.IsNullOrWhiteSpace(opts.Secret))
        {
            throw new InvalidOperationException("Jwt.Secret is not configured.");
        }

        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(Math.Max(1, opts.AccessTokenLifetimeMinutes));

        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = opts.Issuer,
            ["aud"] = opts.Audience,
            ["sub"] = user.Id.ToString("D"),
            ["email"] = user.Email,
            ["name"] = user.DisplayName,
            ["email_verified"] = user.EmailVerified,
            ["roles"] = roles,
            ["permissions"] = permissions,
            ["iat"] = new DateTimeOffset(issuedAt).ToUnixTimeSeconds(),
            ["exp"] = new DateTimeOffset(expiresAt).ToUnixTimeSeconds(),
        };

        var headerSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes(headerSegment + "." + payloadSegment);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(opts.Secret), signingInput);
        var signatureSegment = Base64UrlEncode(signature);

        return new IssuedToken($"{headerSegment}.{payloadSegment}.{signatureSegment}", expiresAt);
    }

    public bool TryValidate(string token, out IReadOnlyDictionary<string, JsonElement> claims)
    {
        claims = new Dictionary<string, JsonElement>();
        var opts = _optionsAccessor.GetCurrent();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(opts.Secret))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var signingInput = Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]);
        var expectedSignature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(opts.Secret), signingInput);
        byte[] providedSignature;
        try
        {
            providedSignature = Base64UrlDecode(parts[2]);
        }
        catch
        {
            return false;
        }
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
        {
            return false;
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[1]);
        }
        catch
        {
            return false;
        }

        var document = JsonDocument.Parse(payloadBytes);
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            dict[property.Name] = property.Value.Clone();
        }

        if (!ClaimMatches(dict, "iss", opts.Issuer) || !ClaimMatches(dict, "aud", opts.Audience))
        {
            return false;
        }

        // Expiry check — do not trust tokens whose `exp` claim has passed.
        if (dict.TryGetValue("exp", out var exp) && exp.ValueKind == JsonValueKind.Number)
        {
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).UtcDateTime;
            if (expiresAt < DateTime.UtcNow)
            {
                return false;
            }
        }

        claims = dict;
        return true;
    }

    private static bool ClaimMatches(IReadOnlyDictionary<string, JsonElement> claims, string key, string expected)
    {
        if (!claims.TryGetValue(key, out var claim) || claim.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return string.Equals(claim.GetString(), expected, StringComparison.Ordinal);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var normalized = input.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
        }
        return Convert.FromBase64String(normalized);
    }
}
