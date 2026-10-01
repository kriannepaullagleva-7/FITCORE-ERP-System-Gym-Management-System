using ERP_api.DTOs;
using ERP_api.Infrastructure;
using ERP_domain.entities;
using ERP_infrastructure.services;
using ERP_infrastructure.tenant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// The company's branch network, under System Administration.
    ///
    /// Medium and Admin/Owner, both enforced here rather than merely in the desktop: the module
    /// guard refuses any tenant below Medium because System Administration is a Medium module,
    /// and the subfeature guard refuses a Manager or Staff because Branches is an Admin
    /// subfeature. Hiding the tab is a courtesy; these two attributes are the boundary.
    ///
    /// Note what is *not* here: nothing lets a caller name a branch for an ordinary read. That
    /// happens through the <c>X-Branch-Id</c> header, which the branch middleware validates
    /// against this same company's branches before anything else in the request runs.
    /// </summary>
    [ApiController]
    [Route("api/branches")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.SystemAdmin)]
    [RequireSubmodule(ErpModules.Sub.Branches)]
    public class BranchesController : ControllerBase
    {
        private readonly IBranchService _branches;

        public BranchesController(IBranchService branches)
        {
            _branches = branches;
        }

        /// <summary>Every branch this company runs, open or closed.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<BranchView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<BranchView>>> GetAll(
            [FromQuery] bool includeInactive = true)
        {
            return Ok(await _branches.GetBranchesAsync(includeInactive));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(BranchView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BranchView>> GetById(int id)
        {
            var branch = await _branches.GetBranchAsync(id);
            return branch is null ? NotFound() : Ok(branch);
        }

        [HttpPost]
        [ProducesResponseType(typeof(BranchView), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<BranchView>> Create([FromBody] CreateBranchDto dto)
        {
            var created = await _branches.CreateBranchAsync(new Branch
            {
                Code = dto.Code,
                Name = dto.Name,
                Address = dto.Address ?? "",
                Phone = dto.Phone ?? "",
                Email = dto.Email ?? "",
                OpenedOn = dto.OpenedOn ?? DateTime.UtcNow.Date
            });

            var view = await _branches.GetBranchAsync(created.BranchId);

            return CreatedAtAction(nameof(GetById), new { id = created.BranchId }, view);
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(BranchView), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BranchView>> Update(int id, [FromBody] UpdateBranchDto dto)
        {
            var updated = await _branches.UpdateBranchAsync(
                id, dto.Code, dto.Name, dto.Address ?? "", dto.Phone ?? "",
                dto.Email ?? "", dto.IsActive);

            return updated is null ? NotFound() : Ok(updated);
        }

        /// <summary>
        /// Makes this the branch an unassigned record falls to. Exactly one branch carries it,
        /// so naming a new one clears the old.
        /// </summary>
        [HttpPost("{id:int}/primary")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetPrimary(int id)
        {
            return await _branches.SetPrimaryAsync(id) ? NoContent() : NotFound();
        }

        /// <summary>
        /// Refused as soon as anything has happened at the branch. Closing it through
        /// <c>PUT</c> is the supported alternative and keeps every figure intact.
        /// </summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            return await _branches.DeleteBranchAsync(id) ? NoContent() : NotFound();
        }

        /// <summary>
        /// Moves people - members, employees, walk-in customers - from one branch to another.
        ///
        /// Transactions are deliberately not transferable: a sale, a payment, a pay run and an
        /// expense say what happened at a particular site on a particular day, and moving one
        /// would rewrite two branches' revenue after the fact.
        /// </summary>
        [HttpPost("transfer")]
        [ProducesResponseType(typeof(BranchTransferResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<BranchTransferResult>> Transfer(
            [FromBody] BranchTransferDto dto)
        {
            var result = await _branches.TransferAsync(
                dto.FromBranchId, dto.ToBranchId, dto.Scope);

            return Ok(result);
        }
    }
}
