using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TMusic.Api.Identity;
using Xunit;
namespace TMusic.Tests;

public sealed class ExternalTokenSecurityTests
{
    [Theory]
    [InlineData("google","https://accounts.google.com","mobile-client",false,false,true)]
    [InlineData("google","https://attacker.example","mobile-client",false,false,false)]
    [InlineData("google","https://accounts.google.com","other-client",false,false,false)]
    [InlineData("google","https://accounts.google.com","mobile-client",true,false,false)]
    [InlineData("google","https://accounts.google.com","mobile-client",false,true,false)]
    [InlineData("apple","https://appleid.apple.com","mobile-client",false,false,true)]
    [InlineData("apple","https://accounts.google.com","mobile-client",false,false,false)]
    public async Task SignatureIssuerAudienceAndExpiryAreRequired(string provider,string issuer,string audience,bool expired,bool otherKey,bool expected)
    {
        using var trusted=RSA.Create(2048); using var attacker=RSA.Create(2048);
        var key=new RsaSecurityKey(trusted) {KeyId="trusted"};
        var signer=otherKey?new RsaSecurityKey(attacker) {KeyId="trusted"}:key;
        var token=new JwtSecurityToken(issuer,audience,[new Claim("sub","subject"),new Claim("email_verified","true")],
            DateTime.UtcNow.AddHours(-2),expired?DateTime.UtcNow.AddHours(-1):DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(signer,SecurityAlgorithms.RsaSha256));
        var result=await new JsonWebTokenHandler().ValidateTokenAsync(new JwtSecurityTokenHandler().WriteToken(token),
            ExternalTokenVerifier.ValidationParameters(provider,["mobile-client"],[key]));
        Assert.Equal(expected,result.IsValid);
    }

    [Fact]
    public async Task UnsignedTokenIsRejected()
    {
        using var trusted=RSA.Create(2048);
        var token=new JwtSecurityToken("https://accounts.google.com","mobile-client",[new Claim("sub","subject")],DateTime.UtcNow,DateTime.UtcNow.AddMinutes(5));
        var result=await new JsonWebTokenHandler().ValidateTokenAsync(new JwtSecurityTokenHandler().WriteToken(token),
            ExternalTokenVerifier.ValidationParameters("google",["mobile-client"],[new RsaSecurityKey(trusted)]));
        Assert.False(result.IsValid);
    }
}
