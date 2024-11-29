using System.ComponentModel;
using System.IdentityModel.Tokens.Jwt;
using System.Text.RegularExpressions;

namespace Altinn.App.Core.Models;

/// <summary>
/// Represents an OAuth 2.0 access token in JWT format
/// Needs to be unencrypted
/// </summary>
[ImmutableObject(true)] // `ImmutableObject` prevents serialization with HybridCache
public readonly partial struct JwtToken : IEquatable<JwtToken>
{
    /// <summary>
    /// The access token value (JWT format)
    /// </summary>
    public string Value { get; }

    private readonly JwtSecurityToken _jwtSecurityToken;

    /// <summary>
    /// The instant in time when the token expires
    /// </summary>
    public DateTimeOffset ExpiresAt => _jwtSecurityToken.ValidTo;

    /// <summary>
    /// Is the token expired?
    /// </summary>
    public bool IsExpired(TimeProvider? timeProvider = null) =>
        ExpiresAt < (timeProvider?.GetUtcNow() ?? DateTimeOffset.UtcNow);

    /// <summary>
    /// The issuing authority of the token
    /// </summary>
    public string Issuer => _jwtSecurityToken.Issuer;

    /// <summary>
    /// The scope(s) associated with the token
    /// </summary>
    public string? Scope => _jwtSecurityToken.Payload.TryGetValue("scope", out var scope) ? scope.ToString() : null;

    private JwtToken(string jwtToken, JwtSecurityToken jwtSecurityToken)
    {
        Value = jwtToken;
        _jwtSecurityToken = jwtSecurityToken;
    }

    /// <summary>
    /// Parses an access token
    /// </summary>
    /// <param name="value">The value to parse</param>
    /// <exception cref="FormatException">The access token is not valid</exception>
    public static JwtToken Parse(string value)
    {
        return TryParse(value, out var accessToken)
            ? accessToken
            : throw new FormatException($"Invalid access token format: {value}");
    }

    /// <summary>
    /// Attempt to parse an access token
    /// </summary>
    /// <param name="value">The value to parse</param>
    /// <param name="jwtToken">The resulting <see cref="JwtToken"/> instance</param>
    /// <returns>`true` on successful parse, `false` otherwise</returns>
    public static bool TryParse(string value, out JwtToken jwtToken)
    {
        jwtToken = default;

        JwtSecurityTokenHandler handler = new();
        try
        {
            JwtSecurityToken jwt = handler.ReadJwtToken(value);
            jwtToken = new JwtToken(value, jwt);
        }
        catch
        {
            return false;
        }

        return true;
    }

    // Matches a string with pattern eyXXX.eyXXX.XXX, allowing underscores and hyphens
    [GeneratedRegex(@"^((?:ey[\w-]+\.){2})([\w-]+)$")]
    private static partial Regex JwtRegex();

    private static string MaskJwtSignature(string accessToken)
    {
        var accessTokenMatch = JwtRegex().Match(accessToken);
        return accessTokenMatch.Success ? $"{accessTokenMatch.Groups[1]}<masked>" : "<masked>";
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object
    /// </summary>
    public bool Equals(JwtToken other) => Value == other.Value;

    /// <summary>
    /// Determines whether the specified object is equal to the current object
    /// </summary>
    public override bool Equals(object? obj) => obj is JwtToken other && Equals(other);

    /// <summary>
    /// Returns the hash code for the access token value
    /// </summary>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>
    /// Returns a string representation of the access token with a masked signature component
    /// </summary>
    public override string ToString() => MaskJwtSignature(Value);

    /// <summary>
    /// Returns a string representation of the access token with an intact signature component
    /// </summary>
    public string ToStringUnmasked() => Value;

    /// <summary>
    /// Determines whether two specified instances of <see cref="JwtToken"/> are equal
    /// </summary>
    public static bool operator ==(JwtToken left, JwtToken right) => left.Equals(right);

    /// <summary>
    /// Determines whether two specified instances of <see cref="JwtToken"/> are not equal
    /// </summary>
    public static bool operator !=(JwtToken left, JwtToken right) => !left.Equals(right);

    /// <summary>
    /// Implicit conversion from <see cref="JwtToken"/> to string
    /// </summary>
    /// <param name="accessToken">The access token instance</param>
    public static implicit operator string(JwtToken accessToken)
    {
        return accessToken.Value;
    }
}