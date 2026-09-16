using FluentValidation;
using FutureKawaSiege.Commons.Models.API.Requests;

namespace FutureKawaSiege.Business.Validators;

/// <summary>
/// Validator for the Odoo webhook payload.
/// Ensures the payload contains the minimum required fields.
/// </summary>
public class OdooOrderWebhookValidator : AbstractValidator<OdooOrderWebhookDto>
{
    public OdooOrderWebhookValidator()
    {
        RuleFor(x => x.OrderId)
            .GreaterThan(0).WithMessage("OrderId must be a positive integer.");

        RuleFor(x => x.Client)
            .NotEmpty().WithMessage("Client is required.")
            .MaximumLength(256).WithMessage("Client name must not exceed 256 characters.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("At least one order line is required.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Product)
                .NotEmpty().WithMessage("Product name is required.")
                .MaximumLength(256);

            line.RuleFor(l => l.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than 0.");
        });
    }
}