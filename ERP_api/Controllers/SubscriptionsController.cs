using ERP_api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ERP_api.DTOs;
using ERP_domain.entities;
using ERP_infrastructure.services;
using Microsoft.AspNetCore.Mvc;

namespace ERP_api.Controllers
{
    /// <summary>
    /// Subscriptions: the enrolment of a member on a membership plan for a period.
    /// </summary>
    [ApiController]
    [Route("api/subscriptions")]
    [Produces("application/json")]
    [Authorize]
    [RequireModule(ErpModules.Membership)]
    public class SubscriptionsController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;

        public SubscriptionsController(
            ISubscriptionService subscriptionService,
            IPaymentService paymentService)
        {
            _subscriptionService = subscriptionService;
            _paymentService = paymentService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<SubscriptionDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SubscriptionDto>>> GetAll()
        {
            var subscriptions = await _subscriptionService.GetAllSubscriptionsAsync();
            return Ok(subscriptions.Select(s => s.ToDto()));
        }

        // Declared before the {id} route so the literal wins the match.
        [HttpGet("active")]
        [ProducesResponseType(typeof(IEnumerable<SubscriptionDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<SubscriptionDto>>> GetActive()
        {
            var subscriptions = await _subscriptionService.GetActiveSubscriptionsAsync();
            return Ok(subscriptions.Select(s => s.ToDto()));
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SubscriptionDto>> GetById(int id)
        {
            var subscription = await _subscriptionService.GetSubscriptionByIdAsync(id);
            return subscription is null ? NotFound() : Ok(subscription.ToDto());
        }

        [HttpPost]
        [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SubscriptionDto>> Create(
            [FromBody] CreateSubscriptionDto dto)
        {
            var subscription = await _subscriptionService.CreateSubscriptionAsync(
                dto.MemberId, dto.PlanId, dto.StartDate ?? DateTime.UtcNow,
                dto.WalkInName, dto.WalkInPhone);

            return CreatedAtAction(
                nameof(GetById),
                new { id = subscription.SubscriptionId },
                subscription.ToDto());
        }

        [HttpPost("{id:int}/renew")]
        [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SubscriptionDto>> Renew(int id)
        {
            var subscription = await _subscriptionService.RenewSubscriptionAsync(id);
            return subscription is null ? NotFound() : Ok(subscription.ToDto());
        }

        [HttpPost("{id:int}/cancel")]
        [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SubscriptionDto>> Cancel(int id)
        {
            var subscription = await _subscriptionService.CancelSubscriptionAsync(id);
            return subscription is null ? NotFound() : Ok(subscription.ToDto());
        }

        [HttpPost("expire-overdue")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ExpireOverdue()
        {
            var count = await _subscriptionService.ExpireOverdueSubscriptionsAsync();
            return Ok(new { expired = count });
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _subscriptionService.DeleteSubscriptionAsync(id);
            return deleted ? NoContent() : NotFound();
        }

        [HttpGet("{subscriptionId:int}/payments")]
        [ProducesResponseType(typeof(IEnumerable<PaymentDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<PaymentDto>>> GetPayments(int subscriptionId)
        {
            List<Payment> payments =
                await _paymentService.GetSubscriptionPaymentsAsync(subscriptionId);

            return Ok(payments.Select(p => p.ToDto()));
        }
    }
}
