using ERP_domain.entities;
using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Members of the gym. The tenant is resolved by the server before this controller runs,
    /// so every action here reads and writes only the caller's own company database.
    /// </summary>
    [ApiController]
    [Route("api/members")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Membership)]
    public class MembersController : ControllerBase
    {
        private readonly IMemberService _memberService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;
        private readonly ISaleService _saleService;

        public MembersController(
            IMemberService memberService,
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            ISaleService saleService)
        {
            _memberService = memberService;
            _subscriptionService = subscriptionService;
            _paymentService = paymentService;
            _saleService = saleService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<MemberDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<MemberDto>>> GetAll()
        {
            var members = await _memberService.GetAllMembersAsync();
            return Ok(members.Select(m => m.ToDto()));
        }

        // Declared before the {id} route so the literal wins the match.
        [HttpGet("search")]
        [ProducesResponseType(typeof(IEnumerable<MemberDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<MemberDto>>> Search([FromQuery] string? term)
        {
            var members = await _memberService.SearchMembersAsync(term ?? string.Empty);
            return Ok(members.Select(m => m.ToDto()));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(MemberDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MemberDto>> GetById(int id)
        {
            var member = await _memberService.GetMemberByIdAsync(id);
            return member is null ? NotFound() : Ok(member.ToDto());
        }

        [HttpPost]
        [ProducesResponseType(typeof(MemberDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<MemberDto>> Create([FromBody] CreateMemberDto dto)
        {
            var member = await _memberService.CreateMemberAsync(
                dto.FirstName, dto.LastName, dto.Phone, dto.Email);

            return CreatedAtAction(
                nameof(GetById), new { id = member.MemberId }, member.ToDto());
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(MemberDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MemberDto>> Update(int id, [FromBody] UpdateMemberDto dto)
        {
            var member = await _memberService.UpdateMemberAsync(
                id, dto.FirstName, dto.LastName, dto.Phone, dto.Email, dto.Status);

            return member is null ? NotFound() : Ok(member.ToDto());
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _memberService.DeleteMemberAsync(id);

            return result switch
            {
                MemberDeleteResult.Deleted => NoContent(),
                MemberDeleteResult.HasHistory => Conflict(new
                {
                    message = "This member has subscriptions, payments or sales on record, " +
                              "which would be lost. Archive them instead to keep their history.",
                    canArchive = true
                }),
                _ => NotFound()
            };
        }

        /// <summary>
        /// Retires a member while keeping everything they ever paid for. This is what a gym
        /// actually wants when somebody stops coming: the person leaves the active list, and
        /// last year's takings still add up.
        /// </summary>
        [HttpPost("{id:int}/archive")]
        [ProducesResponseType(typeof(MemberDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MemberDto>> Archive(int id)
        {
            var member = await _memberService.ArchiveMemberAsync(id);
            return member is null ? NotFound() : Ok(member.ToDto());
        }

        [HttpPost("{id:int}/restore")]
        [ProducesResponseType(typeof(MemberDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MemberDto>> Restore(int id)
        {
            var member = await _memberService.RestoreMemberAsync(id);
            return member is null ? NotFound() : Ok(member.ToDto());
        }

        // Sub-resources. These read through the owning module's service rather than reaching
        // into another controller, which keeps the service layer as the single entry point.

        [HttpGet("{memberId:int}/subscriptions")]
        [ProducesResponseType(typeof(IEnumerable<SubscriptionDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SubscriptionDto>>> GetSubscriptions(int memberId)
        {
            var subscriptions = await _subscriptionService.GetMemberSubscriptionsAsync(memberId);
            return Ok(subscriptions.Select(s => s.ToDto()));
        }

        [HttpGet("{memberId:int}/payments")]
        [ProducesResponseType(typeof(IEnumerable<PaymentView>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<PaymentView>>> GetPayments(int memberId)
        {
            var payments = await _paymentService.GetMemberPaymentsAsync(memberId);
            return Ok(payments);
        }

        [HttpGet("{memberId:int}/sales")]
        [ProducesResponseType(typeof(IEnumerable<SaleDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SaleDto>>> GetSales(int memberId)
        {
            var sales = await _saleService.GetMemberSalesAsync(memberId);
            return Ok(sales.Select(s => s.ToDto()));
        }
    }
}
