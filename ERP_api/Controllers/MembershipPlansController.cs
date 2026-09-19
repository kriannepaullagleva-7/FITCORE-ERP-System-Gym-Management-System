using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Membership plans: the products of the Membership module that subscriptions are sold
    /// against.
    /// </summary>
    [ApiController]
    [Route("api/membership-plans")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Membership)]
    public class MembershipPlansController : ControllerBase
    {
        private readonly IMembershipPlanService _planService;

        public MembershipPlansController(IMembershipPlanService planService)
        {
            _planService = planService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<MembershipPlanDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<MembershipPlanDto>>> GetAll()
        {
            var plans = await _planService.GetAllPlansAsync();
            return Ok(plans.Select(p => p.ToDto()));
        }

        [HttpGet("active")]
        [ProducesResponseType(typeof(IEnumerable<MembershipPlanDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<MembershipPlanDto>>> GetActive()
        {
            var plans = await _planService.GetActivePlansAsync();
            return Ok(plans.Select(p => p.ToDto()));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(MembershipPlanDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MembershipPlanDto>> GetById(int id)
        {
            var plan = await _planService.GetPlanByIdAsync(id);
            return plan is null ? NotFound() : Ok(plan.ToDto());
        }

        [HttpPost]
        [ProducesResponseType(typeof(MembershipPlanDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<MembershipPlanDto>> Create(
            [FromBody] CreateMembershipPlanDto dto)
        {
            var plan = await _planService.CreatePlanAsync(
                dto.PlanName, dto.DurationMonths, dto.Price, dto.Description);

            return CreatedAtAction(nameof(GetById), new { id = plan.PlanId }, plan.ToDto());
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(MembershipPlanDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MembershipPlanDto>> Update(
            int id, [FromBody] UpdateMembershipPlanDto dto)
        {
            var plan = await _planService.UpdatePlanAsync(
                id, dto.PlanName, dto.DurationMonths, dto.Price, dto.Description, dto.IsActive);

            return plan is null ? NotFound() : Ok(plan.ToDto());
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _planService.DeletePlanAsync(id);

            return result switch
            {
                PlanDeleteResult.Deleted => NoContent(),
                PlanDeleteResult.InUse => Conflict(new
                {
                    message = "This plan is used by existing subscriptions. " +
                              "Deactivate it instead of deleting it."
                }),
                _ => NotFound()
            };
        }
    }
}
