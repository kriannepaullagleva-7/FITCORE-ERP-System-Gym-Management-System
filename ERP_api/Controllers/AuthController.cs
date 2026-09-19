using ERP_api.DTOs;
using ERP_api.Infrastructure;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Sign-in and the signed-in user.
    ///
    /// This is the only controller a caller can reach without a token, and the only place a
    /// company is ever decided. Everything downstream reads the company out of the signed token
    /// rather than out of the request, which is what makes tenant isolation hold.
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly IUserAuthenticationService _authentication;
        private readonly IJwtTokenService _tokens;

        public AuthController(
            IUserAuthenticationService authentication,
            IJwtTokenService tokens)
        {
            _authentication = authentication;
            _tokens = tokens;
        }

        /// <summary>
        /// Verifies credentials and returns a bearer token carrying the company, role and
        /// module claims.
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<LoginResponseDto>> Login(
            [FromBody] LoginRequestDto request, CancellationToken cancellationToken)
        {
            var result = await _authentication.AuthenticateAsync(
                request.Username, request.Password, cancellationToken);

            if (!result.Succeeded)
            {
                return result.Reason switch
                {
                    // A deactivated account is told so plainly. It is not a secret from someone
                    // who has already proved they know the password, and "wrong password" would
                    // send them round in circles.
                    LoginFailureReason.AccountDeactivated => Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Account deactivated",
                        detail: "This account has been deactivated. " +
                                "Contact your FitCore administrator to have it reinstated."),

                    LoginFailureReason.CompanyInactive => Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Company inactive",
                        detail: "The company this account belongs to is not active. " +
                                "Contact your FitCore administrator."),

                    // Deliberately does not say which half was wrong.
                    _ => Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Sign-in failed",
                        detail: "That username and password combination was not recognised.")
                };
            }

            var user = result.User!;
            var (token, expiresAt) = _tokens.CreateToken(user);

            return Ok(new LoginResponseDto
            {
                Token = token,
                ExpiresAtUtc = expiresAt,
                User = user.ToDto()
            });
        }

        /// <summary>
        /// Re-reads the signed-in user straight from the database.
        ///
        /// The client calls this on start-up rather than trusting the token it has in storage,
        /// so an account that has since been deactivated, or had a module withdrawn, is caught
        /// on the next page load instead of at the end of the token lifetime.
        /// </summary>
        [HttpGet("me")]
        [Authorize]
        [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken)
        {
            var user = await _authentication.GetAuthenticatedUserAsync(
                User.GetAppUserId(), cancellationToken);

            if (user is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Session no longer valid",
                    detail: "This account is no longer active. Please sign in again.");
            }

            return Ok(user.ToDto());
        }

        /// <summary>
        /// Issues a fresh token for the signed-in user.
        ///
        /// Permissions are baked into a token when it is minted, so an administrator granting a
        /// module does not reach a user who is already signed in. This lets the client pick the
        /// change up without making the person sign out and back in.
        /// </summary>
        [HttpPost("refresh")]
        [Authorize]
        [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<LoginResponseDto>> Refresh(CancellationToken cancellationToken)
        {
            var user = await _authentication.GetAuthenticatedUserAsync(
                User.GetAppUserId(), cancellationToken);

            if (user is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Session no longer valid",
                    detail: "This account is no longer active. Please sign in again.");
            }

            var (token, expiresAt) = _tokens.CreateToken(user);

            return Ok(new LoginResponseDto
            {
                Token = token,
                ExpiresAtUtc = expiresAt,
                User = user.ToDto()
            });
        }

        [HttpPost("change-password")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ChangePassword(
            [FromBody] ChangePasswordRequestDto request, CancellationToken cancellationToken)
        {
            var changed = await _authentication.ChangePasswordAsync(
                User.GetAppUserId(), request.CurrentPassword, request.NewPassword, cancellationToken);

            if (!changed)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Password not changed",
                    detail: "Your current password was not correct.");
            }

            return NoContent();
        }

        /// <summary>
        /// Present so the client has a single place to call on sign-out and so the event is
        /// logged. The token is a bearer token with no server-side session, so the effective
        /// act of signing out is the client discarding it.
        /// </summary>
        [HttpPost("logout")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public IActionResult Logout(
            [FromServices] ILogger<AuthController> logger)
        {
            logger.LogInformation("{Username} signed out.", User.GetUsername());
            return NoContent();
        }
    }
}
