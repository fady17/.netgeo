// src/AutomotiveServices.Api/Dtos/UpdateCartItemQuantityRequestDto.cs
using System.ComponentModel.DataAnnotations;

namespace AutomotiveServices.Api.Dtos;

public class UpdateCartItemQuantityRequestDto
{
    [Range(0, 100, ErrorMessage = "Quantity must be between 0 and 100.")] // Allow 0 for removal by service
    public int NewQuantity { get; set; }
}