using ERP_api.DTOs;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// The settings a tenant administers for itself, under System Administration.
    ///
    /// Available on every tier - every gym needs its own name on a receipt and its own shift
    /// length - but restricted to an owner, because these change how the application behaves
    /// for everybody in the company.
    /// </summary>
    [ApiController]
    [Route("api/settings")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.SystemAdmin)]
    [RequireSubmodule(ErpModules.Sub.Settings)]
    public class SettingsController : ControllerBase
    {
        private readonly ITenantSettingsService _settings;

        public SettingsController(ITenantSettingsService settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Every setting with its current value and whether that value is the shipped default
        /// or a deliberate choice. Only departures are stored, so a missing row is not a
        /// missing setting - it is the default.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<TenantSettingView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<TenantSettingView>>> GetAll()
        {
            return Ok(await _settings.GetAllAsync());
        }

        [HttpPut]
        [ProducesResponseType(typeof(IEnumerable<TenantSettingView>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<TenantSettingView>>> Update(
            [FromBody] UpdateSettingsDto dto)
        {
            await _settings.SetManyAsync(dto.Values);

            // The whole set is returned rather than only what changed, so the screen never has
            // to merge a partial response into what it already had.
            return Ok(await _settings.GetAllAsync());
        }

        [HttpPost("{key}/reset")]
        [ProducesResponseType(typeof(TenantSettingView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<TenantSettingView>> Reset(string key)
        {
            return Ok(await _settings.ResetAsync(key));
        }
    }
}
