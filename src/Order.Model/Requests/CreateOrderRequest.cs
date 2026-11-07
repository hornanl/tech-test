using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Order.Model.Requests
{
    public class CreateOrderRequest : IValidatableObject
    {
        // Nullable IDs let Required catch missing or null values.
        [Required]
        public Guid? ResellerId { get; set; }

        [Required]
        public Guid? CustomerId { get; set; }

        [Required]
        // Required catches a null list. MinLength catches an empty list.
        [MinLength(1)]
        public List<CreateOrderItemRequest> Items { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Required does not reject Guid.Empty, so check it separately.
            if (ResellerId == Guid.Empty)
                yield return new ValidationResult("ResellerId must not be empty.", new[] { nameof(ResellerId) });
            if (CustomerId == Guid.Empty)
                yield return new ValidationResult("CustomerId must not be empty.", new[] { nameof(CustomerId) });
            // The list may contain items, but none of them may be null.
            if (Items != null && Items.Contains(null))
                yield return new ValidationResult("Items must not contain null entries.", new[] { nameof(Items) });
        }
    }

    public class CreateOrderItemRequest : IValidatableObject
    {
        [Required]
        public Guid? ProductId { get; set; }

        [Required]
        public Guid? ServiceId { get; set; }

        [Required]
        // Required catches missing quantities. Range rejects zero and negative quantities.
        [Range(1, int.MaxValue)]
        public int? Quantity { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Required does not reject Guid.Empty, so check it separately.
            if (ProductId == Guid.Empty)
                yield return new ValidationResult("ProductId must not be empty.", new[] { nameof(ProductId) });
            if (ServiceId == Guid.Empty)
                yield return new ValidationResult("ServiceId must not be empty.", new[] { nameof(ServiceId) });
        }
    }
}
