using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ERP_infrastructure.services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ERP_api.Infrastructure
{
    public interface IJwtTokenService
    {
        /// <summary>Mints a bearer token carrying the company, role and module claims.</summary>
        (string Token, DateTime ExpiresAtUtc) CreateToken(AuthenticatedUser user);
    }

    /// <summary>
    /// Turns an authenticated user into a signed bearer token.
    ///
    /// The token carries the company id, which is what the tenant middleware resolves into a
    /// database, and one claim per permitted module, which is what the authorization filter
    /// checks. Both are signed, so a client can read them but cannot change them - which is the
    /// whole reason a header can no longer be trusted to select a tenant.
    /// </summary>
    public class JwtTokenService : IJwtTokenService
    {
        private readonly JwtOptions _options;

        public JwtTokenService(IOptions<JwtOptions> options)
        {
            _options = options.Value;
        }

        public (string Token, DateTime ExpiresAtUtc) CreateToken(AuthenticatedUser user)
        {
            var now = DateTime.UtcNow;
            var expires = now.AddMinutes(Math.Max(5, _options.TokenLifetimeMinutes));

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.AppUserId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, user.RoleKey),

                new(FitCoreClaims.AppUserId, user.AppUserId.ToString()),
                new(FitCoreClaims.CompanyId, user.CompanyId.ToString()),
                new(FitCoreClaims.CompanyName, user.CompanyName),
                new(FitCoreClaims.EnterpriseTier, user.EnterpriseTierName),
                new(FitCoreClaims.RoleKey, user.RoleKey),
                new(FitCoreClaims.RoleLevel, user.RoleLevel.ToString()),
                new(FitCoreClaims.FullName, user.FullName)
            };

            if (user.EmployeeId is int employeeId)
            {
                claims.Add(new Claim(FitCoreClaims.EmployeeId, employeeId.ToString()));
            }

            foreach (var module in user.Modules)
            {
                claims.Add(new Claim(FitCoreClaims.Module, module));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: now,
                expires: expires,
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return (new JwtSecurityTokenHandler().WriteToken(token), expires);
        }
    }
}
