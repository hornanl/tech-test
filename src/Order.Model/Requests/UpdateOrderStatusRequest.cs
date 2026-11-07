using System.ComponentModel.DataAnnotations;

namespace Order.Model.Requests
{
    public class UpdateOrderStatusRequest
    {
        [Required]
        [StringLength(20)]
        public string Status { get; set; }
    }
}
